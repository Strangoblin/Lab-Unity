using System.Collections.Generic;
using UnityEngine;

namespace CelToon.ProceduralController.Runtime
{
    /// <summary>Environment observation for automatic grasping; no limb command or input is exposed.</summary>
    public sealed class GraspableObject : MonoBehaviour
    {
        private static readonly HashSet<GraspableObject> Active = new HashSet<GraspableObject>();
        [SerializeField, Min(0.05f)] private float radius = 0.4f;
        public Vector3 Center => transform.position;
        public float Radius => radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);
        public void Configure(float sphereRadius) { radius = sphereRadius; }
        private void OnEnable() { Active.Add(this); }
        private void OnDisable() { Active.Remove(this); }
        public static GraspableObject Nearest(Vector3 position, float reach)
        {
            GraspableObject best = null;
            float distance = reach;
            foreach (var item in Active)
            {
                if (!item || !item.isActiveAndEnabled) continue;
                float current = Vector3.Distance(position, item.Center);
                if (current < distance) { best = item; distance = current; }
            }
            return best;
        }
    }
}
