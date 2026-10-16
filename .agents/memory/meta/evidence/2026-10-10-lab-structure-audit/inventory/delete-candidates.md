# 存量删除候选（未执行）

审批时以 inventory.json 中各文件 SHA256 为基准，执行前重新核验。Hash fixture/生成物只有在归档与复跑完成后才可执行。下列每项均为现存文件；已缺失路径不在此清单。

## finder-metadata

| 路径 | 字节 | Git 状态 | 保留目标（若为副本） |
|---|---:|---|---|
| `.DS_Store` | 10244 | ignored | — |
| `.claude/.DS_Store` | 6148 | ignored | — |
| `Assets/.DS_Store` | 10244 | ignored | — |
| `Assets/Mine/Shaders/PostProcess/.DS_Store` | 6148 | ignored | — |
| `Assets/Mine/Shaders/Render/.DS_Store` | 6148 | ignored | — |

## hash-generated-harness

| 路径 | 字节 | Git 状态 | 保留目标（若为副本） |
|---|---:|---|---|
| `.codex/tmp/hash-unification/HashFunction.fault-control.hlsl` | 3431 | ignored | — |
| `.codex/tmp/hash-unification/final_float_20.spv` | 1764 | ignored | — |
| `.codex/tmp/hash-unification/final_float_35.spv` | 1764 | ignored | — |
| `.codex/tmp/hash-unification/final_float_50.spv` | 1764 | ignored | — |
| `.codex/tmp/hash-unification/final_uint_35.spv` | 1240 | ignored | — |
| `.codex/tmp/hash-unification/final_uint_50.spv` | 1240 | ignored | — |
| `.codex/tmp/hash-unification/float_20.spv` | 1216 | ignored | — |
| `.codex/tmp/hash-unification/float_35.spv` | 1216 | ignored | — |
| `.codex/tmp/hash-unification/float_50.spv` | 1216 | ignored | — |
| `.codex/tmp/hash-unification/hash_cpu` | 68488 | ignored | — |
| `.codex/tmp/hash-unification/hash_cpu.cpp` | 132153 | ignored | — |
| `.codex/tmp/hash-unification/hash_cpu_fault` | 68496 | ignored | — |
| `.codex/tmp/hash-unification/hash_cpu_fault.cpp` | 132153 | ignored | — |
| `.codex/tmp/hash-unification/hash_final_cpu` | 102640 | ignored | — |
| `.codex/tmp/hash-unification/hash_final_cpu.cpp` | 202333 | ignored | — |
| `.codex/tmp/hash-unification/hash_final_fault_FracGrid22` | 102648 | ignored | — |
| `.codex/tmp/hash-unification/hash_final_fault_FracGrid22.cpp` | 202333 | ignored | — |
| `.codex/tmp/hash-unification/hash_final_fault_Sine21` | 102648 | ignored | — |
| `.codex/tmp/hash-unification/hash_final_fault_Sine21.cpp` | 202333 | ignored | — |
| `.codex/tmp/hash-unification/hash_final_float.frag.hlsl` | 305 | ignored | — |
| `.codex/tmp/hash-unification/hash_final_uint.comp.hlsl` | 281 | ignored | — |
| `.codex/tmp/hash-unification/hash_float.frag.hlsl` | 259 | ignored | — |
| `.codex/tmp/hash-unification/hash_uint.comp.hlsl` | 285 | ignored | — |
| `.codex/tmp/hash-unification/uint_35.spv` | 1240 | ignored | — |
| `.codex/tmp/hash-unification/uint_50.spv` | 1240 | ignored | — |

## hash-isolated-project

| 路径 | 字节 | Git 状态 | 保留目标（若为副本） |
|---|---:|---|---|
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/README.md` | 1985 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/memory/2026-08-04-water-caustics-screen-independent.md` | 2993 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/memory/2026-09-17-uv-aspect-anisotropy.md` | 7442 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/memory/2026-09-20-parameter-layering.md` | 11276 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/memory/2026-09-20-rt-readback-pitfalls.md` | 6971 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/memory/2026-09-20-unityctl-tool-pitfalls.md` | 5992 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/references/README.md` | 780 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/references/csharp-dev/README.md` | 74 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/references/csharp-dev/script-structure.md` | 279 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/references/mcp-gate-usage.md` | 6345 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/references/platform/metal-notes.md` | 2549 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/references/shader/README.md` | 232 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/references/shader/postprocess/README.md` | 775 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/references/shader/postprocess/blit-fullscreen.md` | 1492 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/references/shader/postprocess/blitter-api.md` | 1224 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/references/shader/postprocess/compute-shader.md` | 3277 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/references/shader/postprocess/feature-script-structure.md` | 4966 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/references/shader/postprocess/fullscreen-structure.md` | 9686 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/references/shader/postprocess/postprocess-differences.md` | 10583 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/references/shader/postprocess/spatial-filter-budget.md` | 2244 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/references/shader/postprocess/volume-component.md` | 6115 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/references/standard/README.md` | 190 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/references/standard/compute/README.md` | 225 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/references/standard/compute/compute-shader-api.md` | 2331 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/references/standard/rendering/README.md` | 170 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/references/standard/rendering/render-graph.md` | 1330 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/references/standard/rendering/rthandle-api.md` | 1577 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/references/standard/script/README.md` | 337 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/references/standard/script/parameter-panel.md` | 4193 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/references/standard/script/script-structure.md` | 8420 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/references/standard/shader/README.md` | 354 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/references/standard/shader/hlsl-includes.md` | 2390 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/references/standard/shader/shader-structure.md` | 9031 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/references/unity6-api/README.md` | 110 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/references/urp-shader-lib/README.md` | 124 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/references/urp-shader-lib/shader-structure.md` | 208 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/scripts/roslyn/pipeline-check.cs` | 446 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/scripts/roslyn/scan-temp-objects.cs` | 851 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/scripts/roslyn/scene-organize.cs` | 3316 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/scripts/roslyn/scene-query.cs` | 753 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/templates/script/README.md` | 1225 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/templates/script/baker/README.md` | 1697 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/templates/script/baker/baker-service.cs` | 6207 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/templates/script/controller/README.md` | 1650 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/templates/script/controller/controller-processor.cs` | 5202 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/templates/script/generator/README.md` | 1891 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/templates/script/generator/generator-facade.cs` | 5096 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/templates/script/manager/README.md` | 1958 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/templates/script/manager/runtime-manager.cs` | 6710 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/templates/script/window/README.md` | 2571 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/templates/script/window/editor-window.cs` | 9389 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/templates/shader/README.md` | 744 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/templates/shader/hlsl/README.md` | 6350 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/templates/shader/hlsl/function-lib.hlsl` | 4957 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/templates/shader/particle/README.md` | 73 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/templates/shader/postprocess/README.md` | 2498 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/templates/shader/postprocess/compute-template.compute` | 1910 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/templates/shader/postprocess/fullscreen-postprocess.shader` | 3874 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/templates/shader/postprocess/spatial-filter-budget.hlsl` | 1424 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/templates/shader/postprocess/urp-renderpass.cs` | 4507 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/templates/shader/postprocess/volume-template.cs` | 1710 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/templates/shader/render/README.md` | 3434 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/templates/shader/render/direct-effect.shader` | 6138 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/templates/shader/render/effect-function.hlsl` | 4427 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/templates/shader/render/effect-shader.shader` | 6826 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/templates/standard/script/parameter-panel.cs` | 5897 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/templates/standard/shader/README.md` | 3138 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/templates/standard/shader/shader-doc-template.md` | 1195 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/agents/unity-developer/templates/standard/shader/standard-shader.shader` | 11012 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/rules/README.md` | 521 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/rules/compute-shader.md` | 1102 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/rules/csharp-renderpass.md` | 2406 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/rules/meta-architecture.md` | 4750 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/rules/shader-development.md` | 6567 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/skills/auto-manager/capabilities/script-decision.md` | 2149 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.agents/skills/auto-manager/capabilities/shader-validation.md` | 4164 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.codex/AGENTS.md` | 5528 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.codex/INTERFACE.md` | 8405 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.git/HEAD` | 21 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.git/config` | 137 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.git/description` | 73 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.git/hooks/applypatch-msg.sample` | 478 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.git/hooks/commit-msg.sample` | 896 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.git/hooks/fsmonitor-watchman.sample` | 4726 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.git/hooks/post-update.sample` | 189 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.git/hooks/pre-applypatch.sample` | 424 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.git/hooks/pre-commit.sample` | 1643 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.git/hooks/pre-merge-commit.sample` | 416 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.git/hooks/pre-push.sample` | 1374 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.git/hooks/pre-rebase.sample` | 4898 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.git/hooks/pre-receive.sample` | 544 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.git/hooks/prepare-commit-msg.sample` | 1492 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.git/hooks/push-to-checkout.sample` | 2783 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.git/hooks/update.sample` | 3650 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.git/info/exclude` | 240 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.mcp/deletes.jsonl` | 812 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.mcp/gate_center.py` | 18880 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.mcp/gates/__init__.py` | 251 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.mcp/gates/g_entry.py` | 474 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.mcp/gates/g_knowledge.py` | 3702 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.mcp/moves.jsonl` | 770 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.mcp/policy.json` | 4027 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.mcp/pyproject.toml` | 184 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.mcp/server.py` | 11606 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.mcp/telemetry.py` | 4092 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.mcp/tests/test_knowledge_paths.py` | 4214 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.mcp/tests/test_recipes.py` | 63448 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.mcp/validation/__init__.py` | 0 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.mcp/validation/atomic_io.py` | 1215 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.mcp/validation/check_norm.py` | 1730 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.mcp/validation/delete_gated.py` | 564 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.mcp/validation/deletion.py` | 7973 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.mcp/validation/friction_report.py` | 9625 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.mcp/validation/gate_cli.py` | 8994 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.mcp/validation/judgement.py` | 22020 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.mcp/validation/knowledge_paths.py` | 4953 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.mcp/validation/move_gated.py` | 568 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.mcp/validation/moving.py` | 9908 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.mcp/validation/norms.py` | 5050 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.mcp/validation/policy.py` | 4778 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.mcp/validation/project_paths.py` | 8659 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.mcp/validation/script_library.py` | 5134 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/.mcp/validation/write_gated.py` | 739 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/AGENTS.md` | 1939 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/Assets/Mine/Scripts/CamController/CamController.cs` | 10316 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/Assets/Mine/Scripts/Debug/DebugOutputFeature.md` | 1743 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/Assets/Mine/Shaders/PostProcess/SSL/SSL.md` | 4710 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/Assets/Mine/Shaders/PostProcess/SSO/SSO.shader` | 9546 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/Assets/Mine/Shaders/PostProcess/SSO/SSOFeature.cs` | 8805 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/Assets/Mine/Shaders/PostProcess/SSSM/SSSM.shader` | 7416 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/Assets/Mine/Shaders/PostProcess/SSSM/SSSMFeature.cs` | 6883 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/Assets/Mine/Shaders/Render/PBRToon/PBRToon.shader` | 19431 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/Assets/Mine/Shaders/Render/RainDrops/RainDrop.hlsl` | 4848 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/Assets/Mine/Shaders/Render/RainDrops/RainDrop.hlsl.meta` | 161 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/Assets/Mine/Shaders/Render/RainDrops/RainDrops.mat` | 1544 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/Assets/Mine/Shaders/Render/RainDrops/RainDrops.mat.meta` | 188 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/Assets/Mine/Shaders/Render/RainDrops/RainDrops.md` | 10435 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/Assets/Mine/Shaders/Render/RainDrops/RainDrops.md.meta` | 158 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/Assets/Mine/Shaders/Render/RainDrops/RainDrops.shader` | 5688 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/Assets/Mine/Shaders/Render/RainDrops/RainDrops.shader.meta` | 204 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/Assets/Mine/Special/HLSL/HashFunction.md` | 5332 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/environment-tmp/mcp-policy-strict.json` | 230 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/test_knowledge_paths.log` | 47 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-n2ha1rbk/test_recipes.log` | 4797 | ignored | — |
| `.codex/tmp/hash-unification/mcp-isolated-report.json` | 3381 | ignored | — |

## hash-stage-evidence

| 路径 | 字节 | Git 状态 | 保留目标（若为副本） |
|---|---:|---|---|
| `.codex/tmp/hash-unification/verify_mcp_isolated.py` | 5491 | ignored | — |

## legacy-exact-staging-duplicate

| 路径 | 字节 | Git 状态 | 保留目标（若为副本） |
|---|---:|---|---|
| `tmp/SSSLutBakerWindow.cs` | 8641 | ignored | Assets/Mine/Scripts/SSSLutBaker/Editor/SSSLutBakerWindow.cs |

## legacy-rebuilt-pbr-binary

| 路径 | 字节 | Git 状态 | 保留目标（若为副本） |
|---|---:|---|---|
| `tmp/pbr-btdf-check` | 37488 | ignored | tmp/pbr-btdf-check.cpp |
| `tmp/pbr-organize-check` | 37936 | ignored | tmp/pbr-organize-check.cpp |

## legacy-tmp

| 路径 | 字节 | Git 状态 | 保留目标（若为副本） |
|---|---:|---|---|
| `tmp/maskblur-darkspot-dense/disk_r16_one5-dense-reference.npy` | 3686528 | ignored | — |
| `tmp/maskblur-darkspot-dense/disk_r16_one9-dense-reference.npy` | 3686528 | ignored | — |
| `tmp/maskblur-darkspot-dense/disk_r16_two9-dense-reference.npy` | 3686528 | ignored | — |
| `tmp/maskblur-darkspot-dense/disk_r3_one9-dense-reference.npy` | 3686528 | ignored | — |
| `tmp/maskblur-darkspot-dense/disk_r80_one9-dense-reference.npy` | 3686528 | ignored | — |
| `tmp/maskblur-darkspot-dense/uniform_one9-dense-reference.npy` | 3686528 | ignored | — |

## old-crash-report

| 路径 | 字节 | Git 状态 | 保留目标（若为副本） |
|---|---:|---|---|
| `mono_crash.0.0.json` | 17650 | tracked-clean | — |
| `mono_crash.0.1.json` | 17650 | tracked-clean | — |
| `mono_crash.2ecc0826c4.0.json` | 147541 | tracked-clean | — |
| `mono_crash.8d55ac588.0.json` | 140577 | tracked-clean | — |

## old-ssc-backup

| 路径 | 字节 | Git 状态 | 保留目标（若为副本） |
|---|---:|---|---|
| `Assets/Mine/Shaders/PostProcess/SSC/.backup_v1_pre_unity6/SSC.shader` | 8239 | tracked-clean | — |
| `Assets/Mine/Shaders/PostProcess/SSC/.backup_v1_pre_unity6/SSCFeature.cs` | 6654 | tracked-clean | — |

## platform-generated-output

| 路径 | 字节 | Git 状态 | 保留目标（若为副本） |
|---|---:|---|---|
| `.codex/tests/01_smoke/run.log` | 2085 | ignored | — |
| `.codex/tests/02_readonly/run.log` | 31898 | ignored | — |
| `.codex/tests/03_write/output/HelloWorld.cs` | 113 | ignored | — |
| `.codex/tests/03_write/run.log` | 63046 | ignored | — |
| `.codex/tests/04_batch/output/Alpha.cs` | 87 | ignored | — |
| `.codex/tests/04_batch/output/Beta.cs` | 85 | ignored | — |
| `.codex/tests/04_batch/output/Gamma.cs` | 87 | ignored | — |
| `.codex/tests/04_batch/run.log` | 58469 | ignored | — |
| `.codex/tests/05_spec/output/PathUtils.cs` | 152 | ignored | — |
| `.codex/tests/05_spec/output/USAGE.md` | 94 | ignored | — |
| `.codex/tests/05_spec/run.log` | 30406 | ignored | — |
| `.codex/tests/06_architecture/output/baseline-findings.json` | 619 | ignored | — |
| `.codex/tests/06_architecture/output/migration-inventory.json` | 405865 | ignored | — |

## root-old-run-log

| 路径 | 字节 | Git 状态 | 保留目标（若为副本） |
|---|---:|---|---|
| `run.log` | 3714 | ignored | — |

## screenshot-byte-duplicate

| 路径 | 字节 | Git 状态 | 保留目标（若为副本） |
|---|---:|---|---|
| `Screenshots/ao-probe/raw_ss_base.bmp` | 1340634 | ignored | .agents/agents/unity-developer/memory/evidence/2026-09-21-diffusegi-ao-cleanup/screenshots/raw_hb_base.bmp |
| `Screenshots/ao-probe/raw_ss_base.png` | 206307 | ignored | .agents/agents/unity-developer/memory/evidence/2026-09-21-diffusegi-ao-cleanup/screenshots/raw_hb_base.png |
| `Screenshots/ao-probe/ss_baseline.png` | 220559 | ignored | .agents/agents/unity-developer/memory/evidence/2026-09-21-diffusegi-ao-cleanup/screenshots/hb_baseline.png |
| `Screenshots/screenshot_2026-07-03_15-25-53.png` | 135511 | tracked-clean | Screenshots/screenshot_2026-07-03_15-25-15.png |
| `Screenshots/screenshot_2026-07-09_18-28-39.png` | 25800 | tracked-clean | Screenshots/screenshot_2026-07-09_18-21-36.png |
| `Screenshots/screenshot_2026-07-10_11-11-02.png` | 32859 | tracked-clean | Screenshots/screenshot_2026-07-10_10-58-36.png |
| `Screenshots/screenshot_2026-07-10_14-33-25.png` | 34670 | tracked-clean | Screenshots/screenshot_2026-07-10_11-32-41.png |
| `Screenshots/screenshot_2026-07-10_14-36-38.png` | 34670 | tracked-clean | Screenshots/screenshot_2026-07-10_11-32-41.png |
| `Screenshots/screenshot_2026-07-14_11-29-18.png` | 36819 | tracked-clean | Screenshots/screenshot_2026-07-14_11-24-42.png |
| `Screenshots/screenshot_2026-07-14_15-35-25.png` | 3849 | tracked-clean | Screenshots/screenshot_2026-07-14_15-34-30.png |
| `Screenshots/screenshot_2026-07-14_15-36-28.png` | 3849 | tracked-clean | Screenshots/screenshot_2026-07-14_15-34-30.png |
| `Screenshots/screenshot_2026-07-14_15-47-48.png` | 3849 | tracked-clean | Screenshots/screenshot_2026-07-14_15-34-30.png |

## unity-default-tutorial

| 路径 | 字节 | Git 状态 | 保留目标（若为副本） |
|---|---:|---|---|
| `Assets/Readme.asset` | 1144 | tracked-clean | — |
| `Assets/Readme.asset.meta` | 182 | tracked-clean | — |
| `Assets/TutorialInfo.meta` | 172 | tracked-clean | — |
| `Assets/TutorialInfo/Icons.meta` | 193 | tracked-clean | — |
| `Assets/TutorialInfo/Icons/URP.png` | 24069 | tracked-clean | — |
| `Assets/TutorialInfo/Icons/URP.png.meta` | 3156 | tracked-clean | — |
| `Assets/TutorialInfo/Layout.wlt` | 15479 | tracked-clean | — |
| `Assets/TutorialInfo/Layout.wlt.meta` | 176 | tracked-clean | — |
| `Assets/TutorialInfo/Scripts.meta` | 193 | tracked-clean | — |
| `Assets/TutorialInfo/Scripts/Editor.meta` | 193 | tracked-clean | — |
| `Assets/TutorialInfo/Scripts/Editor/ReadmeEditor.cs` | 6677 | tracked-clean | — |
| `Assets/TutorialInfo/Scripts/Editor/ReadmeEditor.cs.meta` | 264 | tracked-clean | — |
| `Assets/TutorialInfo/Scripts/Readme.cs` | 302 | tracked-clean | — |
| `Assets/TutorialInfo/Scripts/Readme.cs.meta` | 317 | tracked-clean | — |

## weather-exact-staging-duplicate

| 路径 | 字节 | Git 状态 | 保留目标（若为副本） |
|---|---:|---|---|
| `.codex/tmp/weather-implementation/shader/WeatherAtmosphere.hlsl` | 10359 | ignored | Assets/Mine/Shaders/PostProcess/Weather/WeatherAtmosphere.hlsl |
| `.codex/tmp/weather-implementation/validation/FeatureChecks.cs` | 19086 | ignored | Assets/Mine/Shaders/PostProcess/Weather/Validation~/FeatureChecks.cs |
| `.codex/tmp/weather-implementation/validation/GraphBudgetChecks.cs` | 21858 | ignored | Assets/Mine/Shaders/PostProcess/Weather/Validation~/GraphBudgetChecks.cs |
| `.codex/tmp/weather-implementation/validation/ProfileChecks.cs` | 13911 | ignored | Assets/Mine/Shaders/PostProcess/Weather/Validation~/ProfileChecks.cs |
| `.codex/tmp/weather-implementation/validation/ShaderChecks.cs` | 30074 | ignored | Assets/Mine/Shaders/PostProcess/Weather/Validation~/ShaderChecks.cs |
| `.codex/tmp/weather-implementation/validation/graph-results.json` | 4332 | ignored | Assets/Mine/Shaders/PostProcess/Weather/Validation~/graph-results.json |
| `.codex/tmp/weather-implementation/validation/pipeline-results.json` | 64134 | ignored | Assets/Mine/Shaders/PostProcess/Weather/Validation~/pipeline-results.json |
| `.codex/tmp/weather-implementation/validation/shader-results.json` | 57470 | ignored | Assets/Mine/Shaders/PostProcess/Weather/Validation~/shader-results.json |
| `.codex/tmp/weather-implementation/validation/shader-tuned-results.json` | 57830 | ignored | Assets/Mine/Shaders/PostProcess/Weather/Validation~/shader-tuned-results.json |
| `.codex/tmp/weather-localuv/ParticleTransformPipelineChecks.cs` | 35812 | ignored | Assets/Mine/Shaders/PostProcess/Weather/Validation~/PipelineChecks.cs |
| `.codex/tmp/weather-localuv/pipeline-transform-results.json` | 70369 | ignored | Assets/Mine/Shaders/PostProcess/Weather/Validation~/pipeline-transform-results.json |
| `.codex/tmp/weather-localuv/work/Presets/Rain.asset` | 811 | ignored | Assets/Mine/Shaders/PostProcess/Weather/Presets/Rain.asset |
| `.codex/tmp/weather-localuv/work/Presets/Sand.asset` | 798 | ignored | Assets/Mine/Shaders/PostProcess/Weather/Presets/Sand.asset |
| `.codex/tmp/weather-localuv/work/Presets/Snow.asset` | 815 | ignored | Assets/Mine/Shaders/PostProcess/Weather/Presets/Snow.asset |
| `.codex/tmp/weather-localuv/work/WeatherFeature.cs` | 20581 | ignored | Assets/Mine/Shaders/PostProcess/Weather/WeatherFeature.cs |
| `.codex/tmp/weather-localuv/work/WeatherParticles.hlsl` | 8312 | ignored | Assets/Mine/Shaders/PostProcess/Weather/WeatherParticles.hlsl |
| `.codex/tmp/weather-localuv/work/WeatherProfile.cs` | 14613 | ignored | Assets/Mine/Shaders/PostProcess/Weather/WeatherProfile.cs |

