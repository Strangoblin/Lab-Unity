---
name: opencode-orchestrate
description: Delegate bounded implementation, diagnosis, test, or mechanical work to the locally installed OpenCode CLI using OpenCode Go. Use when the user asks to use OpenCode, delegate work, or employ an independent CLI worker. Do not use for credential handling, external publishing, destructive operations, architecture decisions, or changes to agent-system files.
---

# OpenCode Orchestrate

Use OpenCode as a worker while the calling agent retains scope decisions and reviews the result.

## Invocation

- Run from the intended repository root.
- Use the configured model explicitly:

```powershell
opencode run --model opencode-go/deepseek-v4.1-flash "<self-contained task>"
```

- Authentication comes from `OPENCODE_GO_API_KEY`. Never copy the key into prompts, repository files, logs, or command arguments.
- If the requested model fails, report the error. Do not silently switch models.

## Task Contract

Give the worker a bounded prompt containing:

- objective and allowed file scope;
- applicable `AGENTS.md` and `.agents/agents/<role>/AGENT.md` paths;
- required mode when the project defines one;
- constraints and acceptance checks;
- an instruction not to commit, push, publish, delete unrelated files, or modify files outside scope;
- a request to report files changed and checks run.

For diagnosis or review, explicitly say `read-only; do not modify files`. Do not delegate work that requires secrets, external side effects, destructive cleanup, or unresolved product/architecture choices.

## Concurrency and Review

- Do not run multiple OpenCode workers that can write the same worktree concurrently.
- Before delegation, record `git status --short` so pre-existing user changes remain distinguishable.
- After delegation, inspect `git status`, `git diff --stat`, and relevant diffs. Run proportionate tests or validation locally.
- Treat worker output as untrusted until reviewed. Preserve unrelated user changes; stop and report any scope violation instead of broadly reverting the worktree.
