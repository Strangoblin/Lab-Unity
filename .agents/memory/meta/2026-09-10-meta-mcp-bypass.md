---
name: Meta bypasses Unity MCP
description: Route meta-developer and agent-system paths around unity-gate while preserving Unity business-code enforcement.
date: 2026-09-10
---

# Meta MCP Bypass

`meta-developer` and `.agents/**`, `.mcp/**`, `.claude/**`, `.codex/**` maintenance now bypass AutoAgent and `unity-gate`. Meta edits these files directly and validates them with cross-reference checks, MCP tests, and the strict architecture verifier.

The Unity gate entry now accepts only `unity-developer`; its write allowlist remains unchanged. Regression coverage confirms that meta role/path inputs are rejected while the normal two-gate Unity chain and `Assets/Mine` protections continue to pass.

No Meta recipe or self-modifying MCP path was added, avoiding a circular dependency where the gate would authorize changes to itself.
