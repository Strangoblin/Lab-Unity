using UnityEngine;

namespace Mine.Fields
{
    /// <summary>Neutral bindings for absent wave providers, including kernels that already held live textures.</summary>
    public static class WaveFieldBindings
    {
        private static readonly int[] DisplacementIds = {
            Shader.PropertyToID("_WaveDisplacement0"), Shader.PropertyToID("_WaveDisplacement1"), Shader.PropertyToID("_WaveDisplacement2")
        };
        private static readonly int[] NormalIds = {
            Shader.PropertyToID("_WaveNormal0"), Shader.PropertyToID("_WaveNormal1"), Shader.PropertyToID("_WaveNormal2")
        };
        private static readonly int[] PatchIds = {
            Shader.PropertyToID("_WavePatchSize0"), Shader.PropertyToID("_WavePatchSize1"), Shader.PropertyToID("_WavePatchSize2")
        };

        public static void BindNeutral(ComputeShader shader, int kernel)
        {
            for (int i = 0; i < 3; i++)
            {
                shader.SetTexture(kernel, DisplacementIds[i], Texture2D.blackTexture);
                shader.SetTexture(kernel, NormalIds[i], Texture2D.grayTexture);
                shader.SetFloat(PatchIds[i], 1f);
            }
            shader.SetFloat("_WaveFieldValid", 0f);
            shader.SetFloat("_WaveFieldTime", 0f);
            shader.SetFloat("_WaveFieldVersion", 0f);
        }
    }
}
