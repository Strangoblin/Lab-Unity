# FieldManager — shared world fields

Unity 6 / URP 17. Field data is in world coordinates and has explicit physical units.

## Structure

- FieldManager: one active scene scheduler; Wind → Wave → Acceleration before instance simulation.
- FieldProvider: resource ownership, registration, timestamp/version and standalone fallback.
- Wind/WindFieldProvider: finite XZ RenderTexture, RGB = XYZ velocity (m/s), A reserved zero.
  G (vertical) is derived from horizontal divergence, so it is non-zero only around gust fronts.
- Wind/WindField.compute: steady base wind, traveling gust bands, advected spatial disturbance,
  and the divergence-derived vertical channel.
- WindField.hlsl: shared material/Compute sampling, directly beside the manager.
- WaveFieldBindings.cs: neutral per-kernel binding for an absent wave provider.
- Editor/FieldContractProbe.compute: GPU sampling contract checks for wind and waves.
- FieldManager.prefab: ready-to-use manager and wind producer.
- WaveField.hlsl: shared FFT sampling, directly beside the manager.

Instantiate FieldManager.prefab in a scene. Existing FFTWaveOrchestrator components register
automatically as Wave providers; their original shader references and cascade settings remain.
The prefab contains only the manager and wind. The current scene's FFT is an independent object.

The registry accepts one active provider per FieldKind. A second producer for the same output
is rejected and disabled rather than overwriting global textures. Providers may exist on
separate objects; they are not discovered with a per-frame scene scan.

## Wind contract: 2D + analytic height

The field covers a finite, axis-aligned XZ rectangle. Optional Follow Target moves this rectangle.
It is regenerated from world coordinates, so moving the coverage does not translate the noise pattern.

World-to-UV: (positionWS.xz - originXZ) / sizeXZ.
A narrow edge blend returns to the current base wind; outside the rectangle, base wind remains.

Height gain is smoothstep between Reference Height and Reference Height + Height Range:
below: Lower Height Gain; above: Upper Height Gain. The same gain applies to all velocity channels.
No Texture3D is allocated; height response is shared rather than a separate 3D volume.

Quality: Low 64², Medium 128², High 256². ARGBHalf, bilinear, clamp, no mip chain.
Every value published to consumers is m/s; lengths are metres. Force and speed are independent:

- Wind Angle: direction on the XZ plane in degrees (0 = +X, 90 = +Z).
- Wind Strength: steady force (velocity published to consumers, therefore displacement).
- Wind Speed: travel speed of the spatial turbulence pattern; it does not change displacement.
- Gust Strength: gust force amplitude added along the wind direction.
- Gust Speed: travel speed of the gust fronts.
- Gust Length: gust wavelength along the wind direction; a fixed point oscillates at Gust Speed / Gust Length Hz.
- Vertical Scale: mixing height (m). Vertical velocity is derived, not authored: -height × horizontal divergence.
- Turbulence Strength: spatial disturbance amplitude.
- Turbulence Length: spatial period; a point sees it pass at Wind Speed / Length.

The three layers are deliberately different in space: the base is uniform, the gust is a band
pattern that travels downwind, and the turbulence is a 2D spatial pattern that scrolls. A uniform
gust would only add a global oscillation to the uniform base and produce no wave motion at all,
which is why the gust phase includes the position term:

  gustPhase = 2π (dot(direction, positionXZ) − Gust Speed × time) / Gust Length

Vertical velocity is computed from what the field already contains, using continuity:
vy = −Vertical Scale × (∂vx/∂x + ∂vz/∂z). The uniform base has zero divergence, and the
turbulence pattern varies each axis only across the flow, so both are divergence-free by
construction; only gust bands lift and sink the air, and vertical is in quadrature with the gust
push. Consumers add vy to their world-space offset like any other channel, so grass blades rise
while the gust front passes. Making turbulence lift too would require giving that pattern a small
divergent term, which changes its look.

The pattern only moves by parameters that are named as speeds. Strength and gust amplitude affect
the published velocity (displacement) and nothing else. Both travel terms are integrated on the CPU
in double precision and wrapped to one period (turbulence length, gust length) before dispatch, so
a long session does not drift or lose phase precision.

## Consumption

Material shaders include:
`Assets/Mine/Scripts/FieldManager/WindField.hlsl`

Use `SampleWindVelocityWS(positionWS)`. SampleLevel at LOD 0 supports both vertex and Compute use.
Do not normalize the result: magnitude is wind speed. Do not put global field parameters in
UnityPerMaterial or add them to Properties.

The field has one contract for every consumer: it publishes world-space velocity in m/s and
nothing else. How that velocity becomes displacement, acceleration or drag — including the
response parameter and any amplitude bound — belongs to the consumer, not to the field.
Shaders apply the sampled effect directly; the shared field integration does not clamp it.

In Edit mode, `[ExecuteAlways]` Update only ticks when the scene repaints, so a provider would
otherwise keep publishing a stale texture after an Inspector edit. `FieldProvider.OnValidate`
re-dispatches the field and repaints all views when a serialized value changes; Play mode uses
the normal per-frame update.

Before each Compute dispatch:
`FieldManager.BindWindCompute(shader, kernel)`
This binds texture and all metadata explicitly, including the neutral fallback when absent.
The method also ensures the manager has advanced the current play frame.

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

## Wave contract

Include `Assets/Mine/Scripts/FieldManager/WaveField.hlsl` after the pipeline includes.

- SampleWaveDisplacementWS(positionWS): world-space XYZ displacement in metres.
- SampleWaveNormalWS(positionWS): unit world-space normal.
- SampleWaveNormalBlendWS(positionWS): original unnormalized blend, for Water's material mixing.
- SampleWaveFoam(positionWS): original 0.5/0.3/0.2 cascade weights.

The FFT provider retains _WaveDisplacement0..2 / _WaveNormal0..2 / _WavePatchSize0..2.
Textures repeat in XZ, unlike the finite wind rectangle. Normal textures encode [-1,1] to [0,1].
Water displacement scale and normal intensity remain material response parameters.

For Compute use `FieldManager.BindCompute(FieldKind.Wave, shader, kernel)`.
With no Wave provider the function binds neutral textures and validity=0, and returns false.
Wind and Wave are the implemented channels; future channels must define their own neutral bindings.
The generic registry alone does not define an acceleration texture or acceleration solver.

FFT's spectral wind controls remain independent from the instantaneous shared wind.
This integration shares publication/scheduling and sampling contracts, without altering its spectrum model.
Spectrum regeneration on Inspector changes is deferred until the next field update.
FFT.compute and its temporary transform textures remain owned by the FFT provider.

## Verification results

- Real Plant GPU rendering: +X wind shifts the test canopy centroid by 6.39 pixels;
  the disabled-wind control remains fixed.
- Quality change: 128² → 256² reallocates and republishes the new texture.
- FFT shared GPU sampling matches direct original texture mixtures:
  displacement error 5.96e-8, normal error 0; foam mixture agrees.
- Disabled FFT: zero displacement/foam, up normal; no released RT remains bound to the probe.
- Play Mode: wind and wave snapshot times agree; Water/Plant compile and runtime logs contain no errors.
