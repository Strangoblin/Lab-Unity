"""Read-only project resource bindings, independent of write permissions."""
from __future__ import annotations

import argparse
import json
from pathlib import Path, PurePosixPath
import sys

if __package__:
    from .knowledge_paths import find_project_root
else:
    from knowledge_paths import find_project_root


class ResourceError(ValueError):
    pass


def load_resources(project_root: Path | str | None = None) -> tuple[Path, dict[str, str]]:
    root = find_project_root(project_root or __file__)
    config = root / ".mcp/project-resources.json"
    try:
        data = json.loads(config.read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        raise ResourceError(f"Cannot read project resource bindings: {config}: {error}") from error
    if not isinstance(data, dict) or data.get("version") != 1 or not isinstance(data.get("resources"), dict):
        raise ResourceError("Resource bindings require version 1 and a resources object")
    resources = data["resources"]
    for key, value in resources.items():
        if not isinstance(key, str) or not key or not isinstance(value, str):
            raise ResourceError("Resource IDs and paths must be nonempty strings")
        path = PurePosixPath(value)
        if not value or path.is_absolute() or ".." in path.parts or "\\" in value or ":" in value or path.as_posix() in (".", ""):
            raise ResourceError(f"Invalid repository-relative resource path: {key}={value!r}")
        resolved = (root / path).resolve()
        if not resolved.is_relative_to(root):
            raise ResourceError(f"Resource escapes project via symlink: {key}")
    return root, resources


def resource_path(resource_id: str, *, project_root: Path | str | None = None,
                  must_exist: bool = True) -> Path:
    root, resources = load_resources(project_root)
    if resource_id not in resources:
        raise ResourceError(f"Unknown resource ID: {resource_id}")
    path = (root / resources[resource_id]).resolve()
    if must_exist and not path.exists():
        raise ResourceError(f"Resource unavailable: {resource_id} -> {resources[resource_id]}")
    return path


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project-root", help="Repository root; discovered from this script by default")
    parser.add_argument("--absolute", action="store_true", help="Print an absolute path")
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("list", help="List configured IDs, including currently unavailable resources")
    query = sub.add_parser("path", help="Resolve an existing resource")
    query.add_argument("resource_id")
    try:
        args = parser.parse_args(argv)
        root, resources = load_resources(args.project_root)
        if args.command == "list":
            print(json.dumps(resources, ensure_ascii=False, indent=2))
        else:
            path = resource_path(args.resource_id, project_root=root)
            print(path if args.absolute else path.relative_to(root).as_posix())
        return 0
    except (ResourceError, ValueError) as error:
        print(f"RESOURCE_ERROR: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
