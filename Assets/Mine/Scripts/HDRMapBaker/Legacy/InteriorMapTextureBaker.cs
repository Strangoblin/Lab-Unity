using UnityEngine;
using Mine.HDRMaps;

/// <summary>Legacy API adapter. Capture and projection use the HDR tools' shared implementations.</summary>
public static class InteriorMapTextureBaker
{
    public static Texture2D Bake(InteriorMapBaker framework, int faceResolution,
        int cullingMask = -1, InteriorMapBakeType bakeType = InteriorMapBakeType.Box)
    {
        if (framework == null || !framework.IsInitialized)
            throw new UnityException("Select an initialized HDR capture framework.");
        Texture2D panorama = HDRMapCapture.Bake(framework.BakeCamera,
            framework.Direction.Origin, framework.Direction.transform.rotation,
            HDRMapCapture.CalculateFarClip(framework.Volume, framework.Direction.Origin),
            cullingMask, false, faceResolution);
        if (bakeType == InteriorMapBakeType.Box) return panorama;
        try
        {
            return HDRMapConverter.Convert(panorama, HDRMapProjection.Equirectangular,
                HDRMapProjection.Hemisphere, faceResolution, HDRMapCoverage.Interior);
        }
        finally { Object.DestroyImmediate(panorama); }
    }
}
