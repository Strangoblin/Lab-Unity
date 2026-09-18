# HyperSpace

`Render/HyperSpace` 是 Benoit Marini（2020）Shadertoy 片段的最小 Unity 材质移植。

## 源代码对应关系

| Shadertoy | Unity |
|---|---|
| `iTime` | `_Time.y` |
| `iResolution` / `fragCoord` | `GetNormalizedScreenSpaceUV()` + `_ScreenParams.xy` |
| `NUM_LAYERS 16.` | `HYPERSPACE_NUM_LAYERS 16.0` |
| `ITER 23` | `HYPERSPACE_ITER 23` |
| `tex(vec3 p)` | `HyperSpaceTex(float3 p, float time)` |
| `fragColor = vec4(col, 0.0)` | `return half4(col, 0.0)` |

移植只做语法和坐标 API 替换，不加入 4D SDF、Ray March、光照、背景、网格或额外材质参数。

## 核心公式

```hlsl
float time = _Time.y * 0.3;
float d = frac(i + time);
float s = lerp(5.0, 0.5, d);
float f = d * smoothstep(1.0, 0.9, d);
col += HyperSpaceTex(float3(uv * s, i * 4.0), time).xyz * f;
```

注意原文使用 `i <= 1.0`，所以 16 层步长实际执行 17 次采样，最后除以 `NUM_LAYERS`（16），这里保持原行为。

## 使用

给任意朝向相机的平面或全屏网格赋予 `HyperSpace` 材质即可。Shader 使用屏幕坐标，因此网格只需覆盖目标画面区域；材质没有可调参数，时间和常量均保持源片段默认值。
