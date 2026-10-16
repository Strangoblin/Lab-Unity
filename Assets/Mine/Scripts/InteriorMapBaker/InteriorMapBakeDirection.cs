using UnityEngine;
using Mine.SceneGizmo;

/// <summary>
/// Defines the forward direction used by an InteriorMap baker and visualizes it as a Scene view arrow.
/// The component transform supplies the orientation; the local offset supplies the arrow origin.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class InteriorMapBakeDirection : MonoBehaviour, IGizmoSource
{
    [Header("Direction")]
    [SerializeField] private Vector3 _originOffset = Vector3.zero;
    [SerializeField] private float _length = 1.5f;

    [Header("Arrow")]
    [SerializeField] private float _headLength = 0.3f;
    [SerializeField] private float _headWidth = 0.15f;

    [Header("Debug")]
    [SerializeField] private string _gizmoMember = "Interior";

    public Vector3 Origin => transform.TransformPoint(_originOffset);
    public Vector3 Direction => transform.forward;
    public Vector3 EndPoint => Origin + Direction * _length;
    public float Length => _length;

    // ════════════════════════════════════════════════════════════
    //  Gizmo declaration — the bake view direction
    // ════════════════════════════════════════════════════════════
    public string Member
    {
        get => _gizmoMember;
        set => _gizmoMember = value;
    }

    public GizmoForm Form => GizmoForm.Direction;

    private void OnDrawGizmos() => SceneGizmos.Draw(this, false);

    private void OnDrawGizmosSelected() => SceneGizmos.Draw(this, true);

    public void DrawGizmos(GizmoCanvas canvas)
    {
        Vector3 origin = Origin;
        canvas.Arrow(origin, Direction, _length, _headLength, _headWidth);

        // Origin marker — shows where the baked panorama is observed from
        canvas.WireSphere(origin, _headWidth * 0.35f);
    }

    // ════════════════════════════════════════════════════════════
    //  Validation — keep the direction marker readable
    // ════════════════════════════════════════════════════════════
    private void OnValidate()
    {
        _length = Mathf.Max(_length, 0.001f);
        _headLength = Mathf.Clamp(_headLength, 0.001f, _length);
        _headWidth = Mathf.Max(_headWidth, 0.001f);
    }
}
