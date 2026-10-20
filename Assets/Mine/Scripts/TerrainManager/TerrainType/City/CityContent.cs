// ════════════════════════════════════════════════════════════
//  CityContent — 城市方案的园区、子类型与内容 ID
// ════════════════════════════════════════════════════════════

using System;

namespace Mine.Terrain
{
    public enum CityZone
    {
        Industrial = 0,
        Residential = 1,
        Technology = 2,
        Green = 3
    }

    public enum CitySubtype
    {
        Factory = 101,
        ConstructionSite = 102,
        Villa = 201,
        Slum = 202,
        Office = 301,
        ResearchBase = 302,
        Park = 401,
        Undeveloped = 402
    }

    public static class CityRegion
    {
        public const int UrbanBase = 0;
    }

    public static class CityStuffType
    {
        public const int Factory = 1001;
        public const int ConstructionSite = 1002;
        public const int Villa = 2001;
        public const int SlumHouse = 2002;
        public const int OfficeTower = 3001;
        public const int ResearchBuilding = 3002;
        public const int ParkPavilion = 4001;
        public const int Tree = 4002;
    }

    public static class CityPlantType
    {
        public const int Lawn = 5001;
        public const int WildGrass = 5002;
    }

    public static class CityRoadType
    {
        public const int Asphalt = 10;
        public const int Sidewalk = 11;
    }

    /// <summary>
    /// 城市特有的语义映射；通用地图和表现层只保存整数 ID。
    /// </summary>
    public static class CityContent
    {
        public static CityZone ZoneFor(int region)
        {
            switch ((CitySubtype)region)
            {
                case CitySubtype.Factory:
                case CitySubtype.ConstructionSite:
                    return CityZone.Industrial;
                case CitySubtype.Villa:
                case CitySubtype.Slum:
                    return CityZone.Residential;
                case CitySubtype.Office:
                case CitySubtype.ResearchBase:
                    return CityZone.Technology;
                case CitySubtype.Park:
                case CitySubtype.Undeveloped:
                    return CityZone.Green;
                default:
                    throw new ArgumentOutOfRangeException(nameof(region), "Unknown city subtype.");
            }
        }

        public static bool KnownRegion(int region) =>
            region == CityRegion.UrbanBase || Enum.IsDefined(typeof(CitySubtype), region);

        public static int MainStuffFor(int region)
        {
            switch ((CitySubtype)region)
            {
                case CitySubtype.Factory: return CityStuffType.Factory;
                case CitySubtype.ConstructionSite: return CityStuffType.ConstructionSite;
                case CitySubtype.Villa: return CityStuffType.Villa;
                case CitySubtype.Slum: return CityStuffType.SlumHouse;
                case CitySubtype.Office: return CityStuffType.OfficeTower;
                case CitySubtype.ResearchBase: return CityStuffType.ResearchBuilding;
                case CitySubtype.Park: return CityStuffType.ParkPavilion;
                case CitySubtype.Undeveloped: return -1;
                default:
                    if (region == CityRegion.UrbanBase)
                        return -1;
                    throw new ArgumentOutOfRangeException(nameof(region), "Unknown city subtype.");
            }
        }
    }
}
