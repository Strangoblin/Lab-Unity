"""Profile — 业务线声明：这次任务**该读什么、该验什么**。

v3 的 recipe 是六个模式（Production/Research/Experiment/Debug/Minimal/Quick），
但它们的门禁链完全相同（`[g_entry, g_knowledge]`），只影响审计字段 —— 一个不产生任何
差异的声明，就是纯税。更糟的是 `g_knowledge` 对**所有**任务都强制申报同两份结构规范：
写一个 `.md` 也要申报 shader 结构，于是申报退化成形式，编造与漏读同时发生。

v4 的 profile 回答的是另一组问题，差异真实存在：

  shader / compute  必读结构规范；出口须有 Unity 编译证据 + 渲染验证
  script            必读脚本结构规范；出口须有编译证据
  benchmark         必读渲染基准契约；出口须有基准报告，且晚于最后一次改动
  tooling           必读脚本结构规范；出口须有编译证据
  meta              体系任务，不经资产沙箱，出口只要求索引更新
  research          只读调研，出口只要求结论落进记忆

**完成义务**（obligations）是数据，可被 profile 覆盖；收尾校验逐项判定，
缺失项在 `policy.json` 里可降为 warn 或 off —— 信号仍在，阻断与否是策略问题。

`knowledge` 排在第一项是有意的：入口管的是「该读的读了没有」，出口就该能核查它。
只声明不核查的必读项，等于回到 v3 的「申报即通过」。
"""

from __future__ import annotations

# 知识 ID 一律用稳定形式 `unity/<相对知识根路径>`，与 g_knowledge 时期的约定一致。
_SHADER_CORE = "unity/standard/shader/shader-structure.md"
_SCRIPT_CORE = "unity/standard/script/script-structure.md"

PROFILES: dict[str, dict] = {
    "shader": {
        "summary": "Shader / HLSL 材质与后处理实现",
        "required": [_SHADER_CORE, "unity/standard/shader/hlsl-includes.md"],
        "recommended": [
            "unity/shader/postprocess/fullscreen-structure.md",
            "unity/shader/postprocess/blit-fullscreen.md",
            "unity/shader/postprocess/postprocess-differences.md",
            "unity/standard/rendering/render-graph.md",
        ],
        "skills": ["unity-editor", "unity-render-debug", "unity-feature-reuse"],
        "obligations": ["knowledge", "norm", "compile", "verify", "memory"],
    },
    "compute": {
        "summary": "Compute Shader / GPU 计算",
        "required": [_SHADER_CORE, "unity/standard/compute/compute-shader-api.md"],
        "recommended": [
            "unity/shader/postprocess/compute-shader.md",
            "unity/standard/rendering/rthandle-api.md",
        ],
        "skills": ["unity-editor", "unity-render-debug"],
        "obligations": ["knowledge", "norm", "compile", "verify", "memory"],
    },
    "script": {
        "summary": "C# 运行时脚本、MonoBehaviour、组件",
        "required": [_SCRIPT_CORE],
        "recommended": [
            "unity/standard/script/parameter-panel.md",
            "unity/standard/rendering/render-graph.md",
        ],
        "skills": ["unity-editor"],
        "obligations": ["knowledge", "norm", "compile", "memory"],
    },
    "tooling": {
        "summary": "Editor 工具、窗口、烘焙器、生产工具",
        "required": [_SCRIPT_CORE],
        "recommended": ["unity/standard/script/parameter-panel.md"],
        "skills": ["unity-editor", "unityctl-plugins"],
        "obligations": ["knowledge", "norm", "compile", "memory"],
    },
    "benchmark": {
        "summary": "渲染耗时采集与 A/B 判读",
        "required": ["unity/standard/rendering/render-benchmark.md"],
        "recommended": ["unity/platform/metal-notes.md"],
        "skills": ["unity-render-benchmark", "unity-editor"],
        "obligations": ["knowledge", "compile", "verify", "memory"],
    },
    "meta": {
        "summary": "体系维护：角色、技能、知识、门禁与平台接入",
        "required": [],
        "recommended": [
            "meta/architecture.md",
            "meta/enforcement-policy.md",
            "meta/harness-patterns.md",
        ],
        "skills": ["agent-maintenance"],
        "obligations": ["memory"],
    },
    "research": {
        "summary": "只读调研与方案判断，不实施改动",
        "required": [],
        "recommended": [],
        "skills": [],
        "obligations": [],
    },
}

# 每种义务的人类可读名与判定方式（判定实现见 close.py，这里是给人和 agent 看的契约）
OBLIGATIONS: dict[str, str] = {
    "knowledge": "必读知识被真实读取（PostToolUse 观测，非申报）",
    "norm": "本次所有写入无结构规范 / 落点错误",
    "compile": "最后一次写入之后，Unity 编译日志无 error",
    "verify": "存在晚于最后一次写入的验证产物（截图 / 基准 / 复跑报告）",
    "memory": "有实质改动时，dated 记录与索引已更新",
}


def resolve(name: str) -> dict | None:
    return PROFILES.get(name)


def names() -> list[str]:
    return list(PROFILES.keys())


def playbook(profile: str) -> dict:
    """`open` 返回给 agent 的指引：读什么、用什么、验什么。"""
    spec = PROFILES.get(profile)
    if not spec:
        return {}
    return {
        "profile": profile,
        "summary": spec["summary"],
        "required_knowledge": spec["required"],
        "recommended_knowledge": spec["recommended"],
        "skills": spec["skills"],
        "obligations": [{"id": o, "meaning": OBLIGATIONS.get(o, o)}
                        for o in spec["obligations"]],
    }
