using UnityEngine;

namespace Mine.Fields
{
    /// <summary>Wind channel contract: world-space velocity in m/s, finite XZ texture plus analytic height.</summary>
    public sealed class WindFieldBindings : IFieldBindings
    {
        public const string Key = "Wind";

        public static readonly WindFieldBindings Instance = new WindFieldBindings();

        internal static readonly int TextureId = Shader.PropertyToID("_WindFieldTexture");
        internal static readonly int MappingId = Shader.PropertyToID("_WindFieldWorldToUV");
        internal static readonly int HeightId = Shader.PropertyToID("_WindFieldHeight");
        internal static readonly int BaseId = Shader.PropertyToID("_WindFieldBaseVelocity");
        internal static readonly int ValidId = Shader.PropertyToID("_WindFieldValid");
        internal static readonly int TimeId = Shader.PropertyToID("_WindFieldTime");
        internal static readonly int VersionId = Shader.PropertyToID("_WindFieldVersion");

        private WindFieldBindings() { }

        public string FieldKey => Key;

        public void BindNeutral(ComputeShader shader, int kernel)
        {
            shader.SetTexture(kernel, TextureId, Texture2D.blackTexture);
            shader.SetVector(MappingId, new Vector4(0f, 0f, 1f, 1f));
            shader.SetVector(HeightId, new Vector4(0f, 1f, 1f, 1f));
            shader.SetVector(BaseId, Vector4.zero);
            shader.SetFloat(ValidId, 0f);
            shader.SetFloat(TimeId, 0f);
            shader.SetFloat(VersionId, 0f);
        }

        public void BindNeutralGlobals()
        {
            Shader.SetGlobalTexture(TextureId, Texture2D.blackTexture);
            Shader.SetGlobalVector(MappingId, new Vector4(0f, 0f, 1f, 1f));
            Shader.SetGlobalVector(HeightId, new Vector4(0f, 1f, 1f, 1f));
            Shader.SetGlobalVector(BaseId, Vector4.zero);
            Shader.SetGlobalFloat(ValidId, 0f);
            Shader.SetGlobalFloat(TimeId, 0f);
            Shader.SetGlobalFloat(VersionId, 0f);
        }
    }
}
