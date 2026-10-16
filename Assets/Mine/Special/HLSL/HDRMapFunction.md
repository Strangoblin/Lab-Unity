# HDRMapFunction

Shared direction-to-image mapping and radiance sampling. Include URP `Core.hlsl` before `Assets/Mine/Special/HLSL/HDRMapFunction.hlsl`; use shader target 4.5. The library declares no textures or CBUFFER and includes no sibling library. It uses Core Packing for standard octahedral encode/decode.

## Contract

Coordinates: +X right, +Y up, +Z forward, UV origin bottom-left. Equirectangular forward is UV (.5,.5); longitude is `atan2(x,z)`, vertical coordinate is angular latitude `asin(y)`. Standard octahedral and equal-area octahedral use +Y as their central pole. Zero input direction returns transparent; nonzero directions are normalized internally.

| Parameter | Values |
|---|---|
| projection | 0 Equirectangular, 1 Octahedral, 2 EqualAreaOctahedral, 3 Hemisphere |
| coverage | 0 Panorama, 1 Interior (+Z hemisphere) |

Hemisphere is a +Z orthographic disk and cannot represent the back hemisphere. Image RGB is linear colour/radiance; Alpha remains source content and validity, not metadata. This library does not compute lighting, BRDFs or reflection filtering.

```hlsl
float4 color = HDRMapSample(TEXTURE2D_ARGS(_Map, sampler_Map), direction, projection, coverage);
float4 explicitMip = HDRMapSampleLOD(TEXTURE2D_ARGS(_Map, sampler_Map), direction, projection, coverage, lod);
float4 gradient = HDRMapSampleGrad(TEXTURE2D_ARGS(_Map, sampler_Map), direction,
    ddx(direction), ddy(direction), projection, coverage);
```

`HDRMapSample` reads mip 0. LOD is an ordinary image mip, clamped to the actual texture mip count. Grad takes derivatives of the unnormalized direction and accounts for projection seams. Converter outputs currently contain only mip 0. Bilinear filtering reconnects octahedral folded edges; equirectangular U wraps and V clamps independently of source sampler settings.

`HDRMapDirectionToUV` and `HDRMapUVToDirection` expose the pure mapping pair with an `out valid` mask. Equal-area uses the exact trigonometric formula and a matching inverse; no approximation or bitwise sign optimisation is assumed faster on Metal.

## Consumers

- `Assets/Mine/Scripts/HDRMapConverter/HDRMapConvert.shader`: target UV → direction → source sampling, yielding a linear HDR sampling image.
- `Assets/Mine/Shaders/Render/HDRMap/HDRMapSkybox.shader`: outward sky direction, optional Y rotation and exposure.
- `Assets/Mine/Shaders/Render/InteriorMapping/InteriorMappingFunction.hlsl`: room intersection stays effect-private; `(hit.x, hit.y, -hit.z)` adapts its -Z room frame to the shared +Z map frame. Material Map Projection = -1 preserves the previous sampling path.

The same sampler serves panorama and interior coverage. A plane needs UV, camera position and room geometry to construct a parallax direction before calling it. UV mapping alone cannot reproduce view-dependent room parallax.

Usage, tools and reproducible tests: [HDR Map Converter](../../Scripts/HDRMapConverter/HDRMapConverter.md), [validation](../../Scripts/HDRMapConverter/Validation~/README.md).
