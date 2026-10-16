// ════════════════════════════════════════════════════════════
//  CountrysideContent — 郊野方案内部的稳定地区与内容 ID
// ════════════════════════════════════════════════════════════

namespace Mine.Terrain
{
    public static class CountrysideRegion
    {
        public const int Grassland = 0;
        public const int Village = 1;
        public const int WheatField = 2;
        public const int Forest = 3;
        public const int Water = 4;
        public const int Meadow = 5;
        public const int Orchard = 6;
        public const int Count = 7;
    }

    public static class CountrysideRoadType
    {
        public const int Dirt = 1;
    }

    public static class CountrysideStuffType
    {
        public const int House = 0;
        public const int Windmill = 1;
        public const int Tree = 2;
        public const int OrchardTree = 3;
        public const int Count = 4;
    }

    public static class CountrysidePlantType
    {
        public const int ShortGrass = 0;
        public const int TallGrass = 1;
        public const int Wheat = 2;
        public const int Flower = 3;
        public const int Count = 4;
    }
}
