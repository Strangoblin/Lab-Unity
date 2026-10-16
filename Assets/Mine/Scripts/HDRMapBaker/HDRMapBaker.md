# HDR Map Baker

Open `Tools/HDR Map Baker...`. The Baker captures content and always produces a **2:1 linear RGBAHalf equirectangular Texture2D**. Projection and Interior/Panorama coverage belong to [HDR Map Converter](../HDRMapConverter/HDRMapConverter.md).

## Capture

Create a standard framework, or select an existing HDRMapBaker (including a legacy InteriorMapBaker) and repair it. The root owns direct `BakeVolume`, `BakeDirection`, and disabled `BakeCamera` children. Repeated repair reuses owned children; references to another framework are replaced rather than adopted. Creation/repair uses Undo.

| Setting | Meaning |
|---|---|
| Render Layers | GameObject layer mask captured by the dedicated camera |
| Include Sky | Render the scene/camera skybox behind captured objects |
| Face Resolution | Base capture quality; output width = 2 × resolution, height = resolution |
| Capture Range | Box guide; its farthest world corner from the capture origin determines far clipping |
| Capture Frame | Position and orientation of the image, with +Y up / +Z forward |
| Asset Path | Shared Save/Load path; default `Assets/Textures/HDRPanorama.asset` |

`Nothing + Include Sky` captures only the sky. `Nothing + no sky` produces transparent pixels. Render Layers is a **GameObject LayerMask**, not URP light Rendering Layers. The range is a clipping-distance guide, not an exact volume stencil; objects inside that far distance may be captured even outside the box.

Output UV uses bottom-left origin, `u = atan2(x,z)/(2π)+0.5`, `v = asin(y)/π+0.5`; forward is the panorama centre. The capture frame is applied once. Six URP SingleCameraRequests render to a temporary GPU cube, followed by a GPU panorama projection and one final HDR readback. Post processing is disabled while capturing. HDR must be enabled on the active URP Asset.

## Assets and conversion

Bake creates a temporary result. Load and Save use the same path. Save updates an existing Texture2D in place after confirmation, preserving its GUID; only the target asset is saved. Loaded/saved assets belong to AssetDatabase. Unsaved generated textures are released on replacement or closing the window.

Open Converter sends an independent snapshot of an unsaved panorama, so closing/rebaking the Baker does not invalidate the Converter's source. Saved assets are shared by reference.

Camera settings, transform, URP camera overrides, active render target and temporary GPU objects are restored/released with `finally`. Capture does not save scenes, RendererData or Lighting settings.

## Legacy

Old script names and GUIDs remain as thin adapters for existing scenes. `InteriorMapTextureBaker.Bake` delegates to the HDR capture/conversion services. New workflows use HDR Map Baker and HDR Map Converter; room geometry is chosen by the consuming InteriorMapping material rather than this Baker.

See the [Converter validation entry](../HDRMapConverter/Validation~/README.md) for reproducible checks and evidence boundaries.
