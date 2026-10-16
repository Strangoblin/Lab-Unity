# Lab production-tool checks

Connected-Editor regression adapter for the five independent Lab windows:

```sh
unityctl status
unityctl -t 60 script execute .agents/skills/auto-manager/project-adapters/production-tools/ProductionToolChecks.cs
```

`Script.Main()` creates temporary window instances and a unique `__ProductionValidation_<guid>` folder under the Noise tool.
The runner removes only its own fixtures in `finally`; existing windows, scene objects and Renderer settings are left alone.

The 42 checks cover all five windows' independent EditorWindow base, Load / self-Save callbacks, GUID and native object identity
on self-save, path validation, clone-on-save, transient/persistent ownership, Noise Generate replacement, 2D loaded/saved output
stability during preview refresh, three-axis pixels and noncubic dimensions, cache invalidation, and unreadable 3D input.

The adapter reflects each actual window and its private asset IO. It does not require a project-wide production-tool class.
The complete reusable source template is [production-tool-window.cs](../../templates/script/production-tool/production-tool-window.cs).

This does not physically click buttons or exercise different-asset overwrite confirmation/cancellation.
It does not run scene-dependent Interior Map Bake or assess FGD/SSS GPU visual quality.
