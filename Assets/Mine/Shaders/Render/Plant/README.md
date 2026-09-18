# Plant — Quadmesh-to-Billboards

URP 17 / Unity 6，Shader 菜单名称为 `Render/Plant`。

## 使用

1. 将 `PlantQuadSphere.prefab` 拖入场景，已绑定球体网格和 `Plant.mat`。
2. 默认用程序化叶形遮罩，无需外部贴图。
3. 使用自备叶片 RGBA 贴图时，将它赋给 Leaf Texture，并关闭 Procedural Leaf Mask。
4. 调整 Billboard Offset Radius 控制面片扩张；Normal Inflation 控制沿球面法线膨胀。

## 文件

| 文件 | 用途 |
|---|---|
| Plant.shader | Forward、DepthOnly、DepthNormals、ShadowCaster |
| Plant.mat | 默认绿色叶片材质，启用 GPU instancing |
| PlantQuadSphere.obj | 可供 DCC 编辑的全 quad 球体源模型 |
| PlantQuadSphere.asset | prefab 使用的 Unity Mesh，包围盒已扩展 |
| PlantQuadSphere.prefab | 可直接使用的球体实例 |

## 网格约定

- 半径 1；立方体每侧 8×8 细分后投影成球，共 384 个 quad，无退化极点。
- 1536 个独立顶点；各面 UV0 为 (0,0)、(1,0)、(1,1)、(0,1)，硬法线。
- OBJ 保留四边面，Unity 导入后为 768 个三角形；不得焊接逐面 UV 接缝。
- prefab 的 Mesh bounds 为中心 0、半尺寸 2.01；覆盖两个位移参数各自 0–0.5 的范围。
- 直接使用 OBJ 时，需另行扩展 Renderer/Mesh bounds，否则 GPU 位移可能在屏幕边缘被提前剔除。
- OBJ 和 Unity Mesh 是源模型与派生资产关系；修改 OBJ 后需重新生成 Mesh，二者不会自动同步。

## 实现与参数

UV0 重映射至 -1–1，通过视图矩阵旋转得到世界空间相机平面偏移，归一化后叠加到原顶点。
与原教程一致，这不是以面中心完全重建四边形：原 quad 的几何形状仍影响结果。
原始网格法线用于树冠体积光照；不会将整个球体绕对象原点旋转。

| 参数 | 含义 |
|---|---|
| Billboard Offset Radius | 每个角点的额外偏移半径，默认 0.16；0 恢复原网格形状 |
| Normal Inflation | 沿原硬法线的偏移，默认 0 |
| Alpha Cutoff | 叶片遮罩裁剪阈值，默认 0.5 |
| Diffuse Wrap | 主光漫反射包裹程度 |
| Ambient Strength | 球谐环境光强度 |

- 使用世界空间计算避免对象平移漂移；位移随对象最小轴缩放，非均匀缩放时保持新增偏移等比。
- 建议使用均匀缩放；不支持含剪切的层级变换。
- 深度、法线和颜色 Pass 共用 billboard 变形与 alpha 裁剪。
- 投影使用原球壳加 Inflate 的近似形状与同一遮罩，不随相机转动；并非可见 billboard 轮廓的精确投影。
- 基础主光、主光阴影、环境光、雾；本轮未加入风场、附加光或烘焙 GI。
- 默认叶形仅用于验证；网格规则排列仍可见，最终树冠观感取决于叶簇贴图和网格形状。

## 来源

- [Pontus Karlsson 原教程](https://www.youtube.com/watch?v=iASMFba7GeI)
- [作者在视频说明中提供的 URP 节点图](https://i.imgur.com/PFJbHsE.png)
- [作者的技术说明与矩阵乘法讨论](https://www.reddit.com/r/Unity3D/comments/jrqeew/)

## 验证

Unity 导入确认 1536 顶点、768 三角形、384 组逐面硬法线及完整 UV。
隔离预览场景中实际 URP 验证两个观察方向、共同平移、旋转/非均匀缩放、正交投影。
四个 Pass 及八面体法线、点光阴影变体同步编译无错误；共同平移前后平均 RGB 差约 0.000008（0–255）。
Shader 规范检查通过；未修改用户当前场景。
