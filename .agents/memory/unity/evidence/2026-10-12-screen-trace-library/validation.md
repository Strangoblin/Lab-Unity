# ScreenSpaceTrace library validation

Owner: unity-developer / ScreenSpaceTrace module. Date: 2026-10-12.

Environment: Unity 6000.3.14f1, URP 17.3.0, macOS Metal.
Source and repeatable runner: `Assets/Mine/Special/Rendering/ScreenSpaceTrace/Validation~/`.

Positive run: 39 cases / 69,432 assertions, `metal-results.json`.
Original runtime report: `tmp/sessions/2026-10-12-screen-trace-checks-caed4294/output/results.json`.
Negative control: `_TestInjectFault=1` adds 0.5 to GPU DDA hit distance. Runner rejects it with
`13x9 repr 0 ray 0 distance backend 1 expected 2 actual 2.499999`.
Original expected-failure report: `tmp/sessions/2026-10-12-screen-trace-checks-d1022799/output/results.json`.

All temporary imported RuntimeFixture sources/metas are removed by the runner's gated finally cleanup.
The frozen baseline and consumers remain in Validation~. No production SSGI/SSSM behavior was changed.

OpenCode reviews: service contract review plus NPOT min reduction review, both using
`opencode-go/deepseek-v4.1-flash`. The first, larger algorithm review exited after reads without an
artifact; one smaller retry succeeded. Main agent validated all relevant conclusions against source
and actual GPU results. These reviews are not substitutes for the independent oracle or GPU tests.

Evidence purpose: preserve the initial geometry/resource-contract acceptance checkpoint, not claim
effect quality or speed. Dependencies are the module's validation sources and the specified Unity/URP
API generation. Future updates should rerun the current source; retain this historical result until
the next SSGI integration checkpoint is accepted, then reassess its archival value.
