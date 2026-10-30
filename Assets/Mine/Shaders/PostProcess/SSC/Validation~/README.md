# SSC quality validation

Run against the open SampleScene, PC_RPAsset / PC_Renderer and active SSCFeature:

```sh
unityctl script execute Assets/Mine/Shaders/PostProcess/SSC/Validation~/QualityCheck.cs
unityctl script execute Assets/Mine/Shaders/PostProcess/SSC/Validation~/HDRCheck.cs
unityctl script execute Assets/Mine/Shaders/PostProcess/SSC/Validation~/SSCCompileCheck.cs
```

The tilde directory is excluded from Unity compilation. Execute one script at a time through Roslyn.
Each script restores settings, active flags and temporary objects in finally; neither saves scene/assets.

QualityCheck writes unique 960×540 PNGs under tmp/sessions/<date>-ssc-quality-<id>/output.
It uses a Game/Base camera at (0,25,-90), looking at (0,50,0), Renderer 0, no volume postprocessing,
speed=0 and jitter=0. Other settings/light/sky/noise come from the current scene and SSC asset.
The slanted foreground cube is at z=-60, entirely before the cloud box (z=-50..50);
placing it at z=-35 would intersect the cloud and legitimately show cloud in front of its upper end.
The script renders downsample 0..4, with/without the cube, for two shaders: current reconstruction and
an in-memory control that replaces only ReconstructCloud with bilinear sampling. Colors, ray integration,
RGBA16F buffer and full-resolution center occlusion are identical. At downsample=0 both should match.
Finally it synchronously compiles both passes for the 64/128/256 keywords and returns shader messages.

SSCCompileCheck only reimports nothing and compiles both passes for all four ray keywords, then prints every
message. Run it after editing the shader: `AssetDatabase` can keep a stale import, and a compile run against the
stale asset reports 0 messages. Force `unityctl asset import <path>` first when the file was changed on disk.

HDRCheck uses a solid background and cullingMask=0 to avoid unrelated scene material animation and
path-dependent geometry differences. It checks RGB111110Float (no output alpha) and ARGBHalf:
clouds outside camera range must preserve the scene (RMS <=0.01); visible clouds must change it
(RMS >=0.01 positive control). It reads linear float pixels and rejects nonfinite red values.
The empty-cloud and cloud renders use an active feature; the background reference disables it.
For the 2026-10-20 Metal run the empty-cloud difference was exactly zero in both formats.

# Implementation contracts

The cloud buffer stores premultiplied RGB and coverage A, explicitly RGBA16F independently of camera format.
Composite preserves scene alpha and evaluates scene*(1-cloud.a)+cloud.rgb.
The new ramp uses stepShadow³ only for BaseColorB→A interpolation, increasing shadow tint; HG and direct
attenuation are unchanged. Equal A/B colors remain equal.

Upsampling follows SpecularGIFilter.hlsl's texel-center/depth-weight pattern but preserves sky samples and
uses no normals. Core Filtering.hlsl supplies the cubic B-spline functions. The performance implementation
checks the same 16 low-resolution grid depths and packs sky membership into a 16-bit mask. A normal-brightness
sky with all 16 bits set uses four bilinear fetches. Mixed sky decodes the mask for the old weighted samples;
geometry or HDR backgrounds (abs scene RGB > 1) use the original interleaved depth/point-cloud path.
This avoids keeping 16 float depth weights alive across the branch. Both passes require target 3.5 for uint
bit operations. RGB/A stay premultiplied and normalized together; downsample=0 bypasses reconstruction.
Full-resolution cloud-box occlusion protects foregrounds. Missing compatible neighbors, or depth discontinuity
inside the cloud volume, fall back to the same EvaluateCloud center ray. No extra RT/pass/history is added.

Ray integration skips noise outside the cloud slab (a cheap normalized-height test) and skips lighting at zero
local density. It terminates only at Density >= 1, when all remaining stepAlpha contributions are exactly zero.
Approximate transmittance thresholds were rejected after HDR testing; correctness does not depend on half
conversion rounding. Light-probe positions and the color ramp remain; the three quality tiers integrate with
32/64/128 rays. Low resolutions become smoother and softer; this does not recover missing detail or provide
temporal AA. Cloud RGBA16F and guided reconstruction have a cost. Synchronized request-wall A/B was measured
on M2; pure GPU/Pass timing, continuous FPS, XR and dynamic resolution remain unmeasured.

Baseline: 5900bb7, with the remote SSC and local ScreenWarp both retained.
Visual evidence and verified conditions: .agents/memory/unity/2026-10-20-ssc-quality.md.

# Density experiment (2026-10-27, archived — shader reverted)

The adaptive-height rework was measured and then reverted. `SSC.shader` is back to the pre-experiment state:
`DensityLOD(..., out float columnDensity)` returning `low * 0.5 + high * 0.5`, and
`CalculateDensity` doing `localTop = lerp(globalTop, bottom + span * saturate(columnDensity), 0.5)` with the
threshold applied to `noise × height`. The reworked version is kept out of the runtime path as a fixture:
`Fixtures/SSC-adaptive-height-2026-10-27.shader.txt`. To test it, rename to `.shader`, give it a unique
`Shader "..."` name (for example `PostProcess/SSC.AdaptiveHeight`), and point the SSC feature's shader field
at it — do not overwrite `SSC.shader`, or the baseline is lost again.

What the fixture changes: `Noise()` encodes the sample back to the 8-bit data value; `DensityLOD` normalizes
the octaves by `1 - 2^-lod` and drops the `out` parameter; `DensityH` takes a global `heightT` plus `topScale`;
`CalculateDensity` thresholds column coverage instead of `noise × height` and sets the local top to the
coverage itself.

Why the live render read as "every cloud spans the slab top to bottom", measured with `SSCColumnProfile.cs`
against the real Voronoi asset (threshold 0.2, thickness 0.8, middle 0.25, size.y 20, 400x400 columns):

- The encoded noise domain is narrow: p05 0.363, p25 0.427, p50 0.468, p75 0.509, p95 0.568.
- Coverage at threshold 0.2 leaves **0.0% holes** — every column carries cloud, none of the sky is clear.
- The `lerp(1, coverage, 0.5)` envelope holds `topScale` inside 0.60–0.73, so every column is 60–73% thick.
- With `thickness` 0.8 (power 0.2) the vertical profile is nearly a box, so the result is a solid slab.

Two consequences for the fixture, both confirmed by rendering: the threshold has to be compared in the
encoded domain (`0.2` encodes to 0.485, still the noise median, so the tuned value keeps its meaning), and
the coverage denominator has to be an achievable dense value (~0.60) rather than `1 - threshold` — with
`1 - threshold` the coverage span collapses to 0–0.16 and the sky renders bare.

`thickness` controls only the profile shape; at 0.8 the profile is nearly a box, and 0.4–0.6 is the softer
range. The shader Properties default for `_CloudThreshold` is still 0.2; the live value comes from PC_Renderer.

# Three-tier performance budget

```sh
unityctl script execute -t 180 -f Assets/Mine/Shaders/PostProcess/SSC/Validation~/TierCheck.cs
```

TierCheck verifies the Inspector exposes exactly Low/Medium/High with no tier tooltip, while raw ray and
downsample fields remain hidden. Low is 32 rays at shift3, Medium (default) is 64 at shift2, and High is
128 at shift1. An isolated 961x541 camera checks actual odd-size cloud texture dimensions, `_SSCDownsample`,
the single ray keyword, finite/nonempty cloud output and unknown-enum fallback. Legacy serialized downsampling
maps by shift at load. The compatibility setters still preserve arbitrary ray/shift pairs for historical
validation scripts, and selecting a normal quality tier discards that override. The whole Settings snapshot,
feature active flag and scene dirty state are restored in finally. The latest report is under
`tmp/sessions/2026-10-20-ssc-tiers/output/`.

# Dual-HG scattering

`Phase G` controls the peak-normalized forward HG lobe. `Secondary Scattering` adds a separate isotropic
HG0 lobe rather than mixing the two lobes down to one peak. The default is 1.0, so the phase is literally
`HG(g=0.8) + 1.0 * HG(0)`. At cosine 1 the combined phase is 2.0; at cosine 0 it is 1.003809 instead of
0.003809 for the single forward lobe.

The isolated-light A/B showed a 27.67x mean gain in dark cloud pixels and a 3.29x gain in the bright core.
The blue channel and forward lobe remain present; there is no global ambient wash. The repeated single-HG
render was exact. `Secondary Scattering=0` disables the isotropic lobe and restores pure single HG.
```sh
unityctl script execute -t 180 -f Assets/Mine/Shaders/PostProcess/SSC/Validation~/ScatteringCheck.cs
unityctl script execute -t 180 -f Assets/Mine/Shaders/PostProcess/SSC/Validation~/TierBenchmark.cs
```

TierBenchmark uses the active SceneView pose at 1920x1080, six warmups, twelve samples and three rounds in
alternating order. It measures synchronized request wall time, not pure GPU time or FPS. On the 2026-10-21
M2/Metal view, pooled medians were Low 3.44ms, Medium 9.19ms and High 40.24ms. High is a visual-quality
tier rather than a real-time preset on this device.

# Historical cost probe

```sh
unityctl script execute -t 180 Assets/Mine/Shaders/PostProcess/SSC/Validation~/PerformanceProbe.cs
```

This historical diagnostic compares the pinned pre-optimization fixture vs an in-memory Density>=0.995 early-exit shader for
quarter/64, half/64 and quarter/128. It copies the CURRENT SceneView pose into a temporary Game camera,
1920x1080 ARGBHalf, Renderer 0, no postprocessing, speed/jitter=0. The current scene/material time remains
unfrozen. It requires an active SSC and writes unique tmp/sessions/<date>-ssc-cost-<id>/output files.

The metadata is profile.json; authoritative raw data is samples.csv (216 rows, three rounds,
six configurations, eight warmups and twelve timed requests per block; alternate order each round).
Time includes SubmitRenderRequest and 1x1 ReadPixels synchronization, not pure GPU/Pass time or FPS.
PNG captures are display evidence for the quarter/64 pair. Early-exit is an APPROXIMATE candidate;
it is never written into the production shader. Settings and the original material are restored.
Do not infer equal image quality from its small average PNG difference, or infer P95 improvement from
median improvement. Archived 2026-10-20 raw measurements and proposed parameter design are recorded in
.agents/memory/unity/2026-10-20-ssc-redesign-proposal.md.

# Performance regression checks

```sh
unityctl script execute -t 180 -f Assets/Mine/Shaders/PostProcess/SSC/Validation~/OptimizationCheck.cs -- visual
unityctl script execute -t 180 -f Assets/Mine/Shaders/PostProcess/SSC/Validation~/OptimizationCheck.cs -- coverage
unityctl script execute -t 180 -f Assets/Mine/Shaders/PostProcess/SSC/Validation~/OptimizationCheck.cs -- benchmark
unityctl script execute -t 180 -f Assets/Mine/Shaders/PostProcess/SSC/Validation~/OptimizationCheck.cs -- benchmark-edge
unityctl script execute -t 180 -f Assets/Mine/Shaders/PostProcess/SSC/Validation~/HDRBoundaryCheck.cs
```

The fixture Fixtures/SSC-before-optimization.shader.txt pins the pre-performance implementation (SHA256
456dcf3722c79c6eb565ea09c7d91953a517397f7e4bc797a165d787c82194e5). It is a reproducibility input, not a runtime shader.
All runs create unique output directories under tmp/sessions/2026-10-20-ssc-opt-c1/output.

visual captures baseline/repeated-baseline/optimized float HDR images for current view, front rod, thin rod,
cloud-interior geometry and a black-cloud/HDR1000 case. metrics.csv reports all-channel finite checks,
RGB/alpha errors and raw hashes. Final camera alpha is not a cloud-coverage measurement; coverage instead
encodes Pass0 alpha into RGB, keeps showSSC=true, and compares 64-ray downsample0/1/2/3.

benchmark measures the current SceneView copied into a temporary Game camera. benchmark-edge uses eight
isolated thin rods entirely in front of the cloud box. Both use 1080p, baseline/optimized at quarter64,
half64 and quarter128, eight warmups, twelve samples, three rounds with reversal: 216 complete CSV samples
per scene. Shader compile, allocation, encoding and disk writes are outside timing. Input settings and
scene dirty state are restored and recorded.

HDRBoundaryCheck uses actual linear background/material value1000, real and white noise, with/without a
cloud-interior thin rod. cases.csv is authoritative (nested dynamic Roslyn types may be omitted by JsonUtility).
It checks duplicate-baseline exactness, calibrated HDR values, center/full-image errors, and restoration.
Thresholds are diagnostics for this fixture, not a universal absolute-error claim.

Results, raw CSV/metadata and limitations: .agents/memory/unity/2026-10-20-ssc-performance.md.
