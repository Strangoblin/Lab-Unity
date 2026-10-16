# Scene Gizmo — unified scene-view visualization framework

> 脚本结构规范见 [script-structure](../../../../.agents/knowledge/unity/standard/script/script-structure.md)

**Path:** `Assets/Mine/Scripts/SceneGizmo/`
**Type:** Static dispatch + shared canvas + member set (runtime) with a manager window (Editor)
**Runs in:** Editor only for drawing; declaration types compile in players unchanged

## Overview

Every scene visualization in the project used to own its trigger, its literal colors, its color/matrix
save-restore, and its own arrow, ring and box line loops. Grouping (which systems can be toggled
together), color meaning, and the list of live visualizations were three separate mechanisms: a fixed
channel enum, a fixed palette token table, and ad-hoc discovery.

They are now one editable record: a **member**. A member carries the display properties and the array
of sources that currently belong to it, and the whole member list can be added to, removed from,
reordered and retuned without touching code. Sources join a member by naming its label, because the
visualized data changes while the editor runs.

## Architecture

```
component (IGizmoSource)
  │  declares      : Member label + shape family
  │  OnDrawGizmos()          → SceneGizmos.Draw(this, selected: false)
  │  OnDrawGizmosSelected()  → SceneGizmos.Draw(this, selected: true)
  ▼
SceneGizmos  ── resolves the member by label
  │             member.Enabled × member.Presence × session GizmoPolicy
  │             hands canvas the member color, restores Gizmos state in finally
  ▼
GizmoCanvas  ── shape primitives in the active space, colored from BaseColor
  │             Volume  : WireBox / WireSphere / PlaneRect / WireHemisphere
  │             Direction: Arrow
  │             Path    : Polyline / TubeFrames
  │             Points  : Line (per-sample loops)
  ▼
Scene view

GizmoMemberSet (asset: SceneGizmo/SceneGizmoMembers.asset)
  └─ GizmoMember[]  ── Label · Enabled · Presence · Color · Sources[]
                       ▲
Tools/Scene Gizmo/Gizmo Manager (Editor/SceneGizmoWindow.cs)
  add / remove / reorder / retune members, rescan scene rosters, global policy
```

### 类关系

| Class | Location | Responsibility |
|---|---|---|
| `IGizmoSource` | `IGizmoSource.cs` | Contract: declare member label and form, paint in `DrawGizmos` |
| `GizmoMember` | `GizmoMember.cs` | One group: label, enabled, scenario, color, live source array |
| `GizmoMemberSet` | `GizmoMemberSet.cs` | The editable member list, persisted as a project asset |
| `SceneGizmos` | `SceneGizmos.cs` | Dispatch entry, member resolution, session-wide policy |
| `GizmoCanvas` | `GizmoCanvas.cs` | Color/space scope, `BaseColor`, shared shape primitives |
| `GizmoTint` | `GizmoTint.cs` | Derives subordinate colors from a member color |
| `GizmoPresence` / `GizmoPolicy` | `GizmoPresence.cs` | Per-member scenario and session-wide override |
| `GizmoForm` | `GizmoForm.cs` | Shape family label shown in the manager window |
| `SceneGizmoWindow` | `Editor/SceneGizmoWindow.cs` | Member list editor and global policy |

`GizmoMemberSet` lives in the framework root rather than in `Editor/` on purpose: it compiles into the
runtime assembly because the dispatch entry point has to resolve members, and `Assembly-CSharp` cannot
reference `Assembly-CSharp-Editor`. Its implementation body is wrapped in `#if UNITY_EDITOR`, so player
builds keep an empty shell and resolve nothing, matching the fact that gizmos never draw there.

## The member record

| Field | Meaning |
|---|---|
| `Label` | Membership key. Sources name it; renaming leaves the old sources unregistered |
| `Enabled` | Family switch; a disabled member draws nothing regardless of policy |
| `Presence` | Display scenario for the whole group: `Hidden` / `OnSelected` / `Always` |
| `Color` | Base color for the whole group; subordinate parts derive from it |
| `Sources` | Live array of the sources that currently name this label |

The array is a roster, not the membership key. Membership is resolved by label because the data being
visualized varies at runtime — sources enable, disable, appear and disappear — so a serialized object
reference list would go stale. The array is rebuilt by the manager window's scene scan and grows when a
persisted member's source draws.

`SceneGizmoMembers.asset` holds the list. It lives next to the framework so the configuration is
versioned with the repository. When the asset does not exist yet, drawing still works: an unregistered
label resolves to a transient member with the same seeded defaults and a stable label-derived color, so
a new source is never silently invisible. Transient members are never written to the asset; the manager
window registers them for real on its first scan.

## Public API

| Member | Signature | Purpose |
|---|---|---|
| `SceneGizmos.Draw` | `static void Draw(IGizmoSource source, bool selected)` | The only call a source makes |
| `SceneGizmos.IsVisible` | `static bool IsVisible(IGizmoSource source, bool selected)` | Visibility decision without drawing |
| `SceneGizmos.ResolveMember` | `static GizmoMember ResolveMember(IGizmoSource source)` | The member that owns a source |
| `SceneGizmos.Resolve` | `static GizmoPresence Resolve(GizmoPresence declared)` | Applies the session override |
| `SceneGizmos.Policy` | `static GizmoPolicy Policy { get; set; }` | Session override in `EditorPrefs` |
| `SceneGizmos.HideAll/ShowAll/ResetOverrides` | `static void` | Shared actions used by the window |
| `GizmoMemberSet.Ensure` | `static GizmoMemberSet Ensure()` | Loads or creates the member asset |
| `GizmoMemberSet.ResolveMember` | `static GizmoMember ResolveMember(string label)` | Persistent member or transient fallback |
| `GizmoMemberSet.AddMember/RemoveMember/MoveMember` | instance methods | Member CRUD, saved immediately |
| `GizmoMemberSet.RosterFrom` | `void RosterFrom(IEnumerable<IGizmoSource> sources)` | Rebuilds every roster from a scan |
| `GizmoCanvas.LocalSpace` | `MatrixScope LocalSpace(Matrix4x4 localToWorld)` | Draw in local space; dispose restores the matrix |
| `GizmoCanvas.BaseColor` | `Color BaseColor { get; }` | Member color, assigned before `DrawGizmos` |
| `GizmoCanvas.WireBox / WireSphere / PlaneRect / WireHemisphere` | volume primitives | Framework drawing |
| `GizmoCanvas.Arrow` | `void Arrow(origin, direction, length, headLength = 0, headWidth = 0)` | Direction drawing |
| `GizmoCanvas.Polyline / TubeFrames / Line` | path and point primitives | Path drawing and per-sample loops |
| `GizmoTint.Fade` | `static Color Fade(Color baseColor, float alphaScale = 0.35f)` | Secondary extent of the same system |
| `GizmoTint.Emphasis` | `static Color Emphasis(Color baseColor, float lighten = 0.35f, float alphaScale = 1f)` | Terminal face that should stand out |

## Participating

A plain `MonoBehaviour` implements the interface and adds the two one-line hooks:

```csharp
public sealed class MyComponent : MonoBehaviour, IGizmoSource
{
    [Header("Debug")]
    [SerializeField] private string _gizmoMember = "Terrain";

    public string Member { get => _gizmoMember; set => _gizmoMember = value; }
    public GizmoForm Form => GizmoForm.Volume;

    private void OnDrawGizmos() => SceneGizmos.Draw(this, false);
    private void OnDrawGizmosSelected() => SceneGizmos.Draw(this, true);

    public void DrawGizmos(GizmoCanvas canvas)
    {
        using (canvas.LocalSpace(transform.localToWorldMatrix))
        {
            canvas.WireBox(Vector3.zero, _size);
            canvas.Color = GizmoTint.Fade(canvas.BaseColor);
            canvas.WireBox(Vector3.zero, _size * 0.5f);
        }
    }
}
```

A component that already inherits another base class (for example `WindFieldProvider : FieldProvider`)
implements the same interface without a base class; only the two hooks and the member field are added.

The hooks stay on the component rather than moving into a shared base class on purpose: Unity message
dispatch is type-based, and keeping the callbacks on the concrete type removes any dependence on how
inherited private messages resolve.

## Display scenario

| Member `Presence` | Unselected pass (`OnDrawGizmos`) | Selected pass (`OnDrawGizmosSelected`) |
|---|---|---|
| `Hidden` | no | no |
| `OnSelected` | no | yes |
| `Always` | yes | no |

A source therefore draws exactly once. The session-wide `GizmoPolicy` replaces the member declaration
before this table applies: `Default` defers to the member, while `Hidden` / `OnSelected` / `Always`
force every member. A disabled member is silent under every policy.

## Manager window

`Tools/Scene Gizmo/Gizmo Manager`:

- **Display Policy** — session override plus Hide All / Show All / Reset.
- **Members** — one row per member with a foldout roster. Expanded rows edit Enabled, Label, Scenario
  and Color, and list every source in the member's array with its form and a Ping button. Per row:
  move up, move down, remove (with confirmation). Header actions: Add Member, Rescan Scenes.
- Edits are saved to the asset immediately through `AssetDatabase.SaveAssetIfDirty`, which writes only
  this asset rather than flushing the whole session.

Rosters refresh on `EditorApplication.hierarchyChanged`, on Rescan Scenes, and at draw time for
persisted members. A label with no member is registered by the next scan, so a new source becomes a
real, editable member as soon as the window looks at the scene.

Every field writes behind `EditorGUI.BeginChangeCheck` rather than comparing a control's return value:
a popup or toggle can report a value during a layout or repaint pass that was never user intent, and
writing session state from that comparison silently corrupts the display policy.

## Color

A member has one color; secondary parts derive from it with `GizmoTint`, so retuning a member retunes its
whole visualization coherently. The former per-role token table is gone: two visualizations that need
unrelated colors become two members instead of two palette entries. Default seeded members:

| Member | Color so far | Scenario | Owns |
|---|---|---|---|
| `Wind` | cyan `0.20, 0.80, 1.00, 0.85` | `OnSelected` | Wind area, wind sources |
| `Instance` | blue `0.30, 0.70, 1.00, 0.80` | `OnSelected` | Fish path and tube, rain volume |
| `Interior` | amber `1.00, 0.75, 0.15, 0.95` | `Always` | Bake volume, bake view direction |
| `Terrain` | green `0.60, 1.00, 0.60, 0.90` | `OnSelected` | reserved for the grass generator |

## Migration record

Files moved onto the framework, with the display defaults that preserve their previous visible behavior:

| File | Member | Form | Before the framework |
|---|---|---|---|
| `InteriorMapBaker/InteriorMapBakeVolume.cs` | `Interior` | `Volume` | Always-on wire cube or hand-rolled hemisphere grid, own color field |
| `InteriorMapBaker/InteriorMapBakeDirection.cs` | `Interior` | `Direction` | Always-on arrow with its own 4-edge head plus origin sphere |
| `FieldManager/Wind/WindSource.cs` | `Wind` | `Volume` | Selection-only cyan sphere and velocity line, no toggle |
| `FieldManager/Wind/WindFieldProvider.cs` | `Wind` | `Volume` | Selection-only cyan flat box behind a `Debug` bool |
| `InstanceManager/Instance/Fish/FishSimulation.cs` | `Instance` | `Path` | Selection-only polyline, 33 rings, green arrow, three literals |
| `InstanceManager/Instance/Rain/RainSimulation.cs` | `Instance` | `Volume` | Selection-only plane, fall edges, floor and arrow from world axes, three literals |

Deliberate behavior changes from unifying duplicated algorithms and consolidating groups:

- The three separate arrow builders became one `Arrow` with four diagonal head edges; head length and
  width stay per-source parameters.
- The wind velocity line now ends in an arrow head, matching every other direction visualization.
- Colors come from the member. Fish and rain share the `Instance` color, and the bake volume and view
  arrow share the `Interior` color; split a member if a different color is wanted.
- `_gizmoColor`, `_drawGizmo`, `_showArea` and `_gizmoPresence` are gone; existing scene and prefab
  values for them are orphaned and dropped on the next save. Sources now carry `_gizmoMember`.

Path frames still come from a fixed reference axis rather than parallel transport, so a path tangent
close to vertical can roll its ring. This matches the pre-migration fish gizmo and is documented on
`TubeFrames` rather than silently changed.

## Out of scope

- Interactive handles. The grass brush disc, curve window preview and similar `Handles` draws inside
  `OnSceneGUI` or an `EditorWindow` are editing interactions, not visualizations. They stay with their
  owner but should take colors from the owning member and respect its enablement.
- `Shaders/Render/SimpleGrass/GrassGenerator.cs` still owns its point ticks and `ShowGrass` bool. It sits
  outside the directory authorized for this change; migrating it is the remaining `Terrain` member and
  will use `GizmoTint` between light and dark sample ticks.

## Extension points

- Add a member in the window when a new subsystem appears; nothing else needs editing.
- Split a member when two visualizations need different colors, scenarios or enablement.
- Add a shape primitive to `GizmoCanvas` when two sources need the same geometry, rather than in one of them.
