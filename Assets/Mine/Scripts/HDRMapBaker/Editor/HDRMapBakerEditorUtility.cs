#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEditor;

namespace Mine.HDRMaps.Editor
{
    /// <summary>Creates or repairs a dedicated HDR capture framework through Unity Undo.</summary>
    public static class HDRMapBakerEditorUtility
    {
        public static HDRMapBaker CreateStandardFramework()
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create HDR Map Baker Framework");
            try
            {
                var root = new GameObject("HDRMapBaker");
                Undo.RegisterCreatedObjectUndo(root, "Create HDR Map Baker Framework");
                HDRMapBaker baker = Undo.AddComponent<HDRMapBaker>(root);
                BindFramework(baker);
                Selection.activeGameObject = root;
                SceneView.RepaintAll();
                return baker;
            }
            finally
            {
                Undo.CollapseUndoOperations(group);
            }
        }

        public static void InitializeFramework(HDRMapBaker baker)
        {
            if (baker == null)
                return;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Initialize HDR Map Baker Framework");
            try
            {
                Undo.RecordObject(baker, "Bind HDR Map Baker Framework");
                BindFramework(baker);
                Selection.activeGameObject = baker.gameObject;
                SceneView.RepaintAll();
            }
            finally
            {
                Undo.CollapseUndoOperations(group);
            }
        }

        // ════════════════════════════════════════════════════════════
        //  Dedicated children — invalid cross-framework references are never adopted
        // ════════════════════════════════════════════════════════════
        private static void BindFramework(HDRMapBaker baker)
        {
            var serialized = new SerializedObject(baker);
            BindChild<HDRMapBakeVolume>(serialized, baker, "_volume", "BakeVolume");
            BindChild<HDRMapBakeDirection>(serialized, baker, "_direction", "BakeDirection");
            Camera camera = BindChild<Camera>(serialized, baker, "_camera", "BakeCamera");
            serialized.ApplyModifiedProperties();

            Undo.RecordObject(camera, "Configure HDR Capture Camera");
            Undo.RecordObject(camera.transform, "Align HDR Capture Camera");
            camera.enabled = false;
            camera.cameraType = CameraType.Reflection;
            camera.tag = "Untagged";
            camera.allowHDR = true;
            camera.allowMSAA = false;
            camera.clearFlags = baker.IncludeSky ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 100f;
            camera.fieldOfView = 90f;
            camera.aspect = 1f;
            if (!camera.TryGetComponent(out UniversalAdditionalCameraData data))
                data = Undo.AddComponent<UniversalAdditionalCameraData>(camera.gameObject);
            Undo.RecordObject(data, "Configure HDR Capture Camera");
            data.renderPostProcessing = false;
            data.allowHDROutput = false;
            baker.SyncBakeCamera();
            EditorUtility.SetDirty(baker);
            EditorUtility.SetDirty(camera);
            EditorUtility.SetDirty(data);
        }

        private static T BindChild<T>(SerializedObject serialized, HDRMapBaker baker, string field, string childName)
            where T : Component
        {
            SerializedProperty property = serialized.FindProperty(field);
            T component = property.objectReferenceValue as T;
            if (baker.Owns(component))
                return component;

            for (int i = 0; i < baker.transform.childCount; i++)
            {
                Transform child = baker.transform.GetChild(i);
                if (child.name == childName && child.TryGetComponent(out component) && baker.Owns(component))
                {
                    property.objectReferenceValue = component;
                    return component;
                }
            }

            var generated = new GameObject(childName);
            Undo.RegisterCreatedObjectUndo(generated, "Create HDR Map Baker Child");
            Undo.SetTransformParent(generated.transform, baker.transform, "Parent HDR Map Baker Child");
            generated.transform.localPosition = Vector3.zero;
            generated.transform.localRotation = Quaternion.identity;
            generated.transform.localScale = Vector3.one;
            component = Undo.AddComponent<T>(generated);
            property.objectReferenceValue = component;
            return component;
        }
    }
}
#endif
