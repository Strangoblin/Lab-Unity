"""scripts/roslyn/ 脚本库 — 固定脚本清单 + 存在性验证."""

import os
from typing import Optional

# 项目根目录（.mcp 在项目根下）
PROJECT_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SCRIPTS_DIR = os.path.join(PROJECT_ROOT, ".agents", "skills", "unity-editor", "scripts", "roslyn")

# 切分到专门 skill 的脚本仍保留旧 USE <basename> 决策兼容。
SPECIALIZED_SCRIPTS = {
    "camera-offscreen-capture.cs": os.path.join(PROJECT_ROOT, ".agents", "skills",
        "unity-render-debug", "scripts", "camera-offscreen-capture.cs"),
}

# ── 固定脚本库 ──
KNOWN_SCRIPTS: dict[str, str] = {
    "scene-query.cs":         "场景层级遍历（含组件 + active 状态）",
    "scene-organize.cs":      "测试物体分组整理到 __TestObjects__",
    "pipeline-check.cs":      "渲染管线 + Quality Level 诊断",
    "scan-temp-objects.cs":   "扫描临时物体",
    "camera-offscreen-capture.cs": "完整相机 PNG（unity-render-debug）",
}
# 注: 旧位置 tmp/.reusable/ 的 4 个脚本（query_scene / organize_scene / check_pipeline /
# query_framedebugger）曾登记在此，但 script_exists() 只查本目录，`USE <旧名>` 必然
# SCRIPT_NOT_FOUND —— 那 4 条是**读不到的死数据**，2026-10-09 移除（见 dated memory）。
# 2026-10-09 审计: 与 roslyn 重复/过期的 3 个（query_scene / organize_scene / check_pipeline）
# 已删除（走 gate_cli delete，审计在 .mcp/deletes.jsonl）；tmp/.reusable/query_framedebugger.cs
# 是 Frame Debugger 辅助脚本的唯一副本，保留。


def list_scripts() -> list[dict]:
    """列出 Editor 脚本及迁到专门 skill 的兼容入口。"""
    paths = dict(SPECIALIZED_SCRIPTS)
    if os.path.isdir(SCRIPTS_DIR):
        paths.update({name: os.path.join(SCRIPTS_DIR, name)
                      for name in os.listdir(SCRIPTS_DIR) if name.endswith(".cs")})
    return [{"name": name, "known": name in KNOWN_SCRIPTS,
             "description": KNOWN_SCRIPTS.get(name, "(未注册 — 请更新 KNOWN_SCRIPTS)"),
             "exists": True, "bytes": os.path.getsize(path)}
            for name, path in sorted(paths.items()) if os.path.isfile(path)]


def script_exists(name: str) -> bool:
    """按 basename 查询 canonical 或已登记的专门 skill 脚本。"""
    path = SPECIALIZED_SCRIPTS.get(name, os.path.join(SCRIPTS_DIR, name))
    return os.path.isfile(path)


def validate_decision(decision: str, script_name: str = "") -> dict:
    """校验 [G2] 脚本决策.

    decision 格式:
      "USE scene-query.cs"     → 使用已有脚本（必须存在）
      "CREATE reusable x.cs"   → 新建可复用脚本（合法路径）
      "CREATE tmp x.cs"        → 新建临时脚本
      "NONE"                   → 无脚本任务（纯 shader/C# 文件改动）
    """
    if not decision:
        return {"status": "DENIED", "error": "EMPTY_DECISION", "hint": "Decision 不能为空"}

    parts = decision.split(None, 2)
    action = parts[0] if len(parts) > 0 else ""
    target = parts[1] if len(parts) > 1 else ""

    if action == "USE":
        if not target:
            return {"status": "DENIED", "error": "USE_MISSING_SCRIPT",
                    "hint": "USE 需要指定脚本名。示例: USE scene-query.cs"}
        exists = script_exists(target)
        if not exists:
            available = [s["name"] for s in list_scripts()]
            return {"status": "DENIED", "error": "SCRIPT_NOT_FOUND",
                    "hint": f"脚本 '{target}' 不在 scripts/roslyn/ 中。可用: {available}"}
        return {"status": "OK", "action": "USE", "script": target,
                "description": KNOWN_SCRIPTS.get(target, ""), "mode": "static"}

    elif action == "CREATE":
        if not target:
            return {"status": "DENIED", "error": "CREATE_MISSING_TYPE",
                    "hint": "CREATE 需要指定 reusable 或 tmp。示例: CREATE reusable x.cs"}
        if target not in ("reusable", "tmp"):
            return {"status": "DENIED", "error": "CREATE_INVALID_TYPE",
                    "hint": f"CREATE 类型必须是 'reusable' 或 'tmp'，收到: '{target}'"}
        # 第三部分是脚本名
        name = parts[2] if len(parts) > 2 else ""
        if not name:
            return {"status": "DENIED", "error": "CREATE_MISSING_NAME",
                    "hint": "CREATE 需要指定脚本文件名。示例: CREATE reusable my_query.cs"}
        if not name.endswith(".cs"):
            return {"status": "DENIED", "error": "INVALID_SCRIPT_EXT",
                    "hint": "脚本文件名必须以 .cs 结尾"}
        if target == "reusable":
            path = os.path.join(SCRIPTS_DIR, name)
        else:
            path = os.path.join(PROJECT_ROOT, "tmp", name)
        # 不允许覆盖已有脚本
        if os.path.exists(path):
            return {"status": "DENIED", "error": "FILE_EXISTS",
                    "hint": f"文件已存在: {path}。请用其他名称或先确认覆盖。", "path": path}
        return {"status": "OK", "action": "CREATE", "type": target,
                "name": name, "path": path, "mode": "dynamic" if target == "tmp" else "reusable"}

    elif action == "NONE":
        # 宽容实现：允许 "NONE 附带说明"
        return {"status": "OK", "action": "NONE", "mode": "none"}

    return {"status": "DENIED", "error": "INVALID_ACTION",
            "hint": f"Decision 必须以 USE、CREATE 或 NONE 开头。收到: '{decision}'"}
