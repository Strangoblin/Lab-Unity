using CelToon.PathGenerator.Contracts;
using NUnit.Framework;
using UnityEngine;
using CelToon.ProceduralController.Contracts;

namespace CelToon.ProceduralController.Tests
{
    public class ContractsTests
    {
        [Test] public void RejectsNonFiniteRoutePoint()
        {
            var route = new RouteSnapshot(new[] { new RouteWaypoint(new Vector3(float.NaN, 0, 0), 0) }) { Epoch = 1, Status = RouteStatus.Complete };
            Assert.IsFalse(SnapshotValidation.ValidateRoute(route, out var error));
            Assert.That(error, Does.Contain("waypoints"));
        }

        [Test] public void RejectsDuplicatePlanModules()
        {
            var knotA = new ModuleTrajectoryKnot { Time = 0, PoseWS = new Pose(Vector3.zero, Quaternion.identity) };
            var knotB = new ModuleTrajectoryKnot { Time = 1, PoseWS = new Pose(Vector3.zero, Quaternion.identity) };
            var trajectory = new ModuleTrajectory { Module = new ModuleId("FL", ModuleKind.Limb), Knots = new[] { knotA, knotB } };
            var plan = new ModulePlanFrame { Epoch = 1, ModuleIds = new[] { new ModuleId("FL", ModuleKind.Limb), new ModuleId("FL", ModuleKind.Limb) }, Trajectories = new[] { trajectory, trajectory } };
            Assert.IsFalse(SnapshotValidation.ValidatePlan(plan, out _));
        }

        [Test] public void HermiteSamplingKeepsEndpoints()
        {
            var a = new ModuleTrajectoryKnot { Time = 0, PoseWS = new Pose(Vector3.zero, Quaternion.identity), PositionWeight = 0 };
            var b = new ModuleTrajectoryKnot { Time = 1, PoseWS = new Pose(Vector3.right, Quaternion.Euler(0, 90, 0)), PositionWeight = 1 };
            var trajectory = new ModuleTrajectory { Module = new ModuleId("FL", ModuleKind.Limb), Knots = new[] { a, b } };
            var sample = TrajectorySampling.Sample(trajectory, 1);
            Assert.IsTrue(sample.Valid);
            Assert.That(Vector3.Distance(sample.PoseWS.position, Vector3.right), Is.LessThan(1e-5f));
            Assert.That(sample.PositionWeight, Is.EqualTo(1).Within(1e-5f));
        }

        [Test] public void ChainSamplingInterpolatesWithoutMutatingPublishedPlan()
        {
            var a = new ModuleTrajectoryKnot { Time = 0, PoseWS = new Pose(Vector3.right, Quaternion.identity),
                ChainPointsWS = new[] { Vector3.zero, Vector3.right } };
            var b = new ModuleTrajectoryKnot { Time = 1, PoseWS = new Pose(Vector3.right + Vector3.up, Quaternion.identity),
                ChainPointsWS = new[] { Vector3.up, Vector3.up + Vector3.right } };
            var trajectory = new ModuleTrajectory { Module = new ModuleId("Tentacle", ModuleKind.Tentacle), Knots = new[] { a, b } };
            var sample = TrajectorySampling.Sample(trajectory, 0.5d);
            Assert.IsTrue(sample.Valid);
            Assert.AreEqual(Vector3.up * 0.5f, sample.ChainPointsWS[0]);
            sample.ChainPointsWS[0] = Vector3.one * 99f;
            Assert.AreEqual(Vector3.zero, a.ChainPointsWS[0]);
        }

        [Test] public void ChainValidationRejectsMismatchedCountsAndNonFiniteNodes()
        {
            var a = new ModuleTrajectoryKnot { Time = 0, PoseWS = new Pose(Vector3.zero, Quaternion.identity),
                ChainPointsWS = new[] { Vector3.zero, Vector3.right } };
            var b = a; b.Time = 1; b.ChainPointsWS = new[] { Vector3.zero, Vector3.right, Vector3.up };
            var trajectory = new ModuleTrajectory { Module = new ModuleId("Spine", ModuleKind.Spine), Knots = new[] { a, b } };
            Assert.IsFalse(SnapshotValidation.ValidateTrajectory(trajectory, out _));
            b.ChainPointsWS = new[] { Vector3.zero, new Vector3(float.NaN, 0f, 0f) };
            trajectory.Knots[1] = b;
            Assert.IsFalse(TrajectorySampling.Sample(trajectory, 0.5d).Valid);
        }
    }
}
