using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CelToon.CustomEquipment.Contracts;
using CelToon.CustomLauncher.Contracts;
using CelToon.CustomLauncher.Definitions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CelToon.CombatRange.Editor
{
    // Explicit isolated-editor validation. Every product action uses native GameView input.
    [InitializeOnLoad]
    public static class CombatRangeInteractionValidation
    {
        private const string Pending = "CombatRange.InteractionValidation.Pending";
        private sealed class Step
        {
            public Func<CombatRangeController, bool> Perform;
            public float Delay;
            public string Description;
        }
        private static readonly List<Step> steps = new List<Step>();
        private static readonly List<string> log = new List<string>();
        private static EditorWindow view;
        private static double started, nextStep, stepStarted;
        private static int phase, checks, beforeShots, beforeHits, beforeTargetHits;
        private static float beforeHealth, beforeStamina, beforeEnergy, minimumEnergy;
        private static Vector3 beforeMove;
        private static Vector2 click;
        private static bool watchEnergy;
        private static string heavyId;
        private static string Artifacts => Environment.GetEnvironmentVariable("COMBAT_RANGE_ARTIFACTS");
        static CombatRangeInteractionValidation()
        { if (SessionState.GetBool(Pending, false)) EditorApplication.delayCall += Resume; }

        public static void Run()
        {
            if (string.IsNullOrWhiteSpace(Artifacts)) throw new InvalidOperationException("Set COMBAT_RANGE_ARTIFACTS for isolated native-input validation.");
            Directory.CreateDirectory(Artifacts);
            EditorSceneManager.OpenScene(CombatRangeSceneBuilder.ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(Pending, true); Resume(); EditorApplication.EnterPlaymode();
        }
        private static void Resume()
        {
            view = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
            view.Show(); view.maximized = true; view.Focus();
            started = EditorApplication.timeSinceStartup; nextStep = started + 2; stepStarted = nextStep;
            phase = checks = 0; watchEnergy = false; log.Clear(); BuildSteps();
            EditorApplication.update -= Update; EditorApplication.update += Update;
        }
        private static void Add(Action<CombatRangeController> action, string description, float delay = .12f)
        { steps.Add(new Step { Perform = range => { action(range); return true; }, Delay = delay, Description = description }); }
        private static void Wait(Func<CombatRangeController, bool> condition, string description, float delay = .12f)
        { steps.Add(new Step { Perform = range => { if (!condition(range)) return false; Check(true, description); return true; }, Delay = delay, Description = description }); }
        private static void Click(string owner, string id)
        {
            Add(range => Down(range, owner, id), "mouse down " + id);
            Add(range => Up(), "mouse up " + id);
        }
        private static void Press(KeyCode key, float duration = .08f)
        { Add(range => Key(key, true), "key down " + key, duration); Add(range => Key(key, false), "key up " + key); }
        private static int TargetHits(CombatRangeController range)
        { int value = 0; foreach (var target in range.Targets) if (target != null) value += target.Hits; return value; }
        private static void WatchShot(CombatRangeController range)
        {
            beforeShots = range.Launcher.State.ShotCount; beforeTargetHits = TargetHits(range);
            minimumEnergy = beforeEnergy = range.Player.State.Energy; watchEnergy = true;
        }
        private static bool ShotSpentSharedEnergy(CombatRangeController range)
            => range.Launcher.State.ShotCount > beforeShots && minimumEnergy < beforeEnergy - .01f
                && Mathf.Abs(range.Launcher.State.Energy - range.Player.State.Energy) < .001f;

        private static void BuildSteps()
        {
            steps.Clear();
            Add(range => {
                Check(range.Ready && !range.EquipmentTab, "saved scene starts with both systems and launcher tab");
                var heavy = Array.Find(range.LauncherWorkbench.Catalog.Parts, part => part is ProjectileDefinition && part.name == "HeavyRound");
                if (heavy == null) throw new InvalidOperationException("HeavyRound catalog sample missing.");
                heavyId = heavy.PartId;
            }, "initial scene");

            Click("launcher", "recipe.menu");
            Wait(range => range.LauncherWorkbench.PopupOpen, "mouse opens launcher recipe list");
            Click("launcher", "recipe.recipe.Precision");
            Wait(range => range.Launcher.Loadout.Recipe.RecipeId == "recipe.Precision" && !range.PopupOpen, "mouse selects launcher recipe");
            Click("launcher", "slot.Projectile");
            Wait(range => range.LauncherWorkbench.PopupOpen, "mouse opens projectile slot");
            Click("launcher", "part.empty");
            Wait(range => range.LauncherWorkbench.CurrentPart(PartKind.Projectile) == null && !range.PopupOpen, "mouse clears projectile slot");
            Add(range => beforeShots = range.Launcher.State.ShotCount, "record empty launcher");
            Click("range", "player.fire");
            Add(range => Check(range.LastFireResult == FireResult.EmptyPayload && range.Launcher.State.ShotCount == beforeShots, "empty payload click does not fire"), "check empty payload");
            Click("launcher", "slot.Projectile");
            Add(range => Down(range, "launcher", "part." + heavyId), "select heavy projectile");
            Add(range => Up(), "heavy projectile mouse up");
            Wait(range => range.LauncherWorkbench.CurrentPart(PartKind.Projectile)?.PartId == heavyId && !range.PopupOpen, "mouse equips heavy projectile");
            Add(WatchShot, "record shared energy before player click");
            Click("range", "player.fire");
            Wait(ShotSpentSharedEnergy, "player fire click spends shared equipment energy");
            Wait(range => TargetHits(range) > beforeTargetHits && range.PlayerPool.HitCount > 0, "player projectile physically hits a target");
            Add(range => Capture("CombatRange-Launcher.png"), "capture launcher dashboard");

            Click("range", "tab.equipment");
            Wait(range => range.EquipmentTab, "mouse switches to equipment tab");
            Click("equipment", "recipe.menu");
            Click("equipment", "recipe.equipment.recipe.custom");
            Wait(range => range.Player.Loadout.Recipe.RecipeId == "equipment.recipe.custom" && !range.PopupOpen, "mouse selects equipment recipe");
            Press(KeyCode.R);
            Wait(range => range.Player.State.Health == 100 && range.Player.State.Energy == 100 && range.Launcher.State.ShotCount == 0, "R resets resources and firing state");

            Add(range => { beforeHits = range.Player.State.Hits; beforeHealth = range.Player.State.Health; }, "record enemy hit baseline");
            Click("range", "threat.fire");
            Wait(range => range.Player.State.Hits > beforeHits && range.Player.State.Health < beforeHealth && range.ThreatPool.HitCount > 0,
                "enemy fire click physically hits the player");
            Add(range => beforeHealth = range.Player.State.Health, "record direct hit baseline");
            Click("range", "threat.hit");
            Wait(range => range.Player.State.Health <= beforeHealth - 24.9f, "direct damage click removes 25 health");
            Press(KeyCode.R);
            Wait(range => range.Player.State.Health == 100 && range.Player.State.Hits == 0, "R restores health and clears hit counters");

            Add(WatchShot, "record Space shared energy baseline");
            Press(KeyCode.Space);
            Wait(ShotSpentSharedEnergy, "Space fires using shared equipment energy");
            Wait(range => TargetHits(range) > beforeTargetHits, "Space projectile hits the target");
            Add(range => beforeHits = range.Player.State.Hits, "record H hit baseline");
            Press(KeyCode.H);
            Wait(range => range.Player.State.Hits > beforeHits, "H fires an enemy projectile that hits the player");
            Press(KeyCode.R);
            Press(KeyCode.T);
            Wait(range => range.AutoAttack, "T enables continuous enemy fire");
            Press(KeyCode.T);
            Wait(range => !range.AutoAttack, "T stops continuous enemy fire");
            Press(KeyCode.R);

            Add(range => { beforeMove = range.Player.transform.position; beforeStamina = range.Player.State.Stamina; Key(KeyCode.D, true); Key(KeyCode.LeftShift, true); }, "hold D and Shift", .6f);
            Add(range => { Check(range.Player.transform.position.x > beforeMove.x + .1f, "D moves the player"); Check(range.Player.State.Stamina < beforeStamina - 1, "D and Shift consume sprint stamina"); Key(KeyCode.D, false); Key(KeyCode.LeftShift, false); }, "release D and Shift");
            Press(KeyCode.R);
            Wait(range => Vector3.Distance(range.Player.transform.position, beforeMove) < .15f && range.Player.State.Stamina == 100, "R restores position and stamina");
            Click("equipment", "slot.EnergyCore");
            Wait(range => range.EquipmentWorkbench.PopupOpen, "mouse opens energy core slot");
            Press(KeyCode.Escape);
            Wait(range => !range.PopupOpen, "Esc closes the equipment popup");

            Click("equipment", "slot.EnergyCore");
            Click("equipment", "part.equipment.resource.efficientenergy");
            Wait(range => range.EquipmentWorkbench.CurrentPart(EquipmentKind.EnergyCore)?.PartId == "equipment.resource.efficientenergy"
                && range.Player.State.Plan.Stats.MaxEnergy == 130 && Mathf.Abs(range.Player.State.Plan.Stats.EnergyCostScale - .65f) < .0001f && !range.PopupOpen,
                "mouse equips efficient core with capacity 130 and cost multiplier 0.65");
            Add(range => {
                Check(range.Launcher.State.MaxEnergy == 130 && Mathf.Abs(range.EffectiveShotCost - range.Launcher.State.Plan.EnergyCost * .65f) < .0001f,
                    "launcher displays equipment capacity and applies the core cost affix");
                Capture("CombatRange-Equipment.png");
            }, "capture equipment dashboard");
            Press(KeyCode.R);
            Wait(range => range.Player.State.Energy == 130, "R fills equipped energy capacity");
            Click("range", "tab.launcher");
            Wait(range => !range.EquipmentTab, "mouse switches back to launcher tab");
            Add(WatchShot, "record efficient shared energy baseline");
            Click("range", "player.fire");
            Wait(ShotSpentSharedEnergy, "efficient core affects a real shared-energy shot");
            Wait(range => TargetHits(range) > beforeTargetHits, "efficient core shot still physically hits a target");
            Click("launcher", "slot.Projectile");
            Wait(range => range.LauncherWorkbench.PopupOpen, "launcher remains editable after equipment changes");
            Press(KeyCode.Escape);
            Wait(range => !range.PopupOpen, "Esc closes launcher popup");
            Add(range => Capture("CombatRange-Launcher.png"), "capture final launcher dashboard");
            Click("range", "tab.equipment");
            Add(range => Capture("CombatRange-Equipment.png"), "capture final equipment dashboard");
        }

        private static void Update()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - started > 150) { Finish(false, "TIMEOUT phase=" + phase); return; }
            if (!EditorApplication.isPlaying) return;
            view.Focus(); view.Repaint();
            var range = UnityEngine.Object.FindObjectOfType<CombatRangeController>();
            if (range == null || !range.Ready) return;
            if (watchEnergy && range.Launcher.State.ShotCount > beforeShots) minimumEnergy = Mathf.Min(minimumEnergy, range.Player.State.Energy);
            if (now < nextStep) return;
            try
            {
                if (phase >= steps.Count) { Finish(true, checks + " native mouse and keyboard checks passed"); return; }
                var step = steps[phase];
                if (!step.Perform(range))
                {
                    if (now - stepStarted > 8) throw new InvalidOperationException("Timed out waiting for " + step.Description);
                    return;
                }
                phase++; nextStep = now + step.Delay; stepStarted = nextStep;
            }
            catch (Exception exception) { Debug.LogException(exception); Finish(false, exception.Message + " phase=" + phase); }
        }
        private static void Down(CombatRangeController range, string owner, string id)
        {
            Rect rect;
            bool found = owner == "launcher" ? range.LauncherWorkbench.TryGetControlRect(id, out rect)
                : owner == "equipment" ? range.EquipmentWorkbench.TryGetControlRect(id, out rect) : range.TryGetControlRect(id, out rect);
            if (!found) throw new InvalidOperationException("Control not rendered: " + owner + "/" + id);
            click = rect.center;
            if (click.x < 0 || click.y < 0 || click.x >= Screen.width || click.y >= Screen.height)
                throw new InvalidOperationException("Control outside GameView: " + id + " " + rect + " Screen=" + Screen.width + "x" + Screen.height);
            Note("COMBAT_RANGE_INPUT_CLICK " + owner + "/" + id + " rect=" + rect + " Screen=" + Screen.width + "x" + Screen.height);
            Queue(new Event { type = EventType.MouseDown, button = 0, mousePosition = click });
        }
        private static void Up() => Queue(new Event { type = EventType.MouseUp, button = 0, mousePosition = click });
        private static void Key(KeyCode key, bool down)
        {
            Note("COMBAT_RANGE_INPUT_KEY " + key + " " + (down ? "Down" : "Up"));
            Queue(new Event { type = down ? EventType.KeyDown : EventType.KeyUp, keyCode = key });
        }
        private static void Queue(Event input)
        {
            var method = typeof(EditorGUIUtility).GetMethod("QueueGameViewInputEvent", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null) throw new InvalidOperationException("Native GameView input API unavailable.");
            method.Invoke(null, new object[] { input });
        }
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Interaction failed: " + message);
            checks++; Note("COMBAT_RANGE_INPUT_PASS " + message);
        }
        private static void Note(string message) { log.Add(message); Debug.Log(message); }

        private static void Capture(string file)
        {
            RenderTexture target = null;
            for (var cursor = view.GetType(); cursor != null && target == null; cursor = cursor.BaseType)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                var property = cursor.GetProperty("targetTexture", flags);
                if (property != null) target = property.GetValue(view) as RenderTexture;
                if (target == null) foreach (var field in cursor.GetFields(flags))
                    if (typeof(RenderTexture).IsAssignableFrom(field.FieldType)) { target = field.GetValue(view) as RenderTexture; if (target != null) break; }
            }
            if (target == null) throw new InvalidOperationException("Native GameView render target unavailable.");
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
                if (SystemInfo.graphicsUVStartsAtTop)
                {
                    var pixels = texture.GetPixels32(); var upright = new Color32[pixels.Length];
                    for (int row = 0; row < texture.height; row++)
                        Array.Copy(pixels, row * texture.width, upright, (texture.height - 1 - row) * texture.width, texture.width);
                    texture.SetPixels32(upright); texture.Apply();
                }
                File.WriteAllBytes(Path.Combine(Artifacts, file), texture.EncodeToPNG()); Note("COMBAT_RANGE_CAPTURE_SAVED " + file);
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(texture); }
        }
        private static void Finish(bool passed, string message)
        {
            Note((passed ? "COMBAT_RANGE_INTERACTION_PASS " : "COMBAT_RANGE_INTERACTION_FAIL ") + message);
            File.WriteAllText(Path.Combine(Artifacts, "interaction-result.txt"), (passed ? "PASS " : "FAIL ") + message);
            // The runner normally supplies -logFile interaction.log. Create an event-only log otherwise.
            string logPath = Path.Combine(Artifacts, "interaction.log");
            if (!File.Exists(logPath)) File.WriteAllLines(logPath, log);
            SessionState.SetBool(Pending, false); EditorApplication.update -= Update; EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
