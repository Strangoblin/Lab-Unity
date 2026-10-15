using System;
using System.Collections.Generic;
using CelToon.CustomLauncher.Contracts;
using CelToon.CustomLauncher.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CelToon.CustomLauncher.Tests.EditMode
{
    public sealed class LauncherEnergySourceTests
    {
        private static readonly FireAim Aim = new FireAim(Vector3.zero, Vector3.forward, Vector3.up);
        private static CompiledLauncherPlan Plan(float cost = 25f, string projectileId = "round")
            => new CompiledLauncherPlan("test", projectileId, new ProjectileSpec(10, 20, .1f, 3, Color.white),
                DistributionMode.Single, 1, 0, .5f, cost);
        private static LauncherState State(ILauncherEnergySource source)
        {
            var state = new LauncherState(); state.Equip(Plan()); state.BindEnergySource(source); return state;
        }

        private sealed class Emitter : IProjectileEmitter
        {
            public bool Accept = true;
            public bool Throw;
            public int Emitted;
            public bool TryEmit(IReadOnlyList<ProjectileCommand> commands)
            {
                if (Throw) throw new InvalidOperationException("Emitter failure");
                if (!Accept) return false;
                Emitted += commands.Count; return true;
            }
        }

        private sealed class EnergySource : ILauncherEnergySource
        {
            public float Current { get; private set; }
            public float Capacity { get; }
            public float CostScale = 1f;
            public int Attempts, Committed, RolledBack;
            public EnergySource(float capacity, float current) { Capacity = capacity; Current = current; }
            public bool TryReserve(float baseCost, out ILauncherEnergyReservation reservation)
            {
                Attempts++;
                reservation = null;
                float cost = baseCost * CostScale;
                if (Current < cost) return false;
                Current -= cost; reservation = new Reservation(this, cost); return true;
            }
            private sealed class Reservation : ILauncherEnergyReservation
            {
                private readonly EnergySource source;
                private readonly float cost;
                private bool finished;
                public Reservation(EnergySource source, float cost) { this.source = source; this.cost = cost; }
                public void Commit() { if (!finished) { source.Committed++; finished = true; } }
                public void Dispose()
                {
                    if (finished) return;
                    source.Current += cost; source.RolledBack++; finished = true;
                }
            }
        }

        [Test]
        public void SharedEnergyPaysScaledCostOnceEvenWhenBelowTheBaseCost()
        {
            var source = new EnergySource(130, 20) { CostScale = .65f };
            var state = State(source); var emitter = new Emitter();
            Assert.That(LauncherFiring.TryFire(state, emitter, Aim, 0), Is.EqualTo(FireResult.Fired));
            Assert.That(source.Current, Is.EqualTo(3.75f).Within(.0001f));
            Assert.That(state.Energy, Is.EqualTo(source.Current)); Assert.That(state.MaxEnergy, Is.EqualTo(130));
            Assert.That(source.Committed, Is.EqualTo(1)); Assert.That(source.RolledBack, Is.Zero);
            Assert.That(state.ShotCount, Is.EqualTo(1)); Assert.That(state.NextFireTime, Is.EqualTo(.5f));
            Assert.That(emitter.Emitted, Is.EqualTo(1));
        }

        [Test]
        public void InsufficientSharedEnergyDoesNotEmitSpendOrStartCooldown()
        {
            var source = new EnergySource(100, 10); var state = State(source); var emitter = new Emitter();
            Assert.That(LauncherFiring.TryFire(state, emitter, Aim, 0), Is.EqualTo(FireResult.InsufficientEnergy));
            Assert.That(source.Current, Is.EqualTo(10)); Assert.That(source.Committed, Is.Zero);
            Assert.That(source.RolledBack, Is.Zero); Assert.That(emitter.Emitted, Is.Zero);
            Assert.That(state.ShotCount, Is.Zero); Assert.That(state.NextFireTime, Is.EqualTo(float.NegativeInfinity));
        }

        [Test]
        public void CapacityFailureRollsBackSharedReservationAndCanRetryAtTheSameTime()
        {
            var source = new EnergySource(100, 100); var state = State(source); var emitter = new Emitter { Accept = false };
            Assert.That(LauncherFiring.TryFire(state, emitter, Aim, 0), Is.EqualTo(FireResult.CapacityExceeded));
            Assert.That(source.Current, Is.EqualTo(100)); Assert.That(source.Committed, Is.Zero);
            Assert.That(source.RolledBack, Is.EqualTo(1)); Assert.That(state.ShotCount, Is.Zero);
            Assert.That(state.NextFireTime, Is.EqualTo(float.NegativeInfinity));
            emitter.Accept = true;
            Assert.That(LauncherFiring.TryFire(state, emitter, Aim, 0), Is.EqualTo(FireResult.Fired));
            Assert.That(source.Current, Is.EqualTo(75)); Assert.That(source.Committed, Is.EqualTo(1));
        }

        [Test]
        public void EmitterExceptionRollsBackSharedReservation()
        {
            var source = new EnergySource(100, 100); var state = State(source);
            Assert.Throws<InvalidOperationException>(() => LauncherFiring.TryFire(state, new Emitter { Throw = true }, Aim, 0));
            Assert.That(source.Current, Is.EqualTo(100)); Assert.That(source.RolledBack, Is.EqualTo(1));
            Assert.That(state.ShotCount, Is.Zero); Assert.That(state.NextFireTime, Is.EqualTo(float.NegativeInfinity));
        }

        [Test]
        public void CooldownDoesNotRequestAnotherReservation()
        {
            var source = new EnergySource(100, 100); var state = State(source); var emitter = new Emitter();
            Assert.That(LauncherFiring.TryFire(state, emitter, Aim, 0), Is.EqualTo(FireResult.Fired));
            Assert.That(LauncherFiring.TryFire(state, emitter, Aim, .1f), Is.EqualTo(FireResult.CoolingDown));
            Assert.That(source.Attempts, Is.EqualTo(1)); Assert.That(source.Current, Is.EqualTo(75));
            Assert.That(state.ShotCount, Is.EqualTo(1)); Assert.That(state.NextFireTime, Is.EqualTo(.5f));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void InvalidTimeDoesNotReserveEnergy(float now)
        {
            var source = new EnergySource(100, 100); var state = State(source);
            Assert.That(LauncherFiring.TryFire(state, new Emitter(), Aim, now), Is.EqualTo(FireResult.InvalidTime));
            Assert.That(source.Attempts, Is.Zero); Assert.That(state.ShotCount, Is.Zero);
            Assert.That(state.NextFireTime, Is.EqualTo(float.NegativeInfinity));
        }

        [Test]
        public void InvalidAimDoesNotReserveEnergy()
        {
            var source = new EnergySource(100, 100); var state = State(source);
            Assert.That(LauncherFiring.TryFire(state, new Emitter(), default, 0), Is.EqualTo(FireResult.InvalidAim));
            Assert.That(source.Attempts, Is.Zero); Assert.That(state.ShotCount, Is.Zero);
        }

        [Test]
        public void EmptyPayloadAndMissingPlanDoNotReserveEnergy()
        {
            var source = new EnergySource(100, 100); var state = new LauncherState(); state.BindEnergySource(source);
            Assert.That(LauncherFiring.TryFire(state, new Emitter(), Aim, 0), Is.EqualTo(FireResult.NoPlan));
            state.Equip(Plan(projectileId: ""));
            Assert.That(LauncherFiring.TryFire(state, new Emitter(), Aim, 0), Is.EqualTo(FireResult.EmptyPayload));
            Assert.That(source.Attempts, Is.Zero); Assert.That(state.ShotCount, Is.Zero);
            Assert.That(state.NextFireTime, Is.EqualTo(float.NegativeInfinity));
        }

        [Test]
        public void ReequipPreservesSharedEnergyAndFiringState()
        {
            var source = new EnergySource(100, 100); var state = State(source);
            LauncherFiring.TryFire(state, new Emitter(), Aim, 0);
            state.Equip(Plan(10));
            Assert.That(state.Energy, Is.EqualTo(75)); Assert.That(state.ShotCount, Is.EqualTo(1));
            Assert.That(state.NextFireTime, Is.EqualTo(.5f)); Assert.That(source.Attempts, Is.EqualTo(1));
        }

        [Test]
        public void RechargeWhileBoundDoesNotDuplicateSharedRegeneration()
        {
            var source = new EnergySource(100, 30); var state = State(source);
            state.Recharge(40);
            Assert.That(state.Energy, Is.EqualTo(30));
            Assert.Throws<ArgumentOutOfRangeException>(() => state.Recharge(float.NaN));
            state.BindEnergySource(null);
            Assert.That(state.Energy, Is.EqualTo(100));
        }

        [Test]
        public void UnbindingRestoresPreviousLocalBatteryWithoutResettingCooldown()
        {
            var state = State(null); var emitter = new Emitter();
            Assert.That(LauncherFiring.TryFire(state, emitter, Aim, 0), Is.EqualTo(FireResult.Fired));
            var source = new EnergySource(130, 40); state.BindEnergySource(source);
            Assert.That(LauncherFiring.TryFire(state, emitter, Aim, 1), Is.EqualTo(FireResult.Fired));
            state.BindEnergySource(null);
            Assert.That(state.Energy, Is.EqualTo(75)); Assert.That(state.MaxEnergy, Is.EqualTo(100));
            Assert.That(state.ShotCount, Is.EqualTo(2)); Assert.That(state.NextFireTime, Is.EqualTo(1.5f));
            Assert.That(source.Current, Is.EqualTo(15));
        }

        [Test]
        public void UnboundLauncherKeepsItsOriginalBatteryAndRechargeBehavior()
        {
            var state = State(null); var emitter = new Emitter();
            Assert.That(LauncherFiring.TryFire(state, emitter, Aim, 0), Is.EqualTo(FireResult.Fired));
            Assert.That(state.Energy, Is.EqualTo(75)); state.Recharge(500); Assert.That(state.Energy, Is.EqualTo(100));
            emitter.Accept = false;
            Assert.That(LauncherFiring.TryFire(state, emitter, Aim, 1), Is.EqualTo(FireResult.CapacityExceeded));
            Assert.That(state.Energy, Is.EqualTo(100)); Assert.That(state.ShotCount, Is.EqualTo(1));
            Assert.That(state.NextFireTime, Is.EqualTo(.5f));
        }

        [Test]
        public void ExplicitFiringResetPreservesPlanAndBothEnergyStores()
        {
            var state = State(null); var emitter = new Emitter();
            LauncherFiring.TryFire(state, emitter, Aim, 0);
            var source = new EnergySource(130, 40); state.BindEnergySource(source);
            LauncherFiring.TryFire(state, emitter, Aim, 1);
            var plan = state.Plan; state.ResetFiring();
            Assert.That(state.ShotCount, Is.Zero); Assert.That(state.NextFireTime, Is.EqualTo(float.NegativeInfinity));
            Assert.That(state.Plan, Is.SameAs(plan)); Assert.That(state.Energy, Is.EqualTo(15));
            state.BindEnergySource(null); Assert.That(state.Energy, Is.EqualTo(75));
        }

        [Test]
        public void HostCanBindBeforeEquippingAndAwakePreservesTheBinding()
        {
            var go = new GameObject("Bound launcher"); go.SetActive(false);
            try
            {
                var host = go.AddComponent<LauncherHost>(); var source = new EnergySource(140, 30);
                host.BindEnergySource(source); go.SetActive(true);
                // Invoke startup explicitly: native SendMessage asserts for non-ExecuteAlways
                // behaviours outside Play, where the normal Awake lifecycle is not dispatched.
                var awake = typeof(LauncherHost).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.That(awake, Is.Not.Null);
                awake.Invoke(host, null);
                Assert.That(host.State.Energy, Is.EqualTo(30)); Assert.That(host.State.MaxEnergy, Is.EqualTo(140));
                host.State.Equip(Plan());
                Assert.That(host.TryFire(new Emitter(), Aim, 0), Is.EqualTo(FireResult.Fired));
                Assert.That(source.Current, Is.EqualTo(5));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
