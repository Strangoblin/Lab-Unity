using UnityEngine;

namespace Mine.Fields
{
    /// <summary>Wave channel contract: periodic three-cascade displacement in metres, normals and foam.</summary>
    public sealed class WaveFieldBindings : IFieldBindings
    {
        public const string Key = "Wave";

        public static readonly WaveFieldBindings Instance = new WaveFieldBindings();

        internal static readonly int[] DisplacementIds = {
            Shader.PropertyToID("_WaveDisplacement0"), Shader.PropertyToID("_WaveDisplacement1"), Shader.PropertyToID("_WaveDisplacement2")
        };
        internal static readonly int[] NormalIds = {
            Shader.PropertyToID("_WaveNormal0"), Shader.PropertyToID("_WaveNormal1"), Shader.PropertyToID("_WaveNormal2")
        };
        internal static readonly int[] PatchIds = {
            Shader.PropertyToID("_WavePatchSize0"), Shader.PropertyToID("_WavePatchSize1"), Shader.PropertyToID("_WavePatchSize2")
        };
        internal static readonly int ValidId = Shader.PropertyToID("_WaveFieldValid");
        internal static readonly int TimeId = Shader.PropertyToID("_WaveFieldTime");
        internal static readonly int VersionId = Shader.PropertyToID("_WaveFieldVersion");

        private WaveFieldBindings() { }

        public string FieldKey => Key;

        public void BindNeutral(ComputeShader shader, int kernel)
        {
            for (int i = 0; i < 3; i++)
            {
                shader.SetTexture(kernel, DisplacementIds[i], Texture2D.blackTexture);
                shader.SetTexture(kernel, NormalIds[i], Texture2D.grayTexture);
                shader.SetFloat(PatchIds[i], 1f);
            }
            shader.SetFloat(ValidId, 0f);
            shader.SetFloat(TimeId, 0f);
            shader.SetFloat(VersionId, 0f);
        }

        public void BindNeutralGlobals()
        {
            for (int i = 0; i < 3; i++)
            {
                Shader.SetGlobalTexture(DisplacementIds[i], Texture2D.blackTexture);
                Shader.SetGlobalTexture(NormalIds[i], Texture2D.grayTexture);
                Shader.SetGlobalFloat(PatchIds[i], 1f);
            }
            Shader.SetGlobalFloat(ValidId, 0f);
            Shader.SetGlobalFloat(TimeId, 0f);
            Shader.SetGlobalFloat(VersionId, 0f);
        }
    }
}
