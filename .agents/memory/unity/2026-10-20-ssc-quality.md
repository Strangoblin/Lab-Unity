# SSC 颜色与降采样边缘修复（2026-10-20）

## Git 与授权

用户要求先提交当前工作，再拉取远端，随后解决 SSC 颜色不鲜艳和降采样锯齿，允许自行配置观察环境。
已将原场景/Renderer/HDR 资产提交为 `d1d701d`；拉取 `origin/unity6` 至 `ed3426c`，处理 MEMORY/PC_Renderer 冲突并提交合并 `5900bb7`。
保留本地 ScreenWarp 和远端 SSC 的 Feature 引用与 map。下述修复、验证源码和 memory 尚未提交，未 push。

## 已实现

- `SSC.shader`：双色插值的权重改为 `stepShadow³`，让浓密区更多使用 BaseColorB；直射衰减、HG、密度/射线预算保持。此为有意的美术响应变化，并非旧画面等价。
- 固定 4×4 B-spline 深度引导重建，使用 Core Filtering.hlsl 的权重；借鉴 SpecularGIFilter 的 texel-center/深度权重，但云保留天空且不需要法线。RGB/A 同权归一，保持预乘语义。downsample=0 直接读取云像素。
- 全分辨率云盒/场景深度检查阻止前景漏云；邻域不相容且中心在云内，或无可用样本时，调用同一 EvaluateCloud 中心积分回退。不新增 Pass/RT/历史。
- `SSCFeature.cs`：云缓冲明确 RGBA16F，避免继承无 alpha 的相机 HDR 格式；传入实际云纹理尺寸/降采样档，两个 Pass 显式声明深度依赖。
- 明确 URP/Cull/ZWrite/ZTest；非 reversed-Z 重建深度映射补齐，Metal 主路径保持。为避免 Metal 编译器初始化警告，重建结果显式初始化、单 return、权重使用 float4。

## 已验证

Unity 6000.3.14f1 / Metal、PC_RPAsset、SampleScene。未进入 Play；原场景、主相机和 Renderer 持久设置未改。
固定临时 Game/Base 相机 (0,25,-90) 看向 (0,50,0)，960×540，Renderer 0，无 Volume 后处理，speed/jitter=0；其余使用已拉取的 SSC 设置（128 步进、蓝白双色、phaseG=0、云高50/厚10）。

- `Validation~/QualityCheck.cs` 真实 URP 请求 20 张图：线性控制组/重建 × 有无前景 × downsample 0..4。控制组仅替换重建函数，积分/颜色/格式/中心遮挡一致；全分辨率控制组与重建逐像素相等。0/2/3/4 的初轮及最终全档图均打开核对全画幅/方向。
- 两个 Pass × 64/128/256 六变体同步编译，ShaderUtil **0 条消息**；两份正式源码 check_norm 与 git diff --check 通过。
- `Validation~/HDRCheck.cs` 使用受控 solid 背景/cullingMask=0，RGB111110Float/ARGBHalf 两种目标：无云相对关闭 Feature 的背景 max/RMS=0；有云 RMS≈0.05761/0.05728，正控制有效，无非有限 red 值。场景几何版关闭 Feature 的对照会有路径/动画差异，不能据此判断空云失败。
- 固定旧云内部区域 121,263 像素（旧全分辨率 RGB 阈值后腐蚀3px），蓝差 B-(R+G)/2 从0.02411升至0.10998，HSV饱和度0.03811→0.14199；这是本相机的显示 PNG 统计，不是线性 HDR/全场景结论。
- 固定新色彩的线性/重建控制组，天空 ROI y80:350 的水平方向二阶差均值在 1/4、1/8 分辨率下降约32%/29%，支持轮廓平滑，不代表恢复细节/整体误差降低。与各自全分辨率的 MAE 会受颜色/夹白影响，未用来声称质量全面提升。

前景 fixture 最初位于 z=-35，杆顶进入云盒而合法出现前置云，误看作亮孔；最终改为 z=-60 并按投影同比缩放，完全在云盒前，实心遮挡正确。原 fixture 图不作漏云证据。

## 证据和限制

以下为固定条件 PNG，所有者 SSC 质量诊断；依赖当日场景/光照/噪声，后续视觉基线替代时可人工确认后清理：

- [修复前 1/4](evidence/2026-10-20-ssc-quality/before-sky-quarter.png)
- [修复后 1/4](evidence/2026-10-20-ssc-quality/after-sky-quarter.png)
- [固定新颜色的线性控制组](evidence/2026-10-20-ssc-quality/linear-same-color-quarter.png)
- [前景与 1/8 云](evidence/2026-10-20-ssc-quality/foreground-eighth.png)

可复跑源码与条件见 `Assets/Mine/Shaders/PostProcess/SSC/Validation~/README.md`，运行输出回 tmp/sessions。
1/8、尤其1/16仍会损失细节并变软，硬密度阈值和 mip0 噪声保留；不是时域抗锯齿。RGBA16F/16tap/边缘中心积分有成本，未做GPU计时；运动、XR、动态分辨率和其它平台未验证。
OpenCode 指定 deepseek-v4.1-flash 做只读审查时越过文件白名单读取 URP 源码，已中止（130），未发现写入；后续诊断/修改/验收由主会话和原生子代理完成。
