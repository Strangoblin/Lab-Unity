# Weather 分阶段整合任务

日期：2026-10-08。状态：已实施，正在完成真实管线验收。

用户选择：单一天气预设＋内部模块分别调节／关闭。需求编号见 [requirements.md](requirements.md)，详细接口和兼容映射见 [design.md](design.md)。用户已授权实施。

追加决定：Weather只负责模块化Particles＋Atmosphere；Rain blur直接抛弃，雨滴折射与水汽遮罩／染色保留，不新增独立BlurFeature。

## 执行依赖

```mermaid
flowchart LR
    A[0 基线与门禁准备] --> B[1 粒子与氛围模块抽取]
    B --> C[2 Weather正式入口]
    C --> D[3 参数解耦与端点]
    D --> E[4 真管线验收]
    E --> F[5 确认后归档]
```

每个阶段是一个可独立验收与回退的工作单位；实施时以精确 pathspec 提交，不能把其它用户改动纳入检查点。模块与预设检查点：`7fcc8e4`。没有删除或移动旧天气资源。

## 已完成的规划工作

- [x] A. 读取项目角色、结构规范、后处理／RenderGraph参考、Snowy记录及参数范围约定。
- [x] B. 对比三套 Shader 的函数、Pass、非线性参数响应、Alpha和固定采样预算。
- [x] C. 核对三套材质、GUID、同一 Noise2DRG 资源及其它引用；识别文档与实际值差异。
- [x] D. 核对保存的 Renderer 配置、当前URP版本与官方本地包实现。
- [x] E. 确认首版单预设范围，形成需求、架构与实施任务。
- [x] F. 根据用户补充，将Rain blur移出Weather范围，调整目标基线、参数与验收。

完成项仅是静态检查，不代表编译、运行、视觉或性能验收已通过。

## 0. 实施基线与门禁准备

- [x] 0.1 再核当前工作区，按精确路径冻结源文件、材质、meta和PC_Renderer状态。
  - 使用当前 dirty/untracked 工作副本，记录 Rainy/Sandy/Snowy GUID、材质参数、依赖与引用路径。
  - 检查实施前是否已有新的用户改动；保存／提交基线不得覆盖或打包其它工作。
  - _Requirements: REQ-01, REQ-03, REQ-10_
- [x] 0.2 只读核查 Editor 与 AssetDatabase 实际状态。
  - 读取当前Renderer／Feature启用状态，区别磁盘与未保存内存态。
  - 核验 Sandy Material 的可加载性和引用，不凭meta文本重建GUID。
  - 记录桥接是否可用；不可用时只完成静态准备，运行验收保持待办。
  - _Requirements: REQ-01, REQ-06_
- [ ] 0.3 固定基准场景、同帧输入／时间、颜色格式与误差阈值；采三旧效果的正常值参考。
  - 透视相机作为三者共同基线；雨沙补正交基线，Snow旧正交限制单独记录。
  - 另建Rain仅取消blur的目标基线并标注预期柔化差异；原版仍作历史对照，其它逻辑保持不变。
  - 不提前修Sandy coverage、Snow Alpha、噪声色彩空间或Rain折射长宽比。
  - _Requirements: REQ-01, REQ-05, REQ-06, REQ-11, REQ-12_
- [x] 0.4 完成实现文件规范对照、模板加载与门禁准备。
  - 确定C#位于Scripts/Weather、Shader/HLSL位于PostProcess/Weather。
  - 明确静态2D噪声与相机`_X`资源契约，解决门禁规则中宽泛纹理要求的适用边界。
  - 新产物先按项目流程暂存，自查check_norm，并走Assets/Mine合法合入通道。
  - _Requirements: REQ-03, REQ-06, REQ-10_

阶段出口：有可复现的真实基线及可落地的文件清单。回退点：保留当前工作副本，不改原三套功能文件。

## 1. 粒子与氛围模块抽取

- [x] 1.1 新建WeatherCommon，提取相同随机数、周期网格工具、Composite10和屏幕上下文。
  - 保留三种模式的seed分量、网格规格、TRS系数、等比坐标及原浮点求值顺序。
  - _Requirements: REQ-01, REQ-05; Dependencies: 0_
- [x] 1.2 新建WeatherParticles，保留Rain折射／Sand叠色／Snow单落点三条算法。
  - 默认不增粒子层、不将闭式十次复合解释为十个落点、不改差分边界采样。
  - _Requirements: REQ-05, REQ-11; Dependencies: 1.1_
- [x] 1.3 新建WeatherAtmosphere，拆Lens／Fog／Tone函数，保留每种模式的合成编排。
  - Rain直接从输入场景色做水汽／雾共同tint再tone；不迁移RainyBlur、五点核、blur系数0.8或半径；Sand fog→deposit→tone；Snow edge→fog→tone。
  - Snow保留旧深度模型和Alpha；各自UV不同的噪声采样不错误合并。
  - _Requirements: REQ-05, REQ-06, REQ-12; Dependencies: 1.1_
- [x] 1.4 新建Weather.shader两命名Pass，通过固定输入GPU绘制验证新旧输出。
  - 记录并恢复调试配置；三个旧Shader暂不改为共享库包装，保持独立对照，避免共用同一缺陷而虚假通过。
  - 校验导入消息、真实输出与固定预算；Rain与去blur目标对照，Sand/Snow与原版对照；移除blur之外的差异必须先定位。
  - _Requirements: REQ-01, REQ-05, REQ-09, REQ-11; Dependencies: 1.2, 1.3_

阶段出口：Rain对应去blur目标，Sand/Snow对应原效果；没有滤波函数／参数／Pass。回退点：只移除／回退本阶段新增实现；原效果与GUID仍可用。

## 2. Weather 正式入口与预设

- [x] 2.1 实现WeatherProfile数据与Resolve入口；生成Rain／Sand／Snow兼容Profile。
  - 从实际材质取值：Randomness均1，Wind均0.5，温度强度均1；Rain F=0.5，Sand D=1，Snow F=0.3。
  - 除已确认删除Rain blur外保持旧响应，明确参数所有权和模块数据；不用Shader默认值覆盖当前材质风格。
  - _Requirements: REQ-01, REQ-02, REQ-04; Dependencies: 1_
- [x] 2.2 实现WeatherFeature资源和相机设置、自持材质与延迟参数绑定。
  - 一个Shader与一个Noise2DRG引用；Profile是美术数据唯一来源。
  - 诊断缺资源／无效Pass；不回写原材质或Profile；释放与重载无泄漏。
  - _Requirements: REQ-03, REQ-04, REQ-07, REQ-09; Dependencies: 2.1_
- [x] 2.3 实现RenderGraph双阶段调度与cameraColor输出替换。
  - 独立PassData，明确读写依赖，从source获取实际RT描述。
  - 支持单阶段只绘制一次；雾活动或Fog Debug要求时请求Depth；全关且Debug=Off时不入队。
  - 不照搬ConfigureInput(Color)造成额外opaque复制；BackBuffer／intermediate路径按实际URP处理。
  - _Requirements: REQ-06, REQ-07, REQ-11; Dependencies: 2.2_
- [x] 2.4 以AfterRenderingPostProcessing在目标Renderer接入Weather。
  - 先记录原配置；Weather正式入口与Debug天气输出不可同时开。
  - 验证仅resolveFinalTarget=true的Game Base Camera运行，非末端Stack Base及Overlay／Preview／Reflection过滤，以及SceneView／辅助相机策略；Stack跳过时一次诊断。
  - _Requirements: REQ-06, REQ-09, REQ-10; Dependencies: 2.3_

阶段出口：一个正式入口运行三种迁移预设，Rain采用去blur目标，正常路径不超过两次天气draw。回退点：恢复记录的Renderer配置和旧入口。

## 3. 模块参数解耦与集中端点处理

- [x] 3.1 固定模块字段与完整旧→新映射，补齐统一面板。
  - 分开Particle形状／opacity／Rain折射、Lens强度／宽度、Fog强度／距离／滚动、Tone强度／颜色。
  - Profile／Settings／Shader不暴露blur半径、核或滤波开关。
  - Shape字段定义为SDF Threshold；Rain RefractionScale初值1，保留内部0.015，opacity仍在高度函数Composite10之前参与；Rain任一shape／opacity／refraction为0直接零offset。
  - Snow当前材质迁移：EdgeBaseWidth=.03、EdgeNoiseWidth=.3、EdgeOpacity=.3、FogBaseDistance=50、FogNoiseDistance=100、FogOpacity=.3、ScrollX=.5、CycleRate=1。
  - 按Technical／Artistic／Debug分组；Performance保留原预算，未测量的档位不进入首版。
  - _Requirements: REQ-02, REQ-04, REQ-11, REQ-12; Dependencies: 2_
- [x] 3.2 在Profile校验与Resolve实施同源范围规则及0关闭。
  - 模块关闭先裁掉相关运算；按最终像素霜边宽度<=0返回零mask；FogBaseDistance>0且FogNoiseDistance>=0，在整个合法噪声域内保持正距离。
  - 合法范围覆盖运行脚本赋值，不在每个计算点堆epsilon。
  - _Requirements: REQ-08; Dependencies: 3.1_
- [x] 3.3 重新验证兼容预设，验证各模块独立调节及0/1端点。
  - 单独改变周期不再改变雪雾距离／滚动；Wind旧0行为映射为周期冻结。
  - 保留LegacySnow Alpha策略；端点行为变化与正常值兼容分别记录。
  - _Requirements: REQ-01, REQ-02, REQ-04, REQ-06, REQ-08; Dependencies: 3.2_

阶段出口：模块能独立调节与关闭，端点稳定，兼容Profile正常值无未解释差异。回退点：恢复阶段2参数解析与兼容Profile。

## 4. 实际管线验收

- [ ] 4.1 完成导入、C#／Shader编译和运行日志检查，保存验证记录。
  - 负向检查缺Profile／Shader／噪声、invalid Pass、不可采样BackBuffer：安全跳过且不登记非法依赖；检查缺Depth时只裁掉雾，诊断频率受控。
  - _Requirements: REQ-06, REQ-07; Dependencies: 3_
- [ ] 4.2 同帧同输入对比三天气新旧输出，按预设阈值记录差异与人工画面判断。
  - Rain分开记录原版→去blur目标的有意变化，与目标→Weather的结构差异；核验水汽遮罩／染色、雾和调色仍独立可调。
  - 不以Shader消息空白、没有观测到错误或不一致帧截图单独宣布通过。
  - _Requirements: REQ-01, REQ-05, REQ-06, REQ-12; Dependencies: 4.1_
- [ ] 4.3 验证16种模块开关组合及单参数端点。
  - 全关／单模块／全开；Randomness0/1、CycleRate0/1/2、模块强度0/1，无NaN／残留关键词。
  - Rain→Sand→Snow→Rain切换Profile，参数／纹理／local keyword无残留；补零基础宽度但噪声宽度非零的零噪声输入控制用例。
  - Debug强制诊断绘制单独计成本；全关闭零增量以Debug=Off为条件。
  - _Requirements: REQ-02, REQ-07, REQ-08, REQ-09; Dependencies: 4.1_
- [ ] 4.4 验证相机、天空、透明前景、长宽比、动态分辨率与多相机。
  - 雨沙透视／正交；Snow旧正交限制显式保留；多独立Base相机正确；不支持的Stack入队前跳过并诊断，辅助相机可排除。
  - XR若为目标平台，则在声明／采样静态核验之后补目标设备运行，不凭非XR宏等价宣布支持。
  - _Requirements: REQ-06, REQ-09; Dependencies: 4.1_
- [ ] 4.5 测量天气增量GPU与RT预算、模块关闭节省情况和重载生命周期。
  - 0/1/2天气draw对照，记录管线已有depth/opaque成本；不将URP final blit计入天气固有成本。
  - 核查Rain氛围没有五点blur采样与滤波Pass；源色采样为直接合成，GPU收益按实测记录。
  - 覆盖禁用／销毁／domain reload／Shader更换，不改资产、无材质泄漏。
  - _Requirements: REQ-07, REQ-11; Dependencies: 4.1_

阶段出口：编译／运行／结构／兼容输出／性能证据分别齐全；仅静态通过不能关闭此阶段。回退点：阶段3检查点与旧运行入口。

## 5. 文档与确认后归档

- [ ] 5.1 写Weather正式README，记录参数、预设值、模块顺序、Alpha／深度限制及证据。
  - 同步Sandy旧README的实际值与coverage未生效事实；不借文档修正顺便改变Shader。
  - _Requirements: REQ-01, REQ-04, REQ-06, REQ-10; Dependencies: 4_
- [ ] 5.2 单列旧目录、Shader／Material／README／meta的精确保留／移动／删除清单。
  - 再核GUID、AssetDatabase与HLSL文本引用；共享Noise2DRG及其外部引用不受清理影响。
  - 取得人工确认后，才通过合法门禁执行移动／删除；本轮规划没有这类执行授权。
  - _Requirements: REQ-03, REQ-10; Dependencies: 5.1_
- [ ] 5.3 在用户确认的正式状态下收尾，记录项目memory与最终回退点。
  - 归档只记录实际完成及验证内容；可选视觉改进保留为后续任务。
  - _Requirements: REQ-10; Dependencies: 5.2_

阶段出口：一个权威Weather入口与说明，旧资源状态明确，可按提交回退。

## 首版之后另行决策

- Sandy coverage接线及中央／边缘强度设计。
- Snow统一高度雾、正确正交解算与默认保留输入Alpha。
- Rain折射等比坡度→UV的长宽比修正。
- 噪声线性色彩空间／生成配方可追溯及保GUID重烘焙。
- Low／High性能档、天气过渡／混合、Camera Stack跨图输出、Volume、世界空间天气及场景材质联动。

这些不阻塞首版兼容整合；一旦加入，需要独立行为／性能基线，不能回填为此前已验证。
