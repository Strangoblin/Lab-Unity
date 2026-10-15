using NUnit.Framework;
using UnityEngine;
using CelToon.ProceduralController.Execution;

namespace CelToon.ProceduralController.Tests
{
    public sealed class TwoBoneSolverTests
    {
        private static TwoBoneSolver.Input Make(Vector3 target, Vector3 hint, Quaternion? root = null, Quaternion? mid = null)
        {
            return new TwoBoneSolver.Input { RootPosition = Vector3.zero, MidPosition = Vector3.right, TipPosition = Vector3.right * 2f, TargetPosition = target, HintPosition = hint, RootRotation = root ?? Quaternion.identity, MidRotation = mid ?? Quaternion.identity, TipRotation = Quaternion.identity, TargetRotation = Quaternion.identity, PositionWeight = 1f, RotationWeight = 1f, Epsilon = 1e-5f };
        }

        [Test] public void ReachableTargetPreservesLengthsAndReachesTip()
        {
            var r = TwoBoneSolver.Solve(Make(new Vector3(1.2f, .8f, .2f), new Vector3(0, 0, 1)));
            Assert.IsTrue(r.Valid); Assert.IsFalse(r.Clamped);
            Assert.That(Vector3.Distance(Vector3.zero, r.SolvedMidPosition), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(Vector3.Distance(r.SolvedMidPosition, r.ClampedTarget), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test] public void FarAndNearTargetsAreClampedAndFinite()
        {
            foreach (var target in new[] { new Vector3(4, 0, 0), Vector3.zero })
            {
                var r = TwoBoneSolver.Solve(Make(target, Vector3.up));
                Assert.IsTrue(r.Valid); Assert.IsTrue(r.Clamped); Assert.IsFalse(float.IsNaN(r.SolvedMidPosition.x));
                Assert.That(Vector3.Distance(Vector3.zero, r.SolvedMidPosition), Is.EqualTo(1f).Within(1e-4f));
            }
        }

        [Test] public void CollinearHintUsesDeterministicFallbackPlane()
        {
            var r = TwoBoneSolver.Solve(Make(new Vector3(1.999f, 0, 0), new Vector3(2, 0, 0)));
            Assert.IsTrue(r.Valid); Assert.IsTrue(r.UsedFallbackPlane); Assert.IsFalse(float.IsNaN(r.SolvedMidPosition.x));
        }

        [Test] public void RotatedAndNonStandardBoneAxesStillSolve()
        {
            var q = Quaternion.Euler(20, 35, 70);
            var root = Vector3.zero; var mid = q * Vector3.right; var tip = mid + q * Vector3.right;
            var input = Make(root + q * new Vector3(1.3f, .6f, 0), root + q * Vector3.forward, q, q);
            input.MidPosition = mid; input.TipPosition = tip;
            var r = TwoBoneSolver.Solve(input);
            Assert.IsTrue(r.Valid); Assert.That(Vector3.Distance(r.SolvedMidPosition, input.RootPosition), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test] public void ZeroWeightKeepsBasePose()
        {
            var input = Make(new Vector3(0, 2, 0), Vector3.forward); input.PositionWeight = 0; input.RotationWeight = 0;
            var r = TwoBoneSolver.Solve(input);
            Assert.That(r.RootRotation, Is.EqualTo(input.RootRotation)); Assert.That(r.MidRotation, Is.EqualTo(input.MidRotation)); Assert.That(r.TipRotation, Is.EqualTo(input.TipRotation));
        }

        [Test] public void WorldRotationsReachTargetOnRealParentedSkeleton()
        {
            var root = new GameObject("Root").transform;
            var mid = new GameObject("Mid").transform;
            var tip = new GameObject("Tip").transform;
            try
            {
                mid.SetParent(root, false); mid.localPosition = Vector3.right;
                tip.SetParent(mid, false); tip.localPosition = Vector3.forward;
                root.rotation = Quaternion.Euler(20, 35, 70);
                var input = Make(root.position + new Vector3(.7f, .8f, .3f), Vector3.up * 2f);
                input.RootPosition = root.position; input.MidPosition = mid.position; input.TipPosition = tip.position;
                input.RootRotation = root.rotation; input.MidRotation = mid.rotation;
                var result = TwoBoneSolver.Solve(input);
                root.rotation = result.RootRotation; mid.rotation = result.MidRotation;
                Assert.Less(Vector3.Distance(tip.position, input.TargetPosition), .0001f);
            }
            finally { Object.DestroyImmediate(root.gameObject); }
        }

        [Test] public void TargetQuaternionSignIsEquivalent()
        {
            var a = Make(new Vector3(1, .5f, 0), Vector3.forward); a.TargetRotation = Quaternion.Euler(10, 20, 30);
            var b = a; b.TargetRotation = new Quaternion(-a.TargetRotation.x, -a.TargetRotation.y, -a.TargetRotation.z, -a.TargetRotation.w);
            var ra = TwoBoneSolver.Solve(a); var rb = TwoBoneSolver.Solve(b);
            Assert.That(Quaternion.Angle(ra.TipRotation, rb.TipRotation), Is.LessThan(1e-3f));
        }

        [Test] public void InvalidAndZeroSegmentInputsAreRejected()
        {
            var input = Make(Vector3.right, Vector3.up); input.TipPosition = input.MidPosition;
            Assert.IsFalse(TwoBoneSolver.Solve(input).Valid);
            input = Make(Vector3.right, Vector3.up); input.TargetPosition.x = float.NaN;
            Assert.IsFalse(TwoBoneSolver.Solve(input).Valid);
        }
    }
}
