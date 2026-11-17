// ════════════════════════════════════════════════════════════
//  TerrainSurfaceRenderer — 地形渲染层的资源所有者
// ════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.Rendering;
using System;

namespace Mine.Terrain
{
    /// <summary>
    /// 根据 TerrainGrid 创建一块地形网格、材质实例与颜色图，并负责释放。
    /// </summary>
    /// <remarks>
    /// 当前只生成单个子物体；分块、深度代理与交互参考层在后续里程碑接入。
    /// </remarks>
    public sealed class TerrainSurfaceRenderer
    {
        private const string SurfaceShaderName = "TerrainSurface/TerrainSurface";
        private static readonly int ColorMapId = Shader.PropertyToID("_ColorMap");

        private GameObject _root;
        private Mesh _mesh;
        private Material _material;
        private Texture2D _colorMap;

        public Transform Root => _root != null ? _root.transform : null;
        public Mesh Mesh => _mesh;
        /// <summary>逐格地表层烘焙的颜色图；UV 覆盖整块地形，供渲染以外的消费者（如草）采样。</summary>
        public Texture2D ColorMap => _colorMap;

        public TerrainSurfaceRenderer(Transform parent, TerrainGrid grid,
            Material materialOverride, bool castShadows, bool receiveShadows)
        {
            try
            {
                _mesh = TerrainMeshBuilder.Build(grid);
                _colorMap = TerrainColorMapBuilder.Build(grid);
                _material = CreateMaterial(materialOverride);
                _root = CreateRoot(parent, grid.Origin, castShadows, receiveShadows);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  构造 — 材质与场景对象
        // ════════════════════════════════════════════════════════════

        private Material CreateMaterial(Material materialOverride)
        {
            Shader shader = materialOverride != null
                ? materialOverride.shader
                : Shader.Find(SurfaceShaderName);
            if (shader == null)
                throw new InvalidOperationException(
                    $"Terrain surface shader '{SurfaceShaderName}' was not found.");

            var material = new Material(shader) { name = "Terrain Surface (Instance)" };
            if (materialOverride != null)
                material.CopyPropertiesFromMaterial(materialOverride);
            if (material.HasProperty(ColorMapId))
                material.SetTexture(ColorMapId, _colorMap);
            return material;
        }

        private GameObject CreateRoot(Transform parent, Vector3 origin,
            bool castShadows, bool receiveShadows)
        {
            var root = new GameObject("Terrain Surface") { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(parent, true);
            root.transform.position = origin;
            root.transform.rotation = Quaternion.identity;
            root.layer = parent != null ? parent.gameObject.layer : 0;
            root.AddComponent<TerrainSurfaceInstance>();

            var filter = root.AddComponent<MeshFilter>();
            filter.sharedMesh = _mesh;

            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = castShadows
                ? ShadowCastingMode.On
                : ShadowCastingMode.Off;
            renderer.receiveShadows = receiveShadows;
            return root;
        }

        // ════════════════════════════════════════════════════════════
        //  释放 — 只销毁本层创建的实例
        // ════════════════════════════════════════════════════════════

        public void Dispose()
        {
            DestroyObject(_root);
            DestroyObject(_mesh);
            DestroyObject(_material);
            DestroyObject(_colorMap);
            _root = null;
            _mesh = null;
            _material = null;
            _colorMap = null;
        }

        private static void DestroyObject(UnityEngine.Object value)
        {
            if (value == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(value);
            else
                UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
