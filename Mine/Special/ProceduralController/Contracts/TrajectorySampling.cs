using System;
using UnityEngine;

namespace CelToon.ProceduralController.Contracts
{
    public static class TrajectorySampling
    {
        public static ModuleTargetSample Sample(ModuleTrajectory trajectory, double time)
        {
            if (!SnapshotValidation.ValidateTrajectory(trajectory, out _)) return new ModuleTargetSample { Valid = false };
            var knots = trajectory.Knots;
            int next = 1;
            while (next < knots.Length - 1 && time >= knots[next].Time) next++;
            var a = knots[next - 1]; var b = knots[next];
            float segmentDuration = (float)(b.Time - a.Time);
            float t = Mathf.Clamp01((float)((time - a.Time) / (b.Time - a.Time)));
            float t2 = t * t; float t3 = t2 * t;
            Vector3 p = (2f * t3 - 3f * t2 + 1f) * a.PoseWS.position + (t3 - 2f * t2 + t) * segmentDuration * a.VelocityWS + (-2f * t3 + 3f * t2) * b.PoseWS.position + (t3 - t2) * segmentDuration * b.VelocityWS;
            Quaternion r = Quaternion.Slerp(a.PoseWS.rotation, b.PoseWS.rotation, t);
            Vector3[] chain = null;
            if (a.ChainPointsWS != null)
            {
                chain = new Vector3[a.ChainPointsWS.Length];
                for (int i = 0; i < chain.Length; i++) chain[i] = Vector3.Lerp(a.ChainPointsWS[i], b.ChainPointsWS[i], t);
            }
            return new ModuleTargetSample { ChainPointsWS = chain, SampleTime = time, PoseWS = new Pose(p, r), PositionWeight = Mathf.Lerp(a.PositionWeight, b.PositionWeight, t), RotationWeight = Mathf.Lerp(a.RotationWeight, b.RotationWeight, t), HintPositionWS = Vector3.Lerp(a.HintPositionWS, b.HintPositionWS, t), HintWeight = Mathf.Lerp(a.HintWeight, b.HintWeight, t), Phase = t < 1f ? a.Phase : b.Phase, ContactId = t < 1f ? a.ContactId : b.ContactId, Valid = true };
        }
    }
}
