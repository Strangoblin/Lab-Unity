using UnityEngine;

namespace Mine.Fields
{
    /// <summary>
    /// Wave 场 — 3 级 Cascade Phillips 频谱 → Displacement + Normal
    ///
    /// 每帧 pipeline:
    ///   InitSpectrum → TimeEvolve → PrepareChoppy → IFFT2D → MergeOutput
    ///
    /// 三级 cascade 共用 Phillips 频谱，但方向集中度独立：大尺度保持窄方向涌浪，
    /// 小尺度放宽为风浪/交叉浪，避免整片水面呈现同一组平行波峰。
    ///
    /// 全局纹理输出:
    ///   _WaveDisplacement0/1/2  — XYZ 位移 (A=foam)
    ///   _WaveNormal0/1/2       — 世界空间法线
    /// </summary>
    [ExecuteAlways]
    public sealed class WaveFieldProvider : FieldProvider
    {
        public override string FieldKey => WaveFieldBindings.Key;
        public override int FieldOrder => -100;

        // ════════════════════════════════════════════════════════════
        //  Inspector
        // ════════════════════════════════════════════════════════════

        [Header("Compute Shaders")]
        [SerializeField] private ComputeShader _fftCS;

        [Header("Cascade (3 级)")]
        [SerializeField] private float[] _cascadeScales    = { 40f, 8f, 1.6f };
        [SerializeField] private float[] _cascadeAmplitudes = { 0.3f, 0.15f, 0.05f };
        [Tooltip("每级的方向集中度。越大越接近单一方向涌浪；越小越接近多方向风浪。3 为原 Phillips 方向性。")]
        [SerializeField] private float[] _cascadeDirectionality = { 3f, 1.6f, 0.7f };

        [Header("波浪参数")]
        [SerializeField] private float   _windSpeed     = 10f;
        [SerializeField] private Vector2 _windDirection  = new Vector2(1f, 0.3f);
        [SerializeField] private float   _choppyScale   = 0.3f;
        [SerializeField] private float   _heightScale   = 0.5f;
        [SerializeField] private float   _gravity       = 9.81f;

        // ════════════════════════════════════════════════════════════
        //  Shader Property IDs
        // ════════════════════════════════════════════════════════════

        private static readonly int s_NoiseID          = Shader.PropertyToID("_Noise");
        private static readonly int s_H0WriteID        = Shader.PropertyToID("_H0");
        private static readonly int s_H0ReadID         = Shader.PropertyToID("_H0Read");
        private static readonly int s_HtID             = Shader.PropertyToID("_Ht");
        private static readonly int s_ChoppySpecID     = Shader.PropertyToID("_ChoppySpec");
        private static readonly int s_InputID          = Shader.PropertyToID("_Input");
        private static readonly int s_OutputID         = Shader.PropertyToID("_Output");
        private static readonly int s_HeightTexID      = Shader.PropertyToID("_HeightTex");
        private static readonly int s_ChoppyXTexID     = Shader.PropertyToID("_ChoppyXTex");
        private static readonly int s_ChoppyYTexID     = Shader.PropertyToID("_ChoppyYTex");
        private static readonly int s_DisplacementID   = Shader.PropertyToID("_Displacement");
        private static readonly int s_NormalID         = Shader.PropertyToID("_Normal");

        private static readonly int s_AmplitudeID      = Shader.PropertyToID("_Amplitude");
        private static readonly int s_WindSpeedID      = Shader.PropertyToID("_WindSpeed");
        private static readonly int s_WindDirectionID  = Shader.PropertyToID("_WindDirection");
        private static readonly int s_LengthScaleID    = Shader.PropertyToID("_LengthScale");
        private static readonly int s_GravityID        = Shader.PropertyToID("_Gravity");
        private static readonly int s_ResolutionID     = Shader.PropertyToID("_Resolution");
        private static readonly int s_TimeID           = Shader.PropertyToID("_Time");
        private static readonly int s_DirectionID      = Shader.PropertyToID("_Direction");
        private static readonly int s_InvID            = Shader.PropertyToID("_Inv");
        private static readonly int s_ChoppyAxisID     = Shader.PropertyToID("_ChoppyAxis");
        private static readonly int s_ChoppyScaleID    = Shader.PropertyToID("_ChoppyScale");
        private static readonly int s_HeightScaleID    = Shader.PropertyToID("_HeightScale");
        private static readonly int s_PatchSizeID      = Shader.PropertyToID("_PatchSize");
        private static readonly int s_DirectionalPowerID = Shader.PropertyToID("_DirectionalPower");
        private static readonly int s_DirectionalNormalizationID = Shader.PropertyToID("_DirectionalNormalization");

        // ════════════════════════════════════════════════════════════
        //  运行时状态
        // ════════════════════════════════════════════════════════════

        private const int RESOLUTION = 256;

        private static readonly float[] s_DefaultDirectionality = { 3f, 1.6f, 0.7f };

        private Texture2D _noiseTex;

        private RenderTexture _pingA, _pingB;
        private RenderTexture _heightRaw, _choppyXRaw, _choppyYRaw;

        private struct CascadeData
        {
            public RenderTexture h0Tex, htTex, displacementTex, normalTex;
        }
        private CascadeData[] _cascades;

        private int _kInitSpectrum, _kTimeEvolve, _kPrepareChoppy, _kIFFT2D, _kMergeOutput;

        private bool _spectrumDirty;

        // ════════════════════════════════════════════════════════════
        //  生命周期
        // ════════════════════════════════════════════════════════════

        protected override bool InitializeField()
        {
            if (_fftCS == null || _cascadeScales == null || _cascadeScales.Length != 3
                || _cascadeAmplitudes == null || _cascadeAmplitudes.Length != 3)
            {
                Debug.LogError("WaveFieldProvider requires WaveField.compute and three cascade scales/amplitudes.", this);
                return false;
            }
            InitKernels();
            CreateNoiseTexture();
            CreateRenderTextures();
            GenerateInitialSpectra();
            return true;
        }

        protected override void SimulateField(float time, float deltaTime)
        {
            if (_spectrumDirty) GenerateInitialSpectra();
            DispatchFrame(time);
        }

        protected override void OnValidate()
        {
            _spectrumDirty = true;
            base.OnValidate();
        }

        protected override void ReleaseField() => ReleaseAll();

        // ════════════════════════════════════════════════════════════
        //  初始化
        // ════════════════════════════════════════════════════════════

        private void InitKernels()
        {
            _kInitSpectrum   = _fftCS.FindKernel("InitSpectrum");
            _kTimeEvolve     = _fftCS.FindKernel("TimeEvolve");
            _kPrepareChoppy  = _fftCS.FindKernel("PrepareChoppy");
            _kIFFT2D         = _fftCS.FindKernel("IFFT2D");
            _kMergeOutput    = _fftCS.FindKernel("MergeOutput");
        }

        private void CreateNoiseTexture()
        {
            _noiseTex = new Texture2D(RESOLUTION, RESOLUTION, TextureFormat.RGBAFloat, false, true)
            {
                name = "Wave_GaussianNoise", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point,
            };
            var pixels = new Color[RESOLUTION * RESOLUTION];
            var rng = new System.Random(42);
            for (int i = 0; i < pixels.Length; i++)
            {
                float u1 = (float)rng.NextDouble(), u2 = (float)rng.NextDouble();
                float g1 = Mathf.Sqrt(-2f * Mathf.Log(Mathf.Max(u1, 0.0001f))) * Mathf.Cos(2f * Mathf.PI * u2);
                float g2 = Mathf.Sqrt(-2f * Mathf.Log(Mathf.Max(u1, 0.0001f))) * Mathf.Sin(2f * Mathf.PI * u2);
                pixels[i] = new Color(g1, g2, 0, 0);
            }
            _noiseTex.SetPixels(pixels);
            _noiseTex.Apply();
        }

        private void CreateRenderTextures()
        {
            _pingA      = CreateRT("Wave_PingA");
            _pingB      = CreateRT("Wave_PingB");
            _heightRaw  = CreateRT("Wave_HeightRaw");
            _choppyXRaw = CreateRT("Wave_ChoppyXRaw");
            _choppyYRaw = CreateRT("Wave_ChoppyYRaw");

            _cascades = new CascadeData[3];
            for (int i = 0; i < 3; i++)
            {
                _cascades[i] = new CascadeData
                {
                    h0Tex = CreateRT($"Wave_C{i}_H0"), htTex = CreateRT($"Wave_C{i}_Ht"),
                    displacementTex = CreateRT($"Wave_C{i}_Disp"), normalTex = CreateRT($"Wave_C{i}_Norm"),
                };
            }
        }

        private static RenderTexture CreateRT(string name)
        {
            var rt = new RenderTexture(RESOLUTION, RESOLUTION, 0, RenderTextureFormat.ARGBFloat)
            {
                name = name, enableRandomWrite = true,
                wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear,
                useMipMap = false, autoGenerateMips = false,
            };
            rt.Create();
            return rt;
        }

        private void GenerateInitialSpectra()
        {
            _spectrumDirty = false;
            for (int c = 0; c < 3; c++)
            {
                _fftCS.SetTexture(_kInitSpectrum, s_NoiseID, _noiseTex);
                _fftCS.SetTexture(_kInitSpectrum, s_H0WriteID, _cascades[c].h0Tex);
                _fftCS.SetFloat(s_AmplitudeID, _cascadeAmplitudes[c]);
                _fftCS.SetFloat(s_WindSpeedID, _windSpeed);
                _fftCS.SetVector(s_WindDirectionID, _windDirection.normalized);
                _fftCS.SetFloat(s_LengthScaleID, Mathf.Max(0.01f, _cascadeScales[c]));
                _fftCS.SetFloat(s_DirectionalPowerID, Directionality(c));
                _fftCS.SetFloat(s_DirectionalNormalizationID, DirectionalNormalization(Directionality(c)));
                _fftCS.SetFloat(s_GravityID, _gravity);
                _fftCS.SetInt(s_ResolutionID, RESOLUTION);
                Dispatch(_kInitSpectrum);
            }
        }

        /// <summary>Directional exponent for one cascade; array length changes fall back to defaults.</summary>
        private float Directionality(int cascade)
        {
            if (_cascadeDirectionality != null && _cascadeDirectionality.Length == 3)
                return Mathf.Max(0.05f, _cascadeDirectionality[cascade]);
            return s_DefaultDirectionality[cascade];
        }

        /// <summary>
        /// Mean of cos(theta)^power over the downwind hemisphere. Phillips' original
        /// directional term is cos^3, so power 3 is the unnormalized baseline.
        /// </summary>
        private static float DirectionalEnergy(float power)
        {
            float p = Mathf.Max(0.05f, power);
            const int Samples = 128;
            float sum = 0f;
            for (int i = 0; i < Samples; i++)
            {
                float theta = (i + 0.5f) * (0.5f * Mathf.PI) / Samples;
                sum += Mathf.Pow(Mathf.Cos(theta), p);
            }
            return sum / Samples;
        }

        /// <summary>Keeps integrated wave energy stable as directivity widens.</summary>
        private static float DirectionalNormalization(float power)
        {
            return Mathf.Sqrt(DirectionalEnergy(3f) / DirectionalEnergy(power));
        }

        // ════════════════════════════════════════════════════════════
        //  每帧
        // ════════════════════════════════════════════════════════════

        private void DispatchFrame(float time)
        {
            for (int c = 0; c < 3; c++)
            {
                var cd  = _cascades[c];
                float scl = Mathf.Max(0.01f, _cascadeScales[c]);

                // 1. TimeEvolve: H0 → Ht
                _fftCS.SetTexture(_kTimeEvolve, s_H0ReadID, cd.h0Tex);
                _fftCS.SetTexture(_kTimeEvolve, s_HtID, cd.htTex);
                _fftCS.SetFloat(s_TimeID, time);
                _fftCS.SetFloat(s_LengthScaleID, scl);
                _fftCS.SetFloat(s_GravityID, _gravity);
                _fftCS.SetInt(s_ResolutionID, RESOLUTION);
                Dispatch(_kTimeEvolve);

                // 2. Height: Copy Ht → PingA → IFFT → HeightRaw
                Graphics.CopyTexture(cd.htTex, _pingA);
                RunIFFT();
                Graphics.CopyTexture(_pingA, _heightRaw);

                // 3. ChoppyX: PrepareChoppy → IFFT → ChoppyXRaw
                RunPrepareChoppy(cd.htTex, scl, 0);
                Graphics.CopyTexture(_pingA, _choppyXRaw);

                // 4. ChoppyY: PrepareChoppy → IFFT → ChoppyYRaw
                RunPrepareChoppy(cd.htTex, scl, 1);
                Graphics.CopyTexture(_pingA, _choppyYRaw);

                // 5. MergeOutput → Displacement + Normal
                _fftCS.SetTexture(_kMergeOutput, s_HeightTexID,   _heightRaw);
                _fftCS.SetTexture(_kMergeOutput, s_ChoppyXTexID,  _choppyXRaw);
                _fftCS.SetTexture(_kMergeOutput, s_ChoppyYTexID,  _choppyYRaw);
                _fftCS.SetTexture(_kMergeOutput, s_DisplacementID, cd.displacementTex);
                _fftCS.SetTexture(_kMergeOutput, s_NormalID,       cd.normalTex);
                _fftCS.SetFloat(s_ChoppyScaleID, _choppyScale);
                _fftCS.SetFloat(s_HeightScaleID, _heightScale);
                _fftCS.SetFloat(s_PatchSizeID, scl);
                _fftCS.SetInt(s_ResolutionID, RESOLUTION);
                Dispatch(_kMergeOutput);


            }
        }

        // ════════════════════════════════════════════════════════════
        //  Field publication — retain legacy names and reset before resource release
        // ════════════════════════════════════════════════════════════
        protected override void PublishField()
        {
            for (int c = 0; c < 3; c++)
            {
                Shader.SetGlobalTexture(WaveFieldBindings.DisplacementIds[c], _cascades[c].displacementTex);
                Shader.SetGlobalTexture(WaveFieldBindings.NormalIds[c], _cascades[c].normalTex);
                Shader.SetGlobalFloat(WaveFieldBindings.PatchIds[c], Mathf.Max(0.01f, _cascadeScales[c]));
            }
            Shader.SetGlobalFloat(WaveFieldBindings.ValidId, 1f);
            Shader.SetGlobalFloat(WaveFieldBindings.TimeId, SampleTime);
            Shader.SetGlobalFloat(WaveFieldBindings.VersionId, Version);
        }

        public override void BindCompute(ComputeShader shader, int kernel)
        {
            for (int c = 0; c < 3; c++)
            {
                shader.SetTexture(kernel, WaveFieldBindings.DisplacementIds[c], _cascades[c].displacementTex);
                shader.SetTexture(kernel, WaveFieldBindings.NormalIds[c], _cascades[c].normalTex);
                shader.SetFloat(WaveFieldBindings.PatchIds[c], Mathf.Max(0.01f, _cascadeScales[c]));
            }
            shader.SetFloat(WaveFieldBindings.ValidId, 1f);
            shader.SetFloat(WaveFieldBindings.TimeId, SampleTime);
            shader.SetFloat(WaveFieldBindings.VersionId, Version);
        }

        public override void ResetGlobals() => WaveFieldBindings.Instance.BindNeutralGlobals();

        // ── 子步骤 ────────────────────────────────────────────────

        private void RunPrepareChoppy(RenderTexture ht, float scale, uint axis)
        {
            _fftCS.SetTexture(_kPrepareChoppy, s_HtID, ht);
            _fftCS.SetTexture(_kPrepareChoppy, s_ChoppySpecID, _pingA);
            _fftCS.SetFloat(s_LengthScaleID, scale);
            _fftCS.SetInt(s_ResolutionID, RESOLUTION);
            _fftCS.SetInt(s_ChoppyAxisID, (int)axis);
            Dispatch(_kPrepareChoppy);
        }

        private void RunIFFT()
        {
            _fftCS.SetInt(s_InvID, 1);
            _fftCS.SetInt(s_ResolutionID, RESOLUTION);

            _fftCS.SetInt(s_DirectionID, 0);
            _fftCS.SetTexture(_kIFFT2D, s_InputID, _pingA);
            _fftCS.SetTexture(_kIFFT2D, s_OutputID, _pingB);
            _fftCS.Dispatch(_kIFFT2D, 1, RESOLUTION, 1);

            _fftCS.SetInt(s_DirectionID, 1);
            _fftCS.SetTexture(_kIFFT2D, s_InputID, _pingB);
            _fftCS.SetTexture(_kIFFT2D, s_OutputID, _pingA);
            _fftCS.Dispatch(_kIFFT2D, RESOLUTION, 1, 1);
        }

        // ════════════════════════════════════════════════════════════
        //  辅助
        // ════════════════════════════════════════════════════════════

        private void Dispatch(int kernel)
        {
            int tg = RESOLUTION / 8;
            _fftCS.Dispatch(kernel, tg, tg, 1);
        }

        private void ReleaseAll()
        {
            void Rel(ref RenderTexture rt)
            {
                if (rt == null) return;
                rt.Release();
                if (Application.isPlaying) Destroy(rt); else DestroyImmediate(rt);
                rt = null;
            }

            Rel(ref _pingA); Rel(ref _pingB);
            Rel(ref _heightRaw); Rel(ref _choppyXRaw); Rel(ref _choppyYRaw);

            if (_cascades != null)
                for (int i = 0; i < _cascades.Length; i++)
                { Rel(ref _cascades[i].h0Tex); Rel(ref _cascades[i].htTex); Rel(ref _cascades[i].displacementTex); Rel(ref _cascades[i].normalTex); }

            if (_noiseTex != null)
            { if (Application.isPlaying) Destroy(_noiseTex); else DestroyImmediate(_noiseTex); _noiseTex = null; }
        }
    }
}
