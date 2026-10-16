# Interior Map Baker

## Overview

`InteriorMapBaker` is the scene-side framework for baking an interior mapping texture. `InteriorMapBakerWindow` is the corresponding editor tool, opened from `Tools/Interior Map Baker...`.

The scene framework owns the bake volume, view direction, and a dedicated disabled bake camera. The tool window lets the user select a framework, create or repair a standard framework, configure texture settings, choose a bake type, bake a texture, load an existing asset, save the result, and preview the output.

The volume and direction components participate in the shared Scene Gizmo framework: both register into the `Interior` member, and that member owns their enablement, display scenario and color. See [SceneGizmo](../SceneGizmo/SceneGizmo.md).

---

## Workflow

A standard framework can be created in either of two ways:

1. Open `Tools/Interior Map Baker...` and click **Create Standard Framework**; or
2. Add `InteriorMapBaker` to a scene GameObject and click **Create Framework** in its Inspector.

The generated hierarchy is:

```text
InteriorMapBaker
├── BakeVolume       + InteriorMapBakeVolume
├── BakeDirection    + InteriorMapBakeDirection
└── BakeCamera       + Camera (disabled)
```

After creating the framework:

1. Adjust the `BakeVolume` wireframe and the `BakeDirection` view arrow.
2. Confirm that `BakeCamera` exists under the framework; it is the camera owned by the Baker and stays disabled during normal scene rendering.
3. Assign the scene `InteriorMapBaker` object to **Framework** in the Tools window.
4. Set the face resolution, bake type, and render layers, then click **Bake**.
5. Save the generated texture asset using the save path.
6. Set the consuming material's `_ProjectionType` to the same projection as the baked texture.

Clicking **Create Framework** repeatedly does not duplicate existing direct child objects. Clicking **Create Standard Framework** creates a new complete root framework and assigns it to the current tool window. Existing frameworks created before the dedicated camera was added can be repaired with **Create Framework** in the Inspector or **Initialize Framework** in the Tools window.

---

## Responsibilities

| Class | Responsibility |
|---|---|
| `InteriorMapBaker` | Scene entry point; stores references to the volume, direction, and Baker-owned camera |
| `InteriorMapBakeVolume` | Stores and draws the oriented bake volume |
| `InteriorMapBakeDirection` | Stores and draws the bake view direction arrow |
| `InteriorMapTextureBaker` | Configures the framework-owned Camera, renders six explicit cubemap faces, and converts them to the selected output projection |
| `InteriorMapBakerEditorUtility` | Shared Editor-only logic for creating, binding, and initializing framework objects |
| `InteriorMapBakerWindow` | Tools window for framework creation and selection, projection settings, baking, loading, saving, and preview |

---

## Scene Framework Components

### `InteriorMapBaker`

The Inspector provides a **Create Framework** button:

- Reuses already bound volume, direction, and camera references first;
- Searches the current object's direct children when a reference is missing;
- Creates `BakeVolume`, `BakeDirection`, and `BakeCamera` only when they do not exist;
- Configures the generated Camera as disabled, untagged, transparent, Reflection type, and 90° perspective;
- Synchronizes the owned camera pose with `BakeDirection.Origin` and `BakeDirection.Direction`;
- Records object creation and reference binding in Unity Undo.

Public read-only interface:

```csharp
public InteriorMapBakeVolume Volume { get; }
public InteriorMapBakeDirection Direction { get; }
public Camera BakeCamera { get; }
public bool IsInitialized { get; }
```

`IsInitialized` is true only when all three generated framework references are valid.

### `BakeCamera`

The dedicated camera is not tagged `MainCamera` and is disabled outside an explicit Bake call. It is configured as `CameraType.Reflection` so Game-camera-only debug and selection RendererFeatures do not run for the bake. It is never selected through `Camera.main`, `Camera.current`, `SceneView`, or any other scene-camera lookup.

During Bake, `InteriorMapTextureBaker`:

1. Synchronizes the camera position and orientation from `BakeDirection`;
2. Applies the selected culling mask and the volume-derived far clipping plane;
3. Creates a temporary square RenderTexture;
4. Renders +X, -X, +Y, -Y, +Z, and -Z explicitly through `Camera.Render`;
5. Reads each face directly into a Cubemap using Unity's bottom-left texture coordinates;
6. Converts the Cubemap to the selected output projection;
7. Restores the Baker Camera pose and leaves it disabled.

The camera object remains in the scene hierarchy for inspection and later reuse. Only the temporary RenderTexture, face readback Texture2D, Cubemap, and generated preview Texture2D are owned by the bake operation/window.

### `InteriorMapBakeVolume`

| Parameter | Type | Default | Description |
|---|---|---:|---|
| `_center` | `Vector3` | `(0,0,0)` | Local-space center offset relative to the component Transform |
| `_size` | `Vector3` | `(2,2,3)` | Local-space volume size, clamped to positive values |
| `_drawGizmo` | `bool` | `true` | Draw the volume wireframe |
| `_gizmoColor` | `Color` | Cyan | Volume wireframe color |

The component Transform controls the position and orientation of the volume. Its scale affects the Gizmo and the subsequent local-to-world matrix.

### `InteriorMapBakeDirection`

| Parameter | Type | Default | Description |
|---|---|---:|---|
| `_originOffset` | `Vector3` | `(0,0,0)` | Local-space arrow origin offset relative to the component Transform |
| `_length` | `float` | `1.5` | Arrow length |
| `_headLength` | `float` | `0.3` | Arrow head length |
| `_headWidth` | `float` | `0.15` | Arrow head width |
| `_drawGizmo` | `bool` | `true` | Draw the direction arrow |
| `_gizmoColor` | `Color` | Yellow | Direction arrow color |

The component Transform's `forward` vector defines the bake view direction.

---

## Tools Window Settings

| Setting | Description |
|---|---|
| Framework | Scene `InteriorMapBaker` object selected for the current bake |
| Create Standard Framework | Creates and selects a new complete standard framework |
| Initialize Framework | Repairs a selected older framework by creating missing child objects, including `BakeCamera` |
| Face Resolution | Resolution of each cubemap face, currently `16–1024` |
| Bake Type | Selects `Box` or `Hemisphere` output layout |
| Render Layers | Culling Mask used by the Baker-owned camera |
| Save Path | Project path for the generated texture asset |
| Load Path | Project path for an existing texture asset |

The output texture dimensions depend on **Bake Type**:

| Bake Type | Output dimensions | Layout |
|---|---:|---|
| `Box` | `Face Resolution × 2` by `Face Resolution` | 2:1 equirectangular direction map |
| `Hemisphere` | `Face Resolution` by `Face Resolution` | Square texture containing a circular disk; transparent outside the disk |

For example, a face resolution of `256` produces a `512 × 256` Box texture or a `256 × 256` Hemisphere texture.

The window supports:

- **Bake**: render a room panorama from the selected framework-owned camera and create a temporary texture;
- **Load**: load an existing `Texture2D` asset;
- **Save**: save the current texture as a project asset, with an overwrite confirmation;
- **Preview**: display the current generated or loaded texture using the selected layout aspect ratio.

---

## Baking Convention

`InteriorMapTextureBaker` uses the following process:

1. Use `BakeDirection.Origin` as the owned camera position;
2. Use `BakeDirection.Direction` and its Transform Up direction as the owned camera orientation;
3. Estimate the far clipping plane from the world-space size of `BakeVolume`;
4. Render six explicit square views with the framework-owned `Camera.Render`;
5. Preserve the shared bottom-left coordinate convention between `ReadPixels` and `Cubemap.SetPixels`;
6. Pack the views into a Cubemap;
7. Convert the Cubemap to the selected direction texture projection.

### Box

The Box output is a 2:1 equirectangular texture. Set the consuming material's `_ProjectionType` to `0` (`Box`) for this output.

### Hemisphere

The Hemisphere output is a square disk projection of the rear half of a sphere. The shader uses a sphere intersection rather than a box intersection, and the baker samples the same rear-hemisphere direction through the Baker forward frame.

The disk maps the normalized direction directly into UV space:

```text
U = direction.x * 0.5 + 0.5
V = direction.y * 0.5 + 0.5
```

The baked direction must have `direction.z <= 0`. Pixels outside the unit disk are transparent and use Alpha 0. The output uses Clamp addressing. Set the consuming material's `_ProjectionType` to `1` (`Hemisphere`) for this output.

The shader and baker must use the same Bake Type. Changing `_ProjectionType` does not convert an already-baked texture between layouts. The square texture dimensions are only the storage container; the valid Hemisphere image area is circular.

Transparent background pixels remain at texture Alpha 0. `InteriorMapping.shader` uses the texture color when Alpha is non-zero and falls back to the wall color or `_RoomTint` otherwise.

---

## Known Limitations

- The current tool provides single-point panorama baking only; it does not bake multiple observation points or parallax samples.
- The generated texture is not automatically assigned to a material's `_InteriorMap` property.
- The volume is an oriented bounding box used for range visualization and far-clip estimation; Hemisphere intersection uses a sphere derived from the room dimensions.
- The direction component defines only Forward; it does not expose a separate FOV or Up asset.
- A Hemisphere texture is a square disk projection, not a six-face UV atlas or a vertically stretched equirectangular map.

---

## Extension Points

- Add an explicit spherical bake volume and radius property for Hemisphere workflows.
- Automatically assign the generated texture to a selected material's `_InteriorMap` property.
- Add output format, anti-aliasing, and additional LayerMask settings.
- Add multiple observation point blending, room depth correction, and occlusion handling.
- Add an `InteriorMapBakeProfile` ScriptableObject reference.
