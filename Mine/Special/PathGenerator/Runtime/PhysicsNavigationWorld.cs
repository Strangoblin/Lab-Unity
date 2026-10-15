using UnityEngine;

namespace CelToon.PathGenerator.Runtime
{
    public interface INavigationWorld
    {
        bool IsSegmentBlocked(Vector3 start, Vector3 end);
        bool IsCellBlocked(Vector3 center, Vector3 halfExtents);
    }

    /// <summary>Unity Physics adapter for the grid search. Supply another world to replace collision queries.</summary>
    public sealed class PhysicsNavigationWorld : INavigationWorld
    {
        private readonly int obstacleMask;
        public PhysicsNavigationWorld(int obstacleMask) { this.obstacleMask = obstacleMask; }
        public bool IsSegmentBlocked(Vector3 start, Vector3 end) =>
            Physics.Linecast(start + Vector3.up * .35f, end + Vector3.up * .35f, obstacleMask, QueryTriggerInteraction.Ignore);
        public bool IsCellBlocked(Vector3 center, Vector3 halfExtents) =>
            Physics.CheckBox(center, halfExtents, Quaternion.identity, obstacleMask, QueryTriggerInteraction.Ignore);
    }
}
