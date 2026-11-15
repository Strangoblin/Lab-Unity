using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.Rendering.Universal;
#endif

namespace Mine.Interaction
{
    /// <summary>
    /// 正交交互系统总控。
    ///
    /// 正交相机用 IA_Renderer（UniversalRendererData + InteractionCaptureFeature）渲染到 _InteractionOriginTex，
    /// Manager 管理共享的 originRT + 正交矩阵 + 全局 Shader 属性。
    /// 输出 RT 由 IUniversalInteractionProcessor 自行管理。
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
        [Tooltip("不进入 Play 也能在 Scene 视图里看到水面波动（会持续消耗少量 GPU）。")]
        [SerializeField] private bool _editorPreview = true;
#endif

        // ── Shader Property IDs ─────────────────────────────────

        static readonly int s_InteractionOriginTexID = Shader.PropertyToID("_InteractionOriginTex");
        static readonly int s_InteractionOrthoVID    = Shader.PropertyToID("_InteractionOrthoV");
        static readonly int s_InteractionOrthoPID    = Shader.PropertyToID("_InteractionOrthoP");
        static readonly int s_InteractionAreaPosID   = Shader.PropertyToID("_InteractionAreaPos");
        static readonly int s_InteractionAreaSizeID  = Shader.PropertyToID("_InteractionAreaSize");
        static readonly int s_InteractionResID       = Shader.PropertyToID("_InteractionResolution");

        // ── 运行时 ──────────────────────────────────────────────

        private RenderTexture                _originRT;
        private IUniversalInteractionProcessor _processor;
        private Matrix4x4                    _orthoV, _orthoP;
        private Vector3                      _lastAreaPos;

        [ReadOnly]
        [SerializeField]
        private bool _initialized;

        // ── 公开属性 ────────────────────────────────────────────

        public static UniversalInteractionManager Instance { get; private set; }

        public int Resolution => _resolution;
        public float AreaSize => _areaSize;

        public Matrix4x4 OrthoViewMatrix       => _orthoV;
        public Matrix4x4 OrthoProjectionMatrix => _orthoP;

        // ════════════════════════════════════════════════════════
        //  生命周期
        // ════════════════════════════════════════════════════════

        void OnEnable()
        {
            Instance = this;
            _lastAreaPos = AreaCenter;
            FindProcessor();
            SyncMatrices();
            EnsureInteractionRT();
            InitializeProcessor();
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
            _nextEditorTick = 0d;
#endif
            if (Instance == this) Instance = null;
            ReleaseProcessor();
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
            Tick(Time.deltaTime);
        }

        /// <summary>推进一帧：同步矩阵 → 处理（消费上一帧的 originRT）→ 清空 originRT 等下一次捕获。</summary>
        private void Tick(float deltaTime)
        {
            SyncMatrices();
            SyncGlobalProperties();

            var pos        = AreaCenter;
            var worldDelta = new Vector2(pos.x - _lastAreaPos.x, pos.z - _lastAreaPos.z);
            _processor?.Process(deltaTime, worldDelta);
            _lastAreaPos = pos;

            if (_originRT != null) ClearRT(_originRT);
        }

#if UNITY_EDITOR
        // ── 编辑模式预览 ────────────────────────────────────────
        // [ExecuteAlways] 的 Update 在编辑器里只在场景变化/重绘时被调用，波方程不会自己推进。
        // 这里按固定频率主动做三件事：渲染一次正交相机 → 跑一次 Process → 刷新 Scene 视图。

        private double _nextEditorTick;

        private void EditorTick()
        {
            if (!_initialized || !_editorPreview || Application.isPlaying) return;

            double now = EditorApplication.timeSinceStartup;
            if (now < _nextEditorTick) return;
            _nextEditorTick = now + 1.0 / EditorPreviewHz;

            RenderOriginInEditor();
            Tick((float)(1.0 / EditorPreviewHz));

            // 只重绘 lastActiveSceneView 时，分屏/多 Scene 视图会停在旧画面。
            SceneView.RepaintAll();
        }

        /// <summary>
        /// 编辑模式下 URP 不会替带 Target Texture 的相机动渲染，必须自己提交一次渲染请求，
        /// 否则 originRT 永远是空的，看起来就是「水面不响应」。
        /// </summary>
        private void RenderOriginInEditor()
        {
            if (_orthoCamera == null || _originRT == null) return;

            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = _originRT };
            if (!RenderPipeline.SupportsRenderRequest(_orthoCamera, request)) return;

            RenderPipeline.SubmitRenderRequest(_orthoCamera, request);
        }

        private const double EditorPreviewHz = 30d;
#endif

        void OnValidate()
        {
            SyncMatrices();
        }

        // ════════════════════════════════════════════════════════
        //  Processor 发现与初始化
        // ════════════════════════════════════════════════════════

        private void FindProcessor()
        {
            if (_processor == null)
                _processor = GetComponent<IUniversalInteractionProcessor>();
        }

        private void InitializeProcessor()
        {
            if (_processor == null || _originRT == null) return;
            _processor.Initialize(_resolution, _originRT);
        }

        private void ReleaseProcessor()
        {
            _processor?.Release();
            _processor = null;
        }

        // ════════════════════════════════════════════════════════
        //  正交矩阵
        // ════════════════════════════════════════════════════════

        private Vector3 AreaCenter => _followTarget != null ? _followTarget.position : transform.position;

        private void SyncMatrices()
        {
            var center = AreaCenter;
            var camPos = center + Vector3.up * _orthoHeight;
            _orthoV = Matrix4x4.LookAt(camPos, center, Vector3.forward);
            float hs = _areaSize * 0.5f;
            _orthoP = Matrix4x4.Ortho(-hs, hs, -hs, hs, _orthoNear, _orthoFar);

            // 同步到正交相机，确保渲染和采样用的矩阵完全一致
            if (_orthoCamera != null)
            {
                _orthoCamera.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(center - camPos, Vector3.forward));
                _orthoCamera.orthographicSize     = hs;
                _orthoCamera.nearClipPlane        = _orthoNear;
                _orthoCamera.farClipPlane         = _orthoFar;
                // 相机实际使用的 GPU 矩阵
                _orthoV = _orthoCamera.worldToCameraMatrix;
                _orthoP = _orthoCamera.projectionMatrix;
            }
            else
            {
                _orthoP = GL.GetGPUProjectionMatrix(_orthoP, false);
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
            if (_orthoCamera != null) _orthoCamera.targetTexture = _originRT;
        }

        private void ReleaseRT()
        {
            if (_orthoCamera != null) _orthoCamera.targetTexture = null;

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

            // 委托 processor 绑定自己的输出纹理
            _processor?.BindGlobalTextures();

            Shader.SetGlobalMatrix(s_InteractionOrthoVID,  _orthoV);
            Shader.SetGlobalMatrix(s_InteractionOrthoPID,  _orthoP);
            Shader.SetGlobalVector(s_InteractionAreaPosID,  AreaCenter);
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
                    EditorGUILayout.HelpBox("已初始化", MessageType.Info);
                else
                    EditorGUILayout.HelpBox("未初始化", MessageType.Warning);
            }
        }
#endif
    }
}
