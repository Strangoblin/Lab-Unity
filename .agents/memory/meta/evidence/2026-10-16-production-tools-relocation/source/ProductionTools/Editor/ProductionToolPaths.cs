// ════════════════════════════════════════════════════════════
//  ProductionToolPaths — 生产类工具（Generator / Baker）的固定资产路径。
//  统一下沉默认路径：每条路径同时用于 Save 落盘与 Load 回看，不各自硬编码。
// ════════════════════════════════════════════════════════════

namespace Mine.Tools
{
    /// <summary>
    /// 生产类工具的固定资产路径注册表。
    /// </summary>
    ///
    /// <remarks>
    /// 约定：Save 与 Load 共用同一路径，避免两个字段各自漂移。
    /// 新工具在此登记后由窗口引用，窗口内不再出现路径字面量。
    /// 路径与实际落盘位置一致（烘焙纹理在 Assets/Textures，曲线数据在 Assets/Mine/Data/Curves）。
    /// </remarks>
    public static class ProductionToolPaths
    {
        // ── 固定根目录 ──────────────────────────────────────────

        /// <summary>烘焙纹理资产根（FGD / SSS LUT、噪声、Interior Map）。</summary>
        public const string BakedTextureRoot = "Assets/Textures";

        /// <summary>曲线数据资产根。</summary>
        public const string CurveDataRoot = "Assets/Mine/Data/Curves";

        // ── 各工具固定路径 ──────────────────────────────────────

        public const string FgdLut = BakedTextureRoot + "/FGD_LUT.asset";
        public const string SssLut = BakedTextureRoot + "/SSS_LUT.asset";
        public const string Noise2D = BakedTextureRoot + "/Noise2D.asset";
        public const string Noise3D = BakedTextureRoot + "/Noise3D.asset";
        public const string InteriorMap = BakedTextureRoot + "/InteriorMap.asset";
        public const string Curve = CurveDataRoot + "/NewCurve.asset";
    }
}
