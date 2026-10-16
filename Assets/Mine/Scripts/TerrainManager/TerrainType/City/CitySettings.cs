// ════════════════════════════════════════════════════════════
//  CitySettings — 城市路网、园区配额及子类型基础配方
// ════════════════════════════════════════════════════════════

using UnityEngine;
using System;
using System.Collections.Generic;

namespace Mine.Terrain
{
    [Serializable]
    public sealed class CityZoneRule
    {
        [SerializeField] private CityZone _zone;
        [SerializeField, Min(0f)] private float _weight;

        public CityZone Zone { get => _zone; set => _zone = value; }
        public float Weight { get => _weight; set => _weight = value; }

        public CityZoneRule(CityZone zone, float weight)
        {
            _zone = zone;
            _weight = weight;
        }
    }

    [Serializable]
    public sealed class CitySubtypeRule
    {
        [SerializeField] private CitySubtype _subtype;
        [SerializeField, Min(0f)] private float _weight;
        [SerializeField, Min(0)] private int _maxFacilitiesPerBlock;
        [SerializeField, Min(0)] private int _treesPerBlock;
        [SerializeField, Min(0f)] private float _grassDensity;

        public int Region { get => (int)_subtype; set => _subtype = (CitySubtype)value; }
        public float Weight { get => _weight; set => _weight = value; }
        public int MaxFacilitiesPerBlock
        {
            get => _maxFacilitiesPerBlock;
            set => _maxFacilitiesPerBlock = value;
        }
        public int TreesPerBlock { get => _treesPerBlock; set => _treesPerBlock = value; }
        public float GrassDensity { get => _grassDensity; set => _grassDensity = value; }

        public CitySubtypeRule(CitySubtype subtype, float weight, int maxFacilitiesPerBlock,
            int treesPerBlock, float grassDensity)
        {
            _subtype = subtype;
            _weight = weight;
            _maxFacilitiesPerBlock = maxFacilitiesPerBlock;
            _treesPerBlock = treesPerBlock;
            _grassDensity = grassDensity;
        }
    }

    /// <summary>
    /// 城市方案持有路网输入及语义配方；不缓存某次生成的地图状态。
    /// </summary>
    [Serializable]
    public sealed class CitySettings
    {
        [Header("Technical — Grid")]
        [SerializeField] private TerrainGridSettings _grid = new TerrainGridSettings();

        [Header("Artistic — Facilities")]
        [SerializeField, Min(0f)] private float _buildingSetback = 2f;

        [Header("Artistic — Zone Mix")]
        [SerializeField] private CityZoneRule[] _zones =
        {
            new CityZoneRule(CityZone.Industrial, 20f),
            new CityZoneRule(CityZone.Residential, 40f),
            new CityZoneRule(CityZone.Technology, 25f),
            new CityZoneRule(CityZone.Green, 15f)
        };
        [SerializeField] private CitySubtypeRule[] _subtypes =
        {
            new CitySubtypeRule(CitySubtype.Factory, 1f, 2, 0, 0f),
            new CitySubtypeRule(CitySubtype.ConstructionSite, 1f, 1, 0, 0f),
            new CitySubtypeRule(CitySubtype.Villa, 1f, 4, 1, 0f),
            new CitySubtypeRule(CitySubtype.Slum, 1f, 10, 0, 0f),
            new CitySubtypeRule(CitySubtype.Office, 1f, 4, 1, 0f),
            new CitySubtypeRule(CitySubtype.ResearchBase, 1f, 2, 1, 0f),
            new CitySubtypeRule(CitySubtype.Park, 1f, 1, 6, 0.3f),
            new CitySubtypeRule(CitySubtype.Undeveloped, 1f, 0, 10, 0.5f)
        };

        public TerrainGridSettings Grid => _grid;
        public float BuildingSetback { get => _buildingSetback; set => _buildingSetback = value; }
        public CityZoneRule[] ZoneRules => _zones;
        public CitySubtypeRule[] SubtypeRules => _subtypes;

        public CitySubtypeRule RuleFor(int region)
        {
            if (_subtypes != null)
            {
                foreach (CitySubtypeRule rule in _subtypes)
                {
                    if (rule != null && rule.Region == region)
                        return rule;
                }
            }
            throw new ArgumentOutOfRangeException(nameof(region), "No city subtype rule exists for this region.");
        }

        public void Validate()
        {
            if (_grid == null)
                throw new ArgumentException("City grid settings are missing.");
            _grid.Validate();
            if (!IsFinite(_buildingSetback) || _buildingSetback < 0f)
                throw new ArgumentException("City building setback must be finite and non-negative.");
            ValidateZones();
            ValidateSubtypes();
        }

        // ════════════════════════════════════════════════════════════
        //  配方校验 — 拒绝未知、重复 ID 与无法产生内容的启用园区
        // ════════════════════════════════════════════════════════════

        private void ValidateZones()
        {
            if (_zones == null || _zones.Length == 0)
                throw new ArgumentException("City zone rules are missing.");
            var seen = new HashSet<CityZone>();
            bool hasEnabledZone = false;
            foreach (CityZoneRule rule in _zones)
            {
                if (rule == null || !Enum.IsDefined(typeof(CityZone), rule.Zone) ||
                    !seen.Add(rule.Zone) || !IsFinite(rule.Weight) || rule.Weight < 0f)
                    throw new ArgumentException("City zone rules contain an unknown, duplicate, or invalid entry.");
                hasEnabledZone |= rule.Weight > 0f;
            }
            if (!hasEnabledZone)
                throw new ArgumentException("City requires at least one positively weighted zone.");
        }

        private void ValidateSubtypes()
        {
            if (_subtypes == null || _subtypes.Length == 0)
                throw new ArgumentException("City subtype rules are missing.");
            var seen = new HashSet<int>();
            var enabledZones = new HashSet<CityZone>();
            foreach (CitySubtypeRule rule in _subtypes)
            {
                if (rule == null || !Enum.IsDefined(typeof(CitySubtype), rule.Region) ||
                    !seen.Add(rule.Region) || !IsFinite(rule.Weight) || rule.Weight < 0f ||
                    rule.MaxFacilitiesPerBlock < 0 || rule.TreesPerBlock < 0 ||
                    !IsFinite(rule.GrassDensity) || rule.GrassDensity < 0f)
                    throw new ArgumentException("City subtype rules contain an unknown, duplicate, or invalid entry.");
                if (rule.Weight > 0f)
                    enabledZones.Add(CityContent.ZoneFor(rule.Region));
            }
            foreach (CityZoneRule zone in _zones)
            {
                if (zone.Weight > 0f && !enabledZones.Contains(zone.Zone))
                    throw new ArgumentException("Every enabled city zone requires at least one positively weighted subtype.");
            }
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
