using System;
using NUnit.Framework;
using UnityEngine;
using CelToon.PathGenerator.Contracts;
using CelToon.ProceduralController.Contracts;
using CelToon.ProceduralController.Execution;
using CelToon.ProceduralController.Runtime;

namespace CelToon.ProceduralController.Tests
{
    /// <summary>Runs with path contracts alone: no navigation implementation or sample assembly.</summary>
    public sealed class ExternalPathTests
    {
        private GameObject actor;
        private LocomotionPipelineHost host;
        private RigBinding rig;
        private double now;

        [SetUp]
        public void CreateIndividual()
        {
            actor = new GameObject("ExternalPathIndividual");
            actor.transform.position = Vector3.up * .08f;
            host = actor.AddComponent<LocomotionPipelineHost>(); host.AutoUpdate = false;
            var planner = actor.AddComponent<ProceduralAnimationManager>();
            var root = actor.AddComponent<RootMotionController>();
            var driver = Bone(actor.transform, "BodyInput", actor.transform.position);
            var body = Bone(actor.transform, "Body", actor.transform.position + Vector3.up * .88f);
            root.Configure(null, driver);
            rig = actor.AddComponent<RigBinding>();
            var limbs = new RigBinding.ModuleBinding[4];
            var controllers = new IModuleController[5];
            var bodyController = actor.AddComponent<PoseModuleController>();
            bodyController.Configure("Body", ModuleKind.Body, body); controllers[0] = bodyController;
            for (int i = 0; i < 4; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f, z = i < 2 ? .7f : -.7f;
                var upper = Bone(body, "Upper" + i, actor.transform.position + new Vector3(side * .62f, .83f, z));
                var lower = Bone(upper, "Lower" + i, actor.transform.position + new Vector3(side * 1.07f, .45f, z + .05f));
                var foot = Bone(lower, "Foot" + i, actor.transform.position + new Vector3(side * 1.05f, .06f, z + .18f));
                limbs[i] = new RigBinding.ModuleBinding { ModuleId = "Leg" + i, Upper = upper, Lower = lower, Foot = foot };
                var ik = actor.AddComponent<LimbIKController>(); ik.Configure(rig, "Leg" + i); controllers[i + 1] = ik;
            }
            rig.Configure(host.ActorId, body, limbs);
            Assert.IsTrue(host.Configure(TestMorphology.Quadruped, planner, root, rig, controllers, 0), host.LastError);
            now = 0d;
        }

        [TearDown] public void DestroyIndividual() { UnityEngine.Object.DestroyImmediate(actor); }

        [Test]
        public void DirectPathMovesRealRigWithoutProviderAndCopiesCallerPoints()
        {
            Vector3 start = host.MotionAnchor.position;
            Vector3 container = host.transform.position;
            var points = new[] { start, start + Vector3.forward * 2f, start + Vector3.forward * 8f };
            Assert.IsTrue(host.SetPath(points));
            points[1] = new Vector3(float.NaN, 0f, 0f);
            Advance(180);
            Assert.Greater(host.MotionAnchor.position.z, start.z + .5f);
            Assert.AreEqual(container, host.transform.position);
            Assert.IsTrue(PathValidation.IsFinite(host.LastRoute.Waypoints[1].PositionWS));
            host.Stop(); Advance(90);
            Assert.Less(host.GetComponent<ProceduralAnimationManager>().PlannedVelocityWS.magnitude, .001f);
            foreach (var trajectory in host.LastPlan.Trajectories)
                if (trajectory.Module.Kind == ModuleKind.Limb) Assert.AreEqual(ContactPhase.Stance, trajectory.Knots[0].Phase);
        }

        [Test]
        public void ChangingProvidersWithRepeatedVersionsAndShorterPathsRestartsProgress()
        {
            Vector3 start = host.MotionAnchor.position;
            var first = new FixedProvider(Route(start, start + Vector3.forward * .4f, start + Vector3.forward * 10f));
            host.UseRouteProvider(first); Advance(120);
            var second = new FixedProvider(Route(host.MotionAnchor.position, host.MotionAnchor.position + Vector3.right * 8f));
            host.UseRouteProvider(second); Advance(180);
            Assert.Greater(host.MotionAnchor.position.x, start.x + .5f);
            Assert.Greater(first.Cancellations, 0);
            Assert.IsTrue(host.SetPath(new[] { host.MotionAnchor.position + Vector3.forward * 5f }));
            Advance(180);
            Assert.AreEqual(2, host.LastRoute.Waypoints.Length);
            Assert.Greater(second.Cancellations, 0);
        }

        [Test]
        public void SnapshotMutationDoesNotChangeAcceptedInputAndWrongIdentityIsRejected()
        {
            Vector3 start = host.MotionAnchor.position;
            var supplied = Route(start, start + Vector3.forward * 6f);
            Assert.IsTrue(host.SubmitRoute(supplied));
            supplied.Status = RouteStatus.Invalid;
            supplied.Waypoints[1] = new RouteWaypoint(new Vector3(float.NaN, 0f, 0f), 1f);
            Advance(90);
            Assert.AreEqual(RouteStatus.Complete, host.LastRoute.Status);
            Assert.IsTrue(PathValidation.IsFinite(host.LastRoute.Waypoints[1].PositionWS));
            var otherActor = Route(start, start + Vector3.forward); otherActor.ActorId++;
            Assert.IsFalse(host.SubmitRoute(otherActor)); Advance(90, "host.routeIdentity");
            Assert.Less(host.GetComponent<ProceduralAnimationManager>().PlannedVelocityWS.magnitude, .001f);
        }

        [Test]
        public void InvalidAndExpiredPathsKeepIndependentAnimationRunning()
        {
            Assert.IsFalse(host.SetPath(new[] { new Vector3(float.NaN, 0f, 0f) }));
            Advance(10, "route.waypoints[1]");
            int successful = host.SuccessfulFrames;
            var expired = Route(host.MotionAnchor.position, host.MotionAnchor.position + Vector3.forward * 3f);
            expired.ValidUntil = 0d;
            Assert.IsTrue(host.SubmitRoute(expired)); Advance(30, "host.routeExpired");
            Assert.Greater(host.SuccessfulFrames, successful + 20);
            var unsupported = Route(host.MotionAnchor.position, host.MotionAnchor.position + Vector3.forward);
            unsupported.Waypoints[1].SegmentKind = SegmentKind.OffMesh;
            Assert.IsFalse(host.SubmitRoute(unsupported)); Advance(30, "route.segment.unsupported");
            Assert.IsFalse(host.SetDestination(Vector3.forward)); Advance(10, "host.noRouteProvider");
        }

        private void Advance(int frames, string expectedError = null)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                now += 1d / 60d; host.Tick(1f / 60f, now);
                Assert.AreEqual(0, host.FailedFrames, host.LastError);
                Assert.AreEqual(expectedError, host.LastError);
                for (int limb = 0; limb < rig.Modules.Length; limb++)
                {
                    var sample = host.LastPlan.Trajectories[limb + 1].Knots[0];
                    if (sample.Phase == ContactPhase.Stance)
                        Assert.Less(Vector3.Distance(rig.Modules[limb].Foot.position, sample.PoseWS.position), .003f);
                }
            }
        }

        private RouteSnapshot Route(params Vector3[] points)
        {
            var waypoints = new RouteWaypoint[points.Length]; float distance = 0f;
            for (int i = 0; i < points.Length; i++)
            { if (i > 0) distance += Vector3.Distance(points[i - 1], points[i]); waypoints[i] = new RouteWaypoint(points[i], distance); }
            return new RouteSnapshot(waypoints) { ActorId = host.ActorId, Epoch = host.Epoch, RouteVersion = 1, Status = RouteStatus.Complete };
        }

        private static Transform Bone(Transform parent, string name, Vector3 point)
        { var bone = new GameObject(name).transform; bone.SetParent(parent); bone.position = point; return bone; }

        private sealed class FixedProvider : IRouteProvider
        {
            private readonly RouteSnapshot route;
            public int Cancellations;
            public FixedProvider(RouteSnapshot route) { this.route = route; }
            public void Submit(in RouteRequest request) { }
            public void Cancel(int actorId, uint epoch) { Cancellations++; }
            public bool TryGetLatest(int actorId, uint epoch, out RouteSnapshot result) { result = route; return true; }
        }
    }
}
