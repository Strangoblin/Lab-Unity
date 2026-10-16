namespace Mine.HDRMaps
{
    /// <summary>Texture layout; these values also form the HDRMapFunction.hlsl contract.</summary>
    public enum HDRMapProjection
    {
        Equirectangular = 0,
        Octahedral = 1,
        EqualAreaOctahedral = 2,
        Hemisphere = 3
    }

    /// <summary>Coverage is independent of projection and room intersection geometry.</summary>
    public enum HDRMapCoverage
    {
        Panorama = 0,
        Interior = 1
    }
}
