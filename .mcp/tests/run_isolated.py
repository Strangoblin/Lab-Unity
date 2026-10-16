#!/usr/bin/env python3
"""Run the MCP contract suites in a disposable project, never the live gate state."""
from __future__ import annotations

import argparse
import ast
from datetime import datetime
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import traceback
from uuid import uuid4

AUDIT_NAMES = ("state.json", "deletes.jsonl", "moves.jsonl", "friction.jsonl")
EXCLUDED_PARTS = {".venv", "__pycache__", ".pytest_cache", ".git", ".DS_Store"}
SUITES = ("test_recipes.py", "test_knowledge_paths.py", "test_directory_deletion.py", "test_project_resources.py")
FIXTURE_TREES = (
    ".agents/knowledge/rules",
    ".agents/knowledge/unity",
    ".agents/knowledge/meta",
    ".agents/skills/unity-editor/scripts/roslyn",
)
FIXTURE_FILES = (
    "AGENTS.md", ".agents/README.md", ".codex/AGENTS.md", ".codex/INTERFACE.md",
    ".agents/skills/unity-render-debug/scripts/camera-offscreen-capture.cs",
)


def project_root(configured: str | None = None) -> Path:
    root = Path(configured or os.environ.get("PROJECT_ROOT") or Path(__file__).resolve().parents[2])
    root = root.expanduser().resolve()
    if not (root / "AGENTS.md").is_file() or not (root / ".mcp/tests").is_dir():
        raise ValueError(f"Not a Unity Lab project root: {root}")
    return root


def fingerprints(root: Path) -> dict:
    result = {}
    for name in AUDIT_NAMES:
        path = root / ".mcp" / name
        result[f".mcp/{name}"] = {
            "exists": path.exists(),
            "sha256": hashlib.sha256(path.read_bytes()).hexdigest() if path.is_file() else None,
        }
    return result


def copy_fixture(root: Path, fixture: Path) -> list[str]:
    """Copy real inputs, not state, audit, environments, caches or escaping symlinks."""
    copied = []

    def copy_file(source: Path) -> None:
        resolved = source.resolve()
        if not resolved.is_relative_to(root):
            raise ValueError(f"Fixture source escapes project: {source.relative_to(root)}")
        relative = source.relative_to(root)
        target = fixture / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(resolved, target)
        copied.append(relative.as_posix())

    def source_files(directory: Path):
        for current, dirs, files in os.walk(directory, followlinks=False):
            dirs[:] = sorted(name for name in dirs if name not in EXCLUDED_PARTS)
            for name in dirs:
                if (Path(current) / name).is_symlink():
                    raise ValueError(f"Fixture directory symlink is not supported: {Path(current) / name}")
            for name in sorted(files):
                if name not in EXCLUDED_PARTS:
                    yield Path(current) / name

    for source in source_files(root / ".mcp"):
        if source.name not in AUDIT_NAMES and source.suffix in {".py", ".json", ".toml"}:
            copy_file(source)
    for relative in FIXTURE_TREES:
        for source in source_files(root / relative):
            copy_file(source)
    for relative in FIXTURE_FILES:
        copy_file(root / relative)
    # g_knowledge tests project-relative file resolution; no production Assets input is needed.
    synthetic = fixture / "Assets/Mine/Shaders/Render/PBRToon/PBRToon.shader"
    synthetic.parent.mkdir(parents=True, exist_ok=True)
    synthetic.write_text("// synthetic knowledge-resolution fixture\n", encoding="utf-8")
    for relative in ("tmp", "Assets/Mine/Scripts", "Assets/Mine/Shaders", "Assets/Mine/Effects"):
        (fixture / relative).mkdir(parents=True, exist_ok=True)
    return copied


def child_environment(fixture: Path) -> dict[str, str]:
    env = dict(os.environ)
    for name in list(env):
        if name.startswith("GIT_") or name.startswith("MCP_") or name in {
            "PYTHONPATH", "PYTHONHOME", "PYTHONSTARTUP", "PROJECT_ROOT",
        }:
            env.pop(name)
    temporary = fixture / "environment-tmp"
    empty_hooks = fixture / "empty-git-hooks"
    temporary.mkdir()
    empty_hooks.mkdir()
    env.update({
        "PYTHONDONTWRITEBYTECODE": "1", "PROJECT_ROOT": str(fixture),
        "TMPDIR": str(temporary), "TMP": str(temporary), "TEMP": str(temporary),
        "GIT_CONFIG_NOSYSTEM": "1", "GIT_CONFIG_GLOBAL": os.devnull,
        "GIT_CONFIG_COUNT": "3",
        "GIT_CONFIG_KEY_0": "commit.gpgsign", "GIT_CONFIG_VALUE_0": "false",
        "GIT_CONFIG_KEY_1": "core.hooksPath", "GIT_CONFIG_VALUE_1": str(empty_hooks),
        "GIT_CONFIG_KEY_2": "init.templateDir", "GIT_CONFIG_VALUE_2": str(empty_hooks),
    })
    return env


def run_command(command: list[str], fixture: Path, env: dict, log: Path, timeout: float) -> int:
    with log.open("w", encoding="utf-8") as stream:
        process = subprocess.run(command, cwd=fixture, env=env, stdout=stream,
                                 stderr=subprocess.STDOUT, timeout=timeout)
    return process.returncode


def run_isolated(root: Path, task: Path, interpreter: Path,
                 timeout: float, retain_on_failure: bool = False) -> tuple[int, Path]:
    run_id = "mcp-isolated-" + uuid4().hex[:12]
    output = task / "output" / run_id
    evidence = task / "evidence" / run_id
    output.mkdir(parents=True, exist_ok=False)
    evidence.mkdir(parents=True, exist_ok=False)
    report_path = output / "report.json"
    before = fingerprints(root)
    report = {
        "status": "FAIL", "project_root": str(root), "interpreter": str(interpreter),
        "task_directory": str(task), "run_id": run_id, "results": [], "real_before": before,
        "boundary": "Copied project inputs, independent Git root and TMPDIR; no production gate reset.",
    }
    fixture = None
    exit_code = 1
    try:
        if not interpreter.is_file() or not os.access(interpreter, os.X_OK):
            raise ValueError(f"Missing executable interpreter: {interpreter}; use --python (no automatic install).")
        fixture_root = (task / "fixtures").resolve()
        if not fixture_root.is_relative_to(task):
            raise ValueError("fixtures directory must remain inside this task")
        fixture_root.mkdir(parents=True, exist_ok=True)
        fixture = Path(tempfile.mkdtemp(prefix="mcp-isolated-", dir=fixture_root)).resolve()
        report["fixture"] = str(fixture)
        report["copied"] = copy_fixture(root, fixture)
        env = child_environment(fixture)
        git_log = evidence / "git-init.log"
        if run_command(["git", "init", "-q", str(fixture)], fixture, env, git_log, timeout):
            raise RuntimeError(f"Fixture git init failed; see {git_log}")
        preflight = (
            "import sys, pathlib, mcp; "
            "root=pathlib.Path.cwd(); sys.path.insert(0,str(root/'.mcp')); "
            "from validation.project_paths import PROJECT_ROOT; "
            "from validation.knowledge_paths import find_project_root; "
            "from gate_center import STATE_FILE; "
            "assert pathlib.Path(PROJECT_ROOT)==root; "
            "assert find_project_root(root/'.mcp')==root; "
            "assert pathlib.Path(STATE_FILE)==root/'.mcp/state.json'; "
            "print(sys.executable); print(mcp.__file__); print(PROJECT_ROOT)"
        )
        preflight_log = evidence / "preflight.log"
        if run_command([str(interpreter), "-c", preflight], fixture, env, preflight_log, timeout):
            raise RuntimeError(f"Interpreter/dependency/isolation preflight failed; see {preflight_log}")
        report["preflight_log"] = str(preflight_log)
        for name in SUITES:
            suite = fixture / ".mcp/tests" / name
            log = evidence / (suite.stem + ".log")
            result = run_command([str(interpreter), str(suite)], fixture, env, log, timeout)
            definitions = sum(isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef))
                              and node.name.startswith("test_") for node in ast.walk(ast.parse(suite.read_text())))
            report["results"].append({"suite": name, "exit_code": result,
                                       "test_function_definitions": definitions, "log": str(log)})
            print(f"{'PASS' if result == 0 else 'FAIL'} {name}: exit {result}; {log}")
        exit_code = 0 if all(item["exit_code"] == 0 for item in report["results"]) else 1
    except KeyboardInterrupt:
        report["error"] = "Interrupted"
        exit_code = 130
    except Exception as error:
        report["error"] = f"{type(error).__name__}: {error}"
        (evidence / "runner-error.log").write_text(traceback.format_exc(), encoding="utf-8")
        print(report["error"], file=sys.stderr)
    finally:
        after = fingerprints(root)
        unchanged = before == after
        report.update(real_after=after, real_gate_state_and_audit_unchanged=unchanged)
        if not unchanged:
            exit_code = 1
        if fixture is not None:
            retain = exit_code != 0 and retain_on_failure
            report["fixture_retained"] = retain
            if not retain:
                try:
                    # Only the unique directory returned by this run's mkdtemp is owned.
                    shutil.rmtree(fixture)
                    report["fixture_cleaned"] = True
                except OSError as error:
                    report["cleanup_error"] = str(error)
                    report["fixture_retained"] = True
                    report["fixture_cleaned"] = False
                    exit_code = 1
        report["status"] = "PASS" if exit_code == 0 else "FAIL"
        report_path.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        print(f"{'PASS' if unchanged else 'FAIL'} real .mcp state/audit fingerprints unchanged")
        print(f"Report: {report_path}")
    return exit_code, report_path


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project-root", help="Project root (default: PROJECT_ROOT or this script's repository)")
    parser.add_argument("--task-dir", help="Existing/new session directory under tmp/sessions")
    parser.add_argument("--python", dest="interpreter", help="Python with MCP installed (default: .mcp/.venv/bin/python)")
    parser.add_argument("--timeout", type=float, default=300, help="Seconds per subprocess (default: 300)")
    parser.add_argument("--retain-on-failure", action="store_true", help="Keep only this run's failed fixture for diagnosis")
    args = parser.parse_args(argv)
    try:
        root = project_root(args.project_root)
        if args.timeout <= 0:
            raise ValueError("--timeout must be positive")
        sessions = (root / "tmp/sessions").resolve()
        if not sessions.is_relative_to(root):
            raise ValueError("tmp/sessions must remain inside the project")
        sessions.mkdir(parents=True, exist_ok=True)
        if args.task_dir:
            task = Path(args.task_dir).expanduser()
            task = (root / task).resolve() if not task.is_absolute() else task.resolve()
            if not task.is_relative_to(sessions) or task == sessions:
                raise ValueError("--task-dir must be a task subdirectory of tmp/sessions")
            task.mkdir(parents=True, exist_ok=True)
        else:
            prefix = datetime.now().strftime("%Y-%m-%d") + "-mcp-tests-"
            task = Path(tempfile.mkdtemp(prefix=prefix, dir=sessions))
        configured = args.interpreter or os.environ.get("MCP_TEST_PYTHON")
        interpreter = Path(configured).expanduser().absolute() if configured else root / ".mcp/.venv/bin/python"
        return run_isolated(root, task, interpreter, args.timeout, args.retain_on_failure)[0]
    except (OSError, ValueError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
