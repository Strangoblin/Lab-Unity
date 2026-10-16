# SSGI validation

Run from the project root with Unity Editor and the host unityctl bridge connected:

```bash
bash Assets/Mine/Shaders/PostProcess/SSGI/Validation~/run.sh
bash Assets/Mine/Shaders/PostProcess/SSGI/Validation~/run.sh --compare
```

The runner imports its own `RuntimeFixture` through the existing write gate, refuses to overwrite an existing fixture, and removes only its own imported files/metas on exit. It never saves the scene or Renderer, changes Play Mode, or replaces the user's SceneView pose. Outputs go to uniquely named `tmp/sessions/` directories.

## Matrix

- Camera geometry: perspective/orthographic, clipping, resolutions, RAY/DDA, and real consumer raw-trace regression.
- Shared spatial core: independent CPU/RGBA GPU reference for constant HDR, impulse, depth/normal edges, invalid normals/sky, boundaries, stride/fallback and fractional parity.
- Diffuse two-scale reconstruction: independent CPU stride-1 followed by stride-2 reference, compared against both GPU stages; constant HDR/confidence, expanded impulse support, checkerboard phase mixing, both fine/coarse protected edges and invalid guide behavior. This synthetic probe round-trips RGBAFloat input/output; its 2e-5 bound does not characterize production RGBA16F precision or directly inspect the production RenderGraph's edges.
- Unified/standalone entry agreement, exclusive compiled Diffuse tiers and ceil-rounded native Trace dimensions (High divisor 3), Specular frozen-filter regression, AO backend isolation and per-camera/per-channel history rejection, including old single-scale/sequence and half-resolution High signatures. Old High history rejects only Diffuse without reallocating persistent RTs.
- Four negative controls: corrupted DDA distance, deliberately accepted wrong-backend history, spatial red-channel corruption, and an omitted fine stage on nonconstant input. Controls must fail with the intended diagnostic, not silently succeed.

`check_result.py` rejects empty/incomplete matrices and checks the expected positive or injected-defect result. The two-stage pipeline keeps four Shader Pass indices; only the RenderGraph schedules spatial Pass 1 twice.

## Static comparison

`--compare` additionally imports `BaselineDiffuseGIFeature.cs` into the runner-owned fixture for compilation. This freezes the pre-multiscale Feature's single-stage orchestration/stride (1.5/1.5/1), not the whole tracing Shader or shared filter; both branches intentionally share the current gather/filter helpers and current tier resolutions (including High divisor 3) to isolate the spatial orchestration change. It is not the old half-resolution High performance baseline.

`MultiscaleComparison` copies the current SceneView pose (or main camera), renders the real active scene using a disposable Diffuse-only Renderer at 640×360, uses the current trace backend/quality/technical settings and forces temporal blend 0.5 only on the disposable Features. Receiver albedo is white, distance falloff zero, matching unified Diffuse semantics. Other original Features are excluded only from the clone.

Each branch warms 32 requests and collects ray-frame indices 32–47. Values are read from the production persistent Diffuse history color/depth RTHandles, not reclaimed RenderGraph globals or transparent-overlaid screenshots. Shader time, actual budgets/backend, active history and exact surface masks must match. PNGs display the last linear GI frame through linear-to-gamma mapping; HDR display values may clip.

`check_comparison.py` validates the saved report, pair/frame conditions, restored live settings, nonempty signals and derived frame-difference RMS/mean changes. These numbers describe the fixed copied static view, not Game View GPU performance, motion quality, variance over all scenes, or proof that blur/light leaks are acceptable. Multi-scale adds one low-resolution RGBA16F target and one 25-tap Raster Pass; it does not alter ray budgets, temporal weights, sampling or Specular.

## Sequence comparison

```bash
bash Assets/Mine/Shaders/PostProcess/SSGI/Validation~/run.sh --sequence
python3 Assets/Mine/Shaders/PostProcess/SSGI/Validation~/check_sampling.py tmp/sessions/<sampling-run-id>/output
```

The CPU check requires NumPy and writes only to the provided task output (otherwise a unique `tmp/sessions` directory). Its independent float32 model validates the spatial digital net, evolving samples, all three radial strata layouts, cosine mean and simple flat-guide integrands. Synthetic cases are diagnostics, not proof of scene quality; some purely radial horizon cases regress.

`--sequence` runs the positive matrix/four controls, then imports `SequenceBaseline.shader`, its frozen pre-sequence-change helper and `TraceCopy.shader`. Unlike `--compare`, all three A/B/A branches use the **current production two-stage Feature**; only the baseline sampling helper is frozen. The old geometry baseline's live Diffuse helper is not a sampling reference.

The cloned current pose renders the active scene at 640×360, current quality/backend/geometry, forced disposable temporal blend 0.5, 32 warmup requests and 64 samples (ray frames 32–95). The native Trace is exported into a runner-owned RGBA16F RTHandle in a same-frame RenderGraph pass with explicit `UseGlobalTexture(_GITraceTexture, Read)`; it is never read from a reclaimed pooled global. History and depth come from production persistent RTHandles. Raw surface eligibility samples the full-resolution persistent eye-depth mask at each native Trace texel center; the exact masks are checked every sample/across branches.

Reports give per-signal luminance frame-difference RMS, population temporal variance around each pixel's sample mean, mean luminance and mean-image comparison. Budgets, blend, fixed Shader time, frames, active history, dimensions/format and original settings/dirty state are checked. A repeated old baseline measures rendering drift: aggregate metric relative differences and mean-image RMS normalized by mean luminance must stay within 0.2%. This is **not bitwise repeatability**; its actual max RGB/aggregate deviations remain in the report. The validator does not demand an improvement or treat CPU distribution checks as scene evidence.

PNGs show the last/64-frame average linear HDR GI via display gamma and possible clipping, not numeric data. They are observations, not proof of visual acceptance or performance. The runner cleans its imported fixtures even on failed assertions. Necessary reports and representative images belong with the dated Unity memory; rebuildable requests stay under `tmp/sessions`. Historical sequence conclusions and remaining floor noise are recorded in the 2026-10-14 sequence memory; a later resolution change does not retroactively change those measured conditions.

## High budget comparison

```bash
bash Assets/Mine/Shaders/PostProcess/SSGI/Validation~/run_high_budget.sh --isolated-diffuse
```

This explicit entry uses only a disposable Diffuse Renderer; it never enables or changes the original SSGI/Renderer. `prepare_high_budget.py` derives a half-resolution High control from the current production Feature by renaming its type and changing only High divisor 3 to 2. It checks unique replacements and records source SHA256. This controls the resolution rebalance, not all changes since a historical commit. No frozen production implementation is duplicated in the validation library.

`HighBudgetChecks.cs` is concatenated with the existing partial `CameraChecks.cs` into a task-owned Roslyn script. It reuses RendererScope, persistent-history collection and display helpers. Two Feature instances are reused, and their histories are released/restarted outside each measured block; earlier blocks' histories do not accumulate indefinitely.

Timing copies Main Camera and its postprocessing, at its current pixel dimensions plus 1920×1080 (deduplicated): 8 warmup/12 synchronized requests per block, 3 interleaved/reversed rounds. The measured scope is the standalone Diffuse pipeline with Indirect debug output, not current Game View/all modules or pure GPU/Pass time. Both branches use the production Shader, 8 rays/64 steps, current reconstruction and temporal blend 0.5.

Quality separately copies the current SceneView pose (fallback Main), disables postprocessing/AA on the clone, renders 640×360, warms 32 and collects 64 persistent-history frames. HalfHigh / ThirdHigh / HalfHighRepeat must match exact surface masks, Shader time and ray frames. The checker validates complete blocks, actual native sizes/compiled budget, finite synchronized samples, nonempty stochastic quality signals, derived metrics and the repeated baseline's 0.2% aggregate drift bound. It deliberately does not require a speedup or a noise improvement: reduced resolution has a real detail/noise tradeoff. Mean/last PNGs are for inspection, not equal-quality acceptance. Motion, local floor noise, XR and other APIs remain separate checks.

