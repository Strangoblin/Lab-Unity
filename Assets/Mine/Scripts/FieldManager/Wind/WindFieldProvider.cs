using UnityEngine;

namespace Mine.Fields
{
    /// <summary>Owns one finite XZ wind-speed texture; consumers apply a shared analytic height profile.</summary>
    public sealed class WindFieldProvider : FieldProvider
    {
        public enum Quality { Low, Medium, High }

        [Header("Technical")]
        [SerializeField] private ComputeShader _computeShader;
        [SerializeField] private Transform _followTarget;
        [SerializeField] private Vector2 _areaSize = new Vector2(64f, 64f);
        [SerializeField] private float _referenceHeight;
        [SerializeField, Min(0.01f)] private float _heightRange = 20f;

        [Header("Performance")]
        [SerializeField] private Quality _quality = Quality.Medium;

        [Header("Artistic")]
        [SerializeField] private Vector2 _baseWind = new Vector2(2f, 0.3f);
        [SerializeField, Min(0f)] private float _gustStrength = 0.5f;
        [SerializeField, Min(0f)] private float _turbulence = 0.6f;
        [SerializeField, Min(0.01f)] private float _turbulenceLength = 20f;
        [SerializeField, Min(0f)] private float _lowerHeightGain = 0.3f;
        [SerializeField, Min(0f)] private float _upperHeightGain = 1.3f;

        [Header("Debug")]
        [SerializeField] private bool _showArea = true;

        private const int MaxSources = 16;
        private RenderTexture _texture;
        private int _kernel;
        private Vector4 _mapping, _height, _baseVelocity;
        private readonly Vector4[] _sourcePositions = new Vector4[MaxSources];
        private readonly Vector4[] _sourceVelocities = new Vector4[MaxSources];
        private bool _warnedSourceLimit;

        private static readonly int TextureId = Shader.PropertyToID("_WindFieldTexture");
        private static readonly int MappingId = Shader.PropertyToID("_WindFieldWorldToUV");
        private static readonly int HeightId = Shader.PropertyToID("_WindFieldHeight");
        private static readonly int BaseId = Shader.PropertyToID("_WindFieldBaseVelocity");
        private static readonly int ValidId = Shader.PropertyToID("_WindFieldValid");
        private static readonly int TimeId = Shader.PropertyToID("_WindFieldTime");
        private static readonly int VersionId = Shader.PropertyToID("_WindFieldVersion");

        public override FieldKind Kind => FieldKind.Wind;
        public RenderTexture Texture => _texture;
        public Vector3 BaseVelocity => _baseVelocity;
        private int Resolution => _quality == Quality.Low ? 64 : _quality == Quality.High ? 256 : 128;

        // ════════════════════════════════════════════════════════════
        //  Resource ownership and spatial generation
        // ════════════════════════════════════════════════════════════
        protected override bool InitializeField()
        {
            if (_computeShader == null)
            {
                Debug.LogError("WindFieldProvider requires WindField.compute.", this);
                return false;
            }
            _kernel = _computeShader.FindKernel("BuildWind");
            EnsureTexture();
            return true;
        }

        private void EnsureTexture()
        {
            if (_texture != null && _texture.width == Resolution) return;
            ReleaseField();
            _texture = new RenderTexture(Resolution, Resolution, 0, RenderTextureFormat.ARGBHalf)
            {
                name = "SharedWindVelocityXZ",
                enableRandomWrite = true,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                useMipMap = false,
                autoGenerateMips = false
            };
            _texture.Create();
        }

        protected override void SimulateField(float time, float deltaTime)
        {
            EnsureTexture();
            Vector3 center = _followTarget != null ? _followTarget.position : transform.position;
            Vector2 size = new Vector2(Mathf.Max(0.01f, _areaSize.x), Mathf.Max(0.01f, _areaSize.y));
            Vector2 origin = new Vector2(center.x, center.z) - size * 0.5f;
            _mapping = new Vector4(origin.x, origin.y, 1f / size.x, 1f / size.y);
            _height = new Vector4(_referenceHeight, Mathf.Max(0.01f, _heightRange),
                Mathf.Max(0f, _lowerHeightGain), Mathf.Max(0f, _upperHeightGain));
            Vector2 direction = _baseWind.sqrMagnitude > 0.000001f ? _baseWind.normalized : Vector2.zero;
            Vector2 wind = _baseWind + direction * (Mathf.Sin(time * 0.7f) * Mathf.Max(0f, _gustStrength));
            _baseVelocity = new Vector4(wind.x, 0f, wind.y, 0f);

            int count = UploadSources();
            _computeShader.SetInt("_Resolution", Resolution);
            _computeShader.SetVector("_OriginSize", new Vector4(origin.x, origin.y, size.x, size.y));
            _computeShader.SetVector("_BaseVelocity", _baseVelocity);
            _computeShader.SetFloat("_FieldTime", time);
            _computeShader.SetFloat("_Turbulence", Mathf.Max(0f, _turbulence));
            _computeShader.SetFloat("_TurbulenceLength", Mathf.Max(0.01f, _turbulenceLength));
            _computeShader.SetVectorArray("_SourcePositionRadius", _sourcePositions);
            _computeShader.SetVectorArray("_SourceVelocity", _sourceVelocities);
            _computeShader.SetInt("_SourceCount", count);
            _computeShader.SetTexture(_kernel, "_WindOutput", _texture);
            _computeShader.Dispatch(_kernel, Resolution / 8, Resolution / 8, 1);
        }

        private int UploadSources()
        {
            int count = 0;
            foreach (var source in WindSource.ActiveSources)
            {
                if (source == null || !source.isActiveAndEnabled) continue;
                if (count == MaxSources)
                {
                    if (!_warnedSourceLimit)
                        Debug.LogWarning("WindField supports 16 local sources; excess sources are ignored.", this);
                    _warnedSourceLimit = true;
                    break;
                }
                Vector3 position = source.transform.position;
                Vector3 velocity = source.Velocity;
                _sourcePositions[count] = new Vector4(position.x, position.z, source.Radius, 0f);
                _sourceVelocities[count] = new Vector4(velocity.x, velocity.y, velocity.z, 0f);
                count++;
            }
            return count;
        }

        // ════════════════════════════════════════════════════════════
        //  Publication and explicit per-kernel bindings
        // ════════════════════════════════════════════════════════════
        protected override void PublishField()
        {
            Shader.SetGlobalTexture(TextureId, _texture);
            Shader.SetGlobalVector(MappingId, _mapping);
            Shader.SetGlobalVector(HeightId, _height);
            Shader.SetGlobalVector(BaseId, _baseVelocity);
            Shader.SetGlobalFloat(ValidId, 1f);
            Shader.SetGlobalFloat(TimeId, SampleTime);
            Shader.SetGlobalFloat(VersionId, Version);
        }

        public override void BindCompute(ComputeShader shader, int kernel)
        {
            shader.SetTexture(kernel, TextureId, _texture);
            shader.SetVector(MappingId, _mapping);
            shader.SetVector(HeightId, _height);
            shader.SetVector(BaseId, _baseVelocity);
            shader.SetFloat(ValidId, 1f);
            shader.SetFloat(TimeId, SampleTime);
            shader.SetFloat(VersionId, Version);
        }

        public static void BindNeutral(ComputeShader shader, int kernel)
        {
            shader.SetTexture(kernel, TextureId, Texture2D.blackTexture);
            shader.SetVector(MappingId, new Vector4(0f, 0f, 1f, 1f));
            shader.SetVector(HeightId, new Vector4(0f, 1f, 1f, 1f));
            shader.SetVector(BaseId, Vector4.zero);
            shader.SetFloat(ValidId, 0f);
            shader.SetFloat(TimeId, 0f);
            shader.SetFloat(VersionId, 0f);
        }

        public override void ResetGlobals()
        {
            Shader.SetGlobalTexture(TextureId, Texture2D.blackTexture);
            Shader.SetGlobalVector(MappingId, new Vector4(0f, 0f, 1f, 1f));
            Shader.SetGlobalVector(HeightId, new Vector4(0f, 1f, 1f, 1f));
            Shader.SetGlobalVector(BaseId, Vector4.zero);
            Shader.SetGlobalFloat(ValidId, 0f);
            Shader.SetGlobalFloat(TimeId, 0f);
            Shader.SetGlobalFloat(VersionId, 0f);
        }

        protected override void ReleaseField()
        {
            if (_texture == null) return;
            _texture.Release();
            if (Application.isPlaying) Destroy(_texture); else DestroyImmediate(_texture);
            _texture = null;
        }

        private void OnDrawGizmosSelected()
        {
            if (!_showArea) return;
            Vector3 center = _followTarget != null ? _followTarget.position : transform.position;
            center.y = _referenceHeight;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(center, new Vector3(_areaSize.x, 0f, _areaSize.y));
        }
    }
}
