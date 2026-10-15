using CelToon.PathGenerator.Runtime;
using CelToon.ProceduralController.Samples;
using CelToon.PathGenerator.Contracts;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
using CelToon.ProceduralController.Runtime;
using CelToon.ProceduralController.Contracts;

namespace CelToon.ProceduralController.Tests
{
    public sealed class LocomotionPipelinePlayTests
    {
        private float savedCapture;
        [SetUp] public void UseDeterministicFrameTime() { savedCapture = Time.captureDeltaTime; Time.captureDeltaTime = 1f / 60f; }
        [TearDown] public void RestoreFrameTime() { Time.captureDeltaTime = savedCapture; }
        [Test]
        [TestCase(30)]
        [TestCase(60)]
        [TestCase(120)]
        public void SpeedRampBrakesAtGoalAndCancelledRoute(int fps)
        {
            CheckSpeedRamp(fps, TestMorphology.Snake, 1f, 2f);
        }

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(120)]
        public void LinkedSpeedRampBrakesAtGoalAndCancelledRoute(int fps)
        {
            CheckSpeedRamp(fps, TestMorphology.Quadruped, 2f, 4f);
            CheckSpeedRamp(fps, TestMorphology.Hexapod, 2f, 4f);
            CheckSpeedRamp(fps, TestMorphology.Octopod, 2f, 3f);
            CheckSpeedRamp(fps, TestMorphology.Humanoid, 2f, 2.2f);
        }

        private static void CheckSpeedRamp(int fps, TestMorphology shape, float strideMultiplier, float expectedPeak)
        {
            var actor = new GameObject("SpeedRampTest");
            try
            {
                var planner = actor.AddComponent<ProceduralAnimationManager>();
                planner.Configure(shape, null);
                planner.StrideFrequency = strideMultiplier;
                planner.MoveSpeed = 2f; planner.Acceleration = 3f; planner.Deceleration = 5f;
                var route = new RouteSnapshot(new[] { new RouteWaypoint(Vector3.zero, 0f), new RouteWaypoint(Vector3.forward * 8f, 8f) })
                    { Status = RouteStatus.Complete, RouteVersion = 1 };
                var input = new PlannerInput { Tick = new TickContext { DeltaTime = 1f / fps }, Route = route,
                    Character = new CharacterState { RootPoseWS = new Pose(Vector3.zero, Quaternion.identity) } };
                float previousSpeed = 0f, peakSpeed = 0f;
                bool slowed = false;
                for (int frame = 0; frame < fps * 12; frame++)
                {
                    input.Tick.Now = frame / (double)fps;
                    var command = planner.PlanRoot(input);
                    float speed = command.DeltaPositionWS.magnitude * fps;
                    Assert.LessOrEqual(speed - previousSpeed, planner.Acceleration / fps + .0001f);
                    Assert.LessOrEqual(previousSpeed - speed, planner.Deceleration / fps + .0001f,
                        "Arrival cut actual command speed instead of braking");
                    if (previousSpeed > speed + .001f) slowed = true;
                    input.Character.RootPoseWS.position += command.DeltaPositionWS;
                    Assert.LessOrEqual(input.Character.RootPoseWS.position.z, 8f - planner.StoppingDistance + .0001f);
                    peakSpeed = Mathf.Max(peakSpeed, speed); previousSpeed = speed;
                }
                Assert.AreEqual(expectedPeak, peakSpeed, .001f, shape.ToString());
                Assert.IsTrue(slowed); Assert.Less(previousSpeed, .001f);
                Assert.AreEqual(8f - planner.StoppingDistance, input.Character.RootPoseWS.position.z, .003f);
                planner.Reset(new PlannerResetContext());
                input.Character.RootPoseWS.position = Vector3.zero;
                for (int frame = 0; frame < fps * 2; frame++) planner.PlanRoot(input);
                input.Route = null;
                previousSpeed = planner.PlannedVelocityWS.magnitude;
                float coastDistance = 0f;
                for (int frame = 0; frame < fps; frame++)
                {
                    var command = planner.PlanRoot(input);
                    float speed = command.DeltaPositionWS.magnitude * fps;
                    Assert.LessOrEqual(speed, previousSpeed + .0001f);
                    Assert.LessOrEqual(previousSpeed - speed, planner.Deceleration / fps + .0001f);
                    coastDistance += command.DeltaPositionWS.magnitude; previousSpeed = speed;
                }
                Assert.Greater(coastDistance, .2f, "Cancel snapped motion to zero");
                Assert.Less(previousSpeed, .001f);
            }
            finally { Object.DestroyImmediate(actor); }
        }

        [UnityTest]
        public IEnumerator LinkedCadenceRaisesActualTravelSpeedAndStopsWithoutFootSlip()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * 10f;
            int[] actors = { 0, 1, 2, 4 };
            float maxSlip = 0f, maxError = 0f, maxTwist = 0f;
            int stanceSamples = 0;
            try
            {
                foreach (int fps in new[] { 30, 60, 120 })
                {
                    Time.captureDeltaTime = 1f / fps;
                    var distances = new float[4, actors.Length];
                    var frequencies = new float[4, actors.Length];
                    for (int trial = 0; trial < 4; trial++)
                    {
                        var stage = new GameObject("LinkedCadenceRegression");
                        try
                        {
                            var demo = stage.AddComponent<DemoSceneBootstrap>();
                            for (int frame = 0; frame < fps / 2; frame++) yield return null;
                            var starts = new Vector3[actors.Length];
                            var containers = new Vector3[actors.Length];
                            var previousFeet = new Vector3[actors.Length, 8];
                            var previousRotations = new Quaternion[actors.Length, 8];
                            var wasStance = new bool[actors.Length, 8];
                            var previousPlannedSpeed = new float[actors.Length];
                            for (int a = 0; a < actors.Length; a++)
                            {
                                var host = demo.Actors[actors[a]];
                                var planner = host.GetComponent<ProceduralAnimationManager>();
                                // Compare frequency-only, linked multiplier, and automatic
                                // cadence when increasing the base speed instead.
                                planner.CoupleSpeedToStride = trial != 0;
                                planner.StrideFrequency = trial == 3 ? 3f : trial == 2 ? 1f : 2f;
                                planner.MoveSpeed = trial == 3 ? 5f : trial == 2 ? 4f : 2f;
                                starts[a] = host.MotionAnchor.position;
                                containers[a] = host.transform.position;
                                host.SetDestination(starts[a] + Vector3.forward * 30f);
                            }
                            for (int frame = 0; frame < fps * 6; frame++)
                            {
                                if (trial == 3 && (frame == fps * 2 || frame == fps * 3))
                                {
                                    foreach (int actor in actors)
                                    {
                                        var host = demo.Actors[actor];
                                        var planner = host.GetComponent<ProceduralAnimationManager>();
                                        if (frame == fps * 2)
                                        {
                                            Assert.AreEqual(4f, planner.EffectiveStrideFrequency, .02f, "Cadence cap was not exercised");
                                            planner.CoupleSpeedToStride = false;
                                            host.SetDestination(host.MotionAnchor.position + Vector3.right * 30f);
                                        }
                                        else
                                        {
                                            planner.CoupleSpeedToStride = true;
                                            planner.MoveSpeed = .2f; planner.StrideFrequency = 1f;
                                        }
                                    }
                                }
                                if (frame == fps * 4)
                                {
                                    for (int a = 0; a < actors.Length; a++)
                                    {
                                        var host = demo.Actors[actors[a]];
                                        distances[trial, a] = host.MotionAnchor.position.z - starts[a].z;
                                        frequencies[trial, a] = host.GetComponent<ProceduralAnimationManager>().EffectiveStrideFrequency;
                                        host.Stop();
                                    }
                                }
                                yield return null;
                                for (int a = 0; a < actors.Length; a++)
                                {
                                    var host = demo.Actors[actors[a]];
                                    var planner = host.GetComponent<ProceduralAnimationManager>();
                                    float speed = planner.PlannedVelocityWS.magnitude;
                                    float rate = speed < previousPlannedSpeed[a] ? planner.Deceleration : planner.Acceleration;
                                    Assert.LessOrEqual(Mathf.Abs(speed - previousPlannedSpeed[a]), rate / fps + .001f);
                                    // Lowering a live speed limit brakes instead of snapping.
                                    Assert.LessOrEqual(speed, Mathf.Max(previousPlannedSpeed[a], planner.GaitSpeedLimit) + .001f);
                                    Assert.LessOrEqual(planner.EffectiveStrideFrequency, 4.001f);
                                    previousPlannedSpeed[a] = speed;
                                    Assert.AreEqual(containers[a], host.transform.position);
                                    var rig = host.GetComponent<CelToon.ProceduralController.Execution.RigBinding>();
                                    for (int limb = 0; limb < rig.Modules.Length; limb++)
                                    {
                                        var knot = host.LastPlan.Trajectories[limb + 1].Knots[0];
                                        bool stance = knot.Phase == ContactPhase.Stance;
                                        var foot = rig.Modules[limb].Foot;
                                        if (stance)
                                        {
                                            float error = Vector3.Distance(foot.position, knot.PoseWS.position);
                                            maxError = Mathf.Max(maxError, error);
                                            Assert.Less(error, .003f, host.name + " stance target error; FPS=" + fps +
                                                "; trial=" + trial + "; frame=" + frame + "; limb=" + limb);
                                            if (wasStance[a, limb])
                                            {
                                                float slip = Vector3.Distance(previousFeet[a, limb], foot.position);
                                                float twist = Quaternion.Angle(previousRotations[a, limb], foot.rotation);
                                                maxSlip = Mathf.Max(maxSlip, slip); maxTwist = Mathf.Max(maxTwist, twist);
                                                Assert.Less(slip, .003f, host.name + " stance slip");
                                                Assert.Less(twist, .05f, host.name + " stance twist");
                                                stanceSamples++;
                                            }
                                        }
                                        previousFeet[a, limb] = foot.position;
                                        previousRotations[a, limb] = foot.rotation;
                                        wasStance[a, limb] = stance;
                                    }
                                    Assert.AreEqual(0, host.FailedFrames, host.LastError);
                                }
                            }
                            foreach (int actor in actors)
                            {
                                var host = demo.Actors[actor];
                                Assert.Less(host.GetComponent<ProceduralAnimationManager>().PlannedVelocityWS.magnitude, .001f);
                                Assert.Less(new Vector2(host.LastMotionResult.ActualVelocityWS.x, host.LastMotionResult.ActualVelocityWS.z).magnitude, .001f);
                                for (int limb = 1; limb < host.LastPlan.Trajectories.Length; limb++)
                                    Assert.AreEqual(ContactPhase.Stance, host.LastPlan.Trajectories[limb].Knots[0].Phase);
                            }
                        }
                        finally { Object.DestroyImmediate(stage); }
                        if (trial == 3) TestContext.WriteLine("Linked cadence cap, live toggle, turn and slowdown passed at FPS=" + fps);
                        yield return null;
                    }
                    for (int a = 0; a < actors.Length; a++)
                    {
                        TestContext.WriteLine("Linked travel FPS=" + fps + "; shape=" + actors[a] +
                            "; distance in 4s(m), frequency-only/linked/auto=" + distances[0, a] + "/" + distances[1, a] + "/" + distances[2, a] +
                            "; cadence(Hz)=" + frequencies[0, a] + "/" + frequencies[1, a] + "/" + frequencies[2, a]);
                        Assert.Greater(distances[0, a], .5f, "Comparison actor did not move");
                        Assert.Greater(distances[1, a], distances[0, a] * 1.1f, "Linked multiplier did not improve actual travel: " + actors[a]);
                        Assert.Greater(distances[2, a], distances[0, a] * 1.1f, "Higher base speed did not improve actual travel: " + actors[a]);
                        float[] expectedFrequencies = { 3.4f, 2.7f, 2.3f, 1.8f };
                        Assert.AreEqual(expectedFrequencies[a], frequencies[2, a], .02f,
                            "Higher base speed did not raise cadence: " + actors[a]);
                    }
                }
                TestContext.WriteLine("Linked stance samples=" + stanceSamples + "; max slip(m)=" + maxSlip +
                    "; max target error(m)=" + maxError + "; max twist(deg)=" + maxTwist);
            }
            finally { Object.DestroyImmediate(ground); }
        }

        [Test]
        public void NavigationProducesDetourPointsAroundObstacle()
        {
            var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstacle.layer = 10;
            obstacle.transform.position = new Vector3(0f, 0.5f, 0f);
            obstacle.transform.localScale = new Vector3(1.5f, 1f, 1.5f);
            Physics.SyncTransforms();
            var actor = new GameObject("NavigationTest");
            var navigation = actor.AddComponent<NavigationManager>();
            navigation.Submit(new RouteRequest
            {
                ActorId = 42, Epoch = 1, RequestId = 1,
                StartWS = new Vector3(0f, 0f, -3f), DestinationWS = new Vector3(0f, 0f, 3f),
                RequestedAt = Time.timeAsDouble
            });
            Assert.IsTrue(navigation.TryGetLatest(42, 1, out var route));
            Assert.AreEqual(RouteStatus.Complete, route.Status, route.FailureCode);
            Assert.Greater(route.Waypoints.Length, 2);
            Assert.Greater(route.ValidUntil, Time.timeAsDouble + 3600d);
            Object.DestroyImmediate(actor);
            Object.DestroyImmediate(obstacle);
        }

        [UnityTest]
        public IEnumerator SevenMorphologiesGenerateAndExecutePlans()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * 5f;
            var stage = new GameObject("ValidationStage");
            try
            {
            var demo = stage.AddComponent<DemoSceneBootstrap>();
            yield return null;
            Assert.IsNull(demo.SceneError);
            Assert.AreEqual(7, demo.Actors.Length);
            int[] expectedModules = { 5, 7, 9, 1, 3, 9, 9 };
            for (int i = 0; i < demo.Actors.Length; i++)
            {
                Assert.IsTrue(demo.Actors[i].IsInitialized, demo.Actors[i].LastError);
                Assert.AreEqual(expectedModules[i], demo.Actors[i].LastPlan.ModuleIds.Length, demo.Actors[i].name);
                Assert.Greater(demo.Actors[i].SuccessfulFrames, 0, demo.Actors[i].name);
                Assert.IsNull(demo.Actors[i].LastError, demo.Actors[i].name);
            }
            var before = new Vector3[demo.Actors.Length];
            for (int i = 0; i < demo.Actors.Length; i++)
            {
                before[i] = demo.Actors[i].MotionAnchor.position;
                demo.Actors[i].SetDestination(before[i] + Vector3.forward * 3f);
            }
            Vector3 previousStance = Vector3.zero;
            Quaternion previousStanceRotation = Quaternion.identity;
            bool hadStance = false, checkedStance = false;
            float minimumBody = float.PositiveInfinity, maximumBody = float.NegativeInfinity;
            for (int frame = 0; frame < 50; frame++)
            {
                yield return null;
                var leg = demo.Actors[0].LastPlan.Trajectories[1].Knots[0];
                if (leg.Phase == ContactPhase.Stance)
                {
                    if (hadStance)
                    {
                        Assert.Less(Vector3.Distance(previousStance, leg.PoseWS.position), 0.001f, "stance foot slipped");
                        Assert.Less(Quaternion.Angle(previousStanceRotation, leg.PoseWS.rotation), 0.01f, "stance foot twisted");
                        checkedStance = true;
                    }
                    previousStance = leg.PoseWS.position;
                    previousStanceRotation = leg.PoseWS.rotation;
                    hadStance = true;
                }
                else hadStance = false;
                float bodyY = demo.Actors[4].LastPlan.Trajectories[0].Knots[0].PoseWS.position.y;
                minimumBody = Mathf.Min(minimumBody, bodyY);
                maximumBody = Mathf.Max(maximumBody, bodyY);
            }
            Assert.IsTrue(checkedStance, "No stance interval was sampled");
            Assert.Greater(maximumBody - minimumBody, 0.005f, "Fitted human body motion was not applied");
            for (int i = 0; i < demo.Actors.Length; i++)
            {
                Assert.Greater(demo.Actors[i].MotionAnchor.position.z, before[i].z + 0.05f, demo.Actors[i].name);
                Assert.AreEqual(0, demo.Actors[i].FailedFrames, demo.Actors[i].LastError);
                Assert.AreEqual(expectedModules[i], demo.Actors[i].LastFeedback.Length, demo.Actors[i].name);
                Assert.Less(demo.Actors[i].LastMaxReachError, 0.35f, demo.Actors[i].name);
            }
            var snake = demo.Actors[3].LastPlan;
            var snakePoints = snake.Trajectories[0].Knots[0].ChainPointsWS;
            for (int i = 1; i < snakePoints.Length; i++)
            {
                Assert.AreEqual(0.43f, Vector3.Distance(snakePoints[i - 1], snakePoints[i]), 0.001f, "snake segment " + i);
            }
            Quaternion beforeTurn = demo.Actors[0].MotionAnchor.rotation;
            demo.Actors[0].SetDestination(demo.Actors[0].MotionAnchor.position + Vector3.right * 3f);
            for (int frame = 0; frame < 50; frame++) yield return null;
            Assert.Greater(Quaternion.Angle(beforeTurn, demo.Actors[0].MotionAnchor.rotation), 5f);
            Assert.AreEqual(0, demo.Actors[0].FailedFrames, demo.Actors[0].LastError);
            }
            finally { Object.DestroyImmediate(stage); Object.DestroyImmediate(ground); }
        }

        [UnityTest]
        public IEnumerator TurnCadenceShortensNinetyDegreeTurnsWithoutStanceSlip()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * 5f;
            try
            {
                for (int actorIndex = 0; actorIndex < 3; actorIndex++)
                {
                    float[] times = new float[3];
                    for (int trial = 0; trial < 3; trial++)
                    {
                        var stage = new GameObject("TurnCadenceRegression");
                        try
                        {
                            var demo = stage.AddComponent<DemoSceneBootstrap>();
                            for (int frame = 0; frame < 30; frame++) yield return null;
                            var host = demo.Actors[actorIndex];
                            var planner = host.GetComponent<ProceduralAnimationManager>();
                            planner.CoupleSpeedToStride = trial == 2;
                            planner.TurnStrideMultiplier = trial == 0 ? 1f : 3f;
                            var rig = host.GetComponent<CelToon.ProceduralController.Execution.RigBinding>();
                            var previous = new Vector3[rig.Modules.Length];
                            var stance = new bool[rig.Modules.Length];
                            host.SetDestination(host.MotionAnchor.position + Vector3.right * 8f);
                            int elapsed = 0;
                            for (; elapsed < 600; elapsed++)
                            {
                                yield return null;
                                for (int limb = 0; limb < rig.Modules.Length; limb++)
                                {
                                    bool planted = host.LastPlan.Trajectories[limb + 1].Knots[0].Phase == ContactPhase.Stance;
                                    Vector3 actual = rig.Modules[limb].Foot.position;
                                    if (planted)
                                    {
                                        Assert.Less(Vector3.Distance(actual, host.LastPlan.Trajectories[limb + 1].Knots[0].PoseWS.position), .003f);
                                        if (stance[limb]) Assert.Less(Vector3.Distance(previous[limb], actual), .003f);
                                    }
                                    previous[limb] = actual; stance[limb] = planted;
                                }
                                if (Quaternion.Angle(host.MotionAnchor.rotation, Quaternion.LookRotation(Vector3.right)) < 10f) break;
                            }
                            Assert.Less(elapsed, 600, "Turn never completed: " + host.name);
                            Assert.AreEqual(0, host.FailedFrames, host.LastError);
                            times[trial] = (elapsed + 1) / 60f;
                        }
                        finally { Object.DestroyImmediate(stage); }
                        yield return null;
                    }
                    TestContext.WriteLine("Shape=" + actorIndex + "; base turn(s)=" + times[0] + "; boosted turn(s)=" + times[1] +
                        "; linked boosted turn(s)=" + times[2]);
                    Assert.Less(times[1], times[0] * .85f, "Turn cadence did not improve turn time by 15%: " + actorIndex);
                    Assert.LessOrEqual(times[2], times[1] * 1.1f, "Linked stride regressed boosted turning: " + actorIndex);
                }
            }
            finally { Object.DestroyImmediate(ground); }
        }

        [UnityTest]
        public IEnumerator PlantedActualFeetStayFixedDuringTravelTurnAndStop()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * 5f;
            var stage = new GameObject("FootContactRegression");
            try
            {
                var demo = stage.AddComponent<DemoSceneBootstrap>();
                for (int frame = 0; frame < 30; frame++) yield return null;
                int[] actors = { 0, 1, 2, 4 };
                var previous = new Vector3[7, 8];
                var stance = new bool[7, 8];
                var starts = new Vector3[7];
                foreach (int actor in actors) starts[actor] = demo.Actors[actor].MotionAnchor.position;
                float maxSlip = 0f, maxError = 0f;
                int checkedFrames = 0;
                for (int frame = 0; frame < 360; frame++)
                {
                    foreach (int actor in actors)
                    {
                        var host = demo.Actors[actor];
                        if (frame == 120) Assert.Greater(Vector3.Distance(starts[actor], host.MotionAnchor.position), .15f,
                            host.name + " contact lock prevented forward movement");
                        if (frame == 240) Assert.Greater(Quaternion.Angle(Quaternion.identity, host.MotionAnchor.rotation), 10f,
                            host.name + " contact lock prevented turning");
                        if (frame == 0) host.SetDestination(host.MotionAnchor.position + Vector3.forward * 6f);
                        if (frame == 120) host.SetDestination(host.MotionAnchor.position + Vector3.right * 6f);
                        if (frame == 240) host.Stop();
                    }
                    yield return null;
                    foreach (int actor in actors)
                    {
                        var host = demo.Actors[actor];
                        var rig = host.GetComponent<CelToon.ProceduralController.Execution.RigBinding>();
                        for (int limb = 0; limb < rig.Modules.Length; limb++)
                        {
                            var knot = host.LastPlan.Trajectories[limb + 1].Knots[0];
                            Vector3 actual = rig.Modules[limb].Foot.position;
                            bool planted = knot.Phase == ContactPhase.Stance;
                            if (planted)
                            {
                                maxError = Mathf.Max(maxError, Vector3.Distance(actual, knot.PoseWS.position));
                                Assert.Less(Vector3.Distance(actual, knot.PoseWS.position), .003f,
                                    host.name + " foot " + limb + " target error at " + frame);
                                if (stance[actor, limb])
                                {
                                    maxSlip = Mathf.Max(maxSlip, Vector3.Distance(actual, previous[actor, limb]));
                                    Assert.Less(Vector3.Distance(actual, previous[actor, limb]), .003f,
                                        host.name + " foot " + limb + " actual stance slip at " + frame);
                                    checkedFrames++;
                                }
                            }
                            previous[actor, limb] = actual; stance[actor, limb] = planted;
                        }
                    }
                }
                Assert.Greater(checkedFrames, 1000);
                TestContext.WriteLine("Actual stance samples=" + checkedFrames + "; max slip(m)=" + maxSlip + "; max target error(m)=" + maxError);
                foreach (int actor in actors) Assert.AreEqual(0, demo.Actors[actor].FailedFrames);
            }
            finally { Object.DestroyImmediate(stage); Object.DestroyImmediate(ground); }
        }

        [UnityTest]
        public IEnumerator SingleDriverLeavesContainerAndSnakeTailIndependent()
        {
            float previousCapture = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            var stage = new GameObject("IndependentMotionStage");
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * 5f;
            try
            {
                var demo = stage.AddComponent<DemoSceneBootstrap>();
                for (int i = 0; i < 30; i++) yield return null;
                var snake = demo.Actors[3];
                var chain = snake.GetComponent<ChainModuleController>();
                Vector3 container = snake.transform.position;
                Vector3 head = snake.MotionAnchor.position;
                Vector3 tail = chain.Bones[chain.Bones.Length - 1].position;
                snake.SetDestination(head + Vector3.right * 3f);
                // With the configurable 3 m/s2 acceleration, allow 0.3 s to travel 0.1 m.
                for (int i = 0; i < 18; i++) yield return null;
                Assert.AreEqual(container, snake.transform.position, "Whole actor container was moved");
                float headDistance = Vector3.Distance(head, snake.MotionAnchor.position);
                Assert.Greater(headDistance, 0.1f);
                Assert.Less(Vector3.Distance(tail, chain.Bones[chain.Bones.Length - 1].position), headDistance * 0.3f,
                    "Tail inherited the head movement immediately");
                Assert.Less(chain.MaximumLengthError, 0.001f);
                Assert.IsFalse(chain.Bones[1].IsChildOf(snake.MotionAnchor));
            }
            finally { Object.DestroyImmediate(stage); Object.DestroyImmediate(ground); Time.captureDeltaTime = previousCapture; }
        }

        [UnityTest]
        public IEnumerator JellyfishStrokePropelsOnlyBodyAndKeepsTentacleLengths()
        {
            float previousCapture = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            var stage = new GameObject("JellyfishMotionStage");
            try
            {
                var demo = stage.AddComponent<DemoSceneBootstrap>();
                yield return null;
                var jelly = demo.Actors[5];
                Vector3 container = jelly.transform.position;
                Vector3 start = jelly.MotionAnchor.position;
                jelly.SetDestination(start + Vector3.forward * 10f);
                float minimumSpeed = float.PositiveInfinity, maximumSpeed = 0f;
                Vector3 previous = start;
                for (int frame = 0; frame < 150; frame++)
                {
                    yield return null;
                    float speed = Vector3.Distance(previous, jelly.MotionAnchor.position) * 60f;
                    if (frame > 30) { minimumSpeed = Mathf.Min(minimumSpeed, speed); maximumSpeed = Mathf.Max(maximumSpeed, speed); }
                    previous = jelly.MotionAnchor.position;
                    foreach (var chain in jelly.GetComponents<ChainModuleController>())
                    {
                        Assert.Less(chain.MaximumLengthError, 0.012f);
                        Assert.IsFalse(chain.Bones[1].IsChildOf(jelly.MotionAnchor));
                    }
                }
                Assert.AreEqual(container, jelly.transform.position);
                Assert.AreEqual(start.y, jelly.MotionAnchor.position.y, 0.001f, "Swimming body fell under gravity");
                Assert.Greater(maximumSpeed - minimumSpeed, 0.8f, "Tentacle contraction did not affect propulsion");
                Assert.AreEqual(0, jelly.FailedFrames, jelly.LastError);
                int previousFrames = jelly.SuccessfulFrames;
                jelly.SetDestination(new Vector3(float.NaN, 0f, 0f));
                for (int i = 0; i < 30; i++) yield return null;
                Assert.AreEqual("route.invalidRequest", jelly.LastError);
                Assert.Greater(jelly.SuccessfulFrames, previousFrames + 20, "Invalid navigation froze independent animation");
            }
            finally { Object.DestroyImmediate(stage); Time.captureDeltaTime = previousCapture; }
        }

        [UnityTest]
        public IEnumerator OctopusAutomaticallyWrapsMovingObjectWithoutBodyInput()
        {
            float previousCapture = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * 5f;
            var stage = new GameObject("OctopusGraspStage");
            try
            {
                var demo = stage.AddComponent<DemoSceneBootstrap>();
                yield return null;
                var octopus = demo.Actors[6];
                var target = stage.GetComponentInChildren<GraspableObject>();
                Vector3 body = octopus.MotionAnchor.position;
                for (int i = 0; i < 150; i++) yield return null;
                Assert.GreaterOrEqual(octopus.GraspingTentacles, 2, "Automatic surface grasp did not settle");
                target.transform.position += Vector3.right * 0.2f;
                for (int i = 0; i < 150; i++) yield return null;
                Assert.Less(Vector3.Distance(body, octopus.MotionAnchor.position), 0.1f, "Grasping moved the body without input");
                Assert.GreaterOrEqual(octopus.GraspingTentacles, 2);
                foreach (var chain in octopus.GetComponents<ChainModuleController>())
                {
                    Assert.Less(chain.MaximumLengthError, 0.025f, "grasp length error " + chain.ModuleId);
                    float bend = 0f;
                    for (int i = 1; i < chain.Bones.Length; i++)
                    {
                        Assert.GreaterOrEqual(Vector3.Distance(chain.Bones[i].position, target.Center), target.Radius - 0.015f,
                            "Tentacle penetrated grasp object");
                        if (i > 1) bend += Vector3.Angle(chain.Bones[i].position - chain.Bones[i - 1].position,
                            chain.Bones[i - 1].position - chain.Bones[i - 2].position);
                    }
                    Assert.Greater(bend, 45f, "Tentacle remained straight");
                }
                Assert.AreEqual(0, octopus.FailedFrames, octopus.LastError);
                target.transform.position += Vector3.right * 10f;
                for (int i = 0; i < 90; i++) yield return null;
                Assert.AreEqual(0, octopus.GraspingTentacles, "Distant object did not release automatically");
            }
            finally { Object.DestroyImmediate(stage); Object.DestroyImmediate(ground); Time.captureDeltaTime = previousCapture; }
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator SavedSceneRunsSevenActorsAndExportsGraphicsPreviews()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                "Assets/Mine/Special/ProceduralController/Samples/LocomotionIOTest.unity", new LoadSceneParameters(LoadSceneMode.Additive));
            AsyncOperation unload = null;
            try
            {
                yield return null;
                DemoSceneBootstrap demo = null;
                Camera camera = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (!demo) demo = root.GetComponentInChildren<DemoSceneBootstrap>();
                    if (!camera) camera = root.GetComponentInChildren<Camera>();
                }
                Assert.IsNotNull(demo); Assert.IsNotNull(camera);
                Assert.AreEqual(7, demo.Actors.Length);
                demo.Actors[3].SetDestination(demo.Actors[3].MotionAnchor.position + Vector3.right * 2f);
                demo.Actors[5].SetDestination(demo.Actors[5].MotionAnchor.position + Vector3.forward * 2f);
                for (int i = 0; i < 180; i++) yield return null;
                foreach (var actor in demo.Actors)
                {
                    Assert.IsTrue(actor.IsInitialized, actor.LastError);
                    Assert.AreEqual(0, actor.FailedFrames, actor.LastError);
                }
                if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null &&
                    UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null)
                {
                    RenderPreview(camera, "octopus-grasp-preview.png", demo.Actors[6].Body.position + Vector3.forward * 0.5f, 2.8f);
                    RenderPreview(camera, "jellyfish-propulsion-preview.png", demo.Actors[5].Body.position - Vector3.up, 2.5f);
                    RenderPreview(camera, "snake-follow-preview.png", demo.Actors[3].Body.position - Vector3.right * 1.5f, 3.2f);
                }
            }
            finally { unload = SceneManager.UnloadSceneAsync(scene); }
            yield return unload;
        }

        private static void RenderPreview(Camera camera, string filename, Vector3 center, float size)
        {
            camera.transform.position = center + new Vector3(4f, 6f, -7f);
            camera.transform.LookAt(center); camera.orthographicSize = size;
            var target = new RenderTexture(1280, 720, 24);
            var previous = RenderTexture.active;
            var previousTarget = camera.targetTexture;
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
                string path = System.IO.Path.Combine(Application.dataPath, "..", filename);
                System.IO.File.WriteAllBytes(path, pixels.EncodeToPNG());
                Assert.IsTrue(System.IO.File.Exists(path));
            }
            finally
            {
                RenderTexture.active = previous; camera.targetTexture = previousTarget;
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
            }
        }
#endif
    }
}
