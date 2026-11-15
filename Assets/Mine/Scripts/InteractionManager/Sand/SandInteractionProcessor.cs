using UnityEngine;

namespace Mine.Interaction
{
    /// <summary>
    /// 沙地交互子集 — 挖坑 + 侧向挤出堆脊 + 休止角塌落 + 回平消失。
    ///
    /// 输出 _InteractionSandTex（R = 有符号沙面高度，负 = 坑，正 = 堆起的脊）。
    /// 渲染侧（SandGround）把它位移成沙面，并用梯度求法线。
    ///
    /// 沙是小形变、不可压实的颗粒材料：体积不会消失，受压时只能被挤到旁边，
    /// 于是坑的外沿堆起一圈脊。与雪的区别：雪只向下压实、坑沿是平的。
    /// </summary>
    [AddComponentMenu("Mine/Interaction/Sand Interaction Processor")]
    public class SandInteractionProcessor : InteractionProcessor
    {
        public override string InteractionKey => "sand";

        [Header("Compute")]
        [SerializeField] private ComputeShader _computeShader;

        [Header("Sand Look")]
        [Tooltip("侧向挤出：压出的沙子堆在坑沿形成脊的强度。0 = 只挖坑不留边。")]
        [SerializeField] [Range(0f, 1f)] private float _rim = 0.7f;

        [Tooltip("休止角：0 = 20°（陡坡立刻塌）、1 = 40°（几乎不塌）。控制坑壁能有多陡。")]
        [SerializeField] [Range(0f, 1f)] private float _repose = 0.5f;

        [Tooltip("回平：坑和脊一起等比变浅直到消失的速度。0 = 永久保留，1 = 两三秒就没。")]
        [SerializeField] [Range(0f, 1f)] private float _settle = 0.25f;

        [Tooltip("沙面位移尺度（米），必须与 SandGround 的 _DepthScale 一致 —— 只用来把休止角换算成归一化高差。")]
        [SerializeField] private float _surfaceScale = 0.30f;

        const float kContactGain = 5f;
        const float kMaxRimGain = 2.0f;
        const float kMinReposeTan = 0.364f, kMaxReposeTan = 0.839f;  // tan20° … tan40°
        const float kReposeRate = 2f;                                // 塌落速度（1/s）：压住时每帧重新堆，抬离后慢慢塌
        const float kMaxSettlePerSecond = 0.5f;

        static readonly int s_OriginTexID   = Shader.PropertyToID("_InteractionOriginTex");
        static readonly int s_PrevTexID     = Shader.PropertyToID("_SandPrevTex");
        static readonly int s_OutTexID      = Shader.PropertyToID("_SandOutTex");
        static readonly int s_SandTexID     = Shader.PropertyToID("_InteractionSandTex");
        static readonly int s_SandValidID   = Shader.PropertyToID("_InteractionSandValid");
        static readonly int s_RimID         = Shader.PropertyToID("_Rim");
        static readonly int s_ReposeLimitID = Shader.PropertyToID("_ReposeLimit");
        static readonly int s_ReposeRateID  = Shader.PropertyToID("_ReposeRate");
        static readonly int s_SettleID      = Shader.PropertyToID("_Settle");
        static readonly int s_ContactGainID = Shader.PropertyToID("_ContactGain");
        static readonly int s_DtID          = Shader.PropertyToID("_Dt");
        static readonly int s_DeltaPixelsID = Shader.PropertyToID("_DeltaPixels");
        static readonly int s_EdgeAbsorbID  = Shader.PropertyToID("_EdgeAbsorb");
        static readonly int s_ResolutionID  = Shader.PropertyToID("_Resolution");

        private RenderTexture _front;
        private RenderTexture _back;
        private int _kernel;

        // ════════════════════════════════════════════════════════

        protected override bool CreateResources()
        {
            if (_computeShader == null)
            {
                Debug.LogError("[SandInteractionProcessor] ComputeShader is null.", this);
                return false;
            }

            _kernel = _computeShader.FindKernel("CSSand");
            _front = CreateRT("_InteractionSandTex", Resolution);
            _back  = CreateRT("_InteractionSandBackTex", Resolution);
            ClearRT(_front);
            ClearRT(_back);
            return true;
        }

        protected override void BindTextures()
        {
            if (_computeShader == null || SourceRT == null || _front == null || _back == null)
                return;

            _computeShader.SetTexture(_kernel, s_OriginTexID, SourceRT);
            _computeShader.SetTexture(_kernel, s_PrevTexID,   _front);
            _computeShader.SetTexture(_kernel, s_OutTexID,    _back);
            _computeShader.SetInt(s_ResolutionID, Resolution);
        }

        protected override void ProcessStep(float deltaTime, Vector2 worldDelta)
        {
            if (_computeShader == null || _front == null || _back == null)
                return;

            float areaSize = UniversalInteractionManager.Instance != null
                ? UniversalInteractionManager.Instance.AreaSize : 10f;
            var pixelDelta = WorldDeltaToPixels(worldDelta, Resolution, areaSize);

            // 休止角 → 归一化高差：tanθ 是「每走 1 米水平允许多高」，
            // 一个纹素走 texelWorld 米，位移尺度 _surfaceScale 米/单位 → 换回场单位。
            float texelWorld = areaSize / Mathf.Max(Resolution, 1);
            float tanTheta   = Mathf.Lerp(kMinReposeTan, kMaxReposeTan, _repose);
            float reposeLimit = tanTheta * texelWorld / Mathf.Max(_surfaceScale, 1e-3f);

            _computeShader.SetFloat(s_RimID,         Mathf.Lerp(0f, kMaxRimGain, _rim));
            _computeShader.SetFloat(s_ReposeLimitID, reposeLimit);
            _computeShader.SetFloat(s_ReposeRateID,  kReposeRate);
            _computeShader.SetFloat(s_SettleID,      Mathf.Lerp(0f, kMaxSettlePerSecond, _settle));
            _computeShader.SetFloat(s_ContactGainID, kContactGain);
            _computeShader.SetFloat(s_DtID,          deltaTime);
            _computeShader.SetInts(s_DeltaPixelsID,  pixelDelta.x, pixelDelta.y);
            _computeShader.SetVector(s_EdgeAbsorbID, EdgeAbsorbPerStep(worldDelta, areaSize));

            int groups = Mathf.CeilToInt(Resolution / 8f);
            _computeShader.Dispatch(_kernel, groups, groups, 1);

            Swap(ref _front, ref _back);
            BindGlobals();
        }

        protected override void BindGlobals()
        {
            if (_front == null) return;
            Shader.SetGlobalTexture(s_SandTexID, _front);
            Shader.SetGlobalFloat(s_SandValidID, 1f);
        }

        public override void ResetGlobals()
        {
            Shader.SetGlobalTexture(s_SandTexID, Texture2D.blackTexture);
            Shader.SetGlobalFloat(s_SandValidID, 0f);
        }

        protected override void ReleaseResources()
        {
            ReleaseRT(ref _front);
            ReleaseRT(ref _back);
        }

        private static void Swap(ref RenderTexture a, ref RenderTexture b)
        {
            var temp = a;
            a = b;
            b = temp;
        }
    }
}
