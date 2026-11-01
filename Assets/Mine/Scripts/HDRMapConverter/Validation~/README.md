# HDR Map validation

Run against this project with a connected Unity Editor and the normal graphics device. Output belongs to a task directory, not to Assets:

```sh
unityctl script execute Assets/Mine/Scripts/HDRMapConverter/Validation~/HDRMapChecks.cs tmp/sessions/<date-task-id>/output/converter
unityctl script execute Assets/Mine/Scripts/HDRMapConverter/Validation~/HDRMapCaptureChecks.cs tmp/sessions/<date-task-id>/output/capture
unityctl script execute Assets/Mine/Scripts/HDRMapConverter/Validation~/HDRMapCaptureGradientChecks.cs tmp/sessions/<date-task-id>/output/gradient
unityctl script execute Assets/Mine/Scripts/HDRMapConverter/Validation~/HDRMapIntegrationChecks.cs tmp/sessions/<date-task-id>/output/integration
unityctl script execute Assets/Mine/Scripts/HDRMapConverter/Validation~/HDRMapEditorChecks.cs tmp/sessions/<date-task-id>/output/editor
python3 Assets/Mine/Scripts/HDRMapConverter/Validation~/verify_mapping_math.py
```

All C# scripts expose `Script.Main(string[] args)`. The argument is the output directory. Checks return JSON and throw on failure after saving their report. The gradient check is diagnostic: require an empty `error`, `matchesIndependentDirections=true`, `originalSceneUnchanged=true`, `renderStateRestored=true`, and no `unityErrors`. The other suites require `failed=0` plus their required-work counters; an empty or partial run cannot pass. `Validation~` is excluded from normal Unity compilation.

| Script | Evidence |
|---|---|
| HDRMapChecks | Four projections, 65/128 dimensions, independent direction-colour oracle over all output pixels, Interior coverage, round trips, unreadable linear HDR/Alpha input, cube input, rejected arguments, unchanged source, shader messages, wrong-encoding GPU control |
| HDRMapCaptureChecks | Six real HDR-coloured cubes, layer filter, rotation applied once, Nothing/no-sky transparent output, Nothing/sky output, mixed sky/objects, rejected arguments, camera/URP state restoration, wrong-axis control |
| HDRMapCaptureGradientChecks | A real mesh emitting continuous world-direction RGB, including ten non-axis probes across all cube faces; detects face V inversion that axis-centre colours cannot reveal |
| HDRMapIntegrationChecks | Four encodings through the actual sky shader, exposure/rotation, Box and Hemisphere room geometry through the actual InteriorMapping shader, six image comparisons, and physical camera-motion controls with the same window UV coverage |
| HDRMapEditorChecks | Independent temporary-source snapshots, replacement/close ownership, nonowned object survival, path/type guards, repeated framework repair, cross-framework reference rejection, legacy private fields/GUIDs and default Bake argument |
| verify_mapping_math.py | Independent direction round trips, folds/poles/centre, bilinear edge topology and equal-area finite-difference Jacobian |

Capture fixtures use an isolated preview scene with explicit scene-culling override. Both selected layer 0 and excluded layer 1 must be allowed by the current URP Renderer's opaque filter so the camera mask remains the discriminating filter. Tests do not override RendererData. HDR colour expectations are linear; URP Unlit ShaderLab Color properties are set with `linearColor.gamma` to account for the material colour conversion.

Editor framework checks use one disposable additive scene and their own Undo group. `Camera.scene` is a rendering override; object ownership is checked through `camera.gameObject.scene`. Original scenes, dirty flags, selection, render targets, lighting and pipeline references are checked after cleanup. Tests do not save scenes, renderer assets, import settings or persistent test textures, and do not enter Play.

The Hemisphere disk cannot recover missing back directions. Its near-tangent boundary is singular, so round-trip colour comparison avoids the forward `z < 0.2` band; missing back directions are still checked for transparency. GPU tests establish the sampled numeric contracts on the tested graphics backend, not artistic quality or a platform-wide performance result. Save/Load helpers retain the standalone production-window template; interactive overwrite dialogs are not clicked by these tests.
