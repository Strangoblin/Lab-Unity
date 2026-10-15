using System;
using UnityEngine;

namespace CelToon.PathGenerator.Contracts
{
    public enum RouteStatus { None, Pending, Complete, Partial, Invalid, Cancelled }
    public enum SegmentKind { Walk, OffMesh, Unsupported }

    [Serializable]
    public struct RouteRequest
    {
        public int ActorId; public uint Epoch; public uint RequestId;
        public Vector3 StartWS; public Vector3 DestinationWS;
        public int AgentTypeId; public int AreaMask; public float StopDistance; public double RequestedAt;
    }

    [Serializable]
    public struct RouteWaypoint
    {
        public Vector3 PositionWS; public float CumulativeDistance; public SegmentKind SegmentKind;
        public RouteWaypoint(Vector3 position, float distance, SegmentKind kind = SegmentKind.Walk)
        { PositionWS = position; CumulativeDistance = distance; SegmentKind = kind; }
    }

    [Serializable]
    public sealed class RouteSnapshot
    {
        public int ActorId; public uint Epoch; public uint RequestId; public uint RouteVersion;
        public RouteStatus Status; public Vector3 RequestedDestinationWS; public Vector3 ResolvedDestinationWS;
        public double CreatedAt; public double ValidUntil = double.MaxValue; public string FailureCode;
        public readonly RouteWaypoint[] Waypoints;
        public RouteSnapshot(RouteWaypoint[] waypoints) { Waypoints = waypoints == null ? Array.Empty<RouteWaypoint>() : (RouteWaypoint[])waypoints.Clone(); }
    }

    public interface IRouteProvider { void Submit(in RouteRequest request); void Cancel(int actorId, uint epoch); bool TryGetLatest(int actorId, uint epoch, out RouteSnapshot route); }
}
