// ════════════════════════════════════════════════════════════════
//  Transparent Shadow Feature — light-space colored transmission map.
//
//  Renders the glass layer (TransparentWithShadow layer) from the main
//  light's view into an RGB transmission + normalized light-space depth
//  target (A channel). Receivers compare their own light-space depth
//  against the glass depth: before the glass = white daylight, behind it
//  = glass color.
// ════════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Mine.Rendering
{
    /// <summary>
    /// Pipeline-level transparent transmission map: one light-space pass for
    /// all renderers selected by <see cref="Settings.layerMask"/>.
    /// Align that mask with the <c>TransparentWithShadow</c> layer; glass on it
    /// provides a <c>LightMode = "TransparentShadow"</c> pass.
    /// </summary>
    public sealed class TransparentShadowFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public sealed class Settings
        {
            [Header("Technical")]
            [Tooltip("Pass tag used by glass materials.")]
            public string shaderTag = "TransparentShadow";

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

            // Event groups the effect's frame scope: which visible objects feed
            // the map (space) and when the pass runs (time), read as one pair.
            [Header("Event")]
            [Tooltip("Objects on these layers are additionally rendered into the transparent shadow map; " +
                     "align it with the TransparentWithShadow layer.")]
            public LayerMask layerMask = 0;
            [Tooltip("Must run before opaque receivers sample the transparent map.")]
            public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingOpaques;
        }

        public Settings settings = new Settings();

        TransparentShadowPass _pass;
        static bool s_warnedEmptyLayers;

        public override void Create()
        {
            _pass = new TransparentShadowPass(settings);
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
            ValidateLayers();
            renderer.EnqueuePass(_pass);
        }

        void ValidateLayers()
        {
            bool emptyLayers = settings.layerMask.value == 0;
            if (emptyLayers == s_warnedEmptyLayers)
                return;
            s_warnedEmptyLayers = emptyLayers;
            if (emptyLayers)
                Debug.LogWarning("[TransparentShadow] Layer Mask is Nothing, so no glass feeds the map. " +
                                 "Align it with the TransparentWithShadow layer.");
        }

        protected override void Dispose(bool disposing)
        {
            _pass = null;
        }

        // ════════════════════════════════════════════════════════════
        //  Transparent shadow pass — layer bounds → light-space RT → global.
        // ════════════════════════════════════════════════════════════
        sealed class TransparentShadowPass : ScriptableRenderPass
        {
            static readonly int TexId = Shader.PropertyToID("_TransparentShadowTex");
            static readonly int WorldToLightId = Shader.PropertyToID("_TransparentShadowWorldToLight");
            static readonly int ParamsId = Shader.PropertyToID("_TransparentShadowParams");
            static readonly int DepthId = Shader.PropertyToID("_TransparentShadowDepth");

            readonly Settings _settings;

            sealed class PassData
            {
                public RendererListHandle list;
            }

            public TransparentShadowPass(Settings settings)
            {
                _settings = settings;
                profilingSampler = new ProfilingSampler("Transparent Shadow Map");
                requiresIntermediateTexture = false;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                int layerMask = _settings.layerMask.value;
                if (layerMask == 0
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
                if (!TryComputeBounds(worldToLight, layerMask, out Vector2 center, out Vector2 size,
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

                var filtering = new FilteringSettings(RenderQueueRange.all, layerMask);
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
                    name = "_TransparentShadowMap"
                };
                TextureHandle color = graph.CreateTexture(desc);

                using var builder = graph.AddRasterRenderPass<PassData>(
                    "Transparent Shadow Map", out PassData passData, profilingSampler);

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
            bool TryComputeBounds(Matrix4x4 worldToLight, int mask, out Vector2 center, out Vector2 size,
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
