using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

/// <summary>
/// POSS (Per-Object Soft Shadow) — 逐物体软阴影 Renderer Feature。
/// 两阶段管线：Caster Pass 将动态物体深度渲染到 Shadow Atlas，
/// Resolve Pass 通过 Compute Shader 解算屏幕空间阴影 + PCF 软边。
/// 与 PCSS 级联阴影共存，仅处理平行主光。
/// </summary>
[ExecuteAlways]
public class POSSFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Header("Shadow Atlas")]
        [Range(256, 2048)] public int atlasResolution = 1024;
        [Range(64, 512)] public int tileResolution = 256;

        [Header("POSS Shadow")]
        [Range(0f, 2f)] public float depthBias = 0.05f;
        [Range(0f, 5f)] public float shadowStrength = 1f;

        [Header("PCF Soft Shadow")]
        [Range(0f, 8f)] public float pcfRadius = 2f;
        [Range(1, 16)] public int pcfTaps = 4;

        [Header("Resources")]
        public ComputeShader resolveComputeShader;

        [Header("Debug")]
        public bool showShadowOnly = false;

        internal static readonly int ShadowAtlasID    = Shader.PropertyToID("_POSS_ShadowAtlas");
        internal static readonly int ShadowTextureID  = Shader.PropertyToID("_POSS_ShadowTexture");
        internal static readonly int DepthBiasID      = Shader.PropertyToID("_POSS_DepthBias");
        internal static readonly int ShadowStrengthID = Shader.PropertyToID("_POSS_ShadowStrength");
        internal static readonly int LightDirID       = Shader.PropertyToID("_POSS_LightDir");
    }

    // ════════════════════════════════════════════════════════════
    //  POSSSShadowCasterPass — 逐物体 Shadow Atlas 渲染
    // ════════════════════════════════════════════════════════════
    class POSSSShadowCasterPass : ScriptableRenderPass
    {
        Settings m_S;

        RenderTexture m_AtlasRT;
        RTHandle      m_AtlasHandle;
        int           m_AtlasRes, m_TileRes, m_TilesPerRow;

        // 传递给 Resolve Pass
        public Matrix4x4[] ObjectVPs;
        public Vector4[]   AtlasOffsets;
        public int         ObjectCount;
        public RTHandle    atlasHandle  => m_AtlasHandle;
        public RenderTexture atlasRT    => m_AtlasRT;

        Material m_ShadowCasterMaterial;

        class PassData
        {
            public int         tileRes, tilesPerRow;
            public Matrix4x4[] viewMatrices;
            public Matrix4x4[] projMatrices;
            public Vector4[]   uvOffsets;
            public int         objectCount;
        }

        public POSSSShadowCasterPass(Settings s)
        {
            m_S = s;
            renderPassEvent = RenderPassEvent.AfterRenderingShadows;
            profilingSampler = new ProfilingSampler("POSS Caster");
            var shader = Shader.Find("PostProcess/POSSShadowCaster");
            if (shader != null) m_ShadowCasterMaterial = new Material(shader);
        }

        void EnsureAtlas()
        {
            int ar = m_S.atlasResolution, tr = m_S.tileResolution;
            if (m_AtlasRT != null && m_AtlasRes == ar && m_TileRes == tr) return;
            m_AtlasRT?.Release(); m_AtlasHandle?.Release();
            m_AtlasRT = new RenderTexture(ar, ar, 0, RenderTextureFormat.RFloat);
            m_AtlasRT.filterMode = FilterMode.Point;
            m_AtlasRT.wrapMode = TextureWrapMode.Clamp;
            m_AtlasRT.Create();
            m_AtlasHandle = RTHandles.Alloc(m_AtlasRT);
            m_AtlasRes = ar; m_TileRes = tr; m_TilesPerRow = ar / tr;
        }

        void ComputePerObjectProjection(POSSComponent comp, Light light,
            out Matrix4x4 view, out Matrix4x4 proj)
        {
            Bounds b = comp.CachedRenderer.bounds;
            Vector3 c = b.center, e = b.extents;
            Vector3 ld = light.transform.forward;
            Vector3 lr = Vector3.Cross(ld, Vector3.up).normalized;
            if (lr.sqrMagnitude < 0.001f) lr = Vector3.Cross(ld, Vector3.forward).normalized;
            Vector3 lu = Vector3.Cross(lr, ld).normalized;

            float minR = float.MaxValue, maxR = float.MinValue;
            float minU = float.MaxValue, maxU = float.MinValue;
            float minD = float.MaxValue, maxD = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c;
                corner.x += (i & 1) != 0 ? e.x : -e.x;
                corner.y += (i & 2) != 0 ? e.y : -e.y;
                corner.z += (i & 4) != 0 ? e.z : -e.z;
                float dr = Vector3.Dot(corner, lr);
                float du = Vector3.Dot(corner, lu);
                float dd = Vector3.Dot(corner, ld);
                minR = Mathf.Min(minR, dr); maxR = Mathf.Max(maxR, dr);
                minU = Mathf.Min(minU, du); maxU = Mathf.Max(maxU, du);
                minD = Mathf.Min(minD, dd); maxD = Mathf.Max(maxD, dd);
            }

            float pad = e.magnitude * 0.05f + 0.1f;
            minR -= pad; maxR += pad; minU -= pad; maxU += pad;
            float zN = 0.01f, zF = (maxD - minD) + b.extents.magnitude + 10f + zN;
            Vector3 camPos = lr * ((minR + maxR) * 0.5f) + lu * ((minU + maxU) * 0.5f) + ld * (minD - zN);
            Vector3 vX = -lr, vY = lu, vZ = -ld;

            view = new Matrix4x4(
                new Vector4(vX.x, vY.x, vZ.x, 0), new Vector4(vX.y, vY.y, vZ.y, 0),
                new Vector4(vX.z, vY.z, vZ.z, 0),
                new Vector4(-Vector3.Dot(vX, camPos), -Vector3.Dot(vY, camPos), -Vector3.Dot(vZ, camPos), 1));
            proj = Matrix4x4.Ortho(-(maxR - minR) * 0.5f, (maxR - minR) * 0.5f,
                                    -(maxU - minU) * 0.5f, (maxU - minU) * 0.5f, zN, zF);
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var mgr = POSSManager.Instance;
            if (mgr == null || mgr.Components.Count == 0 || m_ShadowCasterMaterial == null) return;
            Light light = RenderSettings.sun;
            if (light == null) return;

            UniversalCameraData camData = frameData.Get<UniversalCameraData>();
            Camera cam = camData.camera;
            EnsureAtlas();

            var comps = mgr.Components;
            int count = Mathf.Min(comps.Count, m_TilesPerRow * m_TilesPerRow);
            var views   = new Matrix4x4[count];
            var projs   = new Matrix4x4[count];
            var vps     = new Matrix4x4[count];
            var offsets = new Vector4[count];

            var scaleBias = Matrix4x4.identity;
            scaleBias.m00 = 0.5f; scaleBias.m11 = 0.5f; scaleBias.m22 = 0.5f;
            scaleBias.m03 = 0.5f; scaleBias.m13 = 0.5f; scaleBias.m23 = 0.5f;

            for (int i = 0; i < count; i++)
            {
                ComputePerObjectProjection(comps[i], light, out views[i], out projs[i]);
                var proj = projs[i];
                if (SystemInfo.usesReversedZBuffer)
                { proj.m20 = -proj.m20; proj.m21 = -proj.m21; proj.m22 = -proj.m22; proj.m23 = -proj.m23; }
                vps[i] = scaleBias * (proj * views[i]);
                int col = i % m_TilesPerRow, row = i / m_TilesPerRow;
                float tUV = (float)m_TileRes / m_AtlasRes;
                offsets[i] = new Vector4(col * tUV, row * tUV, tUV, 0f);
            }

            ObjectVPs = vps; AtlasOffsets = offsets; ObjectCount = count;

            Shader.SetGlobalFloat(Settings.DepthBiasID, m_S.depthBias);
            Shader.SetGlobalFloat(Settings.ShadowStrengthID, m_S.shadowStrength);
            Shader.SetGlobalVector(Settings.LightDirID, light.transform.forward);

            TextureHandle shadowTH = graph.ImportTexture(m_AtlasHandle);
            var depthDesc = new RenderTextureDescriptor(m_AtlasRes, m_AtlasRes, RenderTextureFormat.Depth, 16, 0);
            TextureHandle depthTH = UniversalRenderer.CreateRenderGraphTexture(graph, depthDesc, "_POSS_Depth", false);

            using (var builder = graph.AddRasterRenderPass<PassData>("POSS Caster", out var pd, profilingSampler))
            {
                pd.tileRes = m_TileRes; pd.tilesPerRow = m_TilesPerRow;
                pd.viewMatrices = views; pd.projMatrices = projs;
                pd.uvOffsets = offsets; pd.objectCount = count;

                builder.SetRenderAttachment(shadowTH, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(depthTH, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetGlobalTextureAfterPass(shadowTH, Settings.ShadowAtlasID);

                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                {
                    ctx.cmd.ClearRenderTarget(RTClearFlags.Color | RTClearFlags.Depth, Color.black, 1f, 0);
                    var c = POSSManager.Instance.Components;
                    for (int i = 0; i < data.objectCount; i++)
                    {
                        var mf = c[i].GetComponent<MeshFilter>();
                        if (mf == null || mf.sharedMesh == null) continue;

                        int col = i % data.tilesPerRow, row = i / data.tilesPerRow;
                        ctx.cmd.SetViewport(new Rect(col * data.tileRes, row * data.tileRes, data.tileRes, data.tileRes));
                        ctx.cmd.SetViewProjectionMatrices(data.viewMatrices[i], data.projMatrices[i]);
                        ctx.cmd.DrawMesh(mf.sharedMesh, c[i].transform.localToWorldMatrix,
                            m_ShadowCasterMaterial, 0, 0);
                    }
                    ctx.cmd.SetViewProjectionMatrices(cam.worldToCameraMatrix, cam.projectionMatrix);
                });
            }
        }

        public void Release()
        {
            if (m_ShadowCasterMaterial != null) { Object.DestroyImmediate(m_ShadowCasterMaterial); m_ShadowCasterMaterial = null; }
            m_AtlasHandle?.Release(); m_AtlasRT?.Release();
            m_AtlasRT = null; m_AtlasHandle = null;
        }
    }

    // ════════════════════════════════════════════════════════════
    //  POSSResolvePass — 屏幕空间阴影解算 + PCF 软边
    // ════════════════════════════════════════════════════════════
    class POSSResolvePass : ScriptableRenderPass
    {
        Settings m_S;
        ComputeShader m_CS;
        int m_Kernel;
        POSSSShadowCasterPass m_Caster;

        RenderTexture m_ShadowRT;
        RTHandle      m_ShadowHandle;
        int           m_W, m_H;

        static readonly int kShadowOut         = Shader.PropertyToID("_POSS_ShadowOut");
        static readonly int kShadowAtlas       = Shader.PropertyToID("_POSS_ShadowAtlas");
        static readonly int kObjectCount       = Shader.PropertyToID("_ObjectCount");
        static readonly int kObjectVPs         = Shader.PropertyToID("_ObjectVPs");
        static readonly int kAtlasOffsets      = Shader.PropertyToID("_AtlasOffsets");
        static readonly int kDepthBias         = Shader.PropertyToID("_DepthBias");
        static readonly int kShadowStrength    = Shader.PropertyToID("_ShadowStrength");
        static readonly int kShowOnly          = Shader.PropertyToID("_ShowOnly");
        static readonly int kPCFRadius         = Shader.PropertyToID("_PCFRadius");
        static readonly int kPCFTaps           = Shader.PropertyToID("_PCFTaps");
        static readonly int kAtlasRes          = Shader.PropertyToID("_AtlasRes");
        static readonly int kScreenSize        = Shader.PropertyToID("_ScreenSize");
        static readonly int kFrustumRay0       = Shader.PropertyToID("_FrustumRay0");
        static readonly int kFrustumRay1       = Shader.PropertyToID("_FrustumRay1");
        static readonly int kFrustumRay2       = Shader.PropertyToID("_FrustumRay2");
        static readonly int kFrustumRay3       = Shader.PropertyToID("_FrustumRay3");
        static readonly int kZBufferParams     = Shader.PropertyToID("_ZBufferParams");
        static readonly int kWorldCamPos       = Shader.PropertyToID("_WorldSpaceCameraPos");
        static readonly int kLightDirection    = Shader.PropertyToID("_LightDirection");
        static readonly int kCameraDepthTex    = Shader.PropertyToID("_CameraDepthTexture");

        class PassData
        {
            public ComputeShader cs;
            public int           kernel;
            public RenderTexture shadowRT, atlasRT;
            public TextureHandle  source, shadowTH, cameraDepthTH;
            public Matrix4x4[]    objectVPs;
            public Vector4[]      atlasOffsets;
            public int            objectCount;
            public Vector4        lightDir, screenSize;
            public Vector4        frustumRay0, frustumRay1, frustumRay2, frustumRay3;
            public Vector4        zBufferParams, worldCamPos;
            public float          depthBias, shadowStrength, pcfRadius;
            public int            pcfTaps, atlasRes;
            public bool           showShadowOnly;
        }

        public POSSResolvePass(Settings s, ComputeShader cs, POSSSShadowCasterPass caster)
        {
            m_S = s; m_CS = cs; m_Caster = caster;
            if (m_CS != null) m_Kernel = m_CS.FindKernel("POSS_Resolve");
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            profilingSampler = new ProfilingSampler("POSS Resolve");
        }

        void EnsureRT(int w, int h)
        {
            if (m_ShadowRT != null && m_W == w && m_H == h) return;
            m_ShadowRT?.Release(); m_ShadowHandle?.Release();
            m_ShadowRT = new RenderTexture(w, h, 0, RenderTextureFormat.R8);
            m_ShadowRT.enableRandomWrite = true;
            m_ShadowRT.filterMode = FilterMode.Bilinear;
            m_ShadowRT.Create();
            m_ShadowHandle = RTHandles.Alloc(m_ShadowRT);
            m_W = w; m_H = h;
        }

        static void FrustumRays(Camera cam, out Vector4 r0, out Vector4 r1, out Vector4 r2, out Vector4 r3)
        {
            Vector3 p = cam.transform.position; float f = cam.farClipPlane;
            r0 = cam.ViewportToWorldPoint(new Vector3(0, 0, f)) - p;
            r1 = cam.ViewportToWorldPoint(new Vector3(1, 0, f)) - p;
            r2 = cam.ViewportToWorldPoint(new Vector3(0, 1, f)) - p;
            r3 = cam.ViewportToWorldPoint(new Vector3(1, 1, f)) - p;
        }

        public void Release()
        {
            m_ShadowHandle?.Release(); m_ShadowRT?.Release();
            m_ShadowRT = null; m_ShadowHandle = null;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            if (m_CS == null || m_Caster.ObjectCount == 0) return;

            var resData = frameData.Get<UniversalResourceData>();
            var camData = frameData.Get<UniversalCameraData>();
            TextureHandle source  = resData.activeColorTexture;
            TextureHandle camDepth = resData.cameraDepthTexture;
            if (!source.IsValid()) return;

            int w = camData.cameraTargetDescriptor.width;
            int h = camData.cameraTargetDescriptor.height;
            EnsureRT(w, h);

            TextureHandle sTH = graph.ImportTexture(m_ShadowHandle);
            TextureHandle aTH = graph.ImportTexture(m_Caster.atlasHandle);

            using (var builder = graph.AddUnsafePass<PassData>("POSS Resolve", out var pd, profilingSampler))
            {
                pd.cs = m_CS; pd.kernel = m_Kernel;
                pd.shadowRT = m_ShadowRT; pd.atlasRT = m_Caster.atlasRT;
                pd.source = source; pd.shadowTH = sTH; pd.cameraDepthTH = camDepth;
                pd.objectVPs = m_Caster.ObjectVPs;
                pd.atlasOffsets = m_Caster.AtlasOffsets;
                pd.objectCount = m_Caster.ObjectCount;
                pd.depthBias = m_S.depthBias;
                pd.shadowStrength = m_S.shadowStrength;
                pd.pcfRadius = m_S.pcfRadius;
                pd.pcfTaps = m_S.pcfTaps;
                pd.atlasRes = m_S.atlasResolution;
                pd.showShadowOnly = m_S.showShadowOnly;
                pd.screenSize = new Vector4(w, h, 1f / w, 1f / h);
                FrustumRays(camData.camera, out pd.frustumRay0, out pd.frustumRay1, out pd.frustumRay2, out pd.frustumRay3);
                pd.zBufferParams = Shader.GetGlobalVector("_ZBufferParams");
                Vector3 cp = camData.camera.transform.position;
                pd.worldCamPos = new Vector4(cp.x, cp.y, cp.z, 0);
                Vector3 ld = -RenderSettings.sun.transform.forward;
                pd.lightDir = new Vector4(ld.x, ld.y, ld.z, 0);

                builder.UseTexture(source,    AccessFlags.ReadWrite);
                builder.UseTexture(sTH,       AccessFlags.ReadWrite);
                builder.UseTexture(aTH,       AccessFlags.Read);
                builder.UseTexture(camDepth,  AccessFlags.Read);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData d, UnsafeGraphContext ctx) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                    int k = d.kernel;

                    cmd.SetComputeTextureParam(d.cs, k, kShadowOut, d.shadowRT);
                    cmd.SetComputeTextureParam(d.cs, k, kShadowAtlas, d.atlasRT);

                    var depthRT = Shader.GetGlobalTexture(kCameraDepthTex) as RenderTexture;
                    if (depthRT != null)
                        cmd.SetComputeTextureParam(d.cs, k, kCameraDepthTex, depthRT);

                    cmd.SetComputeIntParam(d.cs, kObjectCount, d.objectCount);
                    cmd.SetComputeFloatParam(d.cs, kDepthBias, d.depthBias);
                    cmd.SetComputeFloatParam(d.cs, kShadowStrength, d.shadowStrength);
                    cmd.SetComputeFloatParam(d.cs, kPCFRadius, d.pcfRadius);
                    cmd.SetComputeIntParam(d.cs, kPCFTaps, d.pcfTaps);
                    cmd.SetComputeIntParam(d.cs, kAtlasRes, d.atlasRes);
                    cmd.SetComputeIntParam(d.cs, kShowOnly, d.showShadowOnly ? 1 : 0);

                    cmd.SetComputeVectorParam(d.cs, kScreenSize, d.screenSize);
                    cmd.SetComputeVectorParam(d.cs, kFrustumRay0, d.frustumRay0);
                    cmd.SetComputeVectorParam(d.cs, kFrustumRay1, d.frustumRay1);
                    cmd.SetComputeVectorParam(d.cs, kFrustumRay2, d.frustumRay2);
                    cmd.SetComputeVectorParam(d.cs, kFrustumRay3, d.frustumRay3);
                    cmd.SetComputeVectorParam(d.cs, kZBufferParams, d.zBufferParams);
                    cmd.SetComputeVectorParam(d.cs, kWorldCamPos, d.worldCamPos);
                    cmd.SetComputeVectorParam(d.cs, kLightDirection, d.lightDir);

                    cmd.SetComputeMatrixArrayParam(d.cs, kObjectVPs, d.objectVPs);
                    cmd.SetComputeVectorArrayParam(d.cs, kAtlasOffsets, d.atlasOffsets);

                    int tx = (w + 7) / 8, ty = (h + 7) / 8;
                    cmd.DispatchCompute(d.cs, k, tx, ty, 1);

                    cmd.SetGlobalTexture(Settings.ShadowTextureID, d.shadowRT);

                    if (d.showShadowOnly)
                        Blitter.BlitCameraTexture(cmd, d.shadowTH, d.source);
                });
            }
        }
    }

    // ════════════════════════════════════════════════════════════
    //  Feature 入口
    // ════════════════════════════════════════════════════════════

    public Settings settings = new();
    POSSSShadowCasterPass m_CasterPass;
    POSSResolvePass       m_ResolvePass;

    public override void Create()
    {
        m_CasterPass?.Release();
        m_ResolvePass?.Release();
        m_CasterPass = new POSSSShadowCasterPass(settings);
        m_ResolvePass = new POSSResolvePass(settings, settings.resolveComputeShader, m_CasterPass);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (m_CasterPass == null) return;
        renderer.EnqueuePass(m_CasterPass);
        if (settings.resolveComputeShader != null)
            renderer.EnqueuePass(m_ResolvePass);
    }

    protected override void Dispose(bool disposing)
    {
        m_ResolvePass?.Release();
        m_CasterPass?.Release();
        m_CasterPass = null;
        m_ResolvePass = null;
    }
}
