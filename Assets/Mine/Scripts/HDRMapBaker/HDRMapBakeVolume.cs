using UnityEngine;
using Mine.GizmoRendering;

namespace Mine.HDRMaps
{
    /// <summary>A capture-range guide; its farthest corner defines the radial far clip distance.</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class HDRMapBakeVolume : MonoBehaviour, IGizmoSource
    {
        [Header("Capture Range")]
        [SerializeField] private Vector3 _center = Vector3.zero;
        [SerializeField] private Vector3 _size = new Vector3(2f, 2f, 3f);

        [Header("Debug")]
        [SerializeField] private string _gizmoMember = "Interior";

        public Vector3 Center => transform.TransformPoint(_center);
        public Vector3 LocalCenter => _center;
        public Vector3 Size => _size;
        public Matrix4x4 LocalToWorldMatrix => transform.localToWorldMatrix;
        public string Member { get => _gizmoMember; set => _gizmoMember = value; }
        public GizmoForm Form => GizmoForm.Volume;

        // ════════════════════════════════════════════════════════════
        //  Range guide — capture geometry remains independent from converter coverage
        // ════════════════════════════════════════════════════════════
        private void OnDrawGizmos() => GizmoRender.Draw(this, false);
        private void OnDrawGizmosSelected() => GizmoRender.Draw(this, true);

        public void DrawGizmos(GizmoCanvas canvas)
        {
            using (canvas.LocalSpace(transform.localToWorldMatrix))
                canvas.WireBox(_center, _size);
        }

        private void OnValidate()
        {
            _size.x = Mathf.Max(_size.x, 0.001f);
            _size.y = Mathf.Max(_size.y, 0.001f);
            _size.z = Mathf.Max(_size.z, 0.001f);
        }
    }
}
