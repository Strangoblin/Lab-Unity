using UnityEngine;
using Mine.GizmoRendering;

namespace Mine.Fields
{
    /// <summary>Owns one finite XZ wind-speed texture; consumers apply a shared analytic height profile.</summary>
    public sealed class WindFieldProvider : FieldProvider, IGizmoSource
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
        [Tooltip("Wind direction on the XZ plane, in degrees. 0 = +X, 90 = +Z.")]
        [SerializeField] private float _windAngle = 8.53f;
        [Tooltip("Steady wind force published as velocity (m/s). Drives displacement; does not move the pattern.")]
        [SerializeField, Min(0f)] private float _windStrength = 2f;
        [Tooltip("Speed at which the spatial turbulence pattern travels (m/s).")]
        [SerializeField, Min(0f)] private float _windSpeed = 2f;
        [Tooltip("Gust force amplitude (m/s) added along the wind direction.")]
        [SerializeField, Min(0f)] private float _gustStrength = 0.5f;
        [Tooltip("Gust front travel speed (m/s).")]
        [SerializeField, Min(0f)] private float _gustSpeed = 2.2f;
        [Tooltip("Gust wavelength (m) along the wind direction.")]
        [SerializeField, Min(0.01f)] private float _gustLength = 20f;
        [Tooltip("Mixing height (m) for vertical wind: Vertical = -height × horizontal divergence. Gust fronts lift and sink the air.")]
        [SerializeField, Min(0f)] private float _verticalScale = 10f;
        [Tooltip("Turbulence force amplitude (m/s).")]
        [SerializeField, Min(0f)] private float _turbulenceStrength = 0.6f;
        [SerializeField, Min(0.01f)] private float _turbulenceLength = 20f;
        [SerializeField, Min(0f)] private float _lowerHeightGain = 0.3f;
        [SerializeField, Min(0f)] private float _upperHeightGain = 1.3f;

        [Header("Debug")]
        [SerializeField] private string _gizmoMember = "Wind";

        private RenderTexture _texture;
        private int _kernel;
        private Vector4 _mapping, _height, _baseVelocity;

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
        //  Gizmo declaration — the XZ area the field texture covers
        // ════════════════════════════════════════════════════════════
        public string Member
        {
            get => _gizmoMember;
            set => _gizmoMember = value;
        }

        public GizmoForm Form => GizmoForm.Volume;

        private void OnDrawGizmos() => GizmoRender.Draw(this, false);

        private void OnDrawGizmosSelected() => GizmoRender.Draw(this, true);

        public void DrawGizmos(GizmoCanvas canvas)
        {
            Vector3 center = _followTarget != null ? _followTarget.position : transform.position;
            center.y = _referenceHeight;

            canvas.PlaneRect(center, Vector3.right, Vector3.forward, _areaSize.x, _areaSize.y);
        }

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
            float radians = _windAngle * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
            Vector2 wind = direction * Mathf.Max(0f, _windStrength);
            _baseVelocity = new Vector4(wind.x, 0f, wind.y, 0f);

            _computeShader.SetInt("_Resolution", Resolution);
            _computeShader.SetVector("_OriginSize", new Vector4(origin.x, origin.y, size.x, size.y));
            _computeShader.SetVector("_BaseVelocity", _baseVelocity);
            _computeShader.SetVector("_AdvectionOffset", ComputeAdvection(time, direction));
            _computeShader.SetVector("_GustDirection", new Vector4(direction.x, direction.y, 0f, 0f));
            _computeShader.SetFloat("_GustStrength", Mathf.Max(0f, _gustStrength));
            _computeShader.SetFloat("_GustLength", Mathf.Max(0.01f, _gustLength));
            _computeShader.SetFloat("_GustOffset", ComputeGustOffset(time));
            _computeShader.SetFloat("_VerticalScale", Mathf.Max(0f, _verticalScale));
            _computeShader.SetFloat("_TurbulenceStrength", Mathf.Max(0f, _turbulenceStrength));
            _computeShader.SetFloat("_TurbulenceLength", Mathf.Max(0.01f, _turbulenceLength));
            _computeShader.SetTexture(_kernel, "_WindOutput", _texture);
            _computeShader.Dispatch(_kernel, Resolution / 8, Resolution / 8, 1);
        }

        // Turbulence pattern travel is set only by _windSpeed. The gust rides its own spatial
        // phase in the compute shader, so neither force parameter feeds advection.
        // The disturbance repeats every _TurbulenceLength, so wrapping keeps it exact.
        private Vector4 ComputeAdvection(float time, Vector2 direction)
        {
            double distance = System.Math.Max(0f, _windSpeed) * (double)time;
            double x = direction.x * distance;
            double y = direction.y * distance;
            double length = System.Math.Max(0.01f, _turbulenceLength);
            x -= System.Math.Floor(x / length) * length;
            y -= System.Math.Floor(y / length) * length;
            return new Vector4((float)x, (float)y, 0f, 0f);
        }

        // Gust travel repeats every _GustLength; wrapping in double precision keeps the phase
        // exact in long sessions instead of losing it to a single-precision speed * time product.
        private float ComputeGustOffset(float time)
        {
            double distance = System.Math.Max(0f, _gustSpeed) * (double)time;
            double length = System.Math.Max(0.01f, _gustLength);
            return (float)(distance - System.Math.Floor(distance / length) * length);
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
    }
}
