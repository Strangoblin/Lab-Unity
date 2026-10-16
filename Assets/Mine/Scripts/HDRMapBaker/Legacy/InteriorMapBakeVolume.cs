using UnityEngine;
using Mine.HDRMaps;

/// <summary>Legacy scene binding for the shared HDR capture-range guide.</summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("")]
public sealed class InteriorMapBakeVolume : HDRMapBakeVolume
{
    [SerializeField, HideInInspector] private InteriorMapBakeType _bakeType;
    public InteriorMapBakeType BakeType => _bakeType;
    public void SetBakeType(InteriorMapBakeType bakeType) => _bakeType = bakeType;
}
