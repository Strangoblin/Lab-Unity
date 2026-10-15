using UnityEngine;
using CelToon.ProceduralController.Contracts;

namespace CelToon.ProceduralController.Environment
{
    public sealed class PhysicsGroundProbe : MonoBehaviour, IGroundProbe
    {
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;
        [SerializeField] private Collider selfCollider;
        public bool TryProbe(in GroundQuery query, out GroundHit hit)
        {
            hit = default;
            if (!SnapshotValidation.IsFinite(query.OriginWS) || !SnapshotValidation.IsFinite(query.DirectionWS) || query.Distance <= 0f || query.DirectionWS.sqrMagnitude < 1e-8f) return false;
            Vector3 direction = query.DirectionWS.normalized;
            int mask = query.LayerMask != 0 ? query.LayerMask : groundMask.value;
            RaycastHit[] hits = Physics.SphereCastAll(query.OriginWS, Mathf.Max(0f, query.Radius), direction, query.Distance, mask, triggerInteraction);
            float best = float.PositiveInfinity; RaycastHit selected = default; bool found = false;
            foreach (var candidate in hits)
            {
                if (candidate.collider == null || candidate.collider == selfCollider || candidate.collider.transform.IsChildOf(transform)) continue;
                if (candidate.distance < best) { best = candidate.distance; selected = candidate; found = true; }
            }
            if (!found) return false;
            hit = new GroundHit { Found = true, PointWS = selected.point, NormalWS = selected.normal, Distance = selected.distance, SurfaceId = selected.collider.GetInstanceID(), IsStatic = selected.rigidbody == null || selected.rigidbody.isKinematic };
            return SnapshotValidation.IsFinite(hit.PointWS) && SnapshotValidation.IsFinite(hit.NormalWS);
        }
    }
}
