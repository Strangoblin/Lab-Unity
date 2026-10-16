---
name: Unity CLI routing in unity-editor skill
description: Distinguish the official Unity Editor CLI from unityctl and route each workflow safely.
date: 2026-09-10
---

# Unity CLI Routing

Updated `.agents/skills/unity-editor/SKILL.md` to distinguish two command-line interfaces:

- Use `unityctl` for inspecting and controlling an already-running Editor through its same-host bridge.
- Use the official Unity Editor CLI for process-owned CI, builds, cold imports, tests, and `-executeMethod` jobs.
- Treat official `-version` as a safe probe; never open the same project in a second Editor process.

The guidance was merged into the existing skill rather than duplicated in a new reference. The skill remains at the 80-line project limit, and Claude/Codex adapters resolve to the same content.
