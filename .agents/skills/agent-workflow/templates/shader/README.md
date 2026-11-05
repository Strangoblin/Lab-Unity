# Shader Feature Templates

Shader feature families may contain mixed code artifact types (`.cs`, `.shader`, `.compute`, `.hlsl`); each family directory keeps only `README.md` as markdown — documentation templates live under `templates/standard/`.

| Feature family | Status | Entry |
|---|---|---|
| Postprocess | Available | [postprocess/README.md](postprocess/README.md) |
| Render | Available | [render/README.md](render/README.md) |
| Particle | Planned | [particle/](particle/) |
| HLSL libraries | Available | [hlsl/README.md](hlsl/README.md) |

> `hlsl/` 是横切共享库家族(对应资源 ID `library.hlsl.shared`,供各族 `#include`),不是渲染 feature;单效果私有库与其 shader 同目录,归属 render/ 复杂对形态。
