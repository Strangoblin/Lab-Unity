using System;
using CelToon.CustomEquipment.Contracts;
using CelToon.CustomEquipment.Definitions;
using CelToon.CustomEquipment.Samples;
using CelToon.CustomLauncher.Definitions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CelToon.CustomEquipment.Editor
{
    // A scene opened before its scripts were imported can lose every serialized component/asset link.
    // Repair missing links in an already open Storage without rebuilding or saving over user edits.
    [InitializeOnLoad]
    public static class StorageBindingRepair
    {
        static StorageBindingRepair()
        {
            EditorApplication.delayCall += RepairLoadedScenes;
            EditorSceneManager.sceneOpened += (scene, mode) => RepairScene(scene);
            EditorApplication.playModeStateChanged += state => {
                if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode) RepairLoadedScenes();
            };
        }
        [MenuItem("CelToon/Custom Equipment/Repair Storage Bindings")]
        public static void RepairLoadedScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++) RepairScene(SceneManager.GetSceneAt(i));
        }
        private static void RepairScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var controller in root.GetComponentsInChildren<StorageController>(true)) Repair(controller);
        }
        public static bool Repair(StorageController controller)
        {
            if (controller == null) return false;
            string before = Snapshot(controller);
            controller.ResolveSceneBindings(); var ui = controller.Workbench;
            if (ui != null)
            {
                if (ui.Catalog == null) ui.Catalog = AssetDatabase.LoadAssetAtPath<EquipmentCatalog>(StorageSceneBuilder.RootPath + "/Samples/Definitions/Catalog.asset");
                if (ui.Catalog?.Parts != null && Array.Exists(ui.Catalog.Parts, part => part != null && EquipmentSlots.Legacy(part.Kind)))
                    ui.Catalog = StorageContentBuilder.Build();
                if (ui.InitialRecipe == null)
                {
                    ui.InitialRecipe = controller.Player?.Loadout?.Recipe;
                    if (ui.InitialRecipe == null && ui.Catalog?.Recipes != null && ui.Catalog.Recipes.Length > 1) ui.InitialRecipe = ui.Catalog.Recipes[1];
                }
                EquipmentCoreMigration.UpgradeRecipe(ui.InitialRecipe);
                if (controller.Player?.Loadout?.Recipe != null) {
                    var old = controller.Player.Loadout;
                    EquipmentCoreMigration.UpgradeRecipe(old.Recipe);
                    var upgraded = EquipmentCoreMigration.UpgradeLoadout(old);
                    if (!ReferenceEquals(old, upgraded) && !controller.Player.TryEquip(upgraded))
                        throw new InvalidOperationException("四核心装配迁移失败：" + string.Join("; ", controller.Player.LastCompilation.Diagnostics));
                }
                if (controller.Player != null && controller.Player.Loadout?.Recipe == null && ui.InitialRecipe != null)
                    controller.Player.TryEquip(ui.InitialRecipe.CreateLoadout());
            }
            if (controller.Threats == null || controller.Threats.Length == 0)
            {
                var names = new[] { "Kinetic", "Heavy", "Burn", "Poison", "Slow", "Stun", "Mark", "FirePoison" };
                controller.Threats = Array.ConvertAll(names, name => AssetDatabase.LoadAssetAtPath<LauncherRecipeDefinition>(
                    StorageSceneBuilder.RootPath + "/Samples/Threats/" + name + "_Recipe.asset"));
            }
            if (controller.Attacker != null && controller.Attacker.Loadout?.Recipe == null && controller.Threats != null
                && controller.ThreatIndex >= 0 && controller.ThreatIndex < controller.Threats.Length && controller.Threats[controller.ThreatIndex] != null)
                controller.Attacker.TryEquip(controller.Threats[controller.ThreatIndex].CreateLoadout());
            bool changed = before != Snapshot(controller);
            if (changed && !Application.isPlaying)
            {
                EditorUtility.SetDirty(controller);
                if (ui != null) EditorUtility.SetDirty(ui);
                if (controller.Player != null) EditorUtility.SetDirty(controller.Player);
                if (controller.Attacker != null) EditorUtility.SetDirty(controller.Attacker);
                EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            }
            if (changed) Debug.Log("CUSTOM_EQUIPMENT_STORAGE_BINDINGS_REPAIRED " + controller.gameObject.scene.path);
            if (Application.isPlaying) controller.TryInitialize();
            return changed;
        }
        private static string Snapshot(StorageController controller) => JsonUtility.ToJson(controller)
            + (controller.Workbench != null ? JsonUtility.ToJson(controller.Workbench) : "")
            + (controller.Player != null ? JsonUtility.ToJson(controller.Player) : "")
            + (controller.Attacker != null ? JsonUtility.ToJson(controller.Attacker) : "");
    }
}
