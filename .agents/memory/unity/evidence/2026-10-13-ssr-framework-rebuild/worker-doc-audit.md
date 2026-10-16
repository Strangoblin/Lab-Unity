# AUDIT — SSR-framework replacement: documentation contract

Read-only audit. Sources read: `PLAN.md`, `docs/TraceREADME.md`, `docs/SSGI.md`, `docs/Timing.md`.
No Assets or agent files edited, no git operations, no network, no Unity. No retired-algorithm
source is reproduced here. This document only classifies existing doc claims and proposes wording;
the main agent owns implementation and validation.

Repo baseline: `f9b3ad8`. `PLAN.md` is the authority for the *new* contract; the three `docs/*.md`
files express the *current* contract and are the audit surface.

---

## 1. Summary verdict

The docs still describe the **retired** design (grid-interval traversal + analytic slab
intersection, two-dimensional cell budgets 128/256/512). The replacement design is **uniform
perspective-correct DDA plus sample-based HiZ**, with budgets matching the existing per-channel
RAY tiers (Diffuse 24/48/64, Specular 32/64/96). Three edits dominate:

1. `docs/TraceREADME.md` L11, L73, L74, L75, L76, L77, L117, L119 — retire slab/grid wording.
2. `docs/SSGI.md` L36–L44 — replace cell-budget column and traversal text.
3. `docs/Timing.md` — relabel the whole doc as historical evidence for the retired grid DDA.

No claim in the docs asserts performance or visual equivalence; that must remain true after edits
(see §8).

---

## 2. Claims to RETIRE (must not survive in the new contract)

| Location | Current claim (abridged) | Why retired |
|---|---|---|
| TraceREADME L11 | `RayStepFunction.hlsl` = "网格区间与深度 slab 求交" | File now does uniform screen-space DDA stepping + bounded projected-parameter refinement, not grid/slab intersection. |
| TraceREADME L73 | DDA "遍历每个深度网格单元；在单元内用透视正确解析 slab 相交 … 固定单位单元步幅" | Whole grid-cell + analytic slab model is retired. |
| TraceREADME L74 | HiZ "按实际 mip 网格单元边界前进 … mip 0 与 DDA 使用相同 slab 测试" | HiZ is now sample-based: reuses DDA increments, stride ×2^mip, backtracks; not a cell/slab descent. |
| TraceREADME L75 (tail) | "近零屏幕投影直接测试一个单元的完整深度区间" | Tied to the retired cell/slab path. |
| TraceREADME L76 | "网格边界 … 两 ULP 坐标分类" + shared `4 * float epsilon * max(1,sceneDepth+thickness)` slab / empty-cell tolerance | Grid-boundary classification and slab/empty-cell feasibility tolerance are retired; replace with sample-crossing tolerance semantics only. |
| TraceREADME L77 | "DDA 为网格区间" as a distinct `maxSteps` unit | DDA `maxSteps` is now uniform sample count; budget is aligned to RAY tiers. |
| TraceREADME L117 | "DDA / HiZ 为新的像素单元遍历" | No longer cell traversal. |
| TraceREADME L119 | "DDA 是完整二维单元遍历而非均分屏幕线段" | Inverted: DDA is now the uniform sample sweep along the clipped segment; it is explicitly *not* complete coverage. |
| TraceREADME L131 | Validation oracle: "独立 CPU 世界距离 / 像素锥体区间求交" | PLAN L37 replaces the geometric-coverage oracle with fixed-sample/crossing tests. |
| SSGI L42 | "DDA 遍历真实有效相机深度网格 … 每次测试一个二维深度单元及其透视正确的眼深区间" | Same retired traversal model; also the "两种预算不等价" sentence must be re-derived. |
| SSGI L44 | "128/256/512 是初始有界预算" | Those were cell budgets of the retired DDA; new budgets are Diffuse 24/48/64, Specular 32/64/96. |

---

## 3. Claims to RELABEL historical

| Location | Current claim | Required relabel |
|---|---|---|
| Timing.md (whole file) | Headline "SSGI RAY / DDA 渲染计时"; table rows `DDA256/DDA128/DDA48` | Mark entire doc as evidence for the **retired grid-interval DDA**. It is not a current DDA variant and must not be cited as a current backend comparison. Keep RAY numbers as RAY context. |
| Timing.md L20 | "Medium 的 256 是每条 DDA 射线的单元上限" | Belongs to retired implementation; state so. |
| Timing.md L22 | "降低 DDA 预算会提前返回 BUDGET … 它不是加大 stride" | Historic grid-DDA budget control only. |
| Timing.md L24 | "后续优先优化逐格遍历：将每射线不变量和轴边界增量移出热循环" | That future work is now the implemented DDA (increments precomputed); relabel as historical motivation, not a pending task. |
| SSGI L89 (2026-10-13 section) | "双 DDA256 … 主要增量来自 DiffuseGI" | Same: historical grid-DDA timing; keep but prefix as retired-backend evidence. |
| TraceREADME L133 | Thin-single-pixel / zero-projection / budget GPU cases | Keep cases, but their expected results change under sampling; relabel expectations as historical/per-case, not cell-coverage oracle. |
| TraceREADME L135 | Frozen `SST_Trace` regression | Correctly RAY-only; per PLAN L16 keep, but state explicitly these fixtures represent **RAY**, not the retired grid DDA. |
| TraceREADME L138 | Negative control "+0.5 to GPU DDA hit distance" | Concept valid for new DDA; keep as negative control, but it now guards the sampling/revision path, not slab math. |

---

## 4. Claims to REWORD / UPDATE in place

| Location | Current claim | New wording direction |
|---|---|---|
| TraceREADME L44/L46 | depth-encoding + `maxSteps`/`thickness` semantics | Clarify raw reversed-Z vs positive eye-depth comparison (§6.5); state `maxSteps` = uniform sample budget. |
| TraceREADME L62–L68 | Status table | Keep codes; reword `SCREEN_TRACE_BUDGET` to "sampling budget exhausted before the clipped segment was swept, not a complete search"; `SCREEN_TRACE_MISS` stays "per-backend rule completed without hit". |
| TraceREADME L105 | Pyramid "父级取 min"; sky finite far sentinel | Keep; add that min is over **converted positive eye-depth**, distinct from raw reversed-Z per-pixel max (aids §6.5). |
| TraceREADME L127–L140 | Validation list | Replace CPU interval oracle with fixed-sample/crossing tests; keep real-camera/resource/history tests. |
| SSGI L36–L40 | Budget table | Replace "DDA 单元预算（两路）" column with per-channel DDA sample tiers matching RAY: Diffuse 24/48/64, Specular 32/64/96. |
| SSGI L32/L34 | Default `DDA=0`; SST adapts to `ScreenTrace_TraceDDA/TraceRay` | Keep; entry points preserved. |
| TraceREADME L5 | "生产 HiZ 及等质量 GPU 性能选择仍待后续" | Keep; also state HiZ consumes the same budget model and has no automatic RAY fallback. |

---

## 5. Claims to KEEP unchanged

- Public HLSL surface: no sibling includes, no CBUFFER, caller-declared includes, `SCREEN_TRACE_LOAD_DEPTH`/`_PYRAMID` overrides (TraceREADME L21–L31).
- Entry points `ScreenTrace_TraceRay / TraceDDA / TraceHiZ` (TraceREADME L12) — preserved per PLAN L8.
- RAY behavior: uniform world-distance samples + 6 binary refinements; RAY exhaustion returns MISS, not BUDGET (TraceREADME L72).
- `ScreenSpaceDepthPyramid` service contract (TraceREADME L80–L113; keep "正眼深转换与保守 min mip 归约" at L14).
- SSGI wiring, temporal-history signatures and composite semantics (SSGI L5–L69).
- Scope exclusions: BRDF/color coupling, scene/renderer/BRDF/ray-gen/filters/compositing out of scope (PLAN L17).

---

## 6. Proposed concise new contract

Suggested drop-in wording for the docs. Short, non-promissory.

### 6.1 Uniform perspective-correct DDA
- Clip the ray to near/far and viewport; keep `[tMin, tMax]` and world-distance semantics relative to the original origin.
- Precompute per ray: screen position, reciprocal W, and weighted view/distance increments. The loop advances clamped segment parameters by **additions** only.
- Sample uniformly across the **complete clipped distance segment**; no historical `StepSize=0.2` coverage reduction.
- On detecting a depth pass, locate the crossing by **bounded binary refinement of the projected parameters**, not analytic slab intersection.
- Explicitly: DDA is **approximate sampling, not complete pixel coverage**. Thin geometry between samples may be missed.

### 6.2 Sample-based HiZ
- Reuse the DDA increments; stepping at mip `m` multiplies the stride by `2^m`.
- Coarse rejections are confirmed by **backtracking to finer mips**; mip 0 shares the DDA sample-crossing test.
- Explicitly: HiZ is **approximate coarse stepping, not a proof of conservative empty cells**.
- The existing positive-eye-depth min pyramid service is unchanged and stays on-demand; SSGI HiZ integration is out of scope.

### 6.3 Budget semantics
- `maxSteps` is a **sampling/step budget**, per backend and per channel — not a measured average.
- RAY: `maxSteps` uniform world-distance samples + `refinementSteps` binary refinements.
- DDA: `maxSteps` uniform perspective samples across the clipped segment.
- HiZ: step budget includes descent/ascent checks.
- Production tiers **match the existing per-channel RAY tiers**: Diffuse `24 / 48 / 64`, Specular `32 / 64 / 96` (Low/Medium/High). These are bounded initial budgets.
- Exhausting the budget before the segment is swept returns `BUDGET`, distinct from a completed-search `MISS`; effect layer keeps its existing zero-contribution / Cubemap fallback. **No automatic RAY rerun.**

### 6.4 Known limitations (state plainly)
- DDA/HiZ are approximate; neither guarantees complete coverage of thin geometry.
- Bounded budgets mean long rays at high resolution may not be fully covered; this is by design, not a defect to hide.
- HiZ is not a conservative empty-cell proof.
- Switching backend requires re-budgeting; cross-backend `maxSteps` values are not interchangeable.
- No performance or visual-equivalence claim is made (see §8).

### 6.5 Raw reversed-Z max vs positive eye-depth min (dedicated distinction)
- **Raw reversed-Z depth**: the near plane maps to the **maximum** raw value (`near → 1`). A nearest-surface test on raw reversed-Z samples is therefore a **max**, and raw values must never be min-reduced to find the closest surface.
- **Depth pyramid**: converts source depth to **positive eye-depth first**, uses a finite far sentinel for sky, then reduces each parent with **min**, so every mip stores the nearest positive eye-depth.
- DDA/HiZ depth tests and `thickness` operate in positive eye-depth (linear) units; `thickness` is eye-depth, not world ray distance.
- Forward-Z raw (near → 0) is the opposite direction; do not reuse a reversed-Z assumption. Keep the existing `depthIsLinear / orthographic / reversedZ` flags as the single source of truth and do not let a raw-VS-converted comparison silently change direction.

---

## 7. Per-file edit checklist

- `docs/TraceREADME.md`
  - L11: rewrite the `RayStepFunction.hlsl` responsibility cell.
  - L44/L46: budget + depth-encoding clarification (§6.3, §6.5).
  - L62–L68: reword BUDGET (and optionally MISS) descriptions.
  - L73/L74: replace DDA and HiZ bullets with §6.1 / §6.2.
  - L75/L76/L77: delete grid-ULP/slab feasibility text; restate start-rejection and tolerance in sample terms.
  - L105: add "min over converted positive eye-depth".
  - L117/L119: fix DDA/HiZ characterization and reference note.
  - L127–L140: swap geometric-coverage oracle for fixed-sample/crossing tests; keep real-camera/resource/history tests; keep negative control.
- `docs/SSGI.md`
  - L36–L40: budget table column → Diffuse 24/48/64, Specular 32/64/96.
  - L42/L44: rewrite traversal + budget semantics; keep BUDGET-vs-MISS and no-auto-RAY.
  - L89: prefix historical label on the 2026-10-13 DDA timing paragraph.
- `docs/Timing.md`
  - Add a top-of-file historical banner: measured the retired grid-interval DDA; not a current variant.
  - L20–L24: relabel as retired-DDA evidence; mark the "future optimization" as now implemented.

---

## 8. Non-claims / anti-claims (must remain true)

- No statement of performance equivalence or improvement between RAY/DDA/HiZ.
- No statement of visual/image equivalence.
- No retained snapshot or reference copy of the retired grid/slab algorithm.
- Validation may assert geometry/resource contracts and deterministic crossing behavior; it may not assert equal quality or pure-GPU speed. Human visual confirmation remains necessary.
- Historical grid timings (Timing.md) stay explicitly historical and are not presented as a current backend.
