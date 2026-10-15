using System;
using System.Collections.Generic;
using System.IO;
using CelToon.CustomEquipment.Compilation;
using CelToon.CustomEquipment.Contracts;
using CelToon.CustomEquipment.Definitions;
using CelToon.CustomEquipment.Editor;
using CelToon.CustomEquipment.Runtime;
using CelToon.CustomEquipment.Samples;
using CelToon.CustomLauncher.Compilation;
using CelToon.CustomLauncher.Definitions;
using CelToon.CustomLauncher.Editor;
using CelToon.CustomLauncher.Projectiles;
using CelToon.CustomLauncher.Runtime;
using CelToon.CustomLauncher.Samples;
using CelToon.CustomItem.Adapters;
using CelToon.CustomItem.Editor;
using CelToon.CustomItem.Samples;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CelToon.CombatRange.Editor
{
    public static class CombatRangeSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/CombatRange.unity";
        public const string ResourcePath = "Assets/Scenes/CombatRangeResources";

        [MenuItem("CelToon/Combat Range/Create or Rebuild Combat Range")]
        public static void BuildRange()
        {
            if (!Application.isBatchMode)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                { Debug.LogWarning("Stop Play before rebuilding Combat Range."); return; }
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                    { Debug.LogWarning("Save or close untitled scenes before rebuilding Combat Range."); return; }
                var existing = SceneManager.GetSceneByPath(ScenePath);
                if (existing.IsValid() && existing.isLoaded)
                { Debug.LogWarning("Close Combat Range before rebuilding its saved scene."); return; }
            }

            EnsureFolder(ResourcePath);
            EnsureFolder("Assets/Mine/Special/CustomLauncher/Samples/Definitions");
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                // Load content after the scene switch, which can unload unreferenced definitions.
                var launcherCatalog = RangeContentBuilder.Build();
                var equipmentCatalog = StorageContentBuilder.Build();
                var itemCatalog = ItemContentBuilder.Build();
                var threats = StorageContentBuilder.BuildThreats();
                var launcherRecipe = Array.Find(launcherCatalog.Recipes, x => x != null && x.RecipeId == "recipe.Precision");
                var equipmentRecipe = Array.Find(equipmentCatalog.Recipes, x => x != null && x.RecipeId == "equipment.recipe.balanced");
                if (launcherRecipe == null || equipmentRecipe == null || threats.Length != 8)
                    throw new InvalidOperationException("Combat Range default recipes or threats are missing.");

                var floor = MaterialAsset("Floor", new Color(.085f, .13f, .18f));
                var wall = MaterialAsset("Walls", new Color(.16f, .23f, .3f));
                var lines = MaterialAsset("Markings", new Color(.25f, .64f, .7f));
                var body = MaterialAsset("Player", new Color(.35f, .7f, .8f));
                var launcher = MaterialAsset("Launcher", new Color(.24f, .65f, .75f));
                var attacker = MaterialAsset("Attacker", new Color(.95f, .38f, .16f));
                var targetMaterial = MaterialAsset("Targets", new Color(.9f, .35f, .15f));

                var root = new GameObject("CombatRange");
                var controller = root.AddComponent<CombatRangeController>();
                controller.LauncherWorkbench = root.AddComponent<LauncherWorkbenchUI>();
                controller.EquipmentWorkbench = root.AddComponent<EquipmentWorkbenchUI>();
                controller.ItemWorkbench = root.AddComponent<ItemRangeWorkbench>();
                controller.ItemWorkbench.Catalog = itemCatalog;
                controller.ItemWorkbench.LauncherRecipe = Array.Find(launcherCatalog.Recipes, x => x != null && x.RecipeId == "recipe.Custom");
                controller.ItemWorkbench.EquipmentRecipe = Array.Find(equipmentCatalog.Recipes, x => x != null && x.RecipeId == "equipment.recipe.custom");
                controller.LauncherWorkbench.Catalog = launcherCatalog;
                controller.LauncherWorkbench.InitialRecipe = launcherRecipe;
                controller.LauncherWorkbench.DrawAutomatically = false;
                controller.LauncherWorkbench.Embedded = true;
                controller.EquipmentWorkbench.Catalog = equipmentCatalog;
                controller.EquipmentWorkbench.InitialRecipe = equipmentRecipe;
                controller.EquipmentWorkbench.DrawAutomatically = false;
                controller.EquipmentWorkbench.Embedded = true;

                Cube(root.transform, "Floor", new Vector3(0, -.25f, 9), new Vector3(25, .5f, 34), floor);
                Cube(root.transform, "Backstop", new Vector3(0, 2.5f, 26), new Vector3(25, 5, .5f), wall);
                Cube(root.transform, "LeftWall", new Vector3(-12.5f, 1, 9), new Vector3(.4f, 2, 34), wall);
                Cube(root.transform, "RightWall", new Vector3(12.5f, 1, 9), new Vector3(.4f, 2, 34), wall);
                Cube(root.transform, "FrontWall", new Vector3(0, .5f, -8), new Vector3(25, 1, .4f), wall);
                for (int z = 5; z <= 25; z += 5)
                {
                    Cube(root.transform, "DistanceLine_" + z, new Vector3(0, .012f, z), new Vector3(24, .018f, .055f), lines, false);
                    var marker = new GameObject("DistanceLabel_" + z);
                    marker.transform.SetParent(root.transform, false);
                    marker.transform.position = new Vector3(9, .04f, z);
                    marker.transform.rotation = Quaternion.Euler(90, 0, 0);
                    var text = marker.AddComponent<TextMesh>();
                    text.text = z + " m"; text.anchor = TextAnchor.MiddleCenter;
                    text.fontSize = 40; text.characterSize = .32f; text.color = new Color(.55f, .85f, .9f);
                }
                for (int x = -10; x <= 10; x += 5)
                    Cube(root.transform, "LaneLine_" + x, new Vector3(x, .011f, 9), new Vector3(.025f, .015f, 32), lines, false);
                Cube(root.transform, "PlayerTestZone", new Vector3(0, .017f, 0), new Vector3(4, .02f, 3), lines, false);

                var player = new GameObject("Player"); player.transform.SetParent(root.transform, false);
                controller.Player = player.AddComponent<EquipmentHost>(); controller.Player.AutoTick = false;
                if (!controller.Player.TryEquip(equipmentRecipe.CreateLoadout()))
                    throw new InvalidOperationException("Initial player equipment is invalid.");
                controller.Motor = player.AddComponent<CharacterController>();
                controller.Motor.height = 2; controller.Motor.radius = .4f;
                controller.Motor.center = Vector3.up; controller.Motor.stepOffset = .25f;
                var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule); capsule.name = "PlayerBody";
                capsule.transform.SetParent(player.transform, false); capsule.transform.localPosition = Vector3.up;
                capsule.transform.localScale = new Vector3(.7f, 1, .7f);
                UnityEngine.Object.DestroyImmediate(capsule.GetComponent<Collider>());
                controller.PlayerBody = capsule.GetComponent<Renderer>(); controller.PlayerBody.sharedMaterial = body;
                Cube(player.transform, "Visor", new Vector3(0, 1.55f, .32f), new Vector3(.45f, .2f, .12f), lines, false);
                controller.EquipmentWorkbench.Host = controller.Player;

                controller.AttachmentVisuals = new GameObject[EquipmentSlots.Count];
                var positions = new[] { new Vector3(0, 1.15f, -.35f), new Vector3(-.47f, 1.15f, 0),
                    new Vector3(0, .18f, .15f), new Vector3(.47f, 1.15f, 0) };
                for (int i = 0; i < EquipmentSlots.Count; i++)
                {
                    var material = MaterialAsset("Core_" + EquipmentSlots.At(i), Color.HSVToRGB(i / (float)EquipmentSlots.Count, .55f, .95f));
                    var visual = Cube(player.transform, "Attachment_" + EquipmentSlots.At(i), positions[i], new Vector3(.25f, .22f, .22f), material, false);
                    visual.SetActive(controller.Player.Loadout.Find(EquipmentSlots.At(i))?.Part != null);
                    controller.AttachmentVisuals[i] = visual;
                }

                var gun = new GameObject("PlayerLauncher"); gun.transform.SetParent(player.transform, false);
                controller.Launcher = gun.AddComponent<LauncherHost>();
                controller.LauncherWorkbench.Host = controller.Launcher;
                if (!controller.Launcher.TryEquip(launcherRecipe.CreateLoadout()))
                    throw new InvalidOperationException("Initial player launcher is invalid.");
                var muzzle = new GameObject("Muzzle"); muzzle.transform.SetParent(gun.transform, false);
                muzzle.transform.localPosition = new Vector3(0, 1, .65f); controller.PlayerMuzzle = muzzle.transform;
                var barrel = Cube(muzzle.transform, "Barrel", new Vector3(0, 1, .44f), new Vector3(.3f, .28f, .45f), launcher, false);
                barrel.transform.localPosition = new Vector3(0, 0, -.21f);

                controller.Targets = new RangeTarget[3];
                for (int i = 0; i < controller.Targets.Length; i++)
                {
                    float x = (i - 1) * 4;
                    var target = Cube(root.transform, "Target_" + (i + 1), new Vector3(x, 1.5f, 18), new Vector3(2.2f, 3, .6f), targetMaterial);
                    controller.Targets[i] = target.AddComponent<RangeTarget>(); controller.Targets[i].MoveTarget = i != 1;
                    Cube(target.transform, "AimMark", new Vector3(x, 1.5f, 17.67f), new Vector3(.25f, .25f, .03f), lines, false);
                }

                var cannon = new GameObject("TestAttacker"); cannon.transform.SetParent(root.transform, false);
                cannon.transform.position = new Vector3(-6, 0, 6);
                Cube(cannon.transform, "CannonBase", new Vector3(-6, .3f, 6), new Vector3(1.4f, .6f, 1.4f), wall);
                controller.Attacker = cannon.AddComponent<LauncherHost>();
                controller.Threats = threats;
                if (!controller.Attacker.TryEquip(threats[0].CreateLoadout()))
                    throw new InvalidOperationException("Initial attacker is invalid.");
                var turret = new GameObject("Turret"); turret.transform.SetParent(cannon.transform, false);
                turret.transform.localPosition = new Vector3(0, 1, 0);
                turret.transform.LookAt(player.transform.position + Vector3.up);
                var turretBody = Cube(turret.transform, "CannonBody", turret.transform.position, new Vector3(.55f, .55f, .8f), attacker);
                turretBody.transform.localRotation = Quaternion.identity;
                turretBody.transform.localPosition = Vector3.zero;
                var threatMuzzle = new GameObject("Muzzle"); threatMuzzle.transform.SetParent(turret.transform, false);
                threatMuzzle.transform.localPosition = Vector3.forward * .7f; controller.ThreatMuzzle = threatMuzzle.transform;

                var playerPool = new GameObject("PlayerProjectilePool"); playerPool.transform.SetParent(root.transform, false);
                controller.PlayerPool = playerPool.AddComponent<ProjectilePool>(); controller.PlayerPool.ConfigureCapacity(512);
                var threatPool = new GameObject("ThreatProjectilePool"); threatPool.transform.SetParent(root.transform, false);
                controller.ThreatPool = threatPool.AddComponent<ProjectilePool>(); controller.ThreatPool.ConfigureCapacity(128);

                var cameraObject = new GameObject("CombatRangeCamera"); cameraObject.transform.SetParent(root.transform, false);
                cameraObject.tag = "MainCamera"; cameraObject.transform.position = new Vector3(0, 15, -12);
                cameraObject.transform.LookAt(new Vector3(0, 0, 9));
                controller.RangeCamera = cameraObject.AddComponent<Camera>();
                controller.RangeCamera.fieldOfView = 52; controller.RangeCamera.nearClipPlane = .1f;
                controller.RangeCamera.farClipPlane = 120; controller.RangeCamera.rect = new Rect(.28f, 0, .45f, 1);
                controller.RangeCamera.clearFlags = CameraClearFlags.SolidColor;
                controller.RangeCamera.backgroundColor = new Color(.045f, .065f, .095f);
                cameraObject.AddComponent<AudioListener>();
                controller.ItemWorkbench.Launcher = controller.Launcher;
                controller.ItemWorkbench.Equipment = controller.Player;
                controller.ItemWorkbench.Player = controller.Player.transform;
                controller.ItemWorkbench.RangeCamera = controller.RangeCamera;
                BuildWorldItems(root.transform, controller.ItemWorkbench, itemCatalog);
                var lightObject = new GameObject("CombatRangeLight"); lightObject.transform.SetParent(root.transform, false);
                lightObject.transform.rotation = Quaternion.Euler(48, -35, 0);
                var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional;
                light.intensity = 1.3f; light.shadows = LightShadows.Soft;
                RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.45f, .5f, .6f);
                RenderSettings.sun = light;

                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Could not save Combat Range.");
                AssetDatabase.SaveAssets(); Debug.Log("COMBAT_RANGE_BUILT " + ScenePath);
            }
            finally
            {
                if (!Application.isBatchMode) EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
            ValidateRange();
        }

        [MenuItem("CelToon/Combat Range/Open Combat Range")]
        public static void OpenRange()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) BuildRange();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        [MenuItem("CelToon/Combat Range/Validate Combat Range")]
        public static void ValidateRange()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                int missing = 0, controllers = 0, cameras = 0, listeners = 0;
                CombatRangeController controller = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var candidate in root.GetComponentsInChildren<CombatRangeController>(true))
                    { controller = candidate; controllers++; }
                    cameras += root.GetComponentsInChildren<Camera>(true).Length;
                    listeners += root.GetComponentsInChildren<AudioListener>(true).Length;
                    foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    {
                        missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
                        foreach (var script in transform.GetComponents<MonoBehaviour>())
                            if (script != null && (script.GetType().Name == "RangeController" || script.GetType().Name == "StorageController"))
                                throw new InvalidOperationException("Combat Range contains a retired scene controller.");
                    }
                }
                if (missing != 0 || controllers != 1 || cameras != 1 || listeners != 1 || controller == null
                    || controller.Player == null || controller.Motor == null || controller.Launcher == null
                    || controller.PlayerPool == null || controller.PlayerMuzzle == null || controller.Attacker == null
                    || controller.ThreatPool == null || controller.ThreatMuzzle == null || controller.RangeCamera == null
                    || controller.PlayerBody == null || controller.PlayerPool == controller.ThreatPool
                    || controller.PlayerPool.Capacity != 512 || controller.ThreatPool.Capacity != 128
                    || controller.Motor.gameObject != controller.Player.gameObject || !controller.PlayerMuzzle.IsChildOf(controller.Player.transform)
                    || controller.Targets == null || controller.Targets.Length != 3
                    || controller.AttachmentVisuals == null || controller.AttachmentVisuals.Length != EquipmentSlots.Count
                    || controller.Threats == null || controller.Threats.Length != 8)
                    throw new InvalidOperationException("Combat Range has missing scripts, objects or bindings.");
                if (controller.LauncherWorkbench == null || controller.EquipmentWorkbench == null
                    || controller.LauncherWorkbench.Host != controller.Launcher || controller.EquipmentWorkbench.Host != controller.Player
                    || controller.LauncherWorkbench.Catalog == null || controller.EquipmentWorkbench.Catalog == null
                    || controller.LauncherWorkbench.DrawAutomatically || controller.EquipmentWorkbench.DrawAutomatically
                    || !controller.LauncherWorkbench.Embedded || !controller.EquipmentWorkbench.Embedded)
                    throw new InvalidOperationException("Combat Range workbenches are not embedded or bound correctly.");
                if (controller.ItemWorkbench == null || controller.ItemWorkbench.Catalog == null
                    || controller.ItemWorkbench.Launcher != controller.Launcher || controller.ItemWorkbench.Equipment != controller.Player
                    || controller.ItemWorkbench.Player != controller.Player.transform || controller.ItemWorkbench.RangeCamera != controller.RangeCamera
                    || controller.ItemWorkbench.LauncherRecipe == null || controller.ItemWorkbench.EquipmentRecipe == null
                    || controller.ItemWorkbench.WorldViews == null || controller.ItemWorkbench.WorldViews.Length != 9)
                    throw new InvalidOperationException("Combat Range item workbench or physical samples are missing.");
                var worldIds = new HashSet<string>();
                foreach (var view in controller.ItemWorkbench.WorldViews)
                    if (view == null || string.IsNullOrEmpty(view.SpawnId) || !worldIds.Add(view.SpawnId)
                        || view.Visuals == null || view.Visuals.Length == 0 || view.Colliders == null || view.Colliders.Length == 0)
                        throw new InvalidOperationException("Combat Range physical item sample is invalid.");
                foreach (var target in controller.Targets)
                    if (target == null || target.GetComponent<Collider>() == null) throw new InvalidOperationException("Combat Range target missing.");
                foreach (var visual in controller.AttachmentVisuals)
                    if (visual == null || !visual.transform.IsChildOf(controller.Player.transform))
                        throw new InvalidOperationException("Combat Range core visual missing.");
                var launcherCatalog = controller.LauncherWorkbench.Catalog;
                var equipmentCatalog = controller.EquipmentWorkbench.Catalog;
                if (launcherCatalog.Parts == null || launcherCatalog.Parts.Length < 60 || launcherCatalog.Recipes == null || launcherCatalog.Recipes.Length < 12
                    || equipmentCatalog.Parts == null || equipmentCatalog.Parts.Length != 16 || equipmentCatalog.Recipes == null || equipmentCatalog.Recipes.Length != 10)
                    throw new InvalidOperationException("Combat Range catalogs are incomplete.");
                var ids = new HashSet<string>();
                foreach (var part in launcherCatalog.Parts)
                    if (part == null || !ids.Add(part.PartId)) throw new InvalidOperationException("Duplicate or missing launcher part.");
                ids.Clear();
                foreach (var recipe in launcherCatalog.Recipes)
                    if (recipe == null || !ids.Add(recipe.RecipeId) || !LauncherCompiler.Compile(recipe.CreateLoadout()).Success)
                        throw new InvalidOperationException("Invalid launcher recipe.");
                ids.Clear();
                foreach (var part in equipmentCatalog.Parts)
                    if (part == null || !ids.Add(part.PartId)
                        || !EquipmentCompiler.Compile(equipmentCatalog.Recipes[0].CreateLoadout().WithPart(part.Kind, part)).Success)
                        throw new InvalidOperationException("Invalid equipment part.");
                ids.Clear();
                foreach (var recipe in equipmentCatalog.Recipes)
                    if (recipe == null || !ids.Add(recipe.RecipeId) || !EquipmentCompiler.Compile(recipe.CreateLoadout()).Success)
                        throw new InvalidOperationException("Invalid equipment recipe.");
                foreach (var threat in controller.Threats)
                    if (threat == null || !LauncherCompiler.Compile(threat.CreateLoadout()).Success)
                        throw new InvalidOperationException("Invalid threat recipe.");
                if (!LauncherCompiler.Compile(controller.Launcher.Loadout).Success || !EquipmentCompiler.Compile(controller.Player.Loadout).Success
                    || !LauncherCompiler.Compile(controller.Attacker.Loadout).Success)
                    throw new InvalidOperationException("Invalid saved loadout.");
                Debug.Log($"COMBAT_RANGE_VALIDATED missingScripts=0 targets=3 launcherParts={launcherCatalog.Parts.Length} equipmentParts={equipmentCatalog.Parts.Length} threats=8 cameras=1");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        private static Material MaterialAsset(string name, Color color)
        {
            string path = ResourcePath + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path); if (existing != null) return existing;
            var shader = Shader.Find(GraphicsSettings.currentRenderPipeline != null ? "Universal Render Pipeline/Lit" : "Standard");
            if (shader == null) throw new InvalidOperationException("Missing supported scene shader.");
            var material = new Material(shader) { name = name, color = color };
            AssetDatabase.CreateAsset(material, path); return material;
        }
        private static void BuildWorldItems(Transform parent, ItemRangeWorkbench workbench, ItemSpecificationCatalog catalog)
        {
            var factory = new ItemFactory(catalog);
            workbench.WorldViews = new WorldItemView[catalog.WorldSamples.Length];
            for (int i = 0; i < catalog.WorldSamples.Length; i++) {
                var sample = catalog.WorldSamples[i];
                var preview = factory.Create(sample.SpecificationId);
                var color = ItemRangeWorkbench.RarityColor(preview.Rarity);
                var material = MaterialAsset("Item_" + preview.Rarity, color);
                var holder = new GameObject("WorldItem_" + sample.SpawnId);
                holder.transform.SetParent(parent, false); holder.transform.position = sample.Position;
                var visual = Cube(holder.transform, "ItemCase", sample.Position + Vector3.up * .35f,
                    new Vector3(.42f, .48f, .34f), material);
                // Selection/pickup volumes should not intercept range shots or block the character motor.
                visual.GetComponent<Collider>().isTrigger = true;
                var band = Cube(holder.transform, "RarityBand", sample.Position + Vector3.up * .62f,
                    new Vector3(.52f, .06f, .44f), material, false);
                var pad = Cube(holder.transform, "PickupPad", sample.Position + Vector3.up * .03f,
                    new Vector3(.68f, .055f, .68f), material, false);
                var view = holder.AddComponent<WorldItemView>(); view.SpawnId = sample.SpawnId;
                view.Visuals = new[] { visual.GetComponent<Renderer>(), band.GetComponent<Renderer>(), pad.GetComponent<Renderer>() };
                view.Colliders = new[] { visual.GetComponent<Collider>() };
                workbench.WorldViews[i] = view;
            }
        }
        private static GameObject Cube(Transform parent, string name, Vector3 position, Vector3 scale, Material material, bool collider = true)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name = name; cube.transform.SetParent(parent, true);
            cube.transform.position = position; cube.transform.localScale = scale; cube.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider) UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>()); return cube;
        }
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
