// ════════════════════════════════════════════════════════════════
//  SSGI Controls — one owner for quality, intensity and temporal weights.
// ════════════════════════════════════════════════════════════════
using System;
using UnityEngine;

public enum SSGIQuality { Low, Medium, High }

public enum SSGITraceBackend { DDA = 0, Ray = 1 }

public enum SSGITraceMode { DDA = 0, Ray = 1, HiZ = 2 }

[Serializable]
public sealed class SSGITraceControls
{
    public SSGITraceMode mode = SSGITraceMode.DDA;
    public ComputeShader depthPyramidShader;
    [SerializeField, HideInInspector] bool _initialized;

    internal void Initialize(SSGITraceBackend legacyBackend, bool legacyHiZ, ComputeShader legacyShader)
    {
        if (_initialized)
            return;
        if (mode == SSGITraceMode.DDA && depthPyramidShader == null)
        {
            mode = legacyBackend == SSGITraceBackend.Ray ? SSGITraceMode.Ray
                : legacyHiZ ? SSGITraceMode.HiZ : SSGITraceMode.DDA;
            depthPyramidShader = legacyShader;
        }
        _initialized = true;
    }
}

[Serializable]
public sealed class SSGIPerformanceControls
{
    public SSGIQuality ao = SSGIQuality.Medium;
    public SSGIQuality diffuseGI = SSGIQuality.Medium;
    public SSGIQuality specularGI = SSGIQuality.Medium;
}

[Serializable]
public sealed class SSGIIntensityControls
{
    [Range(0f, 10f)] public float ao = 1f;
    [Range(0f, 10f)] public float diffuseGI = 1f;
    [Range(0f, 10f)] public float specularGI = 1f;
}

[Serializable]
public sealed class SSGITemporalControls
{
    [Range(0f, 0.98f)] public float ao = 0.85f;
    [Range(0f, 0.98f)] public float diffuseGI = 0.9f;
    [Range(0f, 0.98f)] public float specularGI = 0.5f;
}

[Serializable]
public class SSGIStandaloneControls
{
    [Header("Resources")]
    public Shader temporalShader;

    [Header("Performance")]
    public SSGIQuality performance = SSGIQuality.Medium;

    [Header("Temporal")]
    [Range(0f, 0.98f)] public float temporalBlend = 0.9f;
}
