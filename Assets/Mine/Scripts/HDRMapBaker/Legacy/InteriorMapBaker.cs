using UnityEngine;
using Mine.HDRMaps;

/// <summary>Legacy serialized room layout. New output layouts belong to HDRMapConverter.</summary>
public enum InteriorMapBakeType { Box = 0, Hemisphere = 1 }

/// <summary>Preserves existing scene script GUIDs while using the HDR capture framework.</summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("")]
public sealed class InteriorMapBaker : HDRMapBaker { }
