// ════════════════════════════════════════════════════════════
//  TerrainVoronoiSettings — 可被不同地图方案复用的种子与距离场参数
// ════════════════════════════════════════════════════════════

using UnityEngine;
using System;
using System.Collections.Generic;

namespace Mine.Terrain
{
    [Serializable]
    public sealed class TerrainFeatureRule
    {
        [SerializeField, Min(0)] private int _region;
        [SerializeField, Min(1)] private int _sitesAtReferenceSize;
        [SerializeField, Min(1f)] private float _minRadiusAtReferenceSize;
        [SerializeField, Min(1f)] private float _maxRadiusAtReferenceSize;
        [SerializeField] private Vector2 _preferredAnchor = new Vector2(0.5f, 0.5f);

        public int Region => _region;
        public int SitesAtReferenceSize => _sitesAtReferenceSize;
        public float MinRadiusAtReferenceSize => _minRadiusAtReferenceSize;
        public float MaxRadiusAtReferenceSize => _maxRadiusAtReferenceSize;
        public Vector2 PreferredAnchor => _preferredAnchor;

        public TerrainFeatureRule(int region, int sites, float minRadius, float maxRadius,
            Vector2? anchor = null)
        {
            _region = region;
            _sitesAtReferenceSize = sites;
            _minRadiusAtReferenceSize = minRadius;
            _maxRadiusAtReferenceSize = maxRadius;
            _preferredAnchor = anchor ?? new Vector2(0.5f, 0.5f);
        }
    }

    /// <summary>
    /// 只描述 Voronoi 生成方式；区域和道路 ID 的具体语义由地图方案决定。
    /// </summary>
    [Serializable]
    public class TerrainVoronoiSettings
    {
        [SerializeField, Min(0)] private int _baseRegion;
        [SerializeField, Min(0)] private int _roadType = 1;

        [SerializeField, Range(3f, 9f)] private float _broadWarpCycles = 7f;
        [SerializeField, Range(0f, 20f)] private float _broadWarpDistance = 18f;
        [SerializeField, Range(8f, 24f)] private float _detailWarpCycles = 20f;
        [SerializeField, Range(0f, 6f)] private float _detailWarpDistance = 5f;

        [SerializeField, HideInInspector, Range(1f, 60f)] private float _voronoiRegionWidth = 22f;
        [SerializeField, HideInInspector, Range(0f, 8f)] private float _voronoiRegionSoftness = 3f;
        [SerializeField, HideInInspector, Range(1f, 20f)] private float _voronoiRoadWidth = 5f;
        [SerializeField, HideInInspector, Range(0f, 8f)] private float _voronoiCornerSoftness = 3f;
        [SerializeField] private TerrainFeatureRule[] _featureRules = Array.Empty<TerrainFeatureRule>();

        public int BaseRegion
        {
            get => _baseRegion;
            set => _baseRegion = value;
        }
        public int RoadType
        {
            get => _roadType;
            set => _roadType = value;
        }
        public float BroadWarpCycles
        {
            get => _broadWarpCycles;
            set => _broadWarpCycles = value;
        }
        public float BroadWarpDistance
        {
            get => _broadWarpDistance;
            set => _broadWarpDistance = value;
        }
        public float DetailWarpCycles
        {
            get => _detailWarpCycles;
            set => _detailWarpCycles = value;
        }
        public float DetailWarpDistance
        {
            get => _detailWarpDistance;
            set => _detailWarpDistance = value;
        }
        public float VoronoiRegionWidth
        {
            get => _voronoiRegionWidth;
            set => _voronoiRegionWidth = value;
        }
        public float VoronoiRegionSoftness
        {
            get => _voronoiRegionSoftness;
            set => _voronoiRegionSoftness = value;
        }
        public float VoronoiRoadWidth
        {
            get => _voronoiRoadWidth;
            set => _voronoiRoadWidth = value;
        }
        public float VoronoiCornerSoftness
        {
            get => _voronoiCornerSoftness;
            set => _voronoiCornerSoftness = value;
        }
        public TerrainFeatureRule[] FeatureRules
        {
            get => _featureRules;
            set => _featureRules = value;
        }

        public virtual void Validate()
        {
            if (_baseRegion < 0 || _roadType < 0)
                throw new ArgumentException("Terrain region and road IDs must not be negative.");
            if (!(_broadWarpCycles >= 3f && _broadWarpCycles <= 9f) ||
                !(_broadWarpDistance >= 0f && _broadWarpDistance <= 20f) ||
                !(_detailWarpCycles >= 8f && _detailWarpCycles <= 24f) ||
                !(_detailWarpDistance >= 0f && _detailWarpDistance <= 6f))
                throw new ArgumentException("Terrain data warp settings are invalid.");
            if (!(_voronoiRegionWidth >= 1f && _voronoiRegionWidth <= 60f) ||
                !(_voronoiRegionSoftness >= 0f && _voronoiRegionSoftness <= 8f) ||
                !(_voronoiRoadWidth >= 1f && _voronoiRoadWidth <= 20f) ||
                !(_voronoiCornerSoftness >= 0f && _voronoiCornerSoftness <= 8f))
                throw new ArgumentException("Terrain Voronoi width or softness is invalid.");
            if (_featureRules == null)
                throw new ArgumentException("Terrain feature rules must not be null.");

            HashSet<int> seen = new HashSet<int>();
            for (int i = 0; i < _featureRules.Length; i++)
            {
                TerrainFeatureRule rule = _featureRules[i];
                if (rule == null || rule.Region < 0 || rule.Region == _baseRegion ||
                    !seen.Add(rule.Region) || rule.SitesAtReferenceSize < 1 ||
                    !IsFinitePositive(rule.MinRadiusAtReferenceSize) ||
                    !IsFinitePositive(rule.MaxRadiusAtReferenceSize) ||
                    rule.MaxRadiusAtReferenceSize < rule.MinRadiusAtReferenceSize ||
                    !(rule.PreferredAnchor.x >= 0f && rule.PreferredAnchor.x <= 1f) ||
                    !(rule.PreferredAnchor.y >= 0f && rule.PreferredAnchor.y <= 1f))
                    throw new ArgumentException($"Invalid or duplicate terrain feature rule at index {i}.");
            }
        }

        private static bool IsFinitePositive(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
    }
}
