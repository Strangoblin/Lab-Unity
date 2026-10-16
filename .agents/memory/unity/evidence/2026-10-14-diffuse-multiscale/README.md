# Diffuse multiscale evidence

Owner: unity-developer; date: 2026-10-14. Retain with the [diagnosis](../../2026-10-14-diffuse-multiscale-reconstruction.md) while this implementation/baseline is relevant. This directory is selected evidence, not an executable project copy.

- `results.json` and `Main.json`: final 66-case camera/spatial/consumer/history matrix and CLI completion.
- Four `*NegativeControl.json` files: intended distance/history/spatial/omitted-Fine defects were detected.
- `comparison.json` and `MultiscaleComparison.json`: current copied static view, 640×360, Medium/DDA, temporal0.5, equal actual Shader time/ray sequence/history/bitwise surface masks; persistent production history frame-difference RMS -22.56395%, mean +0.28905%.
- `BaselineDiffuseGIFeature.png` / `DiffuseGIFeature.png`: last GI history frame, gamma-mapped display (HDR may clip), not final composition or motion/performance proof.
- `protected-before.sha1` / `protected-after.sha1`: 14 scene/source files unchanged; Renderer was externally saved across the overnight pause and was not reset. `compile.json`, `live-final.json`, `accepted-editor-status.log`: compile/current-state observations.

Reusable checks and the necessary narrow single-stage Feature baseline live in `Assets/Mine/Shaders/PostProcess/SSGI/Validation~/`; see its README and `run.sh --compare`. That baseline freezes orchestration/stride, intentionally sharing the current gather/spatial helpers. Existing different-view statistics must not be combined with this pair. Keep evidence inputs/source contracts alongside any future interpretation; do not treat the float synthetic reference tolerance as production half precision or these static images as visual acceptance.
