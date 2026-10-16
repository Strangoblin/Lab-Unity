#!/usr/bin/env python3
"""Shared-library ownership, plain platform routing and architecture-source checks."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
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
# Generated workspaces may contain copies named .agents/.mcp/skills.  They are
# fixtures, not additional architecture layers.  Keep the tracked legacy routing
# README visible while excluding all other contents of its old workspace.
LEGACY_TMP_README = ".codex/tmp/README.md"
GENERATED_PREFIXES = (".codex/tmp/", ".mcp/tmp/")
CACHE_DIRECTORIES = {".venv", "__pycache__", ".pytest_cache", "node_modules"}
PHASE2_REQUIRED = [
    ".agents/README.md",
    ".agents/agents/README.md",
    ".agents/agents/unity-developer/AGENT.md",
    ".agents/agents/meta-developer/AGENT.md",
    ".agents/knowledge/rules/README.md",
    ".agents/knowledge/README.md",
    ".agents/knowledge/unity/README.md",
    ".agents/knowledge/meta/README.md",
    ".agents/knowledge/meta/interfaces/README.md",
    ".agents/knowledge/meta/interfaces/knowledge-paths.md",
    ".agents/memory/README.md",
    ".agents/skills/README.md",
]
# File counts vary with Git staging; the active contract checks behavior and layout.
ACTIVE_BASELINE = {
    "agents_broken_markdown_links": 0,
    "skill_drift_files": 0,
    "mcp_hardcoded_claude_files": 0,
    "platform_hook_drift": 0,
}

CODEX_RETIRED_PATHS = (
    ".codex/agents/auto-developer",
    ".codex/agents/exec-developer",
)

def historical_record(path: Path) -> bool:
    """Only dated records/evidence in explicit memory libraries are historical."""
    relative = rel(path)
    return (re.match(r"^\.agents/memory/(meta|unity)/\d{4}-\d{2}-\d{2}[^/]*\.md$", relative) is not None
            or re.match(r"^\.agents/memory/(meta|unity)/evidence/\d{4}-\d{2}-\d{2}(?:-|/|$)", relative) is not None)


def architecture_symlink_errors() -> list[str]:
    """Shared and platform sources use plain routes, never architecture symlinks."""
    errors: list[str] = []
    def source_scope(path: Path) -> bool:
        # Negative-control text is excluded from content scans, but the checker
        # itself is still an architecture source and must not acquire symlinks.
        if path == TEST_ROOT or TEST_ROOT in path.parents:
            parts = path.relative_to(TEST_ROOT).parts
            return not any(part in CACHE_DIRECTORIES for part in parts) and parts[:1] != ("output",)
        return in_source_scope(path)
    for root_name in ARCH_ROOTS:
        base = ROOT / root_name
        if base.is_symlink():
            errors.append(f"architecture symlink is forbidden: {root_name}")
            continue
        if not base.exists():
            continue
        for directory, dirs, names in os.walk(base, followlinks=False):
            current = Path(directory)
            dirs[:] = [name for name in dirs if source_scope(current / name)
                       and not historical_record(current / name)]
            for name in dirs + names:
                path = current / name
                if source_scope(path) and not historical_record(path) and path.is_symlink():
                    errors.append(f"architecture symlink is forbidden: {rel(path)}")
    return errors


def agent_ownership_errors() -> list[str]:
    """Role folders contain only definitions; libraries own facts, memory and tools."""
    errors: list[str] = []
    base = ROOT / ".agents/agents"
    for directory, dirs, names in os.walk(base, followlinks=False):
        current = Path(directory)
        parts = current.relative_to(base).parts
        for name in dirs + names:
            path = current / name
            allowed = ((not parts and (name == "README.md" or path.is_dir()))
                       or (len(parts) == 1 and name == "AGENT.md" and path.is_file()))
            if path.is_symlink() or not allowed:
                errors.append(f"role definitions must not contain library resources: {rel(path)}")
    return errors


def platform_route_errors() -> list[str]:
    """Platform entry files route straight to the shared role inventory."""
    try:
        contract = json.loads((ROOT / ".codex/tests/06_architecture/platform-whitelist.json").read_text())
        routes = contract["platform_routes"]
    except (OSError, ValueError, KeyError) as error:
        return [f"platform routing contract unavailable: {error}"]
    errors: list[str] = []
    inventory = ROOT / ".agents/agents/README.md"
    for relative in routes:
        path = ROOT / relative
        if not path.is_file() or path.is_symlink():
            errors.append(f"plain platform route missing: {relative}")
            continue
        text = path.read_text(encoding="utf-8")
        if ".agents/agents/README.md" not in text or not inventory.is_file():
            errors.append(f"platform route must expose the shared role inventory: {relative}")
        for error in broken_markdown_links({path: text}):
            errors.append(f"platform route has broken link: {error}")
    for platform, folders in ((".claude", ("agents", "rules", "skills")),
                              (".codex", ("agents", "rules", "skills", "knowledge", "interfaces"))):
        for folder in folders:
            directory = ROOT / platform / folder
            if directory.is_dir() and not directory.is_symlink():
                for path in directory.iterdir():
                    if platform == ".codex" and folder == "agents" and path.name in {
                        "meta-developer.toml", "unity-developer.toml"
                    } and path.is_file() and not path.is_symlink():
                        text = path.read_text(encoding="utf-8")
                        role = path.stem
                        if (len(text.splitlines()) > 12 or ".agents/agents/README.md" not in text
                                or f".agents/agents/{role}/AGENT.md" not in text):
                            errors.append(f"runtime role config must remain a thin inventory route: {rel(path)}")
                        continue
                    if path.name != "README.md" or not path.is_file() or path.is_symlink():
                        errors.append(f"platform discovery directory may contain only a plain README: {rel(path)}")
    return errors


def rel(path: Path) -> str:
    return path.relative_to(ROOT).as_posix()


def tracked(prefix: str) -> list[str]:
    result = subprocess.run(
        ["git", "ls-files", prefix], cwd=ROOT, check=True, capture_output=True, text=True
    )
    return [line for line in result.stdout.splitlines() if line]


def architecture_root(path: Path) -> str | None:
    """Classify only the first component relative to this repository root."""
    try:
        parts = path.relative_to(ROOT).parts
    except ValueError:
        return None
    return parts[0] if parts and parts[0] in ARCH_ROOTS else None


def in_source_scope(path: Path) -> bool:
    """Keep architecture sources; exclude agreed transient/cache/output paths."""
    if architecture_root(path) is None:
        return False
    relp = rel(path)
    parts = path.relative_to(ROOT).parts
    if any(part in CACHE_DIRECTORIES for part in parts[1:]):
        return False
    if relp != LEGACY_TMP_README and relp.startswith(GENERATED_PREFIXES):
        return False
    if relp == ".mcp/tmp":
        return False
    if len(parts) >= 4 and parts[:2] == (".codex", "tests"):
        if parts[3] == "output" or parts[-1] == "run.log":
            return False
    # This checker's intentional negative-control strings are not project docs.
    if path == TEST_ROOT or TEST_ROOT in path.parents:
        return False
    return not path.name.endswith(".pyc")


def readable_files() -> list[Path]:
    files: list[Path] = []
    for root_name in ARCH_ROOTS:
        base = ROOT / root_name
        if not base.exists() or base.is_symlink():
            continue
        # Prune caches before traversal; do not follow directory symlinks into
        # another layer (all architecture links are rejected independently).
        for directory, dirs, names in os.walk(base, followlinks=False):
            dirs[:] = [
                name for name in dirs
                if in_source_scope(Path(directory) / name)
            ]
            for name in names:
                path = Path(directory) / name
                if path.is_file() and not path.is_symlink() and path.suffix.lower() in TEXT_SUFFIXES and in_source_scope(path):
                    files.append(path)
    return sorted(files)


def text_map() -> dict[Path, str]:
    return {path: path.read_text(encoding="utf-8", errors="ignore") for path in readable_files()}


HISTORICAL_MEMORY_ORIGINS = {
    ".agents/memory/meta": ".agents/agents/meta-developer/memory",
    ".agents/memory/unity": ".agents/agents/unity-developer/memory",
}
HISTORICAL_RELOCATIONS = {
    ".agents/agents/unity-developer/scripts/roslyn/camera-offscreen-capture.cs": ".agents/skills/unity-render-debug/scripts/camera-offscreen-capture.cs",
    ".agents/agents/unity-developer/scripts/roslyn": ".agents/skills/unity-editor/scripts/roslyn",
    ".agents/agents/unity-developer/scripts/validation/hash": ".agents/skills/unity-feature-reuse/project-adapters/hash",
    ".agents/agents/meta-developer/scripts": ".agents/skills/agent-maintenance/scripts",
    ".agents/agents/unity-developer/templates": ".agents/skills/auto-manager/templates",
    ".agents/agents/unity-developer/cli": ".agents/skills/unity-editor/references/cli",
    ".agents/agents/unity-developer/references": ".agents/knowledge/unity",
    ".agents/agents/meta-developer/references": ".agents/knowledge/meta",
    ".agents/agents/unity-developer/memory": ".agents/memory/unity",
    ".agents/agents/meta-developer/memory": ".agents/memory/meta",
    ".agents/interfaces": ".agents/knowledge/meta/interfaces",
    ".agents/rules": ".agents/knowledge/rules",
}


def historical_content_origin(path: Path) -> Path:
    relative = rel(path)
    for canonical, original in HISTORICAL_MEMORY_ORIGINS.items():
        if relative.startswith(canonical + "/"):
            return ROOT / (original + relative[len(canonical):])
    return path


def relocated_historical_path(path: Path) -> Path:
    normalized = Path(os.path.abspath(path))
    try:
        relative = normalized.relative_to(ROOT).as_posix()
    except ValueError:
        return normalized
    if relative.startswith(".claude/agents/"):
        relative = relative.replace(".claude/agents/", ".agents/agents/", 1)
    for original, canonical in HISTORICAL_RELOCATIONS.items():
        if relative == original or relative.startswith(original + "/"):
            return ROOT / (canonical + relative[len(original):])
    return ROOT / relative


def broken_markdown_links(texts: dict[Path, str], root_name: str | None = None) -> list[dict[str, object]]:
    link_re = re.compile(r"\[[^\]]*\]\(([^)]+)\)")
    broken: list[dict[str, object]] = []
    for path, text in texts.items():
        if path.suffix.lower() != ".md" or (root_name and architecture_root(path) != root_name):
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
                if historical_record(path):
                    origin = historical_content_origin(path)
                    candidate = relocated_historical_path(origin.parent / target)
                else:
                    candidate = path.parent / target
                # Reference documents use both file-relative links and repository-
                # relative asset links such as Assets/Mine/... .
                fallback = relocated_historical_path(ROOT / target) if historical_record(path) else ROOT / target
                if not candidate.exists() and fallback.exists():
                    candidate = fallback
                if not candidate.exists() and not historical_record(path):
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
                if path.is_file() and path.suffix.lower() in TEXT_SUFFIXES and in_source_scope(path)
            }
    drift: list[str] = []
    for path in sorted(relative_paths):
        left, right = claude / path, agents / path
        if left.is_file() and right.is_file():
            if hashlib.sha256(left.read_bytes()).digest() != hashlib.sha256(right.read_bytes()).digest():
                drift.append(path.as_posix())
    return drift


PLATFORM_HOOKS = (".claude/hooks/guard-bash.sh", ".codex/hooks/guard-bash.sh")


def platform_hook_drift() -> list[str]:
    """两平台的 Bash guard 必须逐字一致 —— 否则红线会静默分叉。

    与 skill_drift 同类：同一份规则在两个平台各存一份，靠人记着同步必然漂移
    （2026-09-21 的 codex-opencode-go skill 就是这么漂的）。改一侧必须同步另一侧，
    规则见 .agents/knowledge/rules/meta-architecture.md §链路保障。
    """
    paths = [ROOT / name for name in PLATFORM_HOOKS]
    existing = [path for path in paths if path.is_file()]
    if len(existing) < 2:
        return []
    digests = {hashlib.sha256(path.read_bytes()).hexdigest() for path in existing}
    return list(PLATFORM_HOOKS) if len(digests) > 1 else []


def mcp_hardcodes(texts: dict[Path, str]) -> list[dict[str, object]]:
    hits: list[dict[str, object]] = []
    for path, text in texts.items():
        if architecture_root(path) != ".mcp" or path.suffix != ".py":
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
    """Machine-local adapters and dated historical records retain their original paths."""
    relative = rel(path)
    return (relative.startswith((".claude/settings", ".claude/hooks/", ".codex/hooks.json", ".codex/config.toml"))
            or historical_record(path))


def semantic_category(path: str) -> tuple[str, str, str, str]:
    """Return category, proposed target, decision and rationale."""
    if path == ".claude/CLAUDE.md":
        return "platform-entry", ".claude/CLAUDE.md", "compatibility", "Claude fixed entry path must remain available."
    if path.startswith(".claude/settings") or path.startswith(".claude/hooks/"):
        suffix = path.removeprefix(".claude/")
        return "claude-platform", path, "retain", "Claude settings and hooks are platform-specific."
    if path.startswith((".claude/agents/", ".codex/agents/")) and path.endswith("README.md"):
        return "platform-route", path, "retain", "Plain entry points to the shared role inventory."
    if path.startswith(".agents/"):
        return "existing-shared-candidate", path, "retain-review", "Existing .agents content is a candidate target, not an automatic overwrite source."
    if path.startswith(".codex/"):
        return "codex-platform", path, "retain", "Codex configuration, hooks, tests and legacy routing documents are platform-specific."
    if path.startswith(".mcp/"):
        return "execution-gate", path, "retain-update-later", "MCP remains execution infrastructure; only its knowledge roots will change later."
    return "unknown", path, "review", "No automated ownership rule applies."


def codex_retired_path_errors() -> list[str]:
    """Prevent the retired exec/auto replacement agents from returning."""
    return [f"Retired Codex path still exists: {path}" for path in CODEX_RETIRED_PATHS if (ROOT / path).exists()]



def phase2_contract() -> list[str]:
    """Validate library indexes, current knowledge fields and plain platform routes."""
    errors: list[str] = []
    errors.extend(codex_retired_path_errors())
    errors.extend(platform_route_errors())
    missing = [path for path in PHASE2_REQUIRED if not (ROOT / path).is_file()]
    if missing:
        errors.append(f"shared library entry missing: {', '.join(missing)}")

    root_entry = ROOT / "AGENTS.md"
    if root_entry.is_symlink():
        errors.append("root AGENTS.md must be a platform-neutral regular file")
    if root_entry.is_file():
        entry = root_entry.read_text(encoding="utf-8", errors="ignore")
        for marker in (".agents/agents/README.md",):
            if marker not in entry:
                errors.append(f"root AGENTS.md does not expose shared entry: {marker}")

    shared_readme = ROOT / ".agents" / "README.md"
    if shared_readme.is_file():
        text = shared_readme.read_text(encoding="utf-8", errors="ignore")
        if "agents/README.md" not in text:
            errors.append(".agents/README.md must route to the role inventory")
        for broken in broken_markdown_links({shared_readme: text}):
            errors.append(f"shared entry has broken link: {broken}")

    paths = ROOT / ".agents" / "knowledge" / "meta" / "interfaces" / "knowledge-paths.md"
    if paths.is_file():
        text = paths.read_text(encoding="utf-8", errors="ignore")
        for marker in ("knowledge_id", "project_root", "canonical_relative_path", "resolved_path", "basename",
                       ".agents/knowledge/meta/", ".agents/knowledge/unity/", ".agents/knowledge/rules/"):
            if marker.lower() not in text.lower():
                errors.append(f"knowledge path contract missing marker: {marker}")
    return errors


def make_inventory(texts: dict[Path, str]) -> list[dict[str, object]]:
    all_candidates = sorted({
        path for path in tracked(".claude/") + tracked(".agents/") + tracked(".codex/")
        if in_source_scope(ROOT / path)
    })
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
    parser.add_argument("--output-dir", type=Path, help="report directory (default: this suite's legacy output directory)")
    args = parser.parse_args()
    output = args.output_dir.resolve() if args.output_dir else OUTPUT

    texts = text_map()
    agents_broken = broken_markdown_links(texts, ".agents")
    drift = skill_drift(texts)
    hook_drift = platform_hook_drift()
    hardcodes = mcp_hardcodes(texts)
    absolute, codex_case = absolute_and_case_hits(texts)
    inventory = make_inventory(texts)
    phase2_errors = phase2_contract()
    ownership_errors = agent_ownership_errors()
    symlink_errors = architecture_symlink_errors()

    summary = {
        "fixture": "plain-routes",
        "tracked_claude": len(tracked(".claude/")),
        "tracked_agents": len(tracked(".agents/")),
        "tracked_codex": len(tracked(".codex/")),
        "inventory_entries": len(inventory),
        "agents_broken_markdown_links": len(agents_broken),
        "skill_drift_files": len(drift),
        "skill_drift_groups": len({item.split("/", 1)[0] for item in drift}),
        "mcp_hardcoded_claude_files": len({item["file"] for item in hardcodes}),
        "mcp_hardcoded_claude_hits": len(hardcodes),
        "platform_hook_drift": len(hook_drift),
        "absolute_path_hits": len(absolute),
        "codex_case_hits": len(codex_case),
        "phase2_contract_errors": len(phase2_errors),
        "agent_ownership_errors": len(ownership_errors),
        "architecture_symlink_errors": len(symlink_errors),
    }

    output.mkdir(parents=True, exist_ok=True)
    (output / "migration-inventory.json").write_text(
        json.dumps({"summary": summary, "entries": inventory}, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    (output / "baseline-findings.json").write_text(
        json.dumps({
            "summary": summary,
            "agents_broken_markdown_links": agents_broken,
            "skill_drift_files": drift,
            "platform_hook_drift": hook_drift,
            "mcp_hardcoded_claude": hardcodes,
            "absolute_path_hits": absolute,
            "codex_case_hits": codex_case,
            "agent_ownership_errors": ownership_errors,
            "architecture_symlink_errors": symlink_errors,
        }, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )

    errors: list[str] = list(phase2_errors) + ownership_errors + symlink_errors
    if len(inventory) != len(set(item["current_path"] for item in inventory)):
        errors.append("inventory contains duplicate current_path entries")
    if not {path for path in tracked(".claude/") if in_source_scope(ROOT / path)} <= {
        item["current_path"] for item in inventory
    }:
        errors.append("inventory does not cover all tracked .claude source files")

    if args.strict:
        if agents_broken:
            errors.append(f".agents has {len(agents_broken)} broken markdown links")
        if drift:
            errors.append(f"skills have {len(drift)} drifted files")
        if hook_drift:
            errors.append(f"platform bash guards drifted apart: {hook_drift}")
        if hardcodes:
            errors.append(f".mcp has {len(hardcodes)} .claude hardcoded references")
        if absolute:
            errors.append(f"architecture docs contain {len(absolute)} absolute path hits")
        if codex_case:
            errors.append(f"architecture docs contain {len(codex_case)} .Codex case hits")
    else:
        for key, expected in ACTIVE_BASELINE.items():
            if summary[key] != expected:
                errors.append(f"architecture metric {key} changed: expected {expected}, got {summary[key]}")

    print(json.dumps(summary, ensure_ascii=False, indent=2))
    print(f"inventory={output / 'migration-inventory.json'}")
    print(f"findings={output / 'baseline-findings.json'}")
    if errors:
        print("FAIL:")
        for error in errors:
            print(f"- {error}")
        return 1
    print("PASS: architecture scaffold, ownership and platform contracts")
    return 0


if __name__ == "__main__":
    sys.exit(main())
