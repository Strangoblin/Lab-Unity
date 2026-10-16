# HDR Map Converter

Open `Tools/HDR Map Converter...`. Select a source Texture2D, its explicit projection, the output projection, coverage and resolution. The Converter works independently of a Baker or scene framework. Source assets can be unreadable; GPU conversion does not change their pixels, importer or sampler settings.

| Projection / shader ID | Output dimensions for resolution N | Coverage |
|---|---|---|
| Equirectangular / 0 | 2N × N | Full sphere |
| Octahedral / 1 | N × N | Full sphere, +Y central pole |
| EqualAreaOctahedral / 2 | N × N | Full sphere, equal solid angle per UV area |
| Hemisphere / 3 | N × N | Orthographic +Z hemisphere disk; outside disk is transparent |

Coverage is independent of projection: `Panorama = 0` retains all available directions; `Interior = 1` retains the forward +Z hemisphere and writes alpha 0 elsewhere. A hemisphere source cannot reconstruct its missing rear half. Projection conversion is resampling and can lose detail.

Resolution is 16–4096 in the service API, subject to device texture-size limits; the UI offers standard sizes. All results are **linear RGBAHalf, mip 0**, with Alpha reserved for source content and coverage. No reflection prefiltering or BRDF processing is performed. Equirectangular U repeats, V clamps; octahedral filtering reconnects folded edges explicitly.

Convert creates a temporary output; Load/Save share `Asset Path` (default `Assets/Textures/HDRSamplingMap.asset`). Saving preserves an existing Texture2D GUID, confirms overwrites, rejects different asset types and saves only the target asset. Unsaved output is released on replacement/close. Baker handoff sources are independently owned snapshots and released separately from output.

## Unified sampling

The shared library is `Assets/Mine/Special/HLSL/HDRMapFunction.hlsl`. Callers first include URP `Core.hlsl`; the current implementation requires shader target 4.5. It has no global texture or material CBUFFER.

```hlsl
float4 c = HDRMapSample(TEXTURE2D_ARGS(_Map, sampler_Map),
    direction, projection, coverage);
```

`HDRMapSampleLOD` takes explicit ordinary texture LOD; `HDRMapSampleGrad` takes the unnormalized direction's derivatives. The output images currently contain only mip 0. Projection IDs and coverage IDs match `HDRMapProjection` / `HDRMapCoverage`.

Sky sampling passes the outward view direction. A mesh can pass any direction expressed in the map frame. **Planar interior mapping also needs a view-dependent ray and room intersection:** UV alone cannot create parallax. `InteriorMappingFunction.hlsl` retains box/hemisphere intersections; its hit direction adapts from room -Z to capture +Z and calls the same HDRMapSample function. The material's Room Shape remains independent of Map Projection / Map Coverage.

In `Render/InteriorMapping`, choose Map Projection matching the converter result. `Legacy = -1` preserves existing material sampling; new assets should use their actual projection ID. Old Hemisphere disk assets remain compatible with the legacy path. Old full-sphere latitude behaviour is retained only in Legacy; new map sampling uses the corrected angular latitude contract. `_InteriorMap_ST` belongs to Legacy sampling; for HDR maps transform the plane's UV before ray construction, not the encoded map UV.

The `Skybox/HDRMap` shader samples the same image projections with Exposure and Y Rotation. Assign its material through Lighting / Environment / Skybox Material. A camera-local Skybox component can override the scene skybox.

## Implementation and validation

`HDRMapConverter.cs` performs GPU projection and one final readback. `HDRMapConvert.shader` and the runtime samplers use identical direction mappings. Standard octahedral mapping reuses Core Packing with an axis swap; equal-area uses the article's exact trigonometric mapping plus its inverse. Standard octahedral and equal-area octahedral are different encodings, not interchangeable labels.

See [Validation~/README.md](Validation~/README.md) for mathematical, Metal conversion, capture and integration checks. Format correctness and numeric checks do not substitute for artistic review in the target scene.
