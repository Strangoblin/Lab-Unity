# REVIEW — SSR-framework HLSL (read-only, adversarial)

Target: `review/RayStepFunction.hlsl`, `review/ScreenSpaceTraceFunction.hlsl`.
Authority: `PLAN.md` (+ updated contract in
`Assets/Mine/Special/Rendering/ScreenSpaceTrace/README.md`).
No Assets/docs edited, no Unity, no network, no git. No source copy reproduced.
All line numbers below are for the `review/` snapshot.

## 0. Scope / snapshot warning

At read time `review/RayStepFunction.hlsl` was byte-identical to the applied
`Assets/Mine/Special/HLSL/RayStepFunction.hlsl`. `review/ScreenSpaceTraceFunction.hlsl`
was later overtaken by a concurrent edit in `staging/` and `Assets/...`
(mtime 11:15): `ScreenTrace_PrepareDDA` lost its internal `ScreenTrace_Validate`
call and its signature changed `out ScreenTraceHit hit` → `inout ScreenTraceHit hit`,
with the two callers now zeroing/validating `hit` themselves. That refactor is
non-semantic (same double-zeroing, same status paths); the algorithmic findings
below apply to both snapshots. Validate placement in `review/` is merely
redundant (`TraceHiZ` validates, then `PrepareDDA` validates again).

## 1. Verdict

No memory-safety, out-of-bounds, NaN, infinite-loop, or unsafe-division defect
found. Near/far/viewport clipping, safe reciprocal-W handling, bounded
diagnostics, and the absence of the retired grid/slab traversal all hold. The
remaining items are (a) a real HiZ logic weakness, (b) unenforced service
assumptions, and (c) explicitly accepted approximations that must not be
silently upgraded into guarantees. Concrete items first.

## 2. Concrete issues

### R1 — HiZ can discard a crossing its own coarse probe detected (logic, low/med)
`ScreenSpaceTraceFunction.hlsl:341-381`. On a `mip>0` penetration the code
backtracks (`state=previous; mip--; continue`) but re-probes from the *same base*
with a smaller power-of-two stride. If that finer probe is a non-hit it commits
the step, restores `mip = min(mip+1, …)` and advances `2^mip` again, so the
sample interval that produced the coarse hit is re-skipped without ever being
swept at mip 0. Net effect: a `delta>=0` observation at mip>0 does not bound a
validating descent; `TraceHiZ` can terminate `MISS`/`BUDGET` after having seen a
penetration. For monotonic ray eye-depth (guaranteed after the near clip, w>0)
and thick surfaces the later probe re-detects it, so this collapses to the
accepted thin-geometry miss in practice. It is *not* a guarantee, and the GPU
oracle mirrors this exact control flow (`Validation~/GpuChecks.cs:351-374`), so
no existing test can catch a regression here. Classify as accepted approximation
(PLAN L15; README L74), but state plainly that coarse hits are advisory.

### R2 — HiZ assumes the bound pyramid's base size equals `depthSize` (contract gap)
`ScreenSpaceTraceFunction.hlsl:355-356` derives the mip texel from
`mipSize = (int2)context.depthSize >> mip`. `ScreenTraceContext` carries
`depthSize`/`depthTextureSize`/`pyramidMipCount` but **no pyramid base size**, so
the HLSL cannot verify the pyramid was built at the same dimensions as
`depthSize`. A consumer that binds a pyramid built at a different base silently
reads the wrong (clamped) texel. The README requires matching sizes
(L107) but the ABI does not expose it. Robustness/contract issue; not exercised
by the suites (they always match). Either add a pyramid-size field or document
the hard precondition in the header comment.

### R3 — `ScreenTrace_EndStatus` uses an absolute 1e-4 tolerance (minor)
`ScreenSpaceTraceFunction.hlsl:229-233`. If near/far or viewport clipping
shortens the clipped end distance by less than 1e-4, a genuine left-view is
reported as `MISS`. Bounded and cosmetic, but it is an absolute constant against
a scale-dependent quantity; a relative term would be more consistent with the
rest of the file.

### R4 — RAY vs DDA/HiZ origin-rejection asymmetry (intentional, document it)
`ScreenTrace_OriginInFront` (`:127-137`) rejects an origin within
`1e-6*max(1,sceneDepth)` of the surface (DDA/HiZ); `ScreenTrace_TraceRay`
(`:155`) compares exactly. So a grazing origin inside that band yields
`MISS` from DDA/HiZ but can proceed to a hit under RAY. This is required to keep
the frozen RAY baseline bit-path, and README L75 scopes the tolerance to
DDA/HiZ — but the divergence is a real backend-visible behavior and should stay
explicitly documented, not "fixed" by touching RAY.

### R5 — `hit.eyeDepth` is the ray eye-depth, not the scene surface depth (API ambiguity)
`ScreenTrace_SetHit:219-227` and the RAY hit path (`:209`) both store the ray's
own eye depth. At a valid hit this differs from the scene sample depth by
`delta ∈ [0, thickness]` (equal only for zero thickness). README L59 lists
`eyeDepth` as a returned field without defining it. Name/define it (e.g. "ray
eye depth at hit") to avoid callers treating it as the reconstructed surface
depth.

## 3. Frozen-RAY preservation, checked

- Sample schedule, sign-crossing bracket, 6 refinements, and the
  `!surface||delta<0||delta>thickness` accept test match
  `Validation~/BaselineScreenSpaceTrace.hlsl` (`review` RAY `:161-216`). Budget
  semantics (uniform samples, exhaustion → `MISS`, never `BUDGET`) are preserved.
- Divergence that is **not** bit-identical: the frozen baseline samples
  `_CameraDepthTexture` via `ClampAndScaleUVForBilinear` +
  `UnityStereoTransformScreenSpaceTex` + point `SAMPLE_TEXTURE2D_X_LOD`, whereas
  the new path integer-`Load`s the effective viewport
  (`ScreenTrace_LoadUV/LoadDepth`, `:96-125`). At RTHandle sub-viewport, clamp/scale
  or stereo edges the two can select different texels. The camera regression only
  asserts within 0.008 distance / 0.003 UV (`CameraChecks.cs:489-492`), so
  "preserved" is tolerance-based and non-stereo/unit-scale only. No claim of
  bit-exact or image equivalence is made here.

## 4. Verified correct (branch/state focus)

- **DDA spans the whole clipped parameter range with the exact endpoint.**
  `RayStep_CreateDDA` feet at `progressRange.x`; the loop runs exactly `maxSteps`
  additions and re-snaps the final sample to `progressRange.y`
  (`:141`, `:295-300`). Budget therefore sets *sampling density*, not *extent*;
  intermediate pixels may be skipped but the end is always probed. (Confirms the
  correction: the earlier "DDA may not span its clipped segment" wording is wrong.)
  `TraceHiZ` snaps the endpoint the same way (`:347-348`).
- **HiZ budget accounting is sound.** `cursor` always equals committed coverage,
  so `cursor>=maxSteps` iff the endpoint was reached → `EndStatus` (`:374-379`);
  `BUDGET` only when backtrack descent exhausted the `maxSteps` iteration bound
  with coverage short (`:382`); `hit.steps<=maxSteps`.
- **Bracket ordering / hit consistency.** `low.progress < high.progress`
  holds on every refine entry (DDA `previous<state`; HiZ backtrack restores
  `previous` before advancing). `RayStep_MidpointDDA` interpolates affine
  per-progress fields, so the midpoint is exact. `SetHit` derives uv/distance/
  eyeDepth from one state, so they are mutually consistent and finite (`w>0`
  enforced at `RayStep_ProjectRay:104` and `ScreenTrace_ProjectPoint:89`).
- **Origin near/far handling.** Clip to `[near,far]` then viewport
  (`RayStep_ProjectRay:98-122`); non-projectable origins are allowed and the
  clipped start is used (`OriginInFront`); empty clipped interval → `LEFT_VIEW`.
  No historic endpoint singularity.
- **Bounded diagnostics.** `steps<=maxSteps`; `depthQueries<=maxSteps+refinementSteps+3`
  in every backend, matching README L77. `status` is set on every exit path.
- **No grid/slab reintroduction.** No `StepSize=0.2`, no cell/slab traversal, no
  nested `StepCount×BinCount`; `RayStep_ClipInterval/ClipHalfspace` are plane
  clipping (retained per PLAN L13), not slab intersection. No `BUDGET` fallback
  to RAY.

## 5. Explicitly accepted approximations (do NOT read as bugs)

- DDA/HiZ are sample sweeps; thin geometry between samples can be missed
  (`RayStep_UniformDistance` is now dead code; the DDA does not use it).
- HiZ stride is in DDA *sample* units, not mip-texel units: `2^mip` samples need
  not equal `2^mip` pixels, so coarse levels are non-conservative and level
  alignment is heuristic. Accepted per PLAN L12/L15 and README L74.
- Budgets are bounded initial counts (Diffuse 24/48/64, Specular 32/64/96,
  confirmed in `DiffuseGIFeature.cs:60-62`, `SpecularGIFeature.cs:288-301`);
  same count ≠ same sample positions between backends.
- `DDA` never returns `SCREEN_TRACE_BUDGET` (code), which now matches the updated
  Assets README L66 and `CameraChecks.cs:505`; the stale `worker/docs` snapshot
  still lists `BUDGET` for DDA — that is a doc-snapshot issue, not a code bug.

## 6. Non-claims

No performance, image, or GPU-equivalence claim. The existing GPU/camera suites
pass, but their oracle reconstructs positions in world space while mirroring the
HiZ control flow, so it validates the incremental arithmetic and contracts, not
HiZ logical completeness. Human/visual and cross-backend verification remain
outside this audit.
