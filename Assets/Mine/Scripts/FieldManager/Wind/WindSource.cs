using UnityEngine;
using System.Collections.Generic;

namespace Mine.Fields
{
    /// <summary>Local wind velocity source in the XZ plane, with smooth radial falloff.</summary>
    [ExecuteAlways]
    public sealed class WindSource : MonoBehaviour
    {
        [Header("Technical")]
        [SerializeField, Min(0.01f)] private float _radius = 5f;
        [Header("Artistic")]
        [SerializeField] private Vector3 _velocity = new Vector3(5f, 0f, 0f);

        internal static readonly List<WindSource> ActiveSources = new List<WindSource>();
        public float Radius => Mathf.Max(0.01f, _radius);
        public Vector3 Velocity => _velocity;

        private void OnEnable()
        {
            if (!ActiveSources.Contains(this)) ActiveSources.Add(this);
        }

        private void OnDisable() => ActiveSources.Remove(this);

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, Radius);
            Gizmos.DrawLine(transform.position, transform.position + Velocity);
        }
    }
}
