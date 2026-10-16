# AutoAgent Templates

Templates are classified by actual responsibility, not by file extension.

## Standard

- [standard/](standard/) — function-independent code skeletons + documentation templates

## Script

- [script/](script/) — responsibility families: Production Tool / Baker / Window / Generator / Manager / Controller

## Shader Features

- [shader/](shader/) — feature families; each family directory keeps only `README.md` as markdown, the rest are code template bodies

A `.cs` file belongs under `shader/` when it is a RendererFeature, RenderPass, Volume, or other shader-feature support script; an `EditorWindow` shell belongs under `script/window/`. A standalone `.hlsl` include library shared across effects belongs under `shader/hlsl/`; a private per-effect library sits next to its shader (the `shader/render/` effect-shader + effect-function pair).

## Project inputs

Resolve resource IDs with `python3 .mcp/validation/project_resources.py path <id>` and intersect the result with the user's authorized workspace before copying a template.
`<category.Script>`, `<category.Render>` and `YOUR_AUTHORIZED_*_PATH` are explicit substitution inputs, never literal output/include defaults. Asset-producing EditorWindow output uses a fixed registered path (Save and Load share one source), not a per-run free choice.
Historical source paths inside provenance comments/tables are examples from this project's past layout; use the resource interface for current addresses. Templates do not grant asset write or workspace expansion permission.
