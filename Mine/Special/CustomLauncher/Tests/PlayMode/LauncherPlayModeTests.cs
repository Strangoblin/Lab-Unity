using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CelToon.CustomLauncher.Contracts;
using CelToon.CustomLauncher.Projectiles;
using CelToon.CustomLauncher.Runtime;
using CelToon.CustomLauncher.Samples;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace CelToon.CustomLauncher.Tests.PlayMode
{
    public sealed class LauncherPlayModeTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private static readonly Vector3 TestOrigin = new Vector3(100f, 100f, 100f);
        private ProjectilePool Pool(int capacity = 16)
        {
            var go = new GameObject("TestPool"); objects.Add(go);
            var pool = go.AddComponent<ProjectilePool>(); pool.ConfigureCapacity(capacity); return pool;
        }
        private RangeTarget Target(Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); objects.Add(go);
            go.transform.position = TestOrigin + position; go.transform.localScale = new Vector3(1f, 1f, .1f);
            return go.AddComponent<RangeTarget>();
        }
        private static ProjectileSpec Spec(float damage = 10f, float speed = 100f, float lifetime = 1f)
            => new ProjectileSpec(damage, speed, .05f, lifetime, Color.cyan);
        private static ProjectileCommand Command(ProjectileSpec spec, Vector3 origin = default, int owner = 0)
            => new ProjectileCommand(TestOrigin + origin, Vector3.forward, spec, owner, 1);
        [UnityTearDown] public IEnumerator TearDown()
        { foreach (var go in objects) if (go != null) UnityEngine.Object.Destroy(go); objects.Clear(); yield return null; }

        [UnityTest] public IEnumerator SweptCollisionHitsThinTargetAtHighSpeedExactlyOnce()
        {
            var pool = Pool(); var target = Target(new Vector3(0f, 0f, 5f)); yield return null;
            Physics.SyncTransforms(); Assert.That(pool.TryEmit(new[] { Command(Spec()) }), Is.True);
            pool.Tick(.1f); Assert.That(target.Hits, Is.EqualTo(1)); Assert.That(target.TotalDamage, Is.EqualTo(10f));
            Assert.That(pool.ActiveCount, Is.Zero); pool.Tick(.1f); Assert.That(target.Hits, Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator NearestColliderReceivesDamage()
        {
            var pool = Pool(); var near = Target(new Vector3(0, 0, 2)); var far = Target(new Vector3(0, 0, 4)); yield return null;
            Physics.SyncTransforms(); pool.TryEmit(new[] { Command(Spec()) }); pool.Tick(.1f);
            Assert.That(near.Hits, Is.EqualTo(1)); Assert.That(far.Hits, Is.Zero);
        }
        [UnityTest] public IEnumerator InitialOverlapIsHandled()
        {
            var pool = Pool(); var target = Target(Vector3.zero); yield return null;
            Physics.SyncTransforms(); pool.TryEmit(new[] { Command(Spec()) }); pool.Tick(.01f);
            Assert.That(target.Hits, Is.EqualTo(1)); Assert.That(pool.ActiveCount, Is.Zero);
        }
        [UnityTest] public IEnumerator OwnerColliderDoesNotBlockTargetsBehindIt()
        {
            var pool = Pool(); var owner = Target(new Vector3(0, 0, 1)); var target = Target(new Vector3(0, 0, 4)); yield return null;
            Physics.SyncTransforms(); pool.TryEmit(new[] { Command(Spec(), default, owner.gameObject.GetInstanceID()) }); pool.Tick(.1f);
            Assert.That(owner.Hits, Is.Zero); Assert.That(target.Hits, Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator LifetimeLimitsTravelAndRecycles()
        {
            var pool = Pool(); var target = Target(new Vector3(0, 0, 5)); yield return null;
            Physics.SyncTransforms(); pool.TryEmit(new[] { Command(Spec(10f, 100f, .02f)) }); pool.Tick(.1f);
            Assert.That(target.Hits, Is.Zero); Assert.That(pool.ActiveCount, Is.Zero);
        }
        [UnityTest] public IEnumerator ReusedProjectileClearsOldDamageAndLifetime()
        {
            var pool = Pool(); var target = Target(new Vector3(0, 0, 5)); yield return null;
            Physics.SyncTransforms(); pool.TryEmit(new[] { Command(Spec(3f, 100f, .02f)) }); pool.Tick(.1f);
            pool.TryEmit(new[] { Command(Spec(27f, 100f, 1f)) }); pool.Tick(.1f);
            Assert.That(pool.CreatedCount, Is.EqualTo(1)); Assert.That(target.Hits, Is.EqualTo(1)); Assert.That(target.TotalDamage, Is.EqualTo(27f));
        }
        [UnityTest] public IEnumerator FullPoolRejectsWholeShotWithoutStateChanges()
        {
            var pool = Pool(2); yield return null;
            pool.TryEmit(new[] { Command(Spec()) });
            var plan = new CompiledLauncherPlan("frame", "round", Spec(), DistributionMode.Fan, 2, 10f, .25f, 5f);
            var state = new LauncherState(); state.Equip(plan);
            var result = LauncherFiring.TryFire(state, pool, new FireAim(Vector3.zero, Vector3.forward, Vector3.up), 0f);
            Assert.That(result, Is.EqualTo(FireResult.CapacityExceeded)); Assert.That(pool.ActiveCount, Is.EqualTo(1));
            Assert.That(state.Energy, Is.EqualTo(100f)); Assert.That(state.ShotCount, Is.Zero);
        }
        [UnityTest] public IEnumerator InvalidCommandRejectsEntireBatch()
        {
            var pool = Pool(); yield return null;
            var invalid = new ProjectileCommand(Vector3.zero, Vector3.zero, Spec(), 0, 0);
            Assert.That(pool.TryEmit(new[] { Command(Spec()), invalid }), Is.False);
            Assert.That(pool.ActiveCount, Is.Zero); Assert.That(pool.CreatedCount, Is.Zero);
        }
        [UnityTest] public IEnumerator DisableClearsProjectilesForNextActivation()
        {
            var pool = Pool(); yield return null;
            pool.TryEmit(new[] { Command(Spec()) }); pool.gameObject.SetActive(false);
            Assert.That(pool.ActiveCount, Is.Zero); pool.gameObject.SetActive(true);
            pool.TryEmit(new[] { Command(Spec()) }); Assert.That(pool.CreatedCount, Is.EqualTo(1));
        }
        private static ProjectileCommand WithEffects(params EffectStep[] steps)
            => new ProjectileCommand(TestOrigin, Vector3.forward, Spec(), 0, 1, new ProjectileProgram(steps, 256));
        [UnityTest] public IEnumerator DoubleSplitActuallyTriggersTwiceAndChildrenDoNotRecurse()
        {
            var pool = Pool(32); yield return null;
            pool.TryEmit(new[] { WithEffects(new EffectStep(EffectOperation.TimedSplit, 20, .8f, 3, .2f),
                new EffectStep(EffectOperation.TimedSplit, 40, .6f, 3, .4f)) });
            pool.Tick(.45f); Assert.That(pool.TriggerCount, Is.EqualTo(2)); Assert.That(pool.GeneratedCount, Is.EqualTo(7));
            pool.Tick(1f); Assert.That(pool.TriggerCount, Is.EqualTo(2)); Assert.That(pool.ActiveCount, Is.Zero);
        }
        [UnityTest] public IEnumerator ExplosionDamagesNearbyTargetsAndDirectTarget()
        {
            var pool = Pool(); var center = Target(new Vector3(0, 0, 5)); var neighbor = Target(new Vector3(1.5f, 0, 5)); yield return null;
            Physics.SyncTransforms(); pool.TryEmit(new[] { WithEffects(new EffectStep(EffectOperation.Explosion, 3, 1)) }); pool.Tick(.1f);
            Assert.That(center.TotalDamage, Is.EqualTo(20)); Assert.That(neighbor.TotalDamage, Is.EqualTo(10));
        }
        [UnityTest] public IEnumerator PierceHitsTwoTargetsWithoutRepeatingFirst()
        {
            var pool = Pool(); var near = Target(new Vector3(0, 0, 2)); var far = Target(new Vector3(0, 0, 4)); yield return null;
            Physics.SyncTransforms(); pool.TryEmit(new[] { WithEffects(new EffectStep(EffectOperation.Pierce, 1, 1, 1)) }); pool.Tick(.1f);
            Assert.That(near.Hits, Is.EqualTo(1)); Assert.That(far.Hits, Is.EqualTo(1)); Assert.That(pool.ActiveCount, Is.Zero);
        }
        [UnityTest] public IEnumerator BounceReflectsVelocityAndKeepsProjectileAlive()
        {
            var pool = Pool(); var target = Target(new Vector3(0, 0, 5)); yield return null;
            Physics.SyncTransforms(); pool.TryEmit(new[] { WithEffects(new EffectStep(EffectOperation.Bounce, 1, 1, 2)) }); pool.Tick(.06f);
            Assert.That(target.Hits, Is.EqualTo(1)); Assert.That(pool.ActiveCount, Is.EqualTo(1));
            float before = pool.transform.GetChild(0).position.z; pool.Tick(.02f);
            Assert.That(pool.transform.GetChild(0).position.z, Is.LessThan(before));
        }
        [UnityTest] public IEnumerator PierceDoesNotSkipNearbyTargetsInSameSubstep()
        {
            var pool = Pool(); var near = Target(new Vector3(0, 0, 1)); var far = Target(new Vector3(0, 0, 1.3f)); yield return null;
            Physics.SyncTransforms(); pool.TryEmit(new[] { WithEffects(new EffectStep(EffectOperation.Pierce, 1, 1, 1)) }); pool.Tick(.02f);
            Assert.That(near.Hits, Is.EqualTo(1)); Assert.That(far.Hits, Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator ChainReachesAnotherTarget()
        {
            var pool = Pool(); var center = Target(new Vector3(0, 0, 5)); var neighbor = Target(new Vector3(2, 0, 5)); yield return null;
            Physics.SyncTransforms(); pool.TryEmit(new[] { WithEffects(new EffectStep(EffectOperation.Chain, 5, .5f, 2)) }); pool.Tick(.1f);
            Assert.That(center.TotalDamage, Is.EqualTo(10)); Assert.That(neighbor.TotalDamage, Is.EqualTo(5));
        }
        [UnityTest] public IEnumerator BurnAndPoisonBothApplyAndDamageOverTime()
        {
            var pool = Pool(); var target = Target(new Vector3(0, 0, 5)); yield return null;
            Physics.SyncTransforms(); pool.TryEmit(new[] { WithEffects(new EffectStep(EffectOperation.Burn, 6, 2), new EffectStep(EffectOperation.Poison, 4, 3)) }); pool.Tick(.1f);
            Assert.That(target.StatusCount, Is.EqualTo(2)); float direct = target.TotalDamage; target.TickStatuses(.5f);
            Assert.That(target.TotalDamage, Is.EqualTo(direct + 5f).Within(.01f));
        }
        [UnityTest] public IEnumerator GravityAndHomingChangeActualPosition()
        {
            var pool = Pool(); yield return null;
            pool.TryEmit(new[] { WithEffects(new EffectStep(EffectOperation.Gravity, 10)) }); pool.Tick(.1f);
            Assert.That(pool.transform.GetChild(0).position.y, Is.LessThan(TestOrigin.y)); pool.Clear();
            var target = Target(new Vector3(3, 0, 8)); Physics.SyncTransforms();
            pool.TryEmit(new[] { WithEffects(new EffectStep(EffectOperation.Homing, 5, 20)) }); pool.Tick(.04f);
            Assert.That(pool.transform.GetChild(0).position.x, Is.GreaterThan(TestOrigin.x));
        }
        [UnityTest] public IEnumerator DelayedBurstDoesNotMoveBeforeItsDelay()
        {
            var pool = Pool(); yield return null;
            var command = new ProjectileCommand(TestOrigin, Vector3.forward, Spec(), 0, 1, ProjectileProgram.Empty, .2f);
            pool.TryEmit(new[] { command }); pool.Tick(.1f); Assert.That(pool.transform.GetChild(0).gameObject.activeSelf, Is.False);
            pool.Tick(.15f); Assert.That(pool.transform.GetChild(0).gameObject.activeSelf, Is.True);
            Assert.That(pool.transform.GetChild(0).position.z, Is.GreaterThan(TestOrigin.z));
        }
        [UnityTest] public IEnumerator SplitCapacityRejectsWholeChildGroupAndReportsIt()
        {
            var pool = Pool(2); yield return null;
            pool.TryEmit(new[] { WithEffects(new EffectStep(EffectOperation.TimedSplit, 20, .8f, 3, .1f)) }); pool.Tick(.15f);
            Assert.That(pool.TriggerCount, Is.Zero); Assert.That(pool.GeneratedCount, Is.EqualTo(1)); Assert.That(pool.RejectedTriggers, Is.EqualTo(1));
        }
    }
}
