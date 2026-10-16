using UnityEngine;
using System.Collections.Generic;
using Mine.GizmoRendering;

namespace Mine.Fields
{
    /// <summary>Local wind velocity source in the XZ plane, with smooth radial falloff.</summary>
    [ExecuteAlways]
    public sealed class WindSource : MonoBehaviour, IGizmoSource
    {
        [Header("Technical")]
        [SerializeField, Min(0.01f)] private float _radius = 5f;
        [Header("Artistic")]
        [SerializeField] private Vector3 _velocity = new Vector3(5f, 0f, 0f);
        [Header("Debug")]
        [SerializeField] private string _gizmoMember = "Wind";

        internal static readonly List<WindSource> ActiveSources = new List<WindSource>();
        public float Radius => Mathf.Max(0.01f, _radius);
        public Vector3 Velocity => _velocity;

        // ════════════════════════════════════════════════════════════
        //  Gizmo declaration — influence sphere and velocity vector
        // ════════════════════════════════════════════════════════════
        public string Member
        {
            get => _gizmoMember;
            set => _gizmoMember = value;
        }

        public GizmoForm Form => GizmoForm.Volume;

        private void OnDrawGizmos() => GizmoRender.Draw(this, false);

        private void OnDrawGizmosSelected() => GizmoRender.Draw(this, true);

        public void DrawGizmos(GizmoCanvas canvas)
        {
            canvas.WireSphere(transform.position, Radius);

            canvas.Color = GizmoTint.Emphasis(canvas.BaseColor);
            canvas.Arrow(transform.position, Velocity, Velocity.magnitude);
        }

        private void OnEnable()
        {
            if (!ActiveSources.Contains(this)) ActiveSources.Add(this);
        }

        private void OnDisable() => ActiveSources.Remove(this);
    }
}
