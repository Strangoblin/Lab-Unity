using System;
using System.Collections;
using System.IO;
using CelToon.CustomEquipment.Contracts;
using CelToon.CustomEquipment.Definitions;
using CelToon.CustomLauncher.Contracts;
using CelToon.CustomLauncher.Definitions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace CelToon.CombatRange.Tests.PlayMode
{
    public sealed class CombatRangePlayTests
    {
        private const string ScenePath = "Assets/Scenes/CombatRange.unity";
        private Scene scene;
        private CombatRangeController range;

        [UnitySetUp]
        public IEnumerator LoadSavedRange()
        {
#if UNITY_EDITOR
            scene = EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            yield return null; yield return null;
            foreach (var root in scene.GetRootGameObjects())
                range = root.GetComponentInChildren<CombatRangeController>() ?? range;
            Assert.IsNotNull(range); Assert.IsTrue(range.TryInitialize()); Assert.IsTrue(range.Ready);
            range.AutoAttack = false;
            range.enabled = false; // Freeze input/recovery; these tests drive real pools explicitly.
            range.ResetRange(); Physics.SyncTransforms();
#else
            Assert.Ignore("Saved Combat Range validation requires Unity Editor."); yield break;
#endif
        }

        [UnityTearDown]
        public IEnumerator UnloadRange()
        {
            if (scene.IsValid() && scene.isLoaded)
            {
                var unloading = SceneManager.UnloadSceneAsync(scene);
                if (unloading != null) yield return unloading;
            }
            range = null;
        }

        [UnityTest]
        public IEnumerator SavedSceneHasCompleteIndependentBindings()
        {
            Assert.AreNotSame(range.PlayerPool, range.ThreatPool);
            Assert.AreEqual(512, range.PlayerPool.Capacity); Assert.AreEqual(128, range.ThreatPool.Capacity);
            Assert.AreSame(range.Launcher, range.LauncherWorkbench.Host);
            Assert.AreSame(range.Player, range.EquipmentWorkbench.Host);
            Assert.IsTrue(range.PlayerMuzzle.IsChildOf(range.Player.transform));
            Assert.AreEqual(3, range.Targets.Length); Assert.IsFalse(range.Targets[1].MoveTarget);
            Assert.AreEqual(EquipmentSlots.Count, range.AttachmentVisuals.Length);
            Assert.AreEqual(16, range.EquipmentWorkbench.Catalog.Parts.Length);
            Assert.GreaterOrEqual(range.LauncherWorkbench.Catalog.Parts.Length, 60);
            Assert.AreEqual(8, range.Threats.Length);
            Assert.IsFalse(range.LauncherWorkbench.DrawAutomatically); Assert.IsFalse(range.EquipmentWorkbench.DrawAutomatically);
            Assert.IsTrue(range.LauncherWorkbench.Embedded); Assert.IsTrue(range.EquipmentWorkbench.Embedded);
            int cameras = 0, listeners = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                cameras += root.GetComponentsInChildren<Camera>(true).Length;
                listeners += root.GetComponentsInChildren<AudioListener>(true).Length;
                foreach (var script in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    Assert.IsNotNull(script, "Scene has a Missing Script.");
                    Assert.AreNotEqual("RangeController", script.GetType().Name);
                    Assert.AreNotEqual("StorageController", script.GetType().Name);
                }
            }
            Assert.AreEqual(1, cameras); Assert.AreEqual(1, listeners);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlayerShotSpendsEquipmentEnergyAndHitsStationaryTarget()
        {
            float energy = range.Player.State.Energy;
            float cost = range.Launcher.State.Plan.EnergyCost * range.Player.State.Plan.Stats.EnergyCostScale;
            Assert.AreEqual(FireResult.Fired, range.FirePlayer());
            Assert.AreEqual(energy - cost, range.Player.State.Energy, .0001f);
            Assert.AreEqual(range.Player.State.Energy, range.Launcher.State.Energy, .0001f);
            Assert.AreEqual(FireResult.CoolingDown, range.FirePlayer());
            Assert.AreEqual(energy - cost, range.Player.State.Energy, .0001f);
            TickPlayerUntilTargetHit();
            Assert.Greater(range.Targets[1].Hits, 0); Assert.GreaterOrEqual(range.Targets[1].TotalDamage, 12);
            Assert.AreEqual(0, range.Player.State.Hits, "The player's own projectile must ignore the player collider.");
            CaptureGeometry();
            yield return null;
        }

        [UnityTest]
        public IEnumerator EnemyProjectileDamagesPlayerWithoutSpendingPlayerEnergy()
        {
            float energy = range.Player.State.Energy;
            float defenses = range.Player.State.Health + range.Player.State.Shield;
            Assert.IsTrue(range.SelectThreat(0));
            Assert.AreEqual(FireResult.Fired, range.FireThreat());
            Assert.AreEqual(energy, range.Player.State.Energy);
            TickThreatUntilPlayerHit();
            Assert.AreEqual(1, range.Player.State.Hits);
            Assert.Less(range.Player.State.Health + range.Player.State.Shield, defenses);
            Assert.AreEqual(energy, range.Player.State.Energy);
            Assert.AreEqual(0, range.Launcher.State.ShotCount);
            Assert.AreEqual(0, range.Targets[1].Hits);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BothWorkbenchesPreserveResourcesCooldownAndSharedBinding()
        {
            Assert.AreEqual(FireResult.Fired, range.FirePlayer());
            range.DirectHit();
            float health = range.Player.State.Health, shield = range.Player.State.Shield, energy = range.Player.State.Energy;
            float nextFire = range.Launcher.State.NextFireTime;
            range.LauncherWorkbench.OpenSlot(PartKind.Trigger); Assert.IsTrue(range.PopupOpen);
            range.SetEquipmentTab(true);
            Assert.IsTrue(range.EquipmentTab); Assert.IsFalse(range.LauncherWorkbench.PopupOpen);
            range.EquipmentWorkbench.OpenSlot(EquipmentKind.EnergyCore); Assert.IsTrue(range.PopupOpen);
            var efficient = FindEquipmentPart("equipment.resource.efficientenergy");
            Assert.IsTrue(range.EquipmentWorkbench.SelectPart(EquipmentKind.EnergyCore, efficient));
            Assert.AreEqual(health, range.Player.State.Health); Assert.AreEqual(shield, range.Player.State.Shield);
            Assert.AreEqual(energy, range.Player.State.Energy);
            Assert.AreEqual(.65f, range.Player.State.Plan.Stats.EnergyCostScale, .0001f);
            range.SetEquipmentTab(false);
            Assert.IsFalse(range.EquipmentTab); Assert.IsFalse(range.EquipmentWorkbench.PopupOpen);
            Assert.IsTrue(range.LauncherWorkbench.SelectRecipe(FindLauncherRecipe("recipe.Scatter")));
            Assert.AreEqual(nextFire, range.Launcher.State.NextFireTime);
            Assert.AreEqual(energy, range.Player.State.Energy); Assert.AreEqual(energy, range.Launcher.State.Energy);
            Assert.AreEqual(FireResult.CoolingDown, range.FirePlayer());
            Assert.AreEqual(energy, range.Player.State.Energy);
            yield return null;
        }

        [UnityTest]
        public IEnumerator EfficientCoreChangesActualShotCostAndNaturalRecharge()
        {
            Assert.IsTrue(range.EquipmentWorkbench.SelectPart(EquipmentKind.EnergyCore, FindEquipmentPart("equipment.resource.efficientenergy")));
            range.ResetRange();
            float energy = range.Player.State.Energy;
            float baseCost = range.Launcher.State.Plan.EnergyCost;
            Assert.AreEqual(FireResult.Fired, range.FirePlayer());
            Assert.AreEqual(energy - baseCost * .65f, range.Player.State.Energy, .0001f);
            Assert.IsTrue(range.Player.State.TrySpendEnergy(25));
            float beforeRecharge = range.Player.State.Energy;
            range.Player.Tick(.5f);
            Assert.AreEqual(Mathf.Min(energy, beforeRecharge + range.Player.State.Plan.Stats.EnergyRegen * .5f), range.Player.State.Energy, .001f);
            Assert.AreEqual(range.Player.State.Energy, range.Launcher.State.Energy);
            yield return null;
        }

        [UnityTest]
        public IEnumerator EmptyAndLowEnergyFailuresLeaveEnergyCooldownAndPoolsUntouched()
        {
            float energy = range.Player.State.Energy;
            float nextFire = range.Launcher.State.NextFireTime;
            Assert.IsTrue(range.LauncherWorkbench.SelectPart(PartKind.Projectile, null));
            Assert.AreEqual(FireResult.EmptyPayload, range.FirePlayer());
            Assert.AreEqual(energy, range.Player.State.Energy); Assert.AreEqual(nextFire, range.Launcher.State.NextFireTime);
            Assert.AreEqual(0, range.PlayerPool.ActiveCount);
            Assert.IsTrue(range.LauncherWorkbench.SelectRecipe(FindLauncherRecipe("recipe.Precision")));
            Assert.IsTrue(range.EquipmentWorkbench.SelectRecipe(FindEquipmentRecipe("equipment.recipe.custom")));
            range.ResetRange();
            Assert.IsTrue(range.Player.State.TrySpendEnergy(range.Player.State.Energy));
            Assert.AreEqual(0, range.Player.State.Energy);
            Assert.AreEqual(FireResult.InsufficientEnergy, range.FirePlayer());
            Assert.AreEqual(0, range.Player.State.Energy); Assert.AreEqual(nextFire, range.Launcher.State.NextFireTime);
            Assert.AreEqual(0, range.PlayerPool.ActiveCount); Assert.AreEqual(0, range.Launcher.State.ShotCount);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FullPlayerPoolRollsBackEnergyAndLeavesEnemyPoolUsable()
        {
            range.PlayerPool.ConfigureCapacity(1);
            var blocker = new ProjectileCommand(new Vector3(1000, 1000, 1000), Vector3.forward,
                new ProjectileSpec(1, 1, .05f, 10, Color.white), 0, 1, ProjectileProgram.Empty);
            Assert.IsTrue(range.PlayerPool.TryEmit(new[] { blocker }));
            float energy = range.Player.State.Energy;
            float nextFire = range.Launcher.State.NextFireTime;
            Assert.AreEqual(FireResult.CapacityExceeded, range.FirePlayer());
            Assert.AreEqual(energy, range.Player.State.Energy); Assert.AreEqual(nextFire, range.Launcher.State.NextFireTime);
            Assert.AreEqual(0, range.Launcher.State.ShotCount);
            Assert.AreEqual(1, range.PlayerPool.ActiveCount);
            Assert.AreEqual(FireResult.Fired, range.FireThreat());
            Assert.AreEqual(1, range.ThreatPool.ActiveCount);
            Assert.AreEqual(energy, range.Player.State.Energy);
            range.PlayerPool.Clear();
            Assert.AreEqual(FireResult.Fired, range.FirePlayer(), "Capacity failure must not start firing cooldown.");
            Assert.Less(range.Player.State.Energy, energy);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SprintMovesMuzzleAndDeathResetRestoresAllRangeState()
        {
            Vector3 home = range.Player.transform.position, muzzleHome = range.PlayerMuzzle.position;
            float stamina = range.Player.State.Stamina;
            range.Player.Tick(.5f, true);
            Assert.IsTrue(range.Player.State.Sprinting); Assert.Less(range.Player.State.Stamina, stamina);
            range.Motor.Move(Vector3.right * range.Player.State.MoveSpeed * .2f);
            Assert.Greater(range.Player.transform.position.x, home.x + .5f);
            Assert.AreEqual(range.Player.transform.position.x - home.x, range.PlayerMuzzle.position.x - muzzleHome.x, .001f);
            Assert.AreEqual(FireResult.Fired, range.FireThreat());
            range.Player.ReceiveStatus(new StatusInfo(StatusKind.Poison, 5, 4, 0, 2));
            range.Player.ReceiveImpulse(Vector3.right * 5);
            range.DirectHit(100000);
            Assert.IsTrue(range.Player.State.IsDead); Assert.AreNotEqual(FireResult.Fired, range.FirePlayer());
            float energy = range.Player.State.Energy;
            range.Player.Tick(1, true); Assert.AreEqual(energy, range.Player.State.Energy);
            Assert.AreEqual(0, range.Player.State.MoveSpeed);
            range.AutoAttack = true; range.ResetRange();
            Assert.IsFalse(range.AutoAttack); Assert.IsFalse(range.Player.State.IsDead);
            Assert.AreEqual(home.x, range.Player.transform.position.x, .001f); Assert.AreEqual(home.z, range.Player.transform.position.z, .001f);
            Assert.AreEqual(range.Player.State.Plan.Stats.MaxEnergy, range.Player.State.Energy);
            Assert.AreEqual(range.Player.State.Plan.Stats.MaxHealth, range.Player.State.Health);
            Assert.AreEqual(range.Player.State.Plan.Stats.MaxShield, range.Player.State.Shield);
            Assert.AreEqual(range.Player.State.Plan.Stats.MaxStamina, range.Player.State.Stamina);
            Assert.AreEqual(0, range.Player.State.StatusCount); Assert.AreEqual(0, range.Player.State.Hits);
            Assert.AreEqual(Vector3.zero, range.Player.ImpulseVelocity);
            Assert.AreEqual(0, range.PlayerPool.ActiveCount); Assert.AreEqual(0, range.ThreatPool.ActiveCount);
            foreach (var target in range.Targets) Assert.AreEqual(0, target.TotalDamage);
            yield return null;
        }

        [UnityTest]
        public IEnumerator KeyboardMovementAndFireAreSuppressedWhileEditingParts()
        {
            range.enabled = true;
            Vector3 home = range.Player.transform.position;
            float stamina = range.Player.State.Stamina;
            SendKey(KeyCode.D, EventType.KeyDown); SendKey(KeyCode.LeftShift, EventType.KeyDown);
            yield return new WaitForSeconds(.12f);
            SendKey(KeyCode.D, EventType.KeyUp); SendKey(KeyCode.LeftShift, EventType.KeyUp);
            Assert.Greater(range.Player.transform.position.x, home.x + .1f);
            Assert.Less(range.Player.State.Stamina, stamina);
            range.LauncherWorkbench.OpenSlot(PartKind.Projectile);
            Vector3 editingPosition = range.Player.transform.position;
            int shots = range.Launcher.State.ShotCount;
            SendKey(KeyCode.D, EventType.KeyDown); SendKey(KeyCode.Space, EventType.KeyDown); SendKey(KeyCode.R, EventType.KeyDown);
            yield return new WaitForSeconds(.08f);
            Assert.AreEqual(editingPosition.x, range.Player.transform.position.x, .001f);
            Assert.AreEqual(shots, range.Launcher.State.ShotCount);
            Assert.IsTrue(range.PopupOpen);
            SendKey(KeyCode.D, EventType.KeyUp); SendKey(KeyCode.Space, EventType.KeyUp); SendKey(KeyCode.R, EventType.KeyUp);
            SendKey(KeyCode.Escape, EventType.KeyDown);
            yield return null;
            Assert.IsFalse(range.PopupOpen);
            SendKey(KeyCode.Escape, EventType.KeyUp); SendKey(KeyCode.Space, EventType.KeyDown);
            yield return null;
            SendKey(KeyCode.Space, EventType.KeyUp);
            Assert.Greater(range.Launcher.State.ShotCount, shots);
            range.enabled = false;
        }

        private void SendKey(KeyCode key, EventType type)
        { range.HandleGUIInput(new Event { type = type, keyCode = key }); }

        private void TickPlayerUntilTargetHit()
        {
            Physics.SyncTransforms();
            for (int i = 0; i < 70 && range.Targets[1].Hits == 0; i++) range.PlayerPool.Tick(.02f);
        }
        private void TickThreatUntilPlayerHit()
        {
            Physics.SyncTransforms();
            for (int i = 0; i < 70 && range.Player.State.Hits == 0; i++) range.ThreatPool.Tick(.02f);
        }
        private LauncherRecipeDefinition FindLauncherRecipe(string id)
        {
            var recipe = Array.Find(range.LauncherWorkbench.Catalog.Recipes, x => x != null && x.RecipeId == id);
            Assert.IsNotNull(recipe, id); return recipe;
        }
        private EquipmentRecipeDefinition FindEquipmentRecipe(string id)
        {
            var recipe = Array.Find(range.EquipmentWorkbench.Catalog.Recipes, x => x != null && x.RecipeId == id);
            Assert.IsNotNull(recipe, id); return recipe;
        }
        private EquipmentPartDefinition FindEquipmentPart(string id)
        {
            var part = Array.Find(range.EquipmentWorkbench.Catalog.Parts, x => x != null && x.PartId == id);
            Assert.IsNotNull(part, id); return part;
        }
        private void CaptureGeometry()
        {
            string path = Environment.GetEnvironmentVariable("COMBAT_RANGE_ARTIFACTS");
            if (string.IsNullOrEmpty(path) || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            Directory.CreateDirectory(path);
            var camera = range.RangeCamera;
            var render = new RenderTexture(1440, 900, 24);
            var texture = new Texture2D(1440, 900, TextureFormat.RGB24, false);
            var oldRect = camera.rect; var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
            try
            {
                camera.rect = new Rect(0, 0, 1, 1); camera.targetTexture = render; camera.Render(); RenderTexture.active = render;
                texture.ReadPixels(new Rect(0, 0, render.width, render.height), 0, 0); texture.Apply();
                File.WriteAllBytes(Path.Combine(path, "CombatRange-Geometry.png"), texture.EncodeToPNG());
            }
            finally
            {
                camera.rect = oldRect; camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
                render.Release(); UnityEngine.Object.Destroy(render); UnityEngine.Object.Destroy(texture);
            }
        }
    }
}
