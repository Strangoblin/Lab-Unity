#if UNITY_EDITOR
using UnityEditor;
using Mine.HDRMaps.Editor;

/// <summary>Existing scene components expose the same Inspector as HDRMapBaker.</summary>
[CustomEditor(typeof(InteriorMapBaker))]
public sealed class InteriorMapBakerEditor : HDRMapBakerEditor { }
#endif
