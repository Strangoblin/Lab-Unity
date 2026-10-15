#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class NormalSmootherWindow : EditorWindow
{
    private struct MeshSourceEntry
    {
        public Object owner;
        public Mesh mesh;
        public Component meshComponent;
        public bool isSceneObject;
    }

    private readonly List<Object> selectionItems = new List<Object>();
    private int selectedPreviewIndex = 0;
    private Dictionary<Mesh, Mesh> previewSmoothedCache = new Dictionary<Mesh, Mesh>();
    private PreviewRenderUtility previewUtility;
    private Material previewMaterial;
    private float previewNormalScale = 0.02f;
    private Vector2 scrollPosition;
    private float angleThreshold = 60f;
    private float positionEpsilon = 0.0001f;
    private NormalSmoother.StorageMode storageMode = NormalSmoother.StorageMode.VertexColors;
    private bool includeChildren = true;
    private bool includeInactive = false;
    private bool applyToSceneObjects = false;
    private bool overwriteExisting = false;
    private string outputFolder = "Assets/NormalSmootherOutput";

    [MenuItem("Tools/Normal Smoother...")]
    public static void ShowWindow()
    {
        NormalSmootherWindow window = GetWindow<NormalSmootherWindow>(false, "Normal Smoother", true);
        window.minSize = new Vector2(420f, 420f);
        window.Show();
    }

    private void OnEnable()
    {
        if (selectionItems.Count == 0)
        {
            CollectSelection();
        }
        EnsurePreviewUtility();
    }

    private void OnDisable()
    {
        CleanupPreviewUtility();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Normal Smoother", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Select FBX / prefab assets or scene objects, then batch-generate smoothed normal data. Vertex Color is the default storage mode.", MessageType.Info);

        EditorGUILayout.Space();
        DrawSettingsGUI();

        EditorGUILayout.Space();
        DrawOutputGUI();

        EditorGUILayout.Space();
        DrawSelectionGUI();

        EditorGUILayout.Space();
        DrawActionGUI();
    }

    private void DrawSettingsGUI()
    {
        EditorGUILayout.LabelField("Smoothing Settings", EditorStyles.boldLabel);
        angleThreshold = EditorGUILayout.Slider("Angle Threshold", angleThreshold, 0f, 180f);
        positionEpsilon = EditorGUILayout.FloatField("Position Epsilon", Mathf.Max(1e-6f, positionEpsilon));
        storageMode = (NormalSmoother.StorageMode)EditorGUILayout.EnumPopup("Storage Mode", storageMode);
        includeChildren = EditorGUILayout.Toggle("Include Children", includeChildren);
        includeInactive = EditorGUILayout.Toggle("Include Inactive", includeInactive);
        applyToSceneObjects = EditorGUILayout.Toggle("Apply Back To Scene", applyToSceneObjects);
        overwriteExisting = EditorGUILayout.Toggle("Overwrite Existing Assets", overwriteExisting);
    }

    private void DrawOutputGUI()
    {
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        outputFolder = EditorGUILayout.TextField("Asset Folder", outputFolder);

        if (GUILayout.Button("Browse...", GUILayout.MaxWidth(90f)))
        {
            string selected = EditorUtility.OpenFolderPanel("Choose Output Folder", Application.dataPath, string.Empty);
            string projectRelative = ConvertToProjectRelativePath(selected);
            if (!string.IsNullOrEmpty(projectRelative))
            {
                outputFolder = projectRelative;
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawSelectionGUI()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Use Current Selection"))
        {
            CollectSelection();
        }

        if (GUILayout.Button("Clear"))
        {
            selectionItems.Clear();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.LabelField("Targets", EditorStyles.boldLabel);
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.MinHeight(120f));

        for (int i = 0; i < selectionItems.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            bool isSelected = (i == selectedPreviewIndex);
            GUIStyle btnStyle = isSelected ? EditorStyles.helpBox : GUIStyle.none;
            EditorGUILayout.BeginVertical(btnStyle);
            selectionItems[i] = EditorGUILayout.ObjectField(selectionItems[i], typeof(Object), true);
            EditorGUILayout.EndVertical();

            if (GUILayout.Button("X", GUILayout.MaxWidth(24f)))
            {
                selectionItems.RemoveAt(i);
                if (selectedPreviewIndex >= selectionItems.Count) selectedPreviewIndex = Mathf.Max(0, selectionItems.Count - 1);
                i--;
            }

            if (GUILayout.Button("Preview", GUILayout.MaxWidth(64f)))
            {
                selectedPreviewIndex = i;
                UpdatePreviewForSelected();
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();

        // Preview area
        Rect previewRect = GUILayoutUtility.GetRect(200, 200, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(previewRect, new Color(0.12f, 0.12f, 0.12f));
        if (selectionItems.Count > 0 && selectedPreviewIndex >= 0 && selectedPreviewIndex < selectionItems.Count)
        {
            RenderPreview(previewRect);
        }
    }

    private void DrawActionGUI()
    {
        using (new EditorGUI.DisabledScope(selectionItems.Count == 0))
        {
            if (GUILayout.Button("Process Selection", GUILayout.Height(32f)))
            {
                ProcessSelection();
            }
        }
    }

    private void CollectSelection()
    {
        foreach (Object obj in Selection.objects)
        {
            AddUniqueSelection(obj);
        }
    }

    private void AddUniqueSelection(Object obj)
    {
        if (obj == null)
        {
            return;
        }

        for (int i = 0; i < selectionItems.Count; i++)
        {
            if (selectionItems[i] == obj)
            {
                return;
            }
        }

        selectionItems.Add(obj);
    }

    private void ProcessSelection()
    {
        if (!ValidateOutputFolder())
        {
            return;
        }

        NormalSmoother.Settings settings = new NormalSmoother.Settings
        {
            angleThreshold = angleThreshold,
            positionEpsilon = positionEpsilon,
            storageMode = storageMode
        };

        Dictionary<Mesh, Mesh> meshCache = new Dictionary<Mesh, Mesh>();
        int processedCount = 0;
        int failedCount = 0;

        try
        {
            for (int targetIndex = 0; targetIndex < selectionItems.Count; targetIndex++)
            {
                Object target = selectionItems[targetIndex];
                float progress = selectionItems.Count == 0 ? 1f : (float)targetIndex / selectionItems.Count;
                EditorUtility.DisplayProgressBar("Normal Smoother", target != null ? target.name : "Unknown", progress);

                List<MeshSourceEntry> sources = CollectSourcesFromTarget(target);
                if (sources.Count == 0)
                {
                    failedCount++;
                    Debug.LogWarning($"Normal Smoother: no mesh sources found on '{target?.name ?? "<null>"}'.");
                    continue;
                }

                for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
                {
                    MeshSourceEntry source = sources[sourceIndex];
                    if (source.mesh == null)
                    {
                        failedCount++;
                        continue;
                    }

                    if (!meshCache.TryGetValue(source.mesh, out Mesh smoothedMesh) || smoothedMesh == null)
                    {
                        smoothedMesh = NormalSmoother.CreateSmoothedMesh(source.mesh, settings);
                        meshCache[source.mesh] = smoothedMesh;
                    }

                    if (smoothedMesh == null)
                    {
                        failedCount++;
                        continue;
                    }

                    string assetPath = BuildOutputPath(source, smoothedMesh);
                    if (string.IsNullOrEmpty(assetPath))
                    {
                        failedCount++;
                        continue;
                    }

                    SaveMeshAsset(smoothedMesh, assetPath);
                    processedCount++;

                    if (applyToSceneObjects && source.isSceneObject && source.meshComponent != null)
                    {
                        ApplyMeshToSceneComponent(source.meshComponent, smoothedMesh);
                    }
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Debug.Log($"Normal Smoother finished. Success: {processedCount}, Failed: {failedCount}");
    }

    private List<MeshSourceEntry> CollectSourcesFromTarget(Object target)
    {
        List<MeshSourceEntry> sources = new List<MeshSourceEntry>();

        if (target == null)
        {
            return sources;
        }

        if (target is Mesh mesh)
        {
            sources.Add(new MeshSourceEntry
            {
                owner = target,
                mesh = mesh,
                meshComponent = null,
                isSceneObject = false
            });
            return sources;
        }

        if (target is MeshFilter meshFilter)
        {
            AppendMeshFilter(sources, meshFilter);
            return sources;
        }

        if (target is SkinnedMeshRenderer skinnedMeshRenderer)
        {
            AppendSkinnedMeshRenderer(sources, skinnedMeshRenderer);
            return sources;
        }

        if (target is GameObject gameObject)
        {
            if (includeChildren)
            {
                MeshFilter[] meshFilters = gameObject.GetComponentsInChildren<MeshFilter>(includeInactive);
                for (int i = 0; i < meshFilters.Length; i++)
                {
                    AppendMeshFilter(sources, meshFilters[i]);
                }

                SkinnedMeshRenderer[] skinnedMeshRenderers = gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive);
                for (int i = 0; i < skinnedMeshRenderers.Length; i++)
                {
                    AppendSkinnedMeshRenderer(sources, skinnedMeshRenderers[i]);
                }
            }
            else
            {
                AppendMeshFilter(sources, gameObject.GetComponent<MeshFilter>());
                AppendSkinnedMeshRenderer(sources, gameObject.GetComponent<SkinnedMeshRenderer>());
            }
        }

        return sources;
    }

    private void AppendMeshFilter(List<MeshSourceEntry> sources, MeshFilter meshFilter)
    {
        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            return;
        }

        sources.Add(new MeshSourceEntry
        {
            owner = meshFilter.gameObject,
            mesh = meshFilter.sharedMesh,
            meshComponent = meshFilter,
            isSceneObject = meshFilter.gameObject.scene.IsValid()
        });
    }

    private void AppendSkinnedMeshRenderer(List<MeshSourceEntry> sources, SkinnedMeshRenderer skinnedMeshRenderer)
    {
        if (skinnedMeshRenderer == null || skinnedMeshRenderer.sharedMesh == null)
        {
            return;
        }

        sources.Add(new MeshSourceEntry
        {
            owner = skinnedMeshRenderer.gameObject,
            mesh = skinnedMeshRenderer.sharedMesh,
            meshComponent = skinnedMeshRenderer,
            isSceneObject = skinnedMeshRenderer.gameObject.scene.IsValid()
        });
    }

    private string BuildOutputPath(MeshSourceEntry source, Mesh mesh)
    {
        string baseFolder = GetBaseOutputFolder(source, mesh);
        if (string.IsNullOrEmpty(baseFolder))
        {
            Debug.LogError("Normal Smoother: output folder is invalid.");
            return string.Empty;
        }

        EnsureFolders(baseFolder);

        string assetName = string.IsNullOrEmpty(mesh.name) ? "SmoothedMesh" : mesh.name;
        string suffix = storageMode == NormalSmoother.StorageMode.VertexColors ? "_Color" : "_Normals";
        string outputPath = $"{baseFolder}/{assetName}{suffix}.asset".Replace('\\', '/');

        if (overwriteExisting)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(outputPath) != null)
            {
                AssetDatabase.DeleteAsset(outputPath);
            }
            return outputPath;
        }

        return AssetDatabase.GenerateUniqueAssetPath(outputPath);
    }

    private string GetBaseOutputFolder(MeshSourceEntry source, Mesh mesh)
    {
        if (!string.IsNullOrEmpty(outputFolder))
        {
            string normalized = outputFolder.Replace('\\', '/');
            if (normalized.StartsWith("Assets"))
            {
                return normalized;
            }
        }

        string meshPath = AssetDatabase.GetAssetPath(mesh);
        if (!string.IsNullOrEmpty(meshPath))
        {
            string folder = Path.GetDirectoryName(meshPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(folder))
            {
                return folder;
            }
        }

        if (source.owner != null)
        {
            string ownerPath = AssetDatabase.GetAssetPath(source.owner);
            if (!string.IsNullOrEmpty(ownerPath))
            {
                string folder = Path.GetDirectoryName(ownerPath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(folder))
                {
                    return folder;
                }
            }
        }

        return "Assets/NormalSmootherOutput";
    }

    private bool ValidateOutputFolder()
    {
        if (string.IsNullOrEmpty(outputFolder))
        {
            outputFolder = "Assets/NormalSmootherOutput";
        }

        outputFolder = outputFolder.Replace('\\', '/');
        if (!outputFolder.StartsWith("Assets"))
        {
            Debug.LogError("Normal Smoother: output folder must be inside the project and start with 'Assets'.");
            return false;
        }

        EnsureFolders(outputFolder);
        return true;
    }

    private static void SaveMeshAsset(Mesh mesh, string assetPath)
    {
        Mesh meshCopy = Object.Instantiate(mesh);
        meshCopy.name = Path.GetFileNameWithoutExtension(assetPath);
        AssetDatabase.CreateAsset(meshCopy, assetPath);
    }

    private static void ApplyMeshToSceneComponent(Component component, Mesh mesh)
    {
        if (component is MeshFilter meshFilter)
        {
            meshFilter.sharedMesh = mesh;
            EditorUtility.SetDirty(meshFilter);
            PrefabUtility.RecordPrefabInstancePropertyModifications(meshFilter);
            return;
        }

        if (component is SkinnedMeshRenderer skinnedMeshRenderer)
        {
            skinnedMeshRenderer.sharedMesh = mesh;
            EditorUtility.SetDirty(skinnedMeshRenderer);
            PrefabUtility.RecordPrefabInstancePropertyModifications(skinnedMeshRenderer);
        }
    }

    private static string ConvertToProjectRelativePath(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath))
        {
            return string.Empty;
        }

        absolutePath = absolutePath.Replace('\\', '/');
        string dataPath = Application.dataPath.Replace('\\', '/');
        if (absolutePath.StartsWith(dataPath))
        {
            return "Assets" + absolutePath.Substring(dataPath.Length);
        }

        return string.Empty;
    }

    private static void EnsureFolders(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
        {
            return;
        }

        fullPath = fullPath.Replace('\\', '/');
        if (!fullPath.StartsWith("Assets"))
        {
            return;
        }

        string[] parts = fullPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = parts[i];
            if (string.IsNullOrEmpty(next))
            {
                continue;
            }

            string combined = current + "/" + next;
            if (!AssetDatabase.IsValidFolder(combined))
            {
                AssetDatabase.CreateFolder(current, next);
            }

            current = combined;
        }
    }

    private void EnsurePreviewUtility()
    {
        if (previewUtility == null)
        {
            previewUtility = new PreviewRenderUtility(true);
            previewUtility.cameraFieldOfView = 30f;
        }

        if (previewMaterial == null)
        {
            Shader s = Shader.Find("Hidden/Internal-Colored");
            if (s == null) s = Shader.Find("Standard");
            previewMaterial = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
        }
    }

    private void CleanupPreviewUtility()
    {
        if (previewUtility != null)
        {
            previewUtility.Cleanup();
            previewUtility = null;
        }

        if (previewMaterial != null)
        {
            DestroyImmediate(previewMaterial);
            previewMaterial = null;
        }
    }

    private void UpdatePreviewForSelected()
    {
        if (selectionItems == null || selectionItems.Count == 0) return;
        if (selectedPreviewIndex < 0 || selectedPreviewIndex >= selectionItems.Count) return;

        Object sel = selectionItems[selectedPreviewIndex];
        Mesh mesh = null;
        if (sel is Mesh m) mesh = m;
        else if (sel is GameObject go)
        {
            MeshFilter mf = go.GetComponent<MeshFilter>();
            if (mf != null) mesh = mf.sharedMesh;
            else
            {
                SkinnedMeshRenderer smr = go.GetComponent<SkinnedMeshRenderer>();
                if (smr != null) mesh = smr.sharedMesh;
            }
        }

        if (mesh == null) return;

        if (!previewSmoothedCache.TryGetValue(mesh, out Mesh smoothed))
        {
            NormalSmoother.Settings s = new NormalSmoother.Settings
            {
                angleThreshold = angleThreshold,
                positionEpsilon = positionEpsilon,
                storageMode = NormalSmoother.StorageMode.VertexColors
            };
            smoothed = NormalSmoother.CreateSmoothedMesh(mesh, s);
            if (smoothed != null) previewSmoothedCache[mesh] = smoothed;
        }
    }

    private void RenderPreview(Rect rect)
    {
        if (previewUtility == null) EnsurePreviewUtility();
        if (selectionItems == null || selectionItems.Count == 0) return;
        if (selectedPreviewIndex < 0 || selectedPreviewIndex >= selectionItems.Count) return;

        Object sel = selectionItems[selectedPreviewIndex];
        Mesh mesh = null;
        if (sel is Mesh m) mesh = m;
        else if (sel is GameObject go)
        {
            MeshFilter mf = go.GetComponent<MeshFilter>();
            if (mf != null) mesh = mf.sharedMesh;
            else
            {
                SkinnedMeshRenderer smr = go.GetComponent<SkinnedMeshRenderer>();
                if (smr != null) mesh = smr.sharedMesh;
            }
        }

        if (mesh == null) return;

        Mesh previewMesh = null;
        previewSmoothedCache.TryGetValue(mesh, out previewMesh);
        if (previewMesh == null)
        {
            UpdatePreviewForSelected();
            previewSmoothedCache.TryGetValue(mesh, out previewMesh);
            if (previewMesh == null) return;
        }

        Bounds b = previewMesh.bounds;
        if (b.size.sqrMagnitude < 1e-8f) b = new Bounds(Vector3.zero, Vector3.one);

        previewUtility.BeginPreview(rect, GUIStyle.none);

        // setup camera
        float dist = b.extents.magnitude * 2.0f;
        previewUtility.camera.transform.position = b.center + new Vector3(0, 0, -dist);
        previewUtility.camera.transform.rotation = Quaternion.LookRotation(b.center - previewUtility.camera.transform.position, Vector3.up);
        previewUtility.camera.farClipPlane = 1000f;
        previewUtility.lights[0].intensity = 1.0f;
        previewUtility.lights[0].transform.rotation = Quaternion.Euler(30f, 30f, 0f);
        previewUtility.lights[1].intensity = 1.0f;

        // draw mesh centered
        Matrix4x4 trs = Matrix4x4.TRS(-b.center, Quaternion.identity, Vector3.one);
        if (previewMaterial != null)
        {
            previewUtility.DrawMesh(previewMesh, trs, previewMaterial, 0);
        }

        previewUtility.Render(true);
        Texture tex = previewUtility.EndPreview();
        if (tex != null)
        {
            GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit, false);
        }
    }
}
#endif