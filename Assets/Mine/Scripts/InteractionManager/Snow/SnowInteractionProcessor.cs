using UnityEngine;

namespace Mine.Interaction
{
    /// <summary>
    /// 雪地交互子集 — 向下压实 + 蠕变摊圆 + 沉降消失。
    ///
    /// 输出 _InteractionSnowTex（R = 压痕深度 0..1）。渲染侧（SnowGround）把它位移成
    /// 真实的雪面凹陷，并用梯度求法线。
    ///
    /// 雪是大形变、可压实的材料：受压只有「压下去」，不产生侧向堆脊（对比沙地）。
    /// 痕迹消失走乘性衰减，形状保持不变、整体等比变浅。
    /// </summary>
    [AddComponentMenu("Mine/Interaction/Snow Interaction Processor")]
    public class SnowInteractionProcessor : InteractionProcessor
    {
        public override string InteractionKey => "snow";

        [Header("Compute")]
        [SerializeField] private ComputeShader _computeShader;

        [Header("Snow Look")]
        [Tooltip("蠕变：压痕向邻域扩散、坑壁摊圆的速度。0 = 坑壁保持一道直壁。")]
        [SerializeField] [Range(0f, 1f)] private float _creep = 0.3f;

        [Tooltip("沉降：压痕整体变浅直到消失的速度。0 = 永久保留，1 = 一两秒就没。")]
        [SerializeField] [Range(0f, 1f)] private float _settle = 0.15f;

        const float kContactGain = 5f;
        const float kMaxCreepPerSecond = 6f;
        const float kMaxSettlePerSecond = 0.5f;

        static readonly int s_OriginTexID   = Shader.PropertyToID("_InteractionOriginTex");
        static readonly int s_PrevTexID     = Shader.PropertyToID("_SnowPrevTex");
        static readonly int s_OutTexID      = Shader.PropertyToID("_SnowOutTex");
        static readonly int s_SnowTexID     = Shader.PropertyToID("_InteractionSnowTex");
        static readonly int s_SnowValidID   = Shader.PropertyToID("_InteractionSnowValid");
        static readonly int s_CreepID       = Shader.PropertyToID("_Creep");
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
                Debug.LogError("[SnowInteractionProcessor] ComputeShader is null.", this);
                return false;
            }

            _kernel = _computeShader.FindKernel("CSSnow");
            _front = CreateRT("_InteractionSnowTex", Resolution);
            _back  = CreateRT("_InteractionSnowBackTex", Resolution);
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

            _computeShader.SetFloat(s_CreepID,       Mathf.Lerp(0f, kMaxCreepPerSecond, _creep));
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
            Shader.SetGlobalTexture(s_SnowTexID, _front);
            Shader.SetGlobalFloat(s_SnowValidID, 1f);
        }

        public override void ResetGlobals()
        {
            Shader.SetGlobalTexture(s_SnowTexID, Texture2D.blackTexture);
            Shader.SetGlobalFloat(s_SnowValidID, 0f);
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
