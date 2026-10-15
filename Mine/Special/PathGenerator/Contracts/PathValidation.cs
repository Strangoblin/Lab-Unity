using UnityEngine;

namespace CelToon.PathGenerator.Contracts
{
    public static class PathValidation
    {
        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        public static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        public static bool ValidateRoute(RouteSnapshot route, out string error)
        {
            error = null;
            if (route == null) { error = "route.null"; return false; }
            if (route.Epoch == 0) { error = "route.epoch"; return false; }
            if (!IsFinite(route.CreatedAt) || !IsFinite(route.ValidUntil)) { error = "route.time"; return false; }
            if (route.Waypoints == null) { error = "route.waypoints.null"; return false; }
            float lastDistance = 0f;
            for (int i = 0; i < route.Waypoints.Length; i++)
            {
                var point = route.Waypoints[i];
                if (!IsFinite(point.PositionWS) || !IsFinite(point.CumulativeDistance) || point.CumulativeDistance < lastDistance)
                { error = "route.waypoints[" + i + "]"; return false; }
                if (point.SegmentKind != SegmentKind.Walk) { error = "route.segment.unsupported"; return false; }
                lastDistance = point.CumulativeDistance;
            }
            if (route.Status == RouteStatus.Complete && route.Waypoints.Length < 2) { error = "route.complete.empty"; return false; }
            return true;
        }
    }
}
