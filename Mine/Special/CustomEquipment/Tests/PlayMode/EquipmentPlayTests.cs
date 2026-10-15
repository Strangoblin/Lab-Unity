using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CelToon.CustomEquipment.Contracts;
using CelToon.CustomEquipment.Definitions;
using CelToon.CustomEquipment.Runtime;
using CelToon.CustomEquipment.Samples;
using CelToon.CustomLauncher.Contracts;
using CelToon.CustomLauncher.Projectiles;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace CelToon.CustomEquipment.Tests.PlayMode
{
    public sealed class EquipmentPlayTests
    {
        private readonly List<UnityEngine.Object> garbage = new List<UnityEngine.Object>();
        private static readonly Vector3 Origin = new Vector3(100, 100, 100);
        private EquipmentHost Player(EquipmentStats stats, bool childCollider = false)
        {
            var go = new GameObject("Player"); garbage.Add(go); go.transform.position = Origin + Vector3.forward * 4;
            var r = ScriptableObject.CreateInstance<EquipmentRecipeDefinition>(); garbage.Add(r); r.BaseStats = stats;
            var host = go.AddComponent<EquipmentHost>(); host.AutoTick = false; Assert.IsTrue(host.TryEquip(r.CreateLoadout()));
            if (childCollider)
            { var child = new GameObject("BodyCollider"); child.transform.SetParent(go.transform, false); child.AddComponent<BoxCollider>(); }
            else go.AddComponent<BoxCollider>(); return host;
        }
        private ProjectilePool Pool()
        { var go = new GameObject("Pool"); garbage.Add(go); return go.AddComponent<ProjectilePool>(); }
        private void Shoot(ProjectilePool pool, float damage, params EffectStep[] steps)
        {
            Physics.SyncTransforms();
            Assert.IsTrue(pool.TryEmit(new[] { new ProjectileCommand(Origin, Vector3.forward, new ProjectileSpec(damage, 50, .05f, 2, Color.yellow), 0, 2, new ProjectileProgram(steps, 128)) }));
            pool.Tick(.1f);
        }
        [UnityTearDown] public IEnumerator Cleanup()
        { foreach (var obj in garbage) if (obj != null) UnityEngine.Object.Destroy(obj); garbage.Clear(); yield return null; }
        [UnityTest] public IEnumerator RealProjectileResolvesArmorShieldAndHealth()
        {
            var stats = EquipmentStats.Standard; stats.Armor = 5; stats.MaxShield = 10; var player = Player(stats); var pool = Pool(); yield return null;
            Shoot(pool, 25); Assert.AreEqual(90, player.State.Health); Assert.AreEqual(0, player.State.Shield); Assert.AreEqual(1, player.State.Hits);
        }
        [UnityTest] public IEnumerator ChildColliderFindsPlayerHost()
        { var player = Player(EquipmentStats.Standard, true); var pool = Pool(); yield return null; Shoot(pool, 10); Assert.AreEqual(90, player.State.Health); }
        [UnityTest] public IEnumerator ProjectileFirePoisonUsesEquipmentResistance()
        {
            var stats = EquipmentStats.Standard; stats.BurnResistance = .5f; stats.PoisonResistance = .5f;
            var player = Player(stats); var pool = Pool(); yield return null;
            Shoot(pool, 10, new EffectStep(EffectOperation.Burn, 6, 4), new EffectStep(EffectOperation.Poison, 4, 6));
            Assert.AreEqual(2, player.State.StatusCount); player.Tick(1); Assert.AreEqual(85, player.State.Health, .01);
        }
        [UnityTest] public IEnumerator ProjectileSlowAndStunChangeMovement()
        {
            var stats = EquipmentStats.Standard; stats.StunResistance = .5f; var player = Player(stats); var pool = Pool(); yield return null;
            Shoot(pool, 1, new EffectStep(EffectOperation.Slow, .3f, 4), new EffectStep(EffectOperation.Stun, 1, 1));
            Assert.AreEqual(0, player.State.MoveSpeed); player.Tick(.6f); Assert.AreEqual(.9f, player.State.MoveSpeed, .001);
        }
        [UnityTest] public IEnumerator ProjectileImpulseIsReducedByAnchor()
        {
            var stats = EquipmentStats.Standard; stats.ImpulseResistance = .8f; var player = Player(stats); var pool = Pool(); yield return null;
            Shoot(pool, 1, new EffectStep(EffectOperation.Push, 10)); Assert.AreEqual(2, player.ImpulseVelocity.magnitude, .01);
        }
        [UnityTest] public IEnumerator ProjectileMarkAmplifiesNextHit()
        {
            var stats = EquipmentStats.Standard; stats.MarkResistance = .5f; var player = Player(stats); var pool = Pool(); yield return null;
            Shoot(pool, 10, new EffectStep(EffectOperation.Mark, 2, 5)); Shoot(pool, 10); Assert.AreEqual(75, player.State.Health, .001);
        }
        [UnityTest] public IEnumerator RapidProjectilesRespectProtection()
        {
            var stats = EquipmentStats.Standard; stats.HitInvulnerability = .3f; var player = Player(stats); var pool = Pool(); yield return null;
            Shoot(pool, 10); Shoot(pool, 10); Assert.AreEqual(90, player.State.Health); player.Tick(.4f); Shoot(pool, 10); Assert.AreEqual(80, player.State.Health);
        }
        [UnityTest] public IEnumerator ResetClearsDeathStatusImpulseAndStatistics()
        {
            var player = Player(EquipmentStats.Standard); yield return null;
            player.ReceiveStatus(new StatusInfo(StatusKind.Poison, 2, 3, 0, 2)); player.ReceiveImpulse(Vector3.right * 5); player.ReceiveDamage(new DamageInfo(1000, Origin, Vector3.forward, 0, 2));
            Assert.IsTrue(player.State.IsDead); player.ResetPlayer(); Assert.AreEqual(100, player.State.Health); Assert.AreEqual(0, player.State.StatusCount);
            Assert.AreEqual(Vector3.zero, player.ImpulseVelocity); Assert.AreEqual(0, player.State.TotalIncoming);
        }
    }
}
