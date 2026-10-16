using UnityEngine;
using Mine.GizmoRendering;

namespace Mine.HDRMaps
{
    /// <summary>Defines the panorama origin and its local +Y up, +Z forward orientation.</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class HDRMapBakeDirection : MonoBehaviour, IGizmoSource
    {
        [Header("Capture Frame")]
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
        public string Member { get => _gizmoMember; set => _gizmoMember = value; }
        public GizmoForm Form => GizmoForm.Direction;

        // ════════════════════════════════════════════════════════════
        //  Frame guide — direction affects capture once, without a second conversion rotation
        // ════════════════════════════════════════════════════════════
        private void OnDrawGizmos() => GizmoRender.Draw(this, false);
        private void OnDrawGizmosSelected() => GizmoRender.Draw(this, true);

        public void DrawGizmos(GizmoCanvas canvas)
        {
            canvas.Arrow(Origin, Direction, _length, _headLength, _headWidth);
            canvas.WireSphere(Origin, _headWidth * 0.35f);
        }

        private void OnValidate()
        {
            _length = Mathf.Max(_length, 0.001f);
            _headLength = Mathf.Clamp(_headLength, 0.001f, _length);
            _headWidth = Mathf.Max(_headWidth, 0.001f);
        }
    }
}
