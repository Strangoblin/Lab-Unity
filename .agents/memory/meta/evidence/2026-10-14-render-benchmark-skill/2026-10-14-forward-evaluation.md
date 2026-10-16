# Offline forward evaluation — unity-render-benchmark

Basis: `.agents/skills/unity-render-benchmark/SKILL.md` and
`.agents/agents/unity-developer/references/standard/rendering/render-benchmark.md`
(采集合同 = capture contract; 相对判读 = relative interpretation).
Offline only — no engine result is claimed or run here.

## Request 1 — Blur "speedup" claim + exact GPU ms

**Input:** Metal; non-SSGI Blur, optimized. Overall synchronized request
medians A=8.20ms, B=8.10ms. Repeat block medians A=(8.20, 8.35, 8.10),
B=(8.30, 8.15, 8.10); view/size/budgets identical.

**Proposed response / next action:**
- Do **not** announce a GPU speedup. Relative change `= (8.10/8.20 − 1) × 100% ≈ −1.2%`
  (protocol 相对判读 3); negative is lower wall clock this run, not GPU speedup.
- Do **not** supply exact Blur GPU milliseconds. The caliber here is
  GPU-synchronized render-request wall time, which is not pure GPU/trace-pass time
  (SKILL 交付限制; protocol 边界/交付限制 — 同步段减提交段不是 GPU 时间, submit+sync
  may overlap). No GPU timer was used, so the true caliber cannot be substituted.
- Block ranges overlap and the A/B gap (~0.10ms) is smaller than the within-arm
  block spread (A ≈0.25ms, B ≈0.20ms), so report **“本次未分辨差异”** rather than a
  speedup, and do not keep adding samples to force significance (protocol 相对判读 4).
- Report instead: relative wall-clock trend, per-size block medians, block range,
  sync caliber, warmup/repeats, and raw report path.

**Missing guidance:** minor — the skill says “差异与波动相当” but does not state a
concrete way to derive observed fluctuation from the block medians (e.g. block
range vs. median gap). A tolerance/decision formula would remove ambiguity.

## Request 2 — no adapter; run SSGI runner and permanently disable other Features

**Input:** a different effect has no timing adapter; user asks to directly run the
SSGI timing runner and permanently disable all other Renderer Features to get the
effect's cost.

**Proposed next step (do not run):**
- Decline the permanent disabling. Skill 边界/入口 states usage does **not** authorize
  modifying the original Renderer/scene or excluding other Features; that is outside
  this bounded workflow.
- Do not treat the SSGI runner as a generic entry: the SSGI `run_timing.sh`/`Timing.md`
  adapter is explicitly **not** a ready entry for arbitrary effects (SKILL 现有适配器).
- Correct path: read the effect's `Validation~/`, scripts and records first for an
  existing adapter; if none, build a **minimal capture script in the task directory**
  per the capture contract, without copying an effect implementation or changing
  production defaults (SKILL 执行 2). A/B = that effect on/off, holding other Features
  fixed, and check the actually executed shader/pass/keywords (protocol 路径检查) —
  otherwise the delta is a pipeline enable/disable delta, not the effect's own cost.
- If a valid path/adapter cannot be established, report **cannot capture**, not an
  empty run (protocol 采集合同 — 不把空跑当计时成功).

**Missing guidance:** the no-adapter fallback is covered, but the skill does not
explicitly name the unsupported-request outcome ("report cannot capture") inside the
SKILL itself; it only appears in the protocol. Surfacing it in 交付限制 would help.

## Request 3 — one-pass vs two-pass GI, "smoother PNG ⇒ faster"

**Input:** compare one-pass and two-pass GI with the existing RAY/DDA timing runner;
claim the two-stage PNG looks smoother, therefore should be faster.

**Proposed next action:**
- Do not reuse the RAY/DDA runner as the comparison entry; it is the current-RAY/DDA
  adapter, not a single/multi-pass dimension. A new dimension needs the effect's
  adapter extended by the owning task (SKILL 现有适配器 / 执行 2).
- Do not infer speed from a smoother PNG. Image smoothness is a **quality** result;
  performance and equal-quality are separate claims, and successful screenshots are
  not performance or equal-quality proof (SKILL 交付限制; protocol 相对判读 结尾).
- Procedure: declare A/B as one-pass vs two-pass with the pass count the **only**
  varied factor; fix camera pose/projection/aspect, size/renderScale, input objects/
  lighting/animation, exposure/post, Feature order and non-measured budgets; verify
  the real shader/pass/keyword path per arm; warm up, interleave A/B (or ABBA), then
  compare same-size block medians, relative delta and drift.
- Keep distinct: (a) the two timing calibers (never mix GPU timer with synchronized
  wall clock); (b) performance vs. quality — route the quality/output comparison to
  `unity-render-debug`; (c) the target pass's marginal cost vs. the whole-pipeline
  wall-clock delta (回读+同步+整管线相互影响).

**Missing guidance:** none material for this case; the quality-vs-performance split
and adapter-extension rule are both already stated.
