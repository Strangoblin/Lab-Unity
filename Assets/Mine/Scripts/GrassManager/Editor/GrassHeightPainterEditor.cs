using System.Collections.Generic;
using Mine.Grass;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Mine.Grass.Editor
{
    /// <summary>Scene-view height brush for the selected procedural grass manager.</summary>
    [CustomEditor(typeof(GrassChunkManager))]
    public sealed class GrassHeightPainterEditor : UnityEditor.Editor
    {
        private sealed class MeshGeometry
        {
            public Vector3[] Vertices;
            public int[] Triangles;
        }

        private readonly Dictionary<Mesh, MeshGeometry> _geometry = new Dictionary<Mesh, MeshGeometry>();
        private bool _painting;
        private bool _raise;
        private bool _strokeActive;
        private float _radius = 2f;
        private float _strength = 0.25f;
        private int _strokeGroup;
        private int _strokeControl;
        private int _terrainVersion = -1;
        private GrassTerrainSource _cachedTerrain;

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            EndStroke();
            _geometry.Clear();
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Grass Height Painter", EditorStyles.boldLabel);
            bool painting = EditorGUILayout.Toggle("Paint in Scene", _painting);
            if (_painting != painting)
            {
                _painting = painting;
                if (!_painting)
                    EndStroke();
                SceneView.RepaintAll();
            }
            using (new EditorGUI.DisabledScope(!_painting))
            {
                float minimumRadius = MinimumRadius((GrassChunkManager)target);
                float maximumRadius = Mathf.Max(20f, minimumRadius * 8f);
                _radius = EditorGUILayout.Slider("Brush Radius", Mathf.Clamp(_radius, minimumRadius, maximumRadius),
                    minimumRadius, maximumRadius);
                _strength = EditorGUILayout.Slider("Brush Strength", _strength, 0.01f, 1f);
                _raise = EditorGUILayout.Toggle("Raise Height", _raise);
            }
            EditorGUILayout.HelpBox("Left-click or drag on this terrain to paint. Default grass height is 1; " +
                "the brush lowers height, and Raise Height restores it. Alt preserves Scene navigation.", MessageType.Info);
        }

        private void OnSceneGUI()
        {
            if (!_painting || Application.isPlaying)
                return;

            GrassChunkManager manager = (GrassChunkManager)target;
            GrassTerrainSource terrain = manager.Terrain;
            if (terrain == null || !terrain.isActiveAndEnabled)
                return;
            terrain.Initialize();
            if (!terrain.HasData)
                return;
            _radius = Mathf.Max(_radius, MinimumRadius(manager));

            Event current = Event.current;
            int control = GUIUtility.GetControlID(FocusType.Passive);
            if (current.type == EventType.Layout)
            {
                if (!current.alt)
                    HandleUtility.AddDefaultControl(control);
                return;
            }
            if (current.type == EventType.MouseMove)
            {
                SceneView.RepaintAll();
                return;
            }

            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            bool hit = TryPickTerrain(terrain, ray, out Vector3 point, out Vector3 normal);
            if (hit && current.type == EventType.Repaint)
            {
                Handles.color = _raise ? new Color(0.3f, 1f, 0.3f, 0.9f) : new Color(1f, 0.4f, 0.2f, 0.9f);
                Handles.DrawWireDisc(point, normal, _radius);
            }

            if (current.alt)
            {
                if (_strokeActive)
                    EndStroke();
                return;
            }

            if (current.type == EventType.MouseDown && current.button == 0 && hit)
            {
                Undo.IncrementCurrentGroup();
                _strokeGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Paint Grass Height");
                _strokeActive = true;
                _strokeControl = control;
                GUIUtility.hotControl = control;
                Paint(manager, point);
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && current.button == 0 && _strokeActive)
            {
                if (hit)
                    Paint(manager, point);
                current.Use();
            }
            else if (current.type == EventType.MouseUp && current.button == 0 && _strokeActive)
            {
                EndStroke();
                current.Use();
            }
        }

        private void Paint(GrassChunkManager manager, Vector3 point)
        {
            Undo.RecordObject(manager, "Paint Grass Height");
            if (!manager.PaintHeight(point, _radius, _raise ? _strength : -_strength))
                return;
            EditorUtility.SetDirty(manager);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            SceneView.RepaintAll();
        }

        private void EndStroke()
        {
            if (!_strokeActive)
                return;
            _strokeActive = false;
            if (Event.current != null && GUIUtility.hotControl == _strokeControl)
                GUIUtility.hotControl = 0;
            Undo.CollapseUndoOperations(_strokeGroup);
        }

        private void OnUndoRedo()
        {
            if (target is GrassChunkManager manager && manager != null)
                manager.Rebuild();
            SceneView.RepaintAll();
        }

        private static float MinimumRadius(GrassChunkManager manager)
        {
            return Mathf.Max(0.1f, manager.HeightPaintMinimumBrushRadius);
        }

        // Pick only the selected terrain's readable surface meshes without adding a collider to the scene.
        private bool TryPickTerrain(GrassTerrainSource terrain, Ray ray, out Vector3 point, out Vector3 normal)
        {
            point = default;
            normal = Vector3.up;
            if (_cachedTerrain != terrain || _terrainVersion != terrain.DataVersion)
            {
                _geometry.Clear();
                _cachedTerrain = terrain;
                _terrainVersion = terrain.DataVersion;
            }

            float nearest = float.PositiveInfinity;
            foreach (MeshFilter filter in terrain.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable || !filter.gameObject.activeInHierarchy)
                    continue;
                if (!_geometry.TryGetValue(mesh, out MeshGeometry geometry))
                {
                    geometry = new MeshGeometry { Vertices = mesh.vertices, Triangles = mesh.triangles };
                    _geometry.Add(mesh, geometry);
                }

                Matrix4x4 localToWorld = filter.transform.localToWorldMatrix;
                Matrix4x4 worldToLocal = filter.transform.worldToLocalMatrix;
                Vector3 origin = worldToLocal.MultiplyPoint3x4(ray.origin);
                Vector3 direction = worldToLocal.MultiplyVector(ray.direction);
                for (int i = 0; i + 2 < geometry.Triangles.Length; i += 3)
                {
                    Vector3 a = geometry.Vertices[geometry.Triangles[i]];
                    Vector3 b = geometry.Vertices[geometry.Triangles[i + 1]];
                    Vector3 c = geometry.Vertices[geometry.Triangles[i + 2]];
                    if (!IntersectTriangle(origin, direction, a, b, c, out float distance) || distance >= nearest)
                        continue;
                    nearest = distance;
                    point = ray.GetPoint(distance);
                    normal = Vector3.Cross(localToWorld.MultiplyVector(b - a), localToWorld.MultiplyVector(c - a)).normalized;
                    if (Vector3.Dot(normal, ray.direction) > 0f)
                        normal = -normal;
                }
            }
            return !float.IsPositiveInfinity(nearest);
        }

        private static bool IntersectTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c,
            out float distance)
        {
            distance = 0f;
            Vector3 edge1 = b - a;
            Vector3 edge2 = c - a;
            Vector3 cross = Vector3.Cross(direction, edge2);
            float determinant = Vector3.Dot(edge1, cross);
            if (Mathf.Abs(determinant) < 0.0000001f)
                return false;
            float inverse = 1f / determinant;
            Vector3 fromA = origin - a;
            float u = Vector3.Dot(fromA, cross) * inverse;
            if (u < 0f || u > 1f)
                return false;
            Vector3 q = Vector3.Cross(fromA, edge1);
            float v = Vector3.Dot(direction, q) * inverse;
            if (v < 0f || u + v > 1f)
                return false;
            distance = Vector3.Dot(edge2, q) * inverse;
            return distance >= 0f;
        }
    }
}
