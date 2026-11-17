using UnityEngine;

namespace Mine.Interaction
{
    /// <summary>
    /// 水面交互子集 — Verlet 积分求解经典 2D 波动方程。
    ///
    /// CSWave kernel: 8×8 线程组
    /// - 读取 _InteractionOriginTex (当前帧正交相机渲染的原始输入)
    /// - 读取 _InteractionWaterTex (当前高度场)
    /// - 读取 _InteractionWaterPTex (上一帧快照，邻域采样无数据竞争)
    /// - 输出 _InteractionWaterTex
    ///
    /// 通过 <see cref="InteractionProcessor"/> 注册到 UniversalInteractionManager；
    /// 可以与 Grass / Snow / Sand 子集共存于同一个 Manager。
    /// </summary>
    [AddComponentMenu("Mine/Interaction/Water Interaction Processor")]
    public class WaterInteractionProcessor : InteractionProcessor
    {
        public override string InteractionKey => "water";

        [Header("Compute")]
        [SerializeField] private ComputeShader _computeShader;

        [Header("Water Look")]
        [Tooltip("0 = 浑浊：反应慢、传播近、振幅小、余波很快散掉；\n" +
                 "1 = 清澈：反应快、传播远、振幅大、余波停留久。\n" +
                 "波速 / 每秒衰减 / 粘稠度 / 步进频率都由它派生，不要再单独调。\n" +
                 "物体互动的强弱不在这里：那是每个物体自己材质的 _Intensity。")]
        [SerializeField] [Range(0f, 1f)] private float _clarity = 0.5f;

        // ── clarity → 三个物理量的映射 ─────────────────────────────
        // 端点经过配平：clarity = 0.5 正好复现原来手调的那组
        //   波速 0.15 / 每秒衰减 0.14（≈ 旧 0.995 每帧 @30 Hz）/ 粘稠度 0.08。
        // 衰减与粘稠度用几何插值：两者都是「小值才有意义」的量，线性插值会让中间段衰减过猛。
        const float kTurbidWaveSpeed = 0.08f, kClearWaveSpeed = 0.22f;
        const float kTurbidDecay     = 0.80f, kClearDecay     = 0.025f;   // 每秒振幅衰减率
        const float kTurbidViscosity = 0.40f, kClearViscosity = 0.016f;

        // 冲量增益固定 1：物体互动的强弱归物体自己管（材质 `_Intensity` 缩放捕获到的穿透深度），
        // 水体只负责「怎么传」，不再替所有物体统一调音量。
        const float kObjectForce = 1f;

        // 仿真的步进频率（步/秒）。隐式格式里波速 c 受 CFL 限制（五点拉普拉斯 c ≤ 0.25），
        // 靠调 c 最多只能再快几个百分点；真正能乘上去的是步进频率：
        //   传播速度 = √c × (面积/分辨率) × 步进频率
        // 30 Hz / c=0.22 / 256 px @10 m 只有 0.55 m/s（跨 10 m 要 18 s），所以清澈端跑 120 步/s。
        // 输出钳位与冲量注入都在第一子步完成，其余子步只推进波动方程。
        const float kTurbidStepsPerSecond = 30f, kClearStepsPerSecond = 120f;
        const int   kMaxSubsteps = 8;   // 帧长很长时（卡顿 / 慢速 Play）别把 dispatch 数量放大到失控
        // 端点系数（衰减 / 粘稠度）是在这个步频下手调的，子步折算都以它为基准。
        const float kReferenceStepsPerSecond = 30f;

        // 边界海绵带的最大吸收率（每秒，贴到 RT 最外圈时）。带内按 (1 − 窗口) 线性渐强，
        // 域心为 0。太小波澜会撞到边界弹回来，太大波刚进带子就没了 —— 12 在带宽 8%（10 m 域上
        // 0.8 m）时刚好让波在带里衰减掉 99% 以上而不显得撞墙。
        const float kSpongeRate = 12f;

        // ── Shader Property IDs ─────────────────────────────────

        static readonly int s_InteractionOriginTexID = Shader.PropertyToID("_InteractionOriginTex");
        static readonly int s_InteractionOriginPrevTexID = Shader.PropertyToID("_InteractionOriginPrevTex");
        static readonly int s_InteractionWaterTexID  = Shader.PropertyToID("_InteractionWaterTex");
        static readonly int s_InteractionWaterPTexID = Shader.PropertyToID("_InteractionWaterPTex");
        static readonly int s_WaveSpeedID   = Shader.PropertyToID("_WaveSpeed");
        static readonly int s_DampingID     = Shader.PropertyToID("_Damping");
        static readonly int s_ObjectForceID = Shader.PropertyToID("_ObjectForce");
        static readonly int s_ViscosityID   = Shader.PropertyToID("_Viscosity");
        static readonly int s_SpongeID      = Shader.PropertyToID("_Sponge");
        static readonly int s_DeltaPixelsID = Shader.PropertyToID("_DeltaPixels");
        static readonly int s_EdgeAbsorbID  = Shader.PropertyToID("_EdgeAbsorb");
        static readonly int s_ResolutionID  = Shader.PropertyToID("_Resolution");
        static readonly int s_PrimedID      = Shader.PropertyToID("_Primed");

        // ── 运行时 ──────────────────────────────────────────────

        private RenderTexture _waterTex;
        private RenderTexture _waterPTex;
        private RenderTexture _originPrevTex;   // 上一帧穿透深度（帧间差值）
        private bool _primed;                   // 上一帧深度 RT 还没有内容
        private bool _bound;
        int _kernel;

        // ════════════════════════════════════════════════════════
        //  InteractionProcessor
        // ════════════════════════════════════════════════════════

        protected override bool CreateResources()
        {
            if (_computeShader == null)
            {
                Debug.LogError("[WaterInteractionProcessor] ComputeShader is null.", this);
                return false;
            }

            _kernel = _computeShader.FindKernel("CSWave");

            // 输出 RT
            _waterTex = CreateRT("_InteractionWaterTex", Resolution);
            // Ping-Pong 快照 RT
            _waterPTex = CreateRT("_InteractionWaterPTex", Resolution);
            // 上一帧穿透深度：物体交互取帧间差值，静止的物体 delta = 0
            _originPrevTex = CreateRT("_InteractionOriginPrevTex", Resolution);
            _primed = false;   // 新 RT 是空的，第一帧只能记录深度，不能拿来算差值
            _bound  = false;
            return true;
        }

        /// <summary>
        /// 重新绑定纹理。compute 资产被重新导入后 shader 实例内部会重建、旧的绑定静默失效，
        /// 表现是 Dispatch 不报错但 RT 全零（日志里偶发 "Property ... is not set"）。
        /// 每帧绑定一次，代价可忽略，省掉「改完 compute 必须重开场景」这条坑。
        /// </summary>
        protected override void BindTextures()
        {
            if (_computeShader == null || SourceRT == null ||
                _originPrevTex == null || _waterTex == null || _waterPTex == null)
            {
                _bound = false;
                return;
            }

            _computeShader.SetTexture(_kernel, s_InteractionOriginTexID, SourceRT);
            _computeShader.SetTexture(_kernel, s_InteractionOriginPrevTexID, _originPrevTex);
            _computeShader.SetTexture(_kernel, s_InteractionWaterTexID,  _waterTex);
            _computeShader.SetTexture(_kernel, s_InteractionWaterPTexID, _waterPTex);
            _computeShader.SetInt(s_ResolutionID, Resolution);
            _bound = true;
        }

        protected override void ProcessStep(float deltaTime, Vector2 worldDelta)
        {
            // Release() 会把 RT 置空；域重载后残留的编辑模式 tick 仍可能调到这里，
            // 此时 dispatch 会报 "Property ... is not set"。
            if (!_bound) return;

            var mgr = UniversalInteractionManager.Instance;
            float areaSize = mgr != null ? mgr.AreaSize : 10f;

            // 世界位移 → 像素偏移
            var pixelDelta = WorldDeltaToPixels(worldDelta, Resolution, areaSize);
            _computeShader.SetInts(s_DeltaPixelsID, pixelDelta.x, pixelDelta.y);
            // 位移吸收按**帧**给一次（冲量只在第一子步注入，其余子步不再移动域）
            _computeShader.SetVector(s_EdgeAbsorbID, EdgeAbsorbPerStep(worldDelta, areaSize));

            // 美术参数 → 物理量
            float clarity = Mathf.Clamp01(_clarity);
            float waveSpeed   = Mathf.Lerp(kTurbidWaveSpeed, kClearWaveSpeed, clarity);
            float decayPerSec = ExpLerp(kTurbidDecay,     kClearDecay,     clarity);
            float viscosity   = ExpLerp(kTurbidViscosity, kClearViscosity, clarity);

            // 本帧拆成几个子步：帧长 × 目标步频，四舍五入（最少 1 步）。
            // 子步等分帧长，所以在任何帧率下每秒的步数都等于 stepsPerSecond，传播速度与帧率无关。
            float dt             = Mathf.Max(deltaTime, 1e-4f);
            float stepsPerSecond = Mathf.Lerp(kTurbidStepsPerSecond, kClearStepsPerSecond, clarity);
            int substeps         = Mathf.Clamp(Mathf.RoundToInt(stepsPerSecond * dt), 1, kMaxSubsteps);
            float subDt          = dt / substeps;

            // 每秒衰减率 → 单个子步的保留率；各子步连乘正好等于整帧的保留率。
            float retention = Mathf.Pow(1f - decayPerSec, subDt);

            // 粘稠度是「每步」的速度阻尼（kernel 里 h_new -= γ·velocity），不是每秒量。
            // 子步变多时必须按子步长折回，否则清澈端 4 子步 = 每秒被阻尼 4 次，
            // 表现为「传播快了但一动就没了」。以 30 Hz 为标定基准，γ_step = 1 − (1−γ)^(30·subDt)。
            float viscosityStep = 1f - Mathf.Pow(1f - viscosity, subDt * kReferenceStepsPerSecond);

            // 像素空间系数，归一化到 areaSize=10 的行为不变
            float shaderWs = waveSpeed * (10f / areaSize) * (10f / areaSize);
            _computeShader.SetFloat(s_WaveSpeedID, shaderWs);
            _computeShader.SetFloat(s_DampingID,     retention);
            _computeShader.SetFloat(s_ObjectForceID, kObjectForce);
            _computeShader.SetFloat(s_ViscosityID,   viscosityStep);
            // 边界海绵带：吸收率正比于 (1 - 窗口)，在域心为 0、贴着边界最强。
            // 按 subDt 折算，所以每秒的吸收量与帧率 / 子步数无关；波是动的，必须真的耗能，
            // 否则会从域边界那条 0 墙反射回来（印记类子集才用「乘窗口」那种纯空间做法）。
            _computeShader.SetFloat(s_SpongeID, kSpongeRate * subDt);

            int threadGroups = Mathf.CeilToInt(Resolution / 8f);
            for (int i = 0; i < substeps; i++)
            {
                // 只有第一个子步消费本帧的输入：像素偏移是帧间的（用完必须清零），
                // 冲量也只在深度有变化的这一帧注入一次，后续子步纯推进波动方程。
                // 位移吸收同理：域一帧只移动一次，不能在每个子步都再吃一遍。
                if (i > 0)
                {
                    _computeShader.SetInts(s_DeltaPixelsID, 0, 0);
                    _computeShader.SetVector(s_EdgeAbsorbID, Vector4.zero);
                }
                _computeShader.SetInt(s_PrimedID, i > 0 || _primed ? 1 : 0);
                _computeShader.Dispatch(_kernel, threadGroups, threadGroups, 1);
            }
            _primed = true;
        }

        protected override void BindGlobals()
        {
            if (_waterTex != null)
                Shader.SetGlobalTexture(s_InteractionWaterTexID, _waterTex);
        }

        public override void ResetGlobals()
        {
            Shader.SetGlobalTexture(s_InteractionWaterTexID, Texture2D.blackTexture);
        }

        protected override void ReleaseResources()
        {
            // 不用 SetTexture(..., null) 解绑：ComputeShader.SetTexture 拒绝 null，
            // 会抛 ArgumentNullException 并让下面的 ReleaseRT 一条都执行不到（RT 泄漏）。
            // 这里只释放 RT；下次 Initialize 会重新绑定，中间没有任何 Dispatch。
            ReleaseRT(ref _waterTex);
            ReleaseRT(ref _waterPTex);
            ReleaseRT(ref _originPrevTex);
            _bound  = false;
            _primed = false;
        }

        // ════════════════════════════════════════════════════════
        //  Helpers
        // ════════════════════════════════════════════════════════

        /// <summary>几何插值：t=0 → a，t=1 → b，中点取 √(ab)。用于跨数量级的衰减类系数。</summary>
        private static float ExpLerp(float a, float b, float t)
        {
            return Mathf.Exp(Mathf.Lerp(Mathf.Log(a), Mathf.Log(b), t));
        }
    }
}
