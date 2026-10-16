# Plant：Quadmesh-to-Billboards 移植方案

## 来源与边界

- 用户视频：https://www.youtube.com/watch?v=iASMFba7GeI
- 视频说明中的作者 URP 节点图：https://i.imgur.com/PFJbHsE.png
- 作者说明：https://www.reddit.com/r/Unity3D/comments/jrqeew/
- 当前 Plant.shader 为空；保留已有 meta/GUID，声明 Render/Plant。
- 原方法是 UV 驱动的相机平面偏移叠加原顶点，不是逐面中心重建，也不依赖几何着色器。

## 交付

1. Assets/Mine/Shaders/Render/Plant/Plant.shader：URP HLSL 实现；UV0 从 0–1 映射到 -1–1，转换为相机 right/up 方向并归一化，乘 Billboard Size 后叠加世界空间原顶点；Inflate 沿原网格法线膨胀。转换回裁剪空间，避免混用对象/世界空间。
2. 同目录 PlantQuadSphere.obj：细分立方体投影成球，默认半径 1、每侧 8×8，共 384 个 quad；逐面独立顶点与硬法线，每个 quad UV0 四角为 (0,0)、(1,0)、(1,1)、(0,1)，OBJ 保留四边面，由 Unity 导入三角化。
3. 同目录示例材质与 prefab：绑定球体和 Plant，便于直接拖入场景；默认提供无需外部纹理的简单叶片遮罩，支持替换为带 alpha 的叶片纹理。
4. 同目录 README：记录原理、网格要求、尺寸/膨胀参数、模型包围盒注意事项、原始来源和验证结果。

## Shader 范围

- 保留原网格法线用于树冠体积光照；基础主光和环境光、颜色、alpha cutoff。
- AlphaTest、ZWrite On、双面；Forward/DepthOnly/DepthNormals 使用一致顶点变形与遮罩。
- 明确检查阴影 Pass 的观察方向，避免直接在阴影视图使用灯光矩阵代替主相机方向；投影策略在实现时以本项目 URP 接口验证。
- 本轮只做面片 billboard 和配套网格，不扩展风场或额外动画系统。

## 验收

- 网格：384 个四边面、1536 个独立顶点，每面完整 UV，无极点退化三角形；所有顶点半径 1，绕序和硬法线一致。
- Shader：规范门禁通过，Unity 导入与 Shader 编译无新增错误；绕物体观察以及平移、旋转、缩放下无空间漂移。
- 材质/prefab：可直接使用，扩大渲染包围盒覆盖 Billboard Size/Inflate 默认位移，避免屏幕边缘错误剔除。
- Editor 不可用时如实报告未执行的验证，不以静态检查代替视觉验收。

## 流程状态

- 已完成原始来源与现有目标检查。
- 根据 auto-manager Experiment 模式 E1，方案确认后进入业务文件写入与验证。
