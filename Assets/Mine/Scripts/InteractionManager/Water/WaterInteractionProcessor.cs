using UnityEngine;

namespace Mine.Interaction
{
    /// <summary>
    /// 水面交互处理器 — Verlet 积分求解经典 2D 波动方程。
    ///
    /// CSWave kernel: 8×8 线程组
    /// - 读取 _InteractionOriginTex (当前帧正交相机渲染的原始输入)
    /// - 读取 _InteractionWaterTex (当前高度场)
    /// - 读取 _InteractionWaterPTex (上一帧快照，邻域采样无数据竞争)
    /// - 输出 _InteractionWaterTex
    ///
    /// 挂载在 UniversalInteractionManager 同一 GameObject 上。
    /// </summary>
    [AddComponentMenu("Mine/Interaction/Water Interaction Processor")]
    public class WaterInteractionProcessor : MonoBehaviour, IUniversalInteractionProcessor
    {
        [Header("Compute")]
        [SerializeField] private ComputeShader _computeShader;

        [Header("Wave Parameters")]
        [SerializeField] [Range(0f, 0.5f)]  private float _waveSpeed   = 0.15f;  // 像素空间波速，自动归一化
        [SerializeField] [Range(0.9f, 1f)]  private float _damping     = 0.995f;
        [SerializeField] [Range(0.1f, 5f)]  private float _objectForce = 1.0f;

        // ── Shader Property IDs ─────────────────────────────────

        static readonly int s_InteractionOriginTexID = Shader.PropertyToID("_InteractionOriginTex");
        static readonly int s_InteractionWaterTexID  = Shader.PropertyToID("_InteractionWaterTex");
        static readonly int s_InteractionWaterPTexID = Shader.PropertyToID("_InteractionWaterPTex");
        static readonly int s_WaveSpeedID   = Shader.PropertyToID("_WaveSpeed");
        static readonly int s_DampingID     = Shader.PropertyToID("_Damping");
        static readonly int s_ObjectForceID = Shader.PropertyToID("_ObjectForce");
        static readonly int s_DeltaPixelsID = Shader.PropertyToID("_DeltaPixels");
        static readonly int s_ResolutionID  = Shader.PropertyToID("_Resolution");

        // ── 运行时 ──────────────────────────────────────────────

        private RenderTexture _waterTex;
        private RenderTexture _waterPTex;
        int _kernel;
        int _resolution;

        // ════════════════════════════════════════════════════════
        //  IUniversalInteractionProcessor
        // ════════════════════════════════════════════════════════

        public void Initialize(int resolution, RenderTexture sourceRT)
        {
            if (_computeShader == null)
            {
                Debug.LogError("[WaterInteractionProcessor] ComputeShader is null.");
                return;
            }

            _resolution = resolution;
            _kernel     = _computeShader.FindKernel("CSWave");

            // 输出 RT
            _waterTex = CreateRT("_InteractionWaterTex", resolution);
            // Ping-Pong 快照 RT
            _waterPTex = CreateRT("_InteractionWaterPTex", resolution);

            _computeShader.SetTexture(_kernel, s_InteractionOriginTexID, sourceRT);
            _computeShader.SetTexture(_kernel, s_InteractionWaterTexID,  _waterTex);
            _computeShader.SetTexture(_kernel, s_InteractionWaterPTexID, _waterPTex);
            _computeShader.SetInt(s_ResolutionID, _resolution);
        }

        public void Process(float deltaTime, Vector2 worldDelta)
        {
            if (_computeShader == null) return;

            var mgr = UniversalInteractionManager.Instance;
            float areaSize = mgr != null ? mgr.AreaSize : 10f;

            // 世界位移 → 像素偏移
            float scale = _resolution / areaSize;
            var pixelDelta = new Vector2Int(
                Mathf.RoundToInt(worldDelta.x * scale),
                Mathf.RoundToInt(worldDelta.y * scale)
            );
            _computeShader.SetInts(s_DeltaPixelsID, pixelDelta.x, pixelDelta.y);

            // 像素空间系数，归一化到 areaSize=10 的行为不变
            float shaderWs = _waveSpeed * (10f / areaSize) * (10f / areaSize);
            _computeShader.SetFloat(s_WaveSpeedID, shaderWs);
            _computeShader.SetFloat(s_DampingID,     _damping);
            _computeShader.SetFloat(s_ObjectForceID, _objectForce);

            int threadGroups = Mathf.CeilToInt(_resolution / 8f);
            _computeShader.Dispatch(_kernel, threadGroups, threadGroups, 1);
        }

        public void BindGlobalTextures()
        {
            if (_waterTex != null)
                Shader.SetGlobalTexture(s_InteractionWaterTexID, _waterTex);
        }

        public void Release()
        {
            // 不用 SetTexture(..., null) 解绑：ComputeShader.SetTexture 拒绝 null，
            // 会抛 ArgumentNullException 并让下面的 ReleaseRT 一条都执行不到（RT 泄漏）。
            // 这里只释放 RT；下次 Initialize 会重新绑定，中间没有任何 Dispatch。
            ReleaseRT(ref _waterTex);
            ReleaseRT(ref _waterPTex);
        }

        // ════════════════════════════════════════════════════════
        //  Helpers
        // ════════════════════════════════════════════════════════

        private static RenderTexture CreateRT(string name, int resolution)
        {
            var rt = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.RFloat)
            {
                name              = name,
                filterMode        = FilterMode.Bilinear,
                wrapMode          = TextureWrapMode.Clamp,
                useMipMap         = false,
                autoGenerateMips  = false,
                enableRandomWrite = true,
            };
            rt.Create();
            return rt;
        }

        private static void ReleaseRT(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            if (Application.isPlaying) Object.Destroy(rt);
            else                       Object.DestroyImmediate(rt);
            rt = null;
        }
    }
}
