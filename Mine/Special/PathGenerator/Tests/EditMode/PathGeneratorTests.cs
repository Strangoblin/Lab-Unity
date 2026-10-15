using NUnit.Framework;
using UnityEngine;
using CelToon.PathGenerator.Contracts;
using CelToon.PathGenerator.Runtime;

namespace CelToon.PathGenerator.Tests
{
    public sealed class PathGeneratorTests
    {
        [Test]
        public void SharedProviderKeepsActorAndEpochRoutesIndependent()
        {
            var go = new GameObject("SharedPathProvider");
            try
            {
                var provider = go.AddComponent<NavigationManager>(); provider.ConfigureWorld(new OpenWorld());
                provider.Submit(Request(11, 1, Vector3.forward));
                provider.Submit(Request(22, 1, Vector3.right));
                provider.Submit(Request(11, 2, Vector3.back));
                provider.Cancel(11, 1);
                Assert.IsTrue(provider.TryGetLatest(11, 1, out var old)); Assert.AreEqual(RouteStatus.Cancelled, old.Status);
                Assert.IsTrue(provider.TryGetLatest(22, 1, out var other)); Assert.AreEqual(RouteStatus.Complete, other.Status);
                Assert.AreEqual(Vector3.right, other.Waypoints[1].PositionWS);
                Assert.IsTrue(provider.TryGetLatest(11, 2, out var current)); Assert.AreEqual(RouteStatus.Complete, current.Status);
                Assert.AreEqual(Vector3.back, current.Waypoints[1].PositionWS);
                provider.Release(11, 1);
                Assert.IsFalse(provider.TryGetLatest(11, 1, out _));
                Assert.IsTrue(provider.TryGetLatest(11, 2, out _));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void InjectedWorldProducesDetourWithoutUnityPhysics()
        {
            var world = new BoxWorld();
            var generator = new GridPathGenerator(world);
            Assert.IsTrue(generator.TryGenerate(Vector3.back * 3f, Vector3.forward * 3f, out var points));
            Assert.Greater(points.Length, 2);
            float previous = 0f;
            foreach (var point in points)
            { Assert.IsFalse(world.IsCellBlocked(point.PositionWS, Vector3.zero)); Assert.GreaterOrEqual(point.CumulativeDistance, previous); previous = point.CumulativeDistance; }
        }

        [Test]
        public void BlockedAndNonFiniteInputsFailWithoutIncompleteWaypoints()
        {
            var generator = new GridPathGenerator(new ClosedWorld());
            Assert.IsFalse(generator.TryGenerate(Vector3.back * 3f, Vector3.forward * 3f, out var points));
            Assert.IsNull(points);
            Assert.IsFalse(generator.TryGenerate(new Vector3(float.NaN, 0f, 0f), Vector3.forward, out points));
            Assert.IsNull(points);
        }

        private static RouteRequest Request(int actor, uint epoch, Vector3 destination) =>
            new RouteRequest { ActorId = actor, Epoch = epoch, RequestId = 1, DestinationWS = destination };

        private sealed class OpenWorld : INavigationWorld
        {
            public bool IsSegmentBlocked(Vector3 start, Vector3 end) => false;
            public bool IsCellBlocked(Vector3 center, Vector3 halfExtents) => false;
        }
        private sealed class BoxWorld : INavigationWorld
        {
            public bool IsSegmentBlocked(Vector3 start, Vector3 end) => true;
            public bool IsCellBlocked(Vector3 center, Vector3 halfExtents) => Mathf.Abs(center.x) < .9f && Mathf.Abs(center.z) < .9f;
        }
        private sealed class ClosedWorld : INavigationWorld
        {
            public bool IsSegmentBlocked(Vector3 start, Vector3 end) => true;
            public bool IsCellBlocked(Vector3 center, Vector3 halfExtents) => true;
        }
    }
}
