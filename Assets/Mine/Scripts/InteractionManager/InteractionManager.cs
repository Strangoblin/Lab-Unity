using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.Rendering.Universal;
#endif

namespace Mine.Interaction
{
    /// <summary>
    /// 正交交互系统总控 — 共享捕获 + 多子集调度。
    ///
    /// 正交相机用 IA_Renderer（UniversalRendererData + InteractionCaptureFeature）渲染到 _InteractionOriginTex，
    /// Manager 只管理这一份共享输入（originRT + 正交矩阵 + 全局 Shader 属性）。
    /// 每种交互表面实现一个 <see cref="InteractionProcessor"/> 子集（Water / Grass / Snow / Sand …），
    /// 按 <see cref="InteractionProcessor.InteractionKey"/> 注册、按 InteractionOrder 排序、各自管理输出 RT。
    ///
    /// 场景里可以同时挂多个处理器：所有子集共用同一台交互相机、同一个跟随目标和同一块交互域。
    /// </summary>
    [ExecuteAlways]
    public class UniversalInteractionManager : MonoBehaviour
    {
        [Header("正交相机")]
        [SerializeField] private Camera    _orthoCamera;
        [SerializeField] private Transform _followTarget;
        [SerializeField] private int       _rendererIndex = 1;
        [SerializeField] private float     _areaSize      = 10f;
        [SerializeField] private float     _orthoHeight   = 5f;
        [SerializeField] private float     _orthoNear     = -10f;
        [SerializeField] private float     _orthoFar      = 10f;

        [Header("RT 设置")]
        [SerializeField] private int _resolution = 256;

#if UNITY_EDITOR
        [Header("编辑模式预览")]
        [Tooltip("不进入 Play 也能在 Scene 视图里看到交互（会持续消耗少量 GPU）。")]
        [SerializeField] private bool _editorPreview = true;
#endif

        // ── Shader Property IDs ─────────────────────────────────

        static readonly int s_InteractionOriginTexID = Shader.PropertyToID("_InteractionOriginTex");
        static readonly int s_InteractionOrthoVID    = Shader.PropertyToID("_InteractionOrthoV");
        static readonly int s_InteractionOrthoPID    = Shader.PropertyToID("_InteractionOrthoP");
        static readonly int s_InteractionAreaPosID   = Shader.PropertyToID("_InteractionAreaPos");
        static readonly int s_InteractionAreaSizeID  = Shader.PropertyToID("_InteractionAreaSize");
        static readonly int s_InteractionResID       = Shader.PropertyToID("_InteractionResolution");

        // ── 处理器注册表（与 FieldManager 同构）─────────────────

        private static readonly List<InteractionProcessor> Processors = new List<InteractionProcessor>();

        // ── 运行时 ──────────────────────────────────────────────

        private RenderTexture _originRT;
        private Matrix4x4     _orthoV, _orthoP;
        private Vector3       _lastAreaPos;
        private bool          _cameraWasEnabled = true;

        [ReadOnly]
        [SerializeField]
        private bool _initialized;

        // ── 公开属性 ────────────────────────────────────────────

        public static UniversalInteractionManager Instance { get; private set; }
        public static bool HasActiveManager => Instance != null && Instance.isActiveAndEnabled;

        public int Resolution => _resolution;
        public float AreaSize => _areaSize;

        public Matrix4x4 OrthoViewMatrix       => _orthoV;
        public Matrix4x4 OrthoProjectionMatrix => _orthoP;

        // ════════════════════════════════════════════════════════
        //  生命周期
        // ════════════════════════════════════════════════════════

        void OnEnable()
        {
            if (HasActiveManager && Instance != this)
            {
                Debug.LogError("Only one active UniversalInteractionManager may own the shared interaction capture.", this);
                enabled = false;
                return;
            }

            Instance = this;
            SyncMatrices();
            _lastAreaPos = SnappedCenter;
            EnsureInteractionRT();
            AttachAllProcessors();
            SyncGlobalProperties();
            _initialized = true;
#if UNITY_EDITOR
            EditorApplication.update += EditorTick;
#endif
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            EditorApplication.update -= EditorTick;
            _lastEditorTime     = 0d;
            _editorAccumulator  = 0d;
#endif
            if (Instance == this) Instance = null;
            DetachAllProcessors();
            ReleaseRT();
            _initialized = false;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (!_initialized) return;
#if UNITY_EDITOR
            // 编辑模式下只有 EditorTick 能驱动模拟：它在 Process 之前会重新渲染 originRT。
            // 这里的 Update 也会被编辑器调用（场景重绘、选中变化、鼠标移动…），
            // 它读到的 originRT 还是上一次 Tick 清空/遗留的内容 → 得到一个假 delta，
            // 一帧「球在」一帧「球不在」，冲量正负交替打进输出 —— 表现就是物体静止时表面一直抽搐。
            if (!Application.isPlaying) return;
#endif
            // Play 模式下 capture 也由 Manager 自己拍：相机组件是关的，不会在帧末自动渲一次。
            // 这样「delta 用的位姿」和「本帧捕获用的位姿」永远是同一个，
            // 不会出现「上一帧位置拍到的穿透被当成这一帧位置的结果」那种一步滞后。
            SyncMatrices();
            RenderOrigin();
            Tick(Time.deltaTime);
        }

        /// <summary>推进一帧：同步矩阵 → 处理（消费 originRT）→ 记录区域位置。</summary>
        private void Tick(float deltaTime)
        {
            SyncMatrices();
            SyncGlobalProperties();

            // 用吸附后的中心算位移：它只可能是纹素边长的整数倍，
            // 于是「相机走了多少」= 整数个纹素，重投影不需要插值也不留残差。
            var pos        = SnappedCenter;
            var worldDelta = new Vector2(pos.x - _lastAreaPos.x, pos.z - _lastAreaPos.z);

            for (int i = Processors.Count - 1; i >= 0; i--)
            {
                var processor = Processors[i];
                if (processor == null) { Processors.RemoveAt(i); continue; }
                if (!processor.isActiveAndEnabled) continue;
                // 处理器可能在 Manager 启用之后才注册/重新启用，这里补一次挂接。
                if (!processor.IsReady) AttachProcessor(processor);
                if (processor.IsReady) processor.Process(deltaTime, worldDelta);
            }

            _lastAreaPos = pos;

            // 不清空 originRT：Processor 用「本帧深度 − 上一帧深度」当输入，
            // 保留上一帧的捕获内容，万一某次没有渲染（相机禁用 / 渲染请求不支持），
            // 差值恰好是 0，系统安静而不是打进一个真假交替的冲量。
        }

#if UNITY_EDITOR
        // ── 编辑模式预览 ────────────────────────────────────────
        // [ExecuteAlways] 的 Update 在编辑器里只在场景变化/重绘时被调用，模拟不会自己推进。
        // 这里按固定频率主动做三件事：渲染一次正交相机 → 跑一次 Process → 刷新 Scene 视图。

        // ── 固定步长累加器 ──────────────────────────────────────
        // EditorApplication.update 的调用间隔不是恒定的（随编辑器负载在几十到几百 Hz 之间跳），
        // 之前用「下次允许时间」限流：跑快了丢掉多余的时间，跑慢了也不补，
        // 于是仿真实际推进速度 = min(update 频率, 30 Hz) 且步长抖动。
        // 改成累加真实流逝时间、只以固定步长 1/30 s 出步，慢一帧就补一步，快一帧就攒着。
        private const double EditorStepSeconds = 1.0 / EditorPreviewHz;
        private const double MaxCatchUpSeconds  = 0.25;   // 单次 update 最多补这么多，防止卡顿后雪崩
        private double _editorAccumulator;
        private double _lastEditorTime;

        private void EditorTick()
        {
            if (!_initialized || !_editorPreview || Application.isPlaying) return;

            double now = EditorApplication.timeSinceStartup;
            if (_lastEditorTime <= 0d)
            {
                _lastEditorTime = now;
                _editorAccumulator = 0d;
                return;
            }

            double elapsed = now - _lastEditorTime;
            _lastEditorTime = now;
            if (elapsed < 0d) return;                       // 时间回拨（极少见），丢掉这个间隔

            _editorAccumulator += System.Math.Min(elapsed, MaxCatchUpSeconds);

            bool stepped = false;
            while (_editorAccumulator >= EditorStepSeconds)
            {
                // 先把正交相机搬到本步的位置，再渲染。
                // 反过来（先渲染再 Tick）会拿「上一帧位姿」拍到的穿透，当成「本帧位姿」的结果塞进场里：
                // 压痕整体滞后一步，PingPong 掉头时滞后翻到另一侧，看起来就是折返点一顿。
                SyncMatrices();

                // 没渲染成功就不要推进：Process 读的是 originRT，跳过渲染等于拿旧数据做差值。
                // 攒下的时间也一并清掉，否则下次 update 会连补好几步旧数据。
                if (!RenderOrigin())
                {
                    _editorAccumulator = 0d;
                    break;
                }

                Tick((float)EditorStepSeconds);
                _editorAccumulator -= EditorStepSeconds;
                stepped = true;
            }

            // 只重绘 lastActiveSceneView 时，分屏/多 Scene 视图会停在旧画面。
            if (stepped) SceneView.RepaintAll();
        }
#endif

        /// <summary>
        /// 提交一次正交捕获。编辑模式下 URP 不会替带 Target Texture 的相机自动渲染，必须自己拍；
        /// Play 模式下相机组件被 Manager 关掉，也由这里拍 —— 保证捕获和 delta 用同一个位姿。
        /// 返回是否真的提交了。
        /// </summary>
        private bool RenderOrigin()
        {
            if (_orthoCamera == null || _originRT == null) return false;

            var request = new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest
            {
                destination = _originRT,
            };
            if (!RenderPipeline.SupportsRenderRequest(_orthoCamera, request)) return false;

            RenderPipeline.SubmitRenderRequest(_orthoCamera, request);
            return true;
        }

#if UNITY_EDITOR
        private const double EditorPreviewHz = 30d;
#endif

        void OnValidate()
        {
            SyncMatrices();
        }

        // ════════════════════════════════════════════════════════
        //  处理器注册与挂接
        // ════════════════════════════════════════════════════════

        /// <summary>注册一个交互子集。重复的 InteractionKey 会被拒绝（返回 false）。</summary>
        internal static bool Register(InteractionProcessor processor)
        {
            if (processor == null) return false;
            if (string.IsNullOrEmpty(processor.InteractionKey))
            {
                Debug.LogError("Interaction processor must declare a non-empty InteractionKey.", processor);
                return false;
            }

            for (int i = Processors.Count - 1; i >= 0; i--)
            {
                var other = Processors[i];
                if (other == null) { Processors.RemoveAt(i); continue; }
                if (other == processor) return true;
                if (string.Equals(other.InteractionKey, processor.InteractionKey, System.StringComparison.Ordinal))
                {
                    Debug.LogError("Duplicate interaction processor for '" + processor.InteractionKey +
                                   "'; disable the existing processor first.", processor);
                    return false;
                }
            }

            // Stable insertion: only processors with a strictly larger order move down.
            int index = Processors.Count;
            for (int i = 0; i < Processors.Count; i++)
            {
                var other = Processors[i];
                if (other != null && other.InteractionOrder > processor.InteractionOrder) { index = i; break; }
            }
            Processors.Insert(index, processor);

            if (HasActiveManager)
                Instance.AttachProcessor(processor);
            return true;
        }

        internal static void Unregister(InteractionProcessor processor)
        {
            Processors.Remove(processor);
        }

        /// <summary>按语义通道名查找当前启用且就绪的处理器。</summary>
        public static InteractionProcessor GetProcessor(string interactionKey)
        {
            if (string.IsNullOrEmpty(interactionKey)) return null;
            for (int i = Processors.Count - 1; i >= 0; i--)
            {
                var processor = Processors[i];
                if (processor == null) { Processors.RemoveAt(i); continue; }
                if (string.Equals(processor.InteractionKey, interactionKey, System.StringComparison.Ordinal)
                    && processor.isActiveAndEnabled && processor.IsReady) return processor;
            }
            return null;
        }

        private void AttachAllProcessors()
        {
            for (int i = Processors.Count - 1; i >= 0; i--)
            {
                if (Processors[i] == null) { Processors.RemoveAt(i); continue; }
                AttachProcessor(Processors[i]);
            }
        }

        private void AttachProcessor(InteractionProcessor processor)
        {
            if (processor == null || !processor.isActiveAndEnabled || _originRT == null) return;
            processor.Initialize(_resolution, _originRT);
        }

        private void DetachAllProcessors()
        {
            for (int i = Processors.Count - 1; i >= 0; i--)
            {
                var processor = Processors[i];
                if (processor == null) { Processors.RemoveAt(i); continue; }
                processor.Detach();
            }
        }

        // ════════════════════════════════════════════════════════
        //  正交矩阵
        // ════════════════════════════════════════════════════════

        private Vector3 AreaCenter => _followTarget != null ? _followTarget.position : transform.position;

        /// <summary>
        /// 吸附到纹素网格后的交互域中心 —— 相机、_InteractionAreaPos、位移全部以它为准。
        ///
        /// 正交视锥的左/下边界就是 center − areaSize/2；只要 center 是纹素边长的整数倍，
        /// 整张 RT 的纹素边界就落在固定的世界坐标上。相机跟随时「走了多少」因此必然是
        /// 整数个纹素，重投影可以整数搬移（见 _DeltaPixels），不会因为每帧丢掉
        /// 亚纹素残差而让压痕相对地面慢慢滑、或在慢速摆动时 0/±1 来回跳。
        /// </summary>
        private Vector3 SnappedCenter
        {
            get
            {
                var center = AreaCenter;
                float texel = _areaSize / Mathf.Max(_resolution, 1);
                if (texel <= 0f) return center;
                center.x = Mathf.Round(center.x / texel) * texel;
                center.z = Mathf.Round(center.z / texel) * texel;
                return center;
            }
        }

        private void SyncMatrices()
        {
            var center = SnappedCenter;
            var camPos = center + Vector3.up * _orthoHeight;
            float hs = _areaSize * 0.5f;

            // 世界 → 相机必须用相机约定的矩阵：Matrix4x4.LookAt 的 Y 轴是反的
            // （对俯视相机，它把世界 +Z 映射成 −Y），无相机时整块交互域会上下翻转：
            // 世界中心算出 uv.y≈1.17 → InteractionDomainMask 判成域外，采样全 0。
            // TRS(...).inverse 与 _orthoCamera.worldToCameraMatrix 的 xy 结果逐位一致。
            var rotation = Quaternion.LookRotation(center - camPos, Vector3.forward);
            _orthoV = Matrix4x4.TRS(camPos, rotation, Vector3.one).inverse;
            _orthoP = Matrix4x4.Ortho(-hs, hs, -hs, hs, _orthoNear, _orthoFar);

            // 同步到正交相机，确保渲染和采样用的矩阵完全一致
            if (_orthoCamera != null)
            {
                _orthoCamera.transform.SetPositionAndRotation(camPos, rotation);
                _orthoCamera.orthographicSize     = hs;
                _orthoCamera.nearClipPlane        = _orthoNear;
                _orthoCamera.farClipPlane         = _orthoFar;
                // 相机实际使用的矩阵（shader 用同一份做世界 → UV，必须取相机原值）
                _orthoV = _orthoCamera.worldToCameraMatrix;
                _orthoP = _orthoCamera.projectionMatrix;
            }
        }

        // ════════════════════════════════════════════════════════
        //  RT 管理 — Manager 只管理共享的 originRT
        // ════════════════════════════════════════════════════════

        private void EnsureInteractionRT()
        {
            if (_originRT != null) return;
            // Render Graph 要求相机输出纹理带深度缓冲，否则每帧告警并回退默认格式。
            _originRT = new RenderTexture(_resolution, _resolution, 24, RenderTextureFormat.RFloat)
            {
                name              = "_InteractionOriginTex",
                filterMode        = FilterMode.Bilinear,
                wrapMode          = TextureWrapMode.Clamp,
                useMipMap         = false,
                autoGenerateMips  = false,
            };
            _originRT.Create();
            ClearRT(_originRT);
            if (_orthoCamera != null)
            {
                _orthoCamera.targetTexture = _originRT;
                // 这台相机只由 Manager 手动提交渲染（见 RenderOrigin）：帧末再让它自己渲一次，
                // 会和本帧 Update 里那次重复，而且渲染时机会落到「已经消费完 originRT」之后，
                // 一帧的位姿和数值就对不上了。
                _cameraWasEnabled = _orthoCamera.enabled;
                _orthoCamera.enabled = false;
            }
        }

        private void ReleaseRT()
        {
            if (_orthoCamera != null)
            {
                _orthoCamera.targetTexture = null;
                _orthoCamera.enabled = _cameraWasEnabled;
            }

            if (_originRT != null)
            {
                _originRT.Release();
                if (Application.isPlaying) Destroy(_originRT);
                else                       DestroyImmediate(_originRT);
                _originRT = null;
            }
        }

        private static void ClearRT(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = prev;
        }

        // ════════════════════════════════════════════════════════
        //  全局 Shader 属性
        // ════════════════════════════════════════════════════════

        private void SyncGlobalProperties()
        {
            if (_originRT != null)
                Shader.SetGlobalTexture(s_InteractionOriginTexID, _originRT);

            // 各子集绑定自己的输出纹理
            for (int i = Processors.Count - 1; i >= 0; i--)
            {
                var processor = Processors[i];
                if (processor == null) { Processors.RemoveAt(i); continue; }
                if (processor.isActiveAndEnabled) processor.BindGlobalTextures();
            }

            Shader.SetGlobalMatrix(s_InteractionOrthoVID,  _orthoV);
            Shader.SetGlobalMatrix(s_InteractionOrthoPID,  _orthoP);
            // 与矩阵同源（都是吸附后的中心），渲染侧采样才不会和模拟网格错半格。
            Shader.SetGlobalVector(s_InteractionAreaPosID,  SnappedCenter);
            Shader.SetGlobalFloat(s_InteractionAreaSizeID, _areaSize);
            Shader.SetGlobalInt(s_InteractionResID,        _resolution);
        }

        // ════════════════════════════════════════════════════════
        //  Editor
        // ════════════════════════════════════════════════════════

#if UNITY_EDITOR
        [CustomEditor(typeof(UniversalInteractionManager))]
        public class UniversalInteractionManagerEditor : Editor
        {
            public override void OnInspectorGUI()
            {
                DrawDefaultInspector();

                var t = (UniversalInteractionManager)target;
                EditorGUILayout.Space();

                if (t._initialized)
                    EditorGUILayout.HelpBox("已初始化（共享捕获 + " + Processors.Count + " 个交互子集）", MessageType.Info);
                else
                    EditorGUILayout.HelpBox("未初始化", MessageType.Warning);
            }
        }
#endif
    }
}
