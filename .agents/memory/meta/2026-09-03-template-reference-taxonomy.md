---
name: template-reference-taxonomy
description: Content-first organization of Unity templates and references.
metadata:
  type: architecture
---

# Template and Reference Taxonomy — 2026-09-03

## Decision

Unity knowledge assets are classified by actual responsibility rather than file extension or filename.

- `templates/standard/` contains function-independent templates.
- `templates/script/` contains responsibility families such as Baker, Generator, Manager, and Controller.
- `templates/shader/` contains shader feature families; each family may contain `.cs`, `.shader`, `.compute`, and `.md` files together.
- `references/standard/` contains cross-feature Script, Shader, Compute, and Rendering guidance.
- `references/shader/postprocess/` contains fullscreen postprocess Shader, RendererFeature / RenderPass, Volume, and screen-space Compute guidance.

## Content corrections

- `script-doc-template.md` is a postprocess Feature / RenderPass documentation template, not a generic Script template.
- `compute-template.compute` is screen/depth-oriented and belongs to postprocess, not generic Compute.
- `render-pass-template.cs`, `urp-renderpass.cs`, and `volume-template.cs` are postprocess support scripts despite being C# files.
- `shader-doc-template.md` is the only currently available function-independent documentation template.

## Migration

Mixed references were split into standard and postprocess documents. Active indexes, rules, knowledge loading, Unity agent dependencies, and Codex route summaries were updated. Old reference directories remain as deprecation stubs for compatibility; duplicate template variants remain grouped under postprocess until explicit deletion is approved.

## Verification

- Relative Markdown link audit: passed.
- Duplicate hash audit: no exact duplicate files.
- `check_norm.py`: all moved code templates passed.
- `git diff --check`: passed.
