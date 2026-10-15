using System;
using System.Collections.Generic;
using UnityEngine;
using CelToon.PathGenerator.Contracts;

namespace CelToon.PathGenerator.Runtime
{
    /// <summary>Optional multi-actor route provider; owns no animation, rig or motion execution.</summary>
    public sealed class NavigationManager : MonoBehaviour, IRouteProvider
    {
        [SerializeField] private LayerMask obstacleMask = 1 << 10;
        [SerializeField, Min(.25f)] private float cellSize = .5f;
        [SerializeField, Min(.1f)] private float clearance = .3f;
        private readonly Dictionary<(int, uint), RouteSnapshot> routes = new Dictionary<(int, uint), RouteSnapshot>();
        private INavigationWorld world;
        private uint version;

        public void ConfigureWorld(INavigationWorld navigationWorld) { world = navigationWorld; }

        public void Submit(in RouteRequest request)
        {
            if (!PathValidation.IsFinite(request.StartWS) || !PathValidation.IsFinite(request.DestinationWS) ||
                !PathValidation.IsFinite(request.RequestedAt) || request.Epoch == 0 || request.RequestId == 0)
            { PublishInvalid(request, "route.invalidRequest"); return; }
            var generator = new GridPathGenerator(world ?? new PhysicsNavigationWorld(obstacleMask), cellSize, clearance);
            if (!generator.TryGenerate(request.StartWS, request.DestinationWS, out var points))
            { PublishInvalid(request, "route.blocked"); return; }
            routes[(request.ActorId, request.Epoch)] = new RouteSnapshot(points)
            {
                ActorId = request.ActorId, Epoch = request.Epoch, RequestId = request.RequestId,
                RouteVersion = ++version, Status = RouteStatus.Complete,
                RequestedDestinationWS = request.DestinationWS, ResolvedDestinationWS = request.DestinationWS,
                CreatedAt = request.RequestedAt
            };
        }

        public void Cancel(int actorId, uint epoch)
        {
            if (!routes.ContainsKey((actorId, epoch))) return;
            routes[(actorId, epoch)] = new RouteSnapshot(Array.Empty<RouteWaypoint>())
            { ActorId = actorId, Epoch = epoch, RouteVersion = ++version, Status = RouteStatus.Cancelled };
        }

        public bool TryGetLatest(int actorId, uint epoch, out RouteSnapshot route) => routes.TryGetValue((actorId, epoch), out route);

        /// <summary>Release a despawned actor's route without affecting other actors.</summary>
        public void Release(int actorId, uint epoch) { routes.Remove((actorId, epoch)); }

        private void PublishInvalid(in RouteRequest request, string reason)
        {
            routes[(request.ActorId, request.Epoch)] = new RouteSnapshot(Array.Empty<RouteWaypoint>())
            {
                ActorId = request.ActorId, Epoch = request.Epoch, RequestId = request.RequestId,
                RouteVersion = ++version, Status = RouteStatus.Invalid, FailureCode = reason
            };
        }
    }
}
