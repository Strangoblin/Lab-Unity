using UnityEngine;
using Mine.SceneGizmo;

/// <summary>
/// Defines the local-space volume that an InteriorMap baker should use as its room range.
/// The transform controls the volume pose; the serialized center and size describe its shape.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class InteriorMapBakeVolume : MonoBehaviour, IGizmoSource
{
    [Header("Range")]
    [SerializeField] private Vector3 _center = Vector3.zero;
    [SerializeField] private Vector3 _size = new Vector3(2f, 2f, 3f);
    [SerializeField] private InteriorMapBakeType _bakeType = InteriorMapBakeType.Box;

    [Header("Debug")]
    [SerializeField] private string _gizmoMember = "Interior";

    public Vector3 Center => transform.TransformPoint(_center);
    public Vector3 LocalCenter => _center;
    public Vector3 Size => _size;
    public InteriorMapBakeType BakeType => _bakeType;
    public Matrix4x4 LocalToWorldMatrix => transform.localToWorldMatrix;

    /// <summary>
    /// Updates the visualization mode used by the selected baker workflow.
    /// </summary>
    public void SetBakeType(InteriorMapBakeType bakeType)
    {
        _bakeType = bakeType;
    }

    // ════════════════════════════════════════════════════════════
    //  Gizmo declaration — the selected Box or rear Hemisphere range
    // ════════════════════════════════════════════════════════════
    public string Member
    {
        get => _gizmoMember;
        set => _gizmoMember = value;
    }

    public GizmoForm Form => GizmoForm.Volume;

    private void OnDrawGizmos() => SceneGizmos.Draw(this, false);

    private void OnDrawGizmosSelected() => SceneGizmos.Draw(this, true);

    public void DrawGizmos(GizmoCanvas canvas)
    {
        using (canvas.LocalSpace(transform.localToWorldMatrix))
        {
            if (_bakeType == InteriorMapBakeType.Hemisphere)
            {
                // Radius mirrors the sphere the Hemisphere projection intersects
                float radius = Mathf.Max(_size.x, Mathf.Max(_size.y, _size.z)) * 0.5f;
                canvas.WireHemisphere(_center, radius, Vector3.back);
            }
            else
            {
                canvas.WireBox(_center, _size);
            }
        }
    }

    // ════════════════════════════════════════════════════════════
    //  Validation — keep the editable bake range valid
    // ════════════════════════════════════════════════════════════
    private void OnValidate()
    {
        _size.x = Mathf.Max(_size.x, 0.001f);
        _size.y = Mathf.Max(_size.y, 0.001f);
        _size.z = Mathf.Max(_size.z, 0.001f);
        _bakeType = (InteriorMapBakeType)Mathf.Clamp(
            (int)_bakeType,
            (int)InteriorMapBakeType.Box,
            (int)InteriorMapBakeType.Hemisphere);
    }
}
