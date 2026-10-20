// ════════════════════════════════════════════════════════════
//  TerrainRenderSettings — 所有地图方案共享的地表渲染配置
// ════════════════════════════════════════════════════════════

using UnityEngine;
using System;

namespace Mine.Terrain
{
    /// <summary>
    /// 与场景类型无关的地表渲染设置。
    /// </summary>
    [Serializable]
    public sealed class TerrainRenderSettings
    {
        [SerializeField] private Shader _surfaceShader;
        [SerializeField, Min(0f)] private float _colorBlendWidth = 3f;

        public Shader SurfaceShader => _surfaceShader;
        public float ColorBlendWidth => _colorBlendWidth;

        public void Validate()
        {
            if (float.IsNaN(_colorBlendWidth) || float.IsInfinity(_colorBlendWidth) ||
                _colorBlendWidth < 0f)
                throw new ArgumentException("Terrain color blend width must be finite and non-negative.");
        }
    }
}
