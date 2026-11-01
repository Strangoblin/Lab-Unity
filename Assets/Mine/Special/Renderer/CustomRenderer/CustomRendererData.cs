using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

#if UNITY_EDITOR
using UnityEditor;
#endif

[Serializable]
public class CustomRendererData : ScriptableRendererData
{
#if UNITY_EDITOR
    [MenuItem("Assets/Create/Rendering/Custom Renderer Data")]
    public static void CreateCustomRendererData()
    {
        CustomRendererData instance = ScriptableObject.CreateInstance<CustomRendererData>();
        ProjectWindowUtil.CreateAsset(instance, "CustomRendererData.asset");
    }
#endif

    [SerializeField] LayerMask m_OpaqueLayerMask = -1;
    [SerializeField] LayerMask m_TransparentLayerMask = -1;
    [SerializeField] LayerMask m_DepthLayerMask  = -1;
    [SerializeField] LayerMask m_ColorLayerMask  = -1;
    [SerializeField] LayerMask m_NormalLayerMask = -1;
    [SerializeField] bool m_CopyDepth  = true;
    [SerializeField] bool m_CopyColor  = true;
    [SerializeField] bool m_CopyNormal = false;
    [SerializeField] bool m_TransparentMode = false;

    public LayerMask opaqueLayerMask      { get => m_OpaqueLayerMask;      set => m_OpaqueLayerMask      = value; }
    public LayerMask transparentLayerMask { get => m_TransparentLayerMask; set => m_TransparentLayerMask = value; }
    public LayerMask depthLayerMask       { get => m_DepthLayerMask;       set => m_DepthLayerMask       = value; }
    public LayerMask colorLayerMask       { get => m_ColorLayerMask;       set => m_ColorLayerMask       = value; }
    public LayerMask normalLayerMask      { get => m_NormalLayerMask;      set => m_NormalLayerMask      = value; }
    public bool copyDepth                 { get => m_CopyDepth;            set => m_CopyDepth            = value; }
    public bool copyColor                 { get => m_CopyColor;            set => m_CopyColor            = value; }
    public bool copyNormal                { get => m_CopyNormal;           set => m_CopyNormal           = value; }
    public bool transparentMode           { get => m_TransparentMode;      set => m_TransparentMode      = value; }

    protected override ScriptableRenderer Create()
    {
        return new CustomRenderer(this);
    }
}
