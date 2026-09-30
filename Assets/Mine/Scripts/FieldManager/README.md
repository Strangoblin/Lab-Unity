# FieldManager — shared world fields

Unity 6 / URP 17. Field data is in world coordinates and has explicit physical units.

## Structure

- FieldManager: one active scene scheduler; Wind → Wave → Acceleration before instance simulation.
- FieldProvider: resource ownership, registration, timestamp/version and standalone fallback.
- Wind/WindFieldProvider: finite XZ RenderTexture, RGB = XYZ velocity (m/s), A reserved zero.
- Wind/WindField.compute: base wind, advected spatial disturbance and up to 16 local sources.
- Wind/WindSource: additive velocity source with smooth radial falloff in XZ.
- WindField.hlsl: shared material/Compute sampling, directly beside the manager.
- WaveField.hlsl: shared FFT sampling, directly beside the manager (Wave integration phase).

The registry accepts one active provider per FieldKind. A second producer for the same output
is rejected and disabled rather than overwriting global textures. Providers may exist on
separate objects; they are not discovered with a per-frame scene scan.

## Wind contract: 2D + analytic height

The field covers a finite, axis-aligned XZ rectangle. Optional Follow Target moves this rectangle.
It is regenerated from world coordinates, so moving the coverage does not translate the noise pattern.

World-to-UV: (positionWS.xz - originXZ) / sizeXZ.
A narrow edge blend returns to the current base wind; outside the rectangle, base wind remains.
Local sources do not repeat outside the rectangle.

Height gain is smoothstep between Reference Height and Reference Height + Height Range:
below: Lower Height Gain; above: Upper Height Gain. The same gain applies to all velocity channels.
No Texture3D is allocated. Local sources also have XZ footprints; height response is shared,
rather than a separate 3D volume per source.

Quality: Low 64², Medium 128², High 256². ARGBHalf, bilinear, clamp, no mip chain.
Wind speed, gust/turbulence strengths and source velocity are m/s; lengths are metres.

## Consumption

Material shaders include:
`Assets/Mine/Scripts/FieldManager/WindField.hlsl`

Use `SampleWindVelocityWS(positionWS)`. SampleLevel at LOD 0 supports both vertex and Compute use.
Do not normalize the result: magnitude is wind speed. Do not put global field parameters in
UnityPerMaterial or add them to Properties.

Before each Compute dispatch:
`FieldManager.BindWindCompute(shader, kernel)`
This binds texture and all metadata explicitly, including the neutral fallback when absent.
The method also ensures the manager has advanced the current play frame.

Rain uses an exact constant-wind drag step:
dv/dt = localAcceleration + dragRate * (windVelocity - velocity).
Drag strength 0 disables response. Absence of a wind provider preserves the previous ballistic step.
A long step samples wind at the step's starting position; it does not integrate all spatial variation.

Plant uses Wind Response (seconds) to turn wind speed into bounded vertex displacement.
The lower canopy is anchored. Forward/Depth/Normals/Shadow share the wind offset.
The existing mesh bounds budget includes wind: displacement is reduced when Billboard/Inflate
already consume that budget. Lighting retains the existing canopy approximation.

## Lifecycle

Providers initialize and publish when enabled, unregister/reset globals before releasing resources
when disabled, and update independently if no FieldManager is active. When managed they have no
separate simulation loop. FFT may therefore keep running in existing scenes without a manager.

The manager's clock is Time.time in play mode and realtime in edit mode.
Field Time and Version describe the published snapshot; they are diagnostics, not material controls.

## Extension

Create a FieldProvider subclass, choose a distinct semantic output, implement resource creation,
simulation, publication, explicit Compute bindings and neutral reset. Keep sampling code beside
FieldManager. Acceleration means m/s² and must remain separate from Wind's m/s and Wave's metres.

Do not force unrelated fields into one RGBA layout or blend them together. Define mixing per
semantic output. If a future producer depends on another field, make that dependency explicit
and schedule it before the consumer; the current fixed order is Wind → Wave → Acceleration.

## Initial validation

GPU Rain kernel with uniform base wind 4 m/s, gains 0.25/1.25, height range 20 m:
- y=0 → 1 m/s; y=20 → 5 m/s.
- Outside coverage at y=10 → 3 m/s.
- At dragRate=1 and dt=1, velocity matches wind * (1-exp(-1)).
- Disabling the provider clears validity and returns stationary zero-force particles.
