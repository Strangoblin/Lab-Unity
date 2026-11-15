// ════════════════════════════════════════════════════════════════
//  Colored Shadow Feature — light-space colored transmission map.
//
//  Renders the transmissive glass layer from the main light's view into
//  an RGB transmission + normalized light-space depth target (A channel).
//  Receivers compare their own light-space depth against the glass depth:
//  before the glass = white daylight, behind it = glass color.
// ════════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Mine.Rendering
{
    /// <summary>
    /// Pipeline-level colored transmission map: one light-space pass for all
    /// renderers selected by <see cref="Settings.layerMask"/>.
    /// </summary>
    public sealed class ColoredShadowFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public sealed class Settings
        {
            [Header("Technical")]
            [Tooltip("Transmissive renderers to render into the colored shadow map. 0 = off.")]
            public LayerMask layerMask = 0;

            [Tooltip("Pass tag used by colored glass materials.")]
            public string shaderTag = "ColoredShadow";

            [Range(256, 4096)]
            [Tooltip("Square light-space map resolution.")]
            public int resolution = 1024;

            [Range(1f, 2f)]
            [Tooltip("Extra light-space XY padding around the layer bounds.")]
            public float padding = 1.25f;

            [Range(0f, 50f)]
            [Tooltip("Extra light-space depth padding around the layer bounds.")]
            public float depthPadding = 10f;

            [Range(0f, 0.05f)]
            [Tooltip("Normalized depth bias when comparing receiver against glass depth.")]
            public float depthBias = 0.003f;

            [Header("Event")]
            [Tooltip("Must run before opaque receivers sample the colored map.")]
            public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingOpaques;
        }

        public Settings settings = new Settings();

        ColoredShadowPass _pass;

        public override void Create()
        {
            _pass = new ColoredShadowPass(settings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_pass == null)
                return;

            CameraData camera = renderingData.cameraData;
            if (camera.renderType == CameraRenderType.Overlay
                || (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView))
                return;

            _pass.renderPassEvent = settings.passEvent;
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            _pass = null;
        }

        // ════════════════════════════════════════════════════════════
        //  Colored shadow pass — layer bounds → light-space RT → global.
        // ════════════════════════════════════════════════════════════
        sealed class ColoredShadowPass : ScriptableRenderPass
        {
            static readonly int TexId = Shader.PropertyToID("_ColoredShadowTex");
            static readonly int WorldToLightId = Shader.PropertyToID("_ColoredShadowWorldToLight");
            static readonly int ParamsId = Shader.PropertyToID("_ColoredShadowParams");
            static readonly int DepthId = Shader.PropertyToID("_ColoredShadowDepth");

            readonly Settings _settings;

            sealed class PassData
            {
                public RendererListHandle list;
            }

            public ColoredShadowPass(Settings settings)
            {
                _settings = settings;
                profilingSampler = new ProfilingSampler("Colored Shadow Map");
                requiresIntermediateTexture = false;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                if (_settings.layerMask.value == 0
                    || lightData.mainLightIndex < 0
                    || lightData.mainLightIndex >= lightData.visibleLights.Length)
                {
                    Disable();
                    return;
                }

                Light mainLight = lightData.visibleLights[lightData.mainLightIndex].light;
                if (mainLight == null || mainLight.type != LightType.Directional)
                {
                    Disable();
                    return;
                }

                Matrix4x4 worldToLight = mainLight.transform.worldToLocalMatrix;
                if (!TryComputeBounds(worldToLight, out Vector2 center, out Vector2 size,
                        out float minZ, out float maxZ))
                {
                    Disable();
                    return;
                }

                Shader.SetGlobalMatrix(WorldToLightId, worldToLight);
                Shader.SetGlobalVector(ParamsId, new Vector4(center.x, center.y, size.x, size.y));
                Shader.SetGlobalVector(DepthId,
                    new Vector4(minZ, maxZ, 1f, _settings.depthBias));

                DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(
                    new ShaderTagId(_settings.shaderTag),
                    renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);
                drawing.perObjectData = PerObjectData.None;

                var filtering = new FilteringSettings(RenderQueueRange.all, _settings.layerMask);
                RendererListHandle list = graph.CreateRendererList(
                    new RendererListParams(renderingData.cullResults, drawing, filtering));

                // Color-only: no depth attachment. The alpha carries raw normalized
                // light-space z, not a reversed-Z clip depth.
                var desc = new TextureDesc(_settings.resolution, _settings.resolution)
                {
                    format = GraphicsFormat.R16G16B16A16_SFloat,
                    depthBufferBits = DepthBits.None,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    clearBuffer = true,
                    clearColor = Color.white,
                    msaaSamples = MSAASamples.None,
                    name = "_ColoredShadowMap"
                };
                TextureHandle color = graph.CreateTexture(desc);

                using var builder = graph.AddRasterRenderPass<PassData>(
                    "Colored Shadow Map", out PassData passData, profilingSampler);

                passData.list = list;
                builder.UseRendererList(list);
                builder.SetRenderAttachment(color, 0, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);
                builder.SetGlobalTextureAfterPass(color, TexId);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    context.cmd.ClearRenderTarget(false, true, Color.white);
                    context.cmd.DrawRendererList(data.list);
                });
            }

            // ════════════════════════════════════════════════════════
            //  TryComputeBounds — layer renderers → light-space XY/Z bounds.
            // ════════════════════════════════════════════════════════
            bool TryComputeBounds(Matrix4x4 worldToLight, out Vector2 center, out Vector2 size,
                out float minZ, out float maxZ)
            {
                center = Vector2.zero;
                size = Vector2.zero;
                minZ = 0f;
                maxZ = 0f;

                float minX = float.MaxValue, maxX = float.MinValue;
                float minY = float.MaxValue, maxY = float.MinValue;
                minZ = float.MaxValue;
                maxZ = float.MinValue;
                bool found = false;

                int mask = _settings.layerMask.value;
                Renderer[] renderers = Object.FindObjectsByType<Renderer>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                foreach (Renderer renderer in renderers)
                {
                    if (renderer == null || (mask & (1 << renderer.gameObject.layer)) == 0)
                        continue;

                    Bounds bounds = renderer.bounds;
                    Vector3 c = bounds.center;
                    Vector3 e = bounds.extents;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 corner = c + new Vector3(
                            (i & 1) == 0 ? -e.x : e.x,
                            (i & 2) == 0 ? -e.y : e.y,
                            (i & 4) == 0 ? -e.z : e.z);
                        Vector3 p = worldToLight.MultiplyPoint3x4(corner);
                        minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                        minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
                        minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
                        found = true;
                    }
                }

                if (!found)
                    return false;

                center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
                size = new Vector2(
                    Mathf.Max(0.001f, (maxX - minX) * _settings.padding),
                    Mathf.Max(0.001f, (maxY - minY) * _settings.padding));

                minZ -= _settings.depthPadding;
                maxZ += _settings.depthPadding;
                if (maxZ - minZ < 0.001f)
                    maxZ = minZ + 0.001f;
                return true;
            }

            void Disable()
            {
                Shader.SetGlobalVector(DepthId, new Vector4(0f, 1f, 0f, 0f));
            }
        }
    }
}
