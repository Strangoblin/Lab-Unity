using UnityEngine;

namespace Mine.Interaction
{
    /// <summary>
    /// 草地交互子集 — 踩踏压弯 + 回弹。
    ///
    /// 输出 _InteractionGrassTex（R = 压弯量 0..1）。渲染侧（GrassWind）用它的梯度
    /// 求「远离压点」的弯曲方向，因此这里只需要一个标量场。
    /// 物体离开后按 Spring Back 回弹；压着时保持压弯。
    /// </summary>
    [AddComponentMenu("Mine/Interaction/Grass Interaction Processor")]
    public class GrassInteractionProcessor : InteractionProcessor
    {
        public override string InteractionKey => "grass";

        [Header("Compute")]
        [SerializeField] private ComputeShader _computeShader;

        [Header("Grass Look")]
        [Tooltip("0 = 倒伏很久才恢复；1 = 一抬脚立刻弹回。")]
        [SerializeField] [Range(0f, 1f)] private float _springBack = 0.55f;

        // 捕获深度（材质 _Intensity 缩放后的 0..0.45）→ 压弯量的总增益。
        // 目前探针压入约 0.4 m × _Intensity 0.4 = 0.16，增益 5 → 0.8，接近满压。
        const float kContactGain = 5f;
        const float kMinRecovery = 0.2f, kMaxRecovery = 4f;   // 每秒

        static readonly int s_OriginTexID   = Shader.PropertyToID("_InteractionOriginTex");
        static readonly int s_PrevTexID     = Shader.PropertyToID("_GrassPrevTex");
        static readonly int s_OutTexID      = Shader.PropertyToID("_GrassOutTex");
        static readonly int s_GrassTexID    = Shader.PropertyToID("_InteractionGrassTex");
        static readonly int s_GrassValidID  = Shader.PropertyToID("_InteractionGrassValid");
        static readonly int s_RecoveryID    = Shader.PropertyToID("_Recovery");
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
                Debug.LogError("[GrassInteractionProcessor] ComputeShader is null.", this);
                return false;
            }

            _kernel = _computeShader.FindKernel("CSGrass");
            _front = CreateRT("_InteractionGrassTex", Resolution);
            _back  = CreateRT("_InteractionGrassBackTex", Resolution);
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

            _computeShader.SetFloat(s_RecoveryID,    Mathf.Lerp(kMinRecovery, kMaxRecovery, _springBack));
            _computeShader.SetFloat(s_ContactGainID, kContactGain);
            _computeShader.SetFloat(s_DtID,          deltaTime);
            _computeShader.SetInts(s_DeltaPixelsID,  pixelDelta.x, pixelDelta.y);
            _computeShader.SetVector(s_EdgeAbsorbID, EdgeAbsorbPerStep(worldDelta, areaSize));

            int groups = Mathf.CeilToInt(Resolution / 8f);
            _computeShader.Dispatch(_kernel, groups, groups, 1);

            Swap(ref _front, ref _back);
            BindGlobals();   // 立刻发布新 front，避免这一帧渲染读到上一帧的纹理
        }

        protected override void BindGlobals()
        {
            if (_front == null) return;
            Shader.SetGlobalTexture(s_GrassTexID, _front);
            Shader.SetGlobalFloat(s_GrassValidID, 1f);
        }

        public override void ResetGlobals()
        {
            Shader.SetGlobalTexture(s_GrassTexID, Texture2D.blackTexture);
            Shader.SetGlobalFloat(s_GrassValidID, 0f);
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
