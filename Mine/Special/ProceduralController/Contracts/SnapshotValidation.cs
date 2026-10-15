using CelToon.PathGenerator.Contracts;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CelToon.ProceduralController.Contracts
{
    // Contract validation is kept engine-compatible for Unity 2022.3.
    public static class SnapshotValidation
    {
        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        public static bool IsFinite(Quaternion value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        public static bool IsValid(Quaternion value) => IsFinite(value) && (value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w) > 1e-10f;
        public static bool IsValid(Pose pose) => IsFinite(pose.position) && IsValid(pose.rotation);

        public static bool ValidateRoute(RouteSnapshot route, out string error)
        {
            return PathValidation.ValidateRoute(route, out error);
        }

        public static bool ValidateTrajectory(ModuleTrajectory trajectory, out string error)
        {
            error = null;
            if (trajectory == null || string.IsNullOrEmpty(trajectory.Module.Value)) { error = "trajectory.module"; return false; }
            if (trajectory.Knots == null || trajectory.Knots.Length < 2) { error = "trajectory.knots"; return false; }
            double previous = double.NegativeInfinity;
            for (int i = 0; i < trajectory.Knots.Length; i++)
            {
                var knot = trajectory.Knots[i];
                if (double.IsNaN(knot.Time) || double.IsInfinity(knot.Time) || knot.Time <= previous || !IsValid(knot.PoseWS) || !IsFinite(knot.VelocityWS))
                { error = "trajectory.knots[" + i + "]"; return false; }
                previous = knot.Time;
                if (knot.ChainPointsWS != null)
                {
                    if (knot.ChainPointsWS.Length < 2 || (i > 0 && knot.ChainPointsWS.Length != trajectory.Knots[0].ChainPointsWS?.Length))
                    { error = "trajectory.chain.count"; return false; }
                    foreach (var point in knot.ChainPointsWS)
                        if (!IsFinite(point)) { error = "trajectory.chain.nonFinite"; return false; }
                }
                else if (i > 0 && trajectory.Knots[0].ChainPointsWS != null)
                { error = "trajectory.chain.missing"; return false; }
            }
            return true;
        }

        public static bool ValidatePlan(ModulePlanFrame plan, out string error)
        {
            error = null;
            if (plan == null || plan.Epoch == 0) { error = "plan.identity"; return false; }
            if (plan.ModuleIds == null || plan.Trajectories == null || plan.ModuleIds.Length != plan.Trajectories.Length) { error = "plan.modules"; return false; }
            var ids = new HashSet<string>();
            for (int i = 0; i < plan.ModuleIds.Length; i++)
            {
                string id = plan.ModuleIds[i].Value;
                if (string.IsNullOrEmpty(id) || !ids.Add(id)) { error = "plan.moduleIds[" + i + "]"; return false; }
                if (!ValidateTrajectory(plan.Trajectories[i], out error) || plan.Trajectories[i].Module.Value != id) return false;
            }
            return true;
        }
    }
}
