using System;
using UnityEngine;

namespace CelToon.ProceduralController.Execution
{
    /// Pure, allocation-free analytic two segment IK. All positions and rotations are world space.
    public static class TwoBoneSolver
    {
        public struct Input
        {
            public Vector3 RootPosition;
            public Vector3 MidPosition;
            public Vector3 TipPosition;
            public Vector3 TargetPosition;
            public Vector3 HintPosition;
            public Quaternion RootRotation;
            public Quaternion MidRotation;
            public Quaternion TipRotation;
            public Quaternion TargetRotation;
            public float PositionWeight;
            public float RotationWeight;
            public float Epsilon;
        }

        public struct Result
        {
            public bool Valid;
            public bool Clamped;
            public bool UsedFallbackPlane;
            public Vector3 ClampedTarget;
            public Vector3 SolvedMidPosition;
            public Quaternion RootRotation;
            public Quaternion MidRotation;
            public Quaternion TipRotation;
            public float UpperLength;
            public float LowerLength;
            public float ReachError;
            public string Error;
        }

        public static Result Solve(in Input input)
        {
            var result = new Result { Valid = false, ClampedTarget = input.TargetPosition };
            var eps = input.Epsilon > 0f ? input.Epsilon : 1e-5f;
            if (!Finite(input.RootPosition) || !Finite(input.MidPosition) || !Finite(input.TipPosition) || !Finite(input.TargetPosition) ||
                !Finite(input.HintPosition) || !Finite(input.RootRotation) || !Finite(input.MidRotation) || !Finite(input.TipRotation) ||
                !Finite(input.TargetRotation)) { result.Error = "nonFinite"; return result; }
            var upper = input.MidPosition - input.RootPosition;
            var lower = input.TipPosition - input.MidPosition;
            var a = upper.magnitude;
            var b = lower.magnitude;
            if (a <= eps || b <= eps) { result.Error = "zeroSegment"; return result; }
            result.UpperLength = a; result.LowerLength = b;

            var requested = Vector3.Lerp(input.TipPosition, input.TargetPosition, Mathf.Clamp01(input.PositionWeight));
            var offset = requested - input.RootPosition;
            var distance = offset.magnitude;
            var direction = distance > eps ? offset / distance : upper.normalized;
            var minReach = Mathf.Abs(a - b) + eps;
            var maxReach = a + b - eps;
            if (maxReach < minReach) { minReach = Mathf.Abs(a - b); maxReach = a + b; }
            var solvedDistance = Mathf.Clamp(distance, minReach, maxReach);
            result.Clamped = distance < minReach || distance > maxReach;
            result.ClampedTarget = input.RootPosition + direction * solvedDistance;

            var plane = Vector3.ProjectOnPlane(input.HintPosition - input.RootPosition, direction);
            if (plane.sqrMagnitude <= eps * eps) plane = Vector3.ProjectOnPlane(upper, direction);
            if (plane.sqrMagnitude <= eps * eps)
            {
                plane = Vector3.ProjectOnPlane(Vector3.up, direction);
                if (plane.sqrMagnitude <= eps * eps) plane = Vector3.ProjectOnPlane(Vector3.right, direction);
                result.UsedFallbackPlane = true;
            }
            plane.Normalize();
            var along = (solvedDistance * solvedDistance + a * a - b * b) / (2f * solvedDistance);
            var height = Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
            var mid = input.RootPosition + direction * along + plane * height;
            var desiredUpper = mid - input.RootPosition;
            var desiredLower = result.ClampedTarget - mid;
            var rootSolved = Quaternion.FromToRotation(upper, desiredUpper) * input.RootRotation;
            // The controller assigns WORLD rotations. Rotate the original world lower vector
            // directly; a parent-relative delta would apply the upper rotation twice/omit it.
            var midSolved = Quaternion.FromToRotation(lower, desiredLower) * input.MidRotation;
            var rotWeight = Mathf.Clamp01(input.RotationWeight);
            result.SolvedMidPosition = mid;
            result.RootRotation = Quaternion.Slerp(input.RootRotation, rootSolved, Mathf.Clamp01(input.PositionWeight));
            result.MidRotation = Quaternion.Slerp(input.MidRotation, midSolved, Mathf.Clamp01(input.PositionWeight));
            result.TipRotation = Quaternion.Slerp(input.TipRotation, input.TargetRotation, rotWeight);
            result.ReachError = Vector3.Distance(result.ClampedTarget, requested);
            result.Valid = true;
            return result;
        }

        private static bool Finite(Vector3 v) => IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
        private static bool Finite(Quaternion q) => IsFinite(q.x) && IsFinite(q.y) && IsFinite(q.z) && IsFinite(q.w) && (q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w) > 1e-12f;
        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
