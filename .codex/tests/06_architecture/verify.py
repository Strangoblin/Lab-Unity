#!/usr/bin/env python3
"""Phase 1 architecture inventory and migration-contract checks.

Default mode records the known pre-migration baseline and writes an inventory.
Strict mode is intended for later phases and fails on unresolved architecture
violations instead of accepting the baseline fixture.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
TEST_ROOT = Path(__file__).resolve().parent
OUTPUT = TEST_ROOT / "output"
TEXT_SUFFIXES = {".md", ".toml", ".json", ".py", ".sh", ".cs", ".hlsl", ".shader", ".compute"}
ARCH_ROOTS = (".agents", ".claude", ".codex", ".mcp")
PHASE2_REQUIRED = [
    ".agents/README.md",
    ".agents/agents/README.md",
    ".agents/agents/unity-developer/AGENT.md",
    ".agents/agents/meta-developer/AGENT.md",
    ".agents/rules/README.md",
    ".agents/knowledge/README.md",
    ".agents/knowledge/unity/README.md",
    ".agents/interfaces/README.md",
    ".agents/interfaces/knowledge-paths.md",
]
# Historical fixtures retained for explaining the migration delta.  The active
# default fixture tracks the current closure architecture (Phase 7 body done:
# skills copies removed, only adapter-layer tracked files remain).
PRE_MIGRATION_BASELINE = {
    "tracked_claude": 155,
    "agents_broken_markdown_links": 20,
    "skill_drift_files": 6,
    "mcp_hardcoded_claude_files": 3,
}
PHASE3_BASELINE = {
    "tracked_claude": 49,
    "agents_broken_markdown_links": 0,
    "skill_drift_files": 6,
    "mcp_hardcoded_claude_files": 3,
}
PHASE4_BASELINE = {
    "tracked_claude": 49,
    "agents_broken_markdown_links": 0,
    "skill_drift_files": 6,
    "mcp_hardcoded_claude_files": 0,
}
# Closure baseline: .claude/skills cutover (fdf9106) removed 32 tracked copies.
# 2026-09-21: 删 codex-orchestrate 兼容软链（Codex 是对等并行引擎，不是派发目标）→ 26 → 25。
PHASE7_BASELINE = {
    "tracked_claude": 25,
    "agents_broken_markdown_links": 0,
    "skill_drift_files": 0,
    "mcp_hardcoded_claude_files": 0,
}

CODEX_SHARED_LINKS = {
    ".codex/agents/unity-developer": "../../.agents/agents/unity-developer",
    ".codex/agents/meta-developer": "../../.agents/agents/meta-developer",
    ".codex/rules": "../.agents/rules",
    ".codex/skills": "../.agents/skills",
    ".codex/knowledge": "../.agents/knowledge",
    ".codex/interfaces": "../.agents/interfaces",
}

CODEX_RETIRED_PATHS = (
    ".codex/agents/auto-developer",
    ".codex/agents/exec-developer",
)


def rel(path: Path) -> str:
    return path.relative_to(ROOT).as_posix()


def tracked(prefix: str) -> list[str]:
    result = subprocess.run(
        ["git", "ls-files", prefix], cwd=ROOT, check=True, capture_output=True, text=True
    )
    return [line for line in result.stdout.splitlines() if line]


def readable_files() -> list[Path]:
    files: list[Path] = []
    for root_name in ARCH_ROOTS:
        base = ROOT / root_name
        if not base.exists():
            continue
        for path in base.rglob("*"):
            if (
                path.is_file()
                and path.suffix.lower() in TEXT_SUFFIXES
                and ".venv" not in path.parts
                and "__pycache__" not in path.parts
                and not path.name.endswith(".pyc")
                and "output" not in path.parts
                and TEST_ROOT not in path.parents
            ):
                files.append(path)
    return sorted(files)


def text_map() -> dict[Path, str]:
    return {path: path.read_text(encoding="utf-8", errors="ignore") for path in readable_files()}


def broken_markdown_links(texts: dict[Path, str], root_name: str | None = None) -> list[dict[str, object]]:
    link_re = re.compile(r"\[[^\]]*\]\(([^)]+)\)")
    broken: list[dict[str, object]] = []
    for path, text in texts.items():
        if path.suffix.lower() != ".md" or (root_name and root_name not in path.parts):
            continue
        for line_number, line in enumerate(text.splitlines(), 1):
            for target in link_re.findall(line):
                target = target.strip().split("#", 1)[0].strip("<>")
                # Examples in validation rules may intentionally contain glob or
                # regex syntax rather than a concrete Markdown link.
                if (
                    not target
                    or target.startswith(("http://", "https://", "mailto:", "/"))
                    or "*" in target
                    or target.startswith(".*")
                ):
                    continue
                candidate = path.parent / target
                # Reference documents use both file-relative links and repository-
                # relative asset links such as Assets/Mine/... .
                if not candidate.exists() and (ROOT / target).exists():
                    candidate = ROOT / target
                if not candidate.exists():
                    broken.append({"file": rel(path), "line": line_number, "target": target})
    return broken


def skill_drift(texts: dict[Path, str]) -> list[str]:
    del texts
    claude = ROOT / ".claude" / "skills"
    agents = ROOT / ".agents" / "skills"
    relative_paths: set[Path] = set()
    for base in (claude, agents):
        if base.exists():
            relative_paths |= {
                path.relative_to(base)
                for path in base.rglob("*")
                if path.is_file() and path.suffix.lower() in TEXT_SUFFIXES and not path.name.endswith(".pyc")
            }
    drift: list[str] = []
    for path in sorted(relative_paths):
        left, right = claude / path, agents / path
        if left.is_file() and right.is_file():
            if hashlib.sha256(left.read_bytes()).digest() != hashlib.sha256(right.read_bytes()).digest():
                drift.append(path.as_posix())
    return drift


def mcp_hardcodes(texts: dict[Path, str]) -> list[dict[str, object]]:
    hits: list[dict[str, object]] = []
    for path, text in texts.items():
        if ".mcp" not in path.parts or path.suffix != ".py":
            continue
        for line_number, line in enumerate(text.splitlines(), 1):
            if ".claude" in line:
                hits.append({"file": rel(path), "line": line_number, "text": line.strip()})
    return hits


def absolute_and_case_hits(texts: dict[Path, str]) -> tuple[list[dict[str, object]], list[dict[str, object]]]:
    absolute, case = [], []
    for path, text in texts.items():
        if _skip_absolute_case_scan(path):
            continue
        for line_number, line in enumerate(text.splitlines(), 1):
            if "/Users/" in line:
                absolute.append({"file": rel(path), "line": line_number, "text": line.strip()[:240]})
            if ".Codex/" in line or ".Codex\\" in line:
                case.append({"file": rel(path), "line": line_number, "text": line.strip()[:240]})
    return absolute, case


def _skip_absolute_case_scan(path: Path) -> bool:
    """Strict absolute/case scanning excludes machine-local or transient files:
    platform settings/hooks (settings.local.json is gitignored), retired Codex
    config archives, .codex/tmp/ workspace documents, and dated memory snapshots
    under .agents (historical records keep their original paths by rule).
    Active .claude/skills compat copies stay in scope so strict mode keeps
    flagging them until the Phase 7 skill symlink cutover removes them.
    """
    relp = rel(path)
    if relp.startswith((".claude/settings", ".claude/hooks/", ".codex/hooks.json", ".codex/config.toml")):
        return True
    if ".codex/tmp/" in relp:
        return True
    return re.match(r"^\d{4}-\d{2}-\d{2}", path.name) is not None and "/memory/" in relp


def semantic_category(path: str) -> tuple[str, str, str, str]:
    """Return category, proposed target, decision and rationale."""
    p = Path(path)
    if path == ".claude/CLAUDE.md":
        return "platform-entry", ".claude/CLAUDE.md", "compatibility", "Claude fixed entry path must remain available."
    if path.startswith(".claude/settings") or path.startswith(".claude/hooks/"):
        suffix = path.removeprefix(".claude/")
        return "claude-platform", path, "retain", "Claude settings and hooks are platform-specific."
    if path.startswith(".claude/rules/"):
        suffix = path.removeprefix(".claude/rules/")
        return "shared-rule", f".agents/rules/{suffix}", "migrate", "Rule semantics are client-independent."
    for role in ("unity-developer", "meta-developer"):
        prefix = f".claude/agents/{role}/"
        if path.startswith(prefix):
            suffix = path.removeprefix(prefix)
            if suffix == "":
                return "role-directory", f".agents/agents/{role}/", "migrate", "Role content belongs to the shared role layer."
            if suffix == "AGENT.md":
                return "role-definition", f".agents/agents/{role}/AGENT.md", "migrate", "Role definition is shared SSOT."
            category = suffix.split("/", 1)[0]
            return category, f".agents/agents/{role}/{suffix}", "migrate", "Role-owned knowledge or tooling follows one-to-one mapping."
    if path.startswith(".claude/skills/"):
        suffix = path.removeprefix(".claude/skills/")
        return "skill", f".agents/skills/{suffix}", "review-then-migrate", "Skill content is shared but drift must be reconciled file by file."
    if path.startswith(".agents/"):
        return "existing-shared-candidate", path, "retain-review", "Existing .agents content is a candidate target, not an automatic overwrite source."
    if path.startswith(".codex/"):
        return "codex-platform", path, "retain", "Codex configuration, hooks, tests and tmp are platform-specific."
    if path.startswith(".mcp/"):
        return "execution-gate", path, "retain-update-later", "MCP remains execution infrastructure; only its knowledge roots will change later."
    return "unknown", path, "review", "No automated ownership rule applies."


def codex_retired_path_errors() -> list[str]:
    """Prevent the retired exec/auto replacement agents from returning."""
    return [f"Retired Codex path still exists: {path}" for path in CODEX_RETIRED_PATHS if (ROOT / path).exists()]


def codex_shared_link_errors() -> list[str]:
    """Require Codex compatibility paths to remain relative links into .agents."""
    errors: list[str] = []
    for link, target in CODEX_SHARED_LINKS.items():
        path = ROOT / link
        if not path.is_symlink():
            errors.append(f"Codex shared compatibility path is not a symlink: {link}")
            continue
        actual = path.readlink().as_posix()
        if actual != target:
            errors.append(f"Codex shared link target mismatch: {link} -> {actual!r}, expected {target!r}")
        if not path.exists():
            errors.append(f"Codex shared link is broken: {link} -> {target}")
    return errors


def phase2_contract() -> list[str]:
    """Validate the shared-core scaffold without requiring the later cutover."""
    errors: list[str] = []
    errors.extend(codex_retired_path_errors())
    errors.extend(codex_shared_link_errors())
    missing = [path for path in PHASE2_REQUIRED if not (ROOT / path).is_file()]
    if missing:
        errors.append(f"Phase 2 scaffold missing: {', '.join(missing)}")

    root_entry = ROOT / "AGENTS.md"
    if root_entry.is_symlink():
        errors.append("root AGENTS.md must be a platform-neutral regular file after Phase 2")
    if root_entry.is_file():
        entry = root_entry.read_text(encoding="utf-8", errors="ignore")
        for marker in (".agents/README.md", ".agents/agents/", ".agents/rules/", ".agents/knowledge/"):
            if marker not in entry:
                errors.append(f"root AGENTS.md does not expose shared entry: {marker}")

    shared_readme = ROOT / ".agents" / "README.md"
    if shared_readme.is_file():
        text = shared_readme.read_text(encoding="utf-8", errors="ignore")
        for marker in ("ssot", "禁止", ".agents/skills/"):
            if marker.lower() not in text.lower():
                errors.append(f".agents/README.md missing ownership marker: {marker}")

    paths = ROOT / ".agents" / "interfaces" / "knowledge-paths.md"
    if paths.is_file():
        text = paths.read_text(encoding="utf-8", errors="ignore")
        for marker in ("knowledge id", "project-root", "git rev-parse", "basename"):
            if marker.lower() not in text.lower():
                errors.append(f"knowledge path contract missing marker: {marker}")
    return errors


def make_inventory(texts: dict[Path, str]) -> list[dict[str, object]]:
    all_candidates = sorted(set(tracked(".claude/") + tracked(".agents/") + tracked(".codex/")))
    references: dict[str, list[str]] = defaultdict(list)
    for source in all_candidates:
        basename = Path(source).name
        for path, text in texts.items():
            if path.as_posix().endswith(source):
                continue
            if source in text or (basename and basename in text):
                references[source].append(rel(path))
    inventory = []
    for source in all_candidates:
        category, target, decision, rationale = semantic_category(source)
        inventory.append({
            "current_path": source,
            "semantic_category": category,
            "proposed_target_path": target,
            "references": sorted(set(references[source])),
            "decision": decision,
            "rationale": rationale,
        })
    return inventory


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--strict", action="store_true", help="fail on unresolved baseline issues")
    args = parser.parse_args()

    texts = text_map()
    agents_broken = broken_markdown_links(texts, ".agents")
    drift = skill_drift(texts)
    hardcodes = mcp_hardcodes(texts)
    absolute, codex_case = absolute_and_case_hits(texts)
    inventory = make_inventory(texts)
    phase2_errors = phase2_contract()

    summary = {
        "fixture": "phase7",
        "tracked_claude": len(tracked(".claude/")),
        "tracked_agents": len(tracked(".agents/")),
        "tracked_codex": len(tracked(".codex/")),
        "inventory_entries": len(inventory),
        "agents_broken_markdown_links": len(agents_broken),
        "skill_drift_files": len(drift),
        "skill_drift_groups": len({item.split("/", 1)[0] for item in drift}),
        "mcp_hardcoded_claude_files": len({item["file"] for item in hardcodes}),
        "mcp_hardcoded_claude_hits": len(hardcodes),
        "absolute_path_hits": len(absolute),
        "codex_case_hits": len(codex_case),
        "phase2_contract_errors": len(phase2_errors),
    }

    OUTPUT.mkdir(parents=True, exist_ok=True)
    (OUTPUT / "migration-inventory.json").write_text(
        json.dumps({"summary": summary, "entries": inventory}, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    (OUTPUT / "baseline-findings.json").write_text(
        json.dumps({
            "summary": summary,
            "agents_broken_markdown_links": agents_broken,
            "skill_drift_files": drift,
            "mcp_hardcoded_claude": hardcodes,
            "absolute_path_hits": absolute,
            "codex_case_hits": codex_case,
        }, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )

    errors: list[str] = list(phase2_errors)
    if len(inventory) != len(set(item["current_path"] for item in inventory)):
        errors.append("inventory contains duplicate current_path entries")
    if len(inventory) < len(tracked(".claude/")):
        errors.append("inventory does not cover all tracked .claude files")

    if args.strict:
        if agents_broken:
            errors.append(f".agents has {len(agents_broken)} broken markdown links")
        if drift:
            errors.append(f"skills have {len(drift)} drifted files")
        if hardcodes:
            errors.append(f".mcp has {len(hardcodes)} .claude hardcoded references")
        if absolute:
            errors.append(f"architecture docs contain {len(absolute)} absolute path hits")
        if codex_case:
            errors.append(f"architecture docs contain {len(codex_case)} .Codex case hits")
    else:
        for key, expected in PHASE7_BASELINE.items():
            if summary[key] != expected:
                errors.append(f"Phase 7 fixture metric {key} changed: expected {expected}, got {summary[key]}")

    print(json.dumps(summary, ensure_ascii=False, indent=2))
    print(f"inventory={OUTPUT / 'migration-inventory.json'}")
    print(f"findings={OUTPUT / 'baseline-findings.json'}")
    if errors:
        print("FAIL:")
        for error in errors:
            print(f"- {error}")
        return 1
    print("PASS: Phase 7 closure fixture/contract checks + Phase 2 scaffold contract")
    return 0


if __name__ == "__main__":
    sys.exit(main())
