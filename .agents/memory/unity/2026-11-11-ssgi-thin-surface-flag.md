# SSGI 命中面朝向判定按「薄壳标记」豁免（2026-11-11）

Diffuse 的背面剔除 `dot(hitNormal, -rayWS) <= 0` 与 Specular 的 `facing = saturate(dot(hitNormal, -rayWS))`，前提是 `_CameraNormalsTexture` 存的是**几何朝向**。这条前提只在**不透明闭合物**上成立。

## 问题

`GrassWind.shader` 的 DepthNormals Pass 写入的是贴合地形的**着色法线**（`grassProp.normal`，接近 ±up），竖直叶片也报告自己是地面。于是整片草地被背面剔除判成假命中，地面 DiffuseGI 全黑。

试过从深度缓冲反推「这个像素是不是薄壳」，**没有分辨力**：草冠的深度场本身就是一张准地形高度场，重建出的几何法线同样接近 ±up。实测草 70.0 % 判闭合、地表 69.9 % 判闭合，等于抛硬币。

## 做法

由**生产者自己声明**，而不是让消费者猜：

- `_CameraNormalsTexture.a` 写 `-1`（`SSGI_THIN_SURFACE_FLAG`）表示薄壳/双面；生产者在 `GrassWind.shader` 的 `FragNormals`，两种法线打包分支（OCT / 非 OCT）都要带。
- URP 现状下其余不透明几何该通道是 `+1`（SNORM 127）、天空是 `0`，两者都按闭合面处理。**不能用 `1.0` 当标记**——那正是默认值，探针会读出「草地 alpha=127、地表也是 127」。
- `SST_HitSurfaceIsClosed(uv)` 为假时：Diffuse 退回纯深度判据（只保留 `depth < normalBias` 的拒绝）；Specular 令 `facing = 1`，只保留屏幕边缘衰减。

闭合面走的分支与改动前逐字相同，所以非薄壳几何零影响。

## 踩到的大坑：跨 shader 的隐式依赖

把 `SST_IsThinSurface` 放进 `ScreenSpaceTrace.hlsl` 后，`SSGITemporal.shader` 也跟着编译失败：

```
Error (Metal): undeclared identifier '_CameraNormalsTexture_TexelSize'
    at ScreenSpaceTrace.hlsl:73
```

它只 include 了 `DeclareDepthTexture.hlsl`。时间滤波着色器是 error shader 之后，整条 diffuse 链的输出不再是当前代码算出来的——**现场表现是「改 DiffuseGI 毫无反应」**：改 Frag_Trace 洋红、改 Frag_Resolve 绿、改 Shader 名、`SetActive(false/true)` 全部无效，而同一时刻改 `SSGIComposite.shader` 却立刻生效。因为只有下游那段断了。

由此得到的排查顺序，别再从头绕：

1. **先跑 `ShaderUtil.GetShaderMessages(shader)`**（反射调用，`GetShaderMessageCount` 只给数量）。`Dim` 点：`isSupported == true` 不代表编译通过，error shader 照样 `supported=True`；控制台也未必报错。
2. 再确认材质指的是不是那个资产（`_material.shader` + `AssetDatabase.GetAssetPath`）。
3. `Graphics.Blit(rt, material, passIndex)` 常数探针可以确认某个 pass 的程序是不是新的（本项目验证过 pass 0/2 的常数色确实编译进去了）。
4. **最后**才怀疑着色器缓存。

次级教训：**include 一个共享头就等于继承它的纹理依赖**。`ScreenSpaceTrace.hlsl` 原本就要 `DeclareDepthTexture.hlsl`，加进 `_CameraNormalsTexture` 后所有 includer 都得补 `DeclareNormalsTexture.hlsl`；已在头文件顶部写了依赖说明。

## 另一个反复骗到我的读数

`Shader.GetGlobalTexture("_SSGIDiffuseTexture")` 在帧后读回是**假的**：URP 每帧结束会还原全局纹理，`endCameraRendering` 回调里读也一样。读到的是上一纪元的 RT（内容全黑），和合并 pass 真正采样的那张不是一回事。要判断链路，只能靠 `renderGraph.GetTextureDesc(handle)` 的尺寸/名字，或者把常数塞进各个 pass 看最终画面对不对。

## 验证

同机位 A/B（640×360，`GrassManagerDemo`，Debug 视图读回相机目标）：

| 视图 | 薄壳豁免 | 恒闭合（改动前） |
|---|---|---|
| Debug DiffuseGI | nearBlack 1.8 %，116.8 / 100.3 / 105.9 | nearBlack 49.3 %，101.2 / 84.7 / 101.2 |
| Debug SpecularGI | 171.8 / 148.8 / 171.6 | 176.9 / 147.2 / 176.9 |
| poseB-specular（俯视 roughness 0.05） | 127.6 / 103.1 / 124.4 | 131.8 / 96.5 / 131.8 |
| poseB-color | 133.5 / 133.5 / 64.6 | 128.0 / 128.0 / 63.1 |

DiffuseGI 分量图从「地面整片黑」变成「地面收到草的反照绿」；最终色只亮约 4 %（乘强度与接收反照率后）。证据图在 `Assets/Mine/Scripts/GrassManager/Validation~/2026-11-11-ssgi-diag/ab/`，A/B 脚本 `tmp/after-ssgi-closure.cs`。

未跑 `Validation~/run.sh`（要新建 `RuntimeFixture`，需要用户追加沙箱授权），未做长时间运动镜头复核。

相关：[草与地形写入深度 / 法线](2026-11-11-grass-terrain-depth-normals.md)（本轮的前置）、[草色改采地形颜色图](2026-11-11-grass-terrain-colormap.md)、[Unity 6 全屏后处理 Shader 差异](../../../.claude/projects/-Users-xiaokangji-Unity-Lab/memory/unity6-postprocessing-shader-differences.md)。
