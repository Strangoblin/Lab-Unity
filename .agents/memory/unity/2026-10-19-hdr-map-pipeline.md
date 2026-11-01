# HDR Map Baker / Converter — implementation and final verification

Started 2026-10-16; final verification 2026-10-19. User approved splitting InteriorMapBaker into two tools, the precise GUID-preserving legacy moves, and workspace extensions for the shared library and Render shaders. This is one continued task.

## Delivered scope

- `Assets/Mine/Scripts/HDRMapBaker/`: capture framework, LayerMask, Include Sky, and one fixed 2:1 linear RGBAHalf panorama output. `Nothing + Include Sky` captures only the sky; the range sets radial far clipping, not a spatial stencil.
- `Assets/Mine/Scripts/HDRMapConverter/`: independent source image, explicit source/target projection, target resolution, Panorama/Interior coverage. Four 2D encodings: Equirectangular (0), Octahedral (1), EqualAreaOctahedral (2), Hemisphere disk (3). Interior is the capture frame's +Z hemisphere, with missing directions transparent. Results are linear RGBAHalf with mip 0.
- `Assets/Mine/Special/HLSL/HDRMapFunction.hlsl`: direction mapping and parameterized sampling. +Y up / +Z forward; full panorama latitude is angular asin(y), not linear y. Standard octahedral wraps Core Packing with an axis swap; equal-area has matching exact trig encode/decode.
- `Skybox/HDRMap` and the existing InteriorMapping shader call the same sampling library. Room geometry stays private to InteriorMapping; its hit direction is adapted from room -Z to map +Z. UV alone does not produce room parallax: camera position and room intersection remain necessary.
- Existing scene components and GUIDs remain under `HDRMapBaker/Legacy/` as thin adapters. The old directory was removed only after the user's exact move-list confirmation. Compatibility Bake(framework) retains the default face resolution 256.
- Material Map Projection `Legacy = 4` preserves old textures; the ShaderLab Enum attribute rejected the initially proposed negative ID. Negative values still fall through the compatibility branch. Newly baked angular-latitude maps select Equirectangular (0), including new results returned by the compatibility Box API.

FGD LUTs, BRDF changes, GGX prefiltering and reflection-map production are explicitly excluded by the user. Existing scenes, RendererData, Lighting assets and sample materials were not saved or rewired.

## Capture and resource ownership

Six SingleCameraRequests use a reusable 2D HDR face target with depth. A GPU face-copy pass corrects vertical orientation before writing the cube. Cube-to-panorama projection stays on the GPU, with one final ReadPixels. The initial direct Cube RenderRequest produced a face-V inversion: centre-axis colours passed, but continuous direction RGB exposed side-face Y and polar-face Z inversion. Merely negating camera Up would also invert U and is not the fix.

Camera/AdditionalCameraData, transform, render-target and sRGB state are restored in finally. Unsaved Baker-to-Converter handoff copies an independently owned source snapshot; saved sources are referenced without ownership. Window Save/Load helpers retain the complete standalone production-tool template, including GUID-preserving CopySerialized and target-only SaveAssetIfDirty.

## Verified evidence

Unity 6000.3.14f1 / URP 17.3.0, Metal / Apple M2. Final scripts were run from the authoritative `Assets/Mine/Scripts/HDRMapConverter/Validation~/` directory:

- Conversion: **38/38**, 122,998 independent direct-oracle pixels, 125,772 round-trip pixels, 29,026 cube pixels, wrong-encoding GPU control.
- Capture: **15/15**, six captures, 18 axis-colour assertions, layer/sky/empty/rotation/state checks and wrong-axis control.
- Integration: **12/12**, six sky captures and 5,112 sky-oracle samples; ten room renders, six encoding comparisons and two physical-camera-motion controls. Maximum sky error below .002 at the tested settings; the room encodings agree within the defined .02 threshold.
- Editor lifecycle: **15/15**, source snapshots and ownership, path/type guards, idempotent repair, cross-framework rejection, old script GUIDs/private fields/default argument.
- Continuous geometry-direction diagnostic: all six faces valid, max channel error **.012408** at 65-pixel capture height and the ten tested off-axis directions; scene/render state unchanged.
- Mathematical round trips/fold topology/centres/poles and equal-area Jacobian checks pass. 22 implementation files pass project norm checks; documentation relative links and git diff whitespace checks pass.

Reports: `tmp/sessions/2026-10-16-hdr-maps-main/output/final-2026-10-19/`. Entry and limits: `Assets/Mine/Scripts/HDRMapConverter/Validation~/README.md`. These are numerical/functional checks on Metal, not a cross-platform GPU speed claim or artistic approval. Overwrite dialogs are retained from the template and were not clicked automatically.

## Diagnostics useful for future work

- `Camera.scene` is a rendering-scene override. Ownership uses `camera.gameObject.scene`; the last Editor fixture failure was an incorrect Camera.scene assertion, not a framework defect.
- Capture test layers must also be allowed by the URP Renderer. Current PC Renderer mask 119 excludes layer 31; the isolated fixture uses included layers 0/1 so the camera mask remains the tested filter. Do not silently overwrite RendererData to make the fixture pass.
- ShaderLab Color fixture values undergo colour-space conversion; tests set `linearColor.gamma` to produce the intended linear shader output, preserving HDR values above 1.
- A fresh Editor launched from the execution environment initially failed Bee with `String conversion error: Illegal byte sequence` because one inherited environment value was not valid UTF-8. The task-owned Editor was restarted with only invalid environment entries omitted, without printing values or changing project/global configuration; connection and all final checks then passed.

## Sources and history

Core pipeline is already present in commit `ef9ce9c`; current branch included it at task resumption (`75fb0f2`). The 2026-10-19 closeout adds the reproducible validators, fixes documentation and restores the legacy default argument, with only this task's exact files committed. Initial pre-existing window/document bytes and their hashes remain as comparison fixtures under this task directory; no stash or broad cleanup was used. Window boilerplate was actually delegated once through OpenCode CLI (`opencode-go/deepseek-v4.1-flash`) in isolated staging and reviewed before gated integration.
