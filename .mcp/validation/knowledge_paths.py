"""Canonical project and knowledge-path resolution for MCP gates.

The resolver deliberately keeps basename lookup as a compatibility convenience only.
Gate callers should prefer stable IDs such as
``unity/standard/shader/shader-structure.md`` or ``rules/meta-architecture.md``.
"""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path, PurePosixPath
import os
import subprocess
from collections.abc import Sequence


class KnowledgePathError(ValueError):
    """Base error with a stable machine-readable code."""

    code = "KNOWLEDGE_PATH_ERROR"


class MissingKnowledgePath(KnowledgePathError):
    code = "MISSING"


class AmbiguousKnowledgePath(KnowledgePathError):
    code = "AMBIGUOUS"


@dataclass(frozen=True)
class KnowledgeResolution:
    project_root: Path
    knowledge_id: str
    canonical_relative_path: str
    resolved_path: Path

    def as_dict(self) -> dict[str, str]:
        return {
            "project_root": self.project_root.as_posix(),
            "knowledge_id": self.knowledge_id,
            "canonical_relative_path": self.canonical_relative_path,
            "resolved_path": self.resolved_path.as_posix(),
        }


def _valid_root(path: Path) -> bool:
    return (path / ".agents").is_dir() and (path / "AGENTS.md").is_file()


def find_project_root(start: str | os.PathLike[str] | None = None) -> Path:
    """Find the repository root without depending on a user-specific absolute path."""
    origin = Path(start or __file__).expanduser().resolve()
    if origin.is_file():
        origin = origin.parent

    for candidate in (origin, *origin.parents):
        if _valid_root(candidate):
            return candidate

    try:
        result = subprocess.run(
            ["git", "rev-parse", "--show-toplevel"],
            cwd=origin,
            check=True,
            capture_output=True,
            text=True,
        )
        candidate = Path(result.stdout.strip()).resolve()
        if _valid_root(candidate):
            return candidate
    except (OSError, subprocess.CalledProcessError):
        pass

    configured = os.environ.get("PROJECT_ROOT", "").strip()
    if configured:
        candidate = Path(configured).expanduser().resolve()
        if _valid_root(candidate):
            return candidate

    raise KnowledgePathError(f"无法确定项目根目录: start={origin}")


def canonical_relative(value: str) -> str:
    """Normalize a repository-relative path and reject traversal/absolute paths."""
    raw = value.strip().replace("\\", "/")
    if not raw or raw.startswith("/") or "://" in raw:
        raise KnowledgePathError(f"不是仓库相对路径: {value!r}")
    parts = [part for part in PurePosixPath(raw).parts if part not in ("", ".")]
    if not parts or ".." in parts:
        raise KnowledgePathError(f"路径越界或为空: {value!r}")
    return "/".join(parts)



def _index(kb_roots: Sequence[tuple[str, Path]], project_root: Path) -> dict[str, list[Path]]:
    result: dict[str, list[Path]] = {}
    for domain, root in kb_roots:
        if not root.is_dir():
            continue
        for path in sorted(root.rglob("*.md")):
            rel = path.relative_to(root).as_posix()
            key = f"{domain}/{rel}"
            result.setdefault(key, []).append(path)
    return result


def resolve_entry(
    entry: str,
    project_root: Path,
    kb_roots: Sequence[tuple[str, Path]],
) -> KnowledgeResolution:
    """Resolve a stable ID, canonical path, project path, or unique basename."""
    normalized = canonical_relative(entry)
    index = _index(kb_roots, project_root)

    candidates: list[Path] = []
    knowledge_id = ""
    if normalized in index:
        knowledge_id = normalized
        candidates = index[normalized]
    elif "/" not in normalized:
        for key, paths in index.items():
            if Path(key).name == normalized:
                candidates.extend(paths)
        if len(candidates) == 1:
            knowledge_id = next(key for key, paths in index.items() if paths == candidates)
    else:
        project_path = (project_root / normalized).resolve()
        if project_path.is_file() and project_root in project_path.parents:
            candidates = [project_path]
            knowledge_id = f"project/{normalized}"

    if len(candidates) > 1:
        raise AmbiguousKnowledgePath(f"知识路径不唯一: {entry!r} -> {[p.as_posix() for p in candidates]}")
    if not candidates:
        raise MissingKnowledgePath(f"找不到知识文件: {entry!r}")

    resolved = candidates[0].resolve()
    canonical = resolved.relative_to(project_root).as_posix()
    return KnowledgeResolution(project_root, knowledge_id, canonical, resolved)


def default_kb_roots(project_root: Path) -> list[tuple[str, Path]]:
    return [
        ("unity", project_root / ".agents" / "knowledge" / "unity"),
        ("meta", project_root / ".agents" / "knowledge" / "meta"),
        ("rules", project_root / ".agents" / "knowledge" / "rules"),
    ]
