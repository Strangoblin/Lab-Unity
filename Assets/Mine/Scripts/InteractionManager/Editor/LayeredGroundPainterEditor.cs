// ═══════════════════════════════════════════════════════════════
//  LayeredGroundPainterEditor — Scene 视图里的地表笔刷
//
//  笔刷的层列表直接读 LayeredGroundSurface 的层数组：添加一层，笔刷里
//  立刻多一个可选层；删一层，列表同步收缩。每层一组 BRDF 参数在这里
//  编辑，权重数据由 Surface 自动扩/缩通道。
//
//  一次落笔 = 一次普通 alpha 合成：
//    weights = lerp(weights, oneHot(layer), alpha)
//  后刷的层自然盖在先刷的层上面，没有高度或顺序状态。
// ═══════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Mine.Interaction.Editor
{
    [CustomEditor(typeof(LayeredGroundSurface))]
    public sealed class LayeredGroundPainterEditor : UnityEditor.Editor
    {
        private bool _painting;
        private int _layerIndex;
        private float _radius = 1.5f;
        private float _alpha = 0.35f;

        private bool _strokeActive;
        private int _strokeControl;
        private int _strokeGroup;
        private bool _strokeRecorded;

        // 拖动事件可以是每帧好几条；这里只记下最新位置，每帧统一落一次笔。
        private bool _pending;
        private Vector3 _pendingPoint;
        private bool _hasLastPoint;
        private Vector3 _lastPoint;

        private Mesh _cachedMesh;
        private Vector3[] _vertices;
        private int[] _triangles;

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.update += FlushPending;
            // 默认选最上层：第 0 层是底色，拿它落笔看不出任何变化。
            if (target is LayeredGroundSurface surface && surface.Layers.Count > 0)
                _layerIndex = surface.Layers.Count - 1;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.update -= FlushPending;
            EndStroke();
            _cachedMesh = null;
            _vertices = null;
            _triangles = null;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("_sourceMaterial"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_mesh"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_size"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_surfaceLayer"));

            EditorGUILayout.Space();
            DrawLayerList();

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            DrawPainter();

            EditorGUILayout.HelpBox(
                "左键在场景里拖动落笔；Alt 保留场景导航。\n" +
                "每笔把当前地表往选中的层 lerp 一次：Alpha=1 完全覆盖，较小的 Alpha 叠出半透明过渡。",
                MessageType.Info);
        }

        /// <summary>
        /// 层数组就是地形层的定义。增删会立刻改变笔刷的可选层，并把笔画数据的
        /// 通道数一并扩/缩（Surface.OnValidate → SyncLayers）。
        /// </summary>
        private void DrawLayerList()
        {
            SerializedProperty layers = serializedObject.FindProperty("_layers");
            int count = layers.arraySize;

            EditorGUILayout.LabelField($"Terrain Layers ({count}/{LayeredGroundSurface.MaxLayers})",
                EditorStyles.boldLabel);

            int removeAt = -1;
            for (int i = 0; i < count; i++)
            {
                SerializedProperty layer = layers.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Layer {i}", EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(count <= 1))
                {
                    if (GUILayout.Button("移除", GUILayout.Width(48f)))
                        removeAt = i;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.PropertyField(layer.FindPropertyRelative("Name"));
                EditorGUILayout.PropertyField(layer.FindPropertyRelative("BaseColor"));
                EditorGUILayout.PropertyField(layer.FindPropertyRelative("ShadowTint"));
                EditorGUILayout.PropertyField(layer.FindPropertyRelative("Smoothness"));
                EditorGUILayout.PropertyField(layer.FindPropertyRelative("Metallic"));

                // 交互绑定就是这一层的一个参数：选一路交互场，再调它在这层上的表现。
                SerializedProperty interaction = layer.FindPropertyRelative("Interaction");
                EditorGUILayout.PropertyField(interaction);
                if ((LayeredGroundInteractionSource)interaction.intValue != LayeredGroundInteractionSource.None)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(layer.FindPropertyRelative("ImprintDepth"));
                    EditorGUILayout.PropertyField(layer.FindPropertyRelative("NormalStrength"));
                    EditorGUILayout.PropertyField(layer.FindPropertyRelative("Darkening"));
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.EndVertical();
            }

            if (removeAt >= 0)
            {
                layers.DeleteArrayElementAtIndex(removeAt);
                return;
            }

            using (new EditorGUI.DisabledScope(count >= LayeredGroundSurface.MaxLayers))
            {
                if (GUILayout.Button("添加地形层"))
                {
                    int index = layers.arraySize;
                    layers.InsertArrayElementAtIndex(index);
                    SerializedProperty layer = layers.GetArrayElementAtIndex(index);
                    layer.FindPropertyRelative("Name").stringValue = $"Layer {index}";
                    layer.FindPropertyRelative("BaseColor").colorValue = Color.white;
                    layer.FindPropertyRelative("ShadowTint").colorValue = new Color(0.5f, 0.5f, 0.5f, 1f);
                    layer.FindPropertyRelative("Smoothness").floatValue = 0.1f;
                    layer.FindPropertyRelative("Metallic").floatValue = 0f;
                    layer.FindPropertyRelative("Interaction").intValue = 0;
                    layer.FindPropertyRelative("ImprintDepth").floatValue = 0.22f;
                    layer.FindPropertyRelative("NormalStrength").floatValue = 1f;
                    layer.FindPropertyRelative("Darkening").floatValue = 0.18f;
                }
            }
        }

        private void DrawPainter()
        {
            var surface = (LayeredGroundSurface)target;
            IReadOnlyList<LayeredGroundLayerSettings> layers = surface.Layers;
            if (layers == null || layers.Count == 0)
                return;

            EditorGUILayout.LabelField("Layered Ground Painter", EditorStyles.boldLabel);

            bool painting = EditorGUILayout.Toggle("Paint in Scene", _painting);
            if (_painting != painting)
            {
                _painting = painting;
                if (!_painting)
                    EndStroke();
                SceneView.RepaintAll();
            }

            if (_layerIndex >= layers.Count)
                _layerIndex = layers.Count - 1;

            using (new EditorGUI.DisabledScope(!_painting))
            {
                var names = new string[layers.Count];
                for (int i = 0; i < layers.Count; i++)
                {
                    LayeredGroundLayerSettings layer = layers[i];
                    names[i] = layer == null || string.IsNullOrEmpty(layer.Name)
                        ? $"Layer {i}"
                        : $"{i} — {layer.Name}";
                }

                _layerIndex = EditorGUILayout.Popup("Layer", _layerIndex, names);
                _radius = EditorGUILayout.Slider("Brush Radius", _radius, 0.1f, 12f);
                _alpha = EditorGUILayout.Slider("Alpha", _alpha, 0f, 1f);
            }
        }

        private void OnSceneGUI()
        {
            if (!_painting || Application.isPlaying)
                return;

            var surface = (LayeredGroundSurface)target;
            if (surface == null || !surface.isActiveAndEnabled || surface.Mesh == null)
                return;

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
            bool hit = TryPick(surface, ray, out Vector3 point);

            if (hit && current.type == EventType.Repaint)
            {
                Handles.color = LayerColor(surface);
                Handles.DrawWireDisc(point, Vector3.up, _radius);
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
                Undo.SetCurrentGroupName("Paint Layered Ground");
                _strokeActive = true;
                _strokeRecorded = false;
                _hasLastPoint = false;
                _strokeControl = control;
                GUIUtility.hotControl = control;
                _pendingPoint = point;
                _pending = true;
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && current.button == 0 && _strokeActive)
            {
                if (hit)
                {
                    _pendingPoint = point;
                    _pending = true;
                }
                current.Use();
            }
            else if (current.type == EventType.MouseUp && current.button == 0 && _strokeActive)
            {
                FlushPending();
                EndStroke();
                current.Use();
            }
        }

        /// <summary>
        /// 每帧最多落一笔。拖动可能在一帧内产生多条 MouseDrag，逐条绘制会把
        /// 同一段距离算上好几遍；这里把它们合并成一段，并按笔刷直径的一半补点，
        /// 快速拖动也不会画成断续的点。
        /// </summary>
        private void FlushPending()
        {
            if (!_pending || !_strokeActive)
                return;
            _pending = false;

            var surface = (LayeredGroundSurface)target;
            if (surface == null || !surface.isActiveAndEnabled)
            {
                EndStroke();
                return;
            }

            bool changed = false;
            if (_hasLastPoint)
            {
                float spacing = Mathf.Max(_radius * 0.5f, 0.01f);
                int steps = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(_lastPoint, _pendingPoint) / spacing), 1, 3);
                for (int i = 1; i <= steps; i++)
                    changed |= Stamp(surface, Vector3.Lerp(_lastPoint, _pendingPoint, i / (float)steps));
            }
            else
            {
                changed |= Stamp(surface, _pendingPoint);
            }

            _lastPoint = _pendingPoint;
            _hasLastPoint = true;

            if (!changed)
                return;
            EditorUtility.SetDirty(surface);
            EditorSceneManager.MarkSceneDirty(surface.gameObject.scene);
            SceneView.RepaintAll();
        }

        private bool Stamp(LayeredGroundSurface surface, Vector3 point)
        {
            if (!_strokeRecorded)
            {
                Undo.RecordObject(surface, "Paint Layered Ground");
                _strokeRecorded = true;
            }
            return surface.Paint(point, _radius, _layerIndex, _alpha);
        }

        private void EndStroke()
        {
            _pending = false;
            _hasLastPoint = false;
            if (!_strokeActive)
                return;
            _strokeActive = false;
            if (Event.current != null && GUIUtility.hotControl == _strokeControl)
                GUIUtility.hotControl = 0;
            Undo.CollapseUndoOperations(_strokeGroup);
        }

        private void OnUndoRedo()
        {
            if (target is LayeredGroundSurface surface && surface != null)
            {
                surface.PaintData.Invalidate();
                surface.Rebuild();
            }
            SceneView.RepaintAll();
        }

        // 网格是静态的（混合不位移顶点），直接对三角形求交即可。
        private bool TryPick(LayeredGroundSurface surface, Ray ray, out Vector3 point)
        {
            point = default;
            Mesh mesh = surface.Mesh;
            if (mesh == null || !mesh.isReadable)
                return false;

            if (_cachedMesh != mesh)
            {
                _cachedMesh = mesh;
                _vertices = mesh.vertices;
                _triangles = mesh.triangles;
            }

            Matrix4x4 worldToLocal = surface.transform.worldToLocalMatrix;
            Vector3 origin = worldToLocal.MultiplyPoint3x4(ray.origin);
            Vector3 direction = worldToLocal.MultiplyVector(ray.direction);

            float nearest = float.PositiveInfinity;
            for (int i = 0; i + 2 < _triangles.Length; i += 3)
            {
                Vector3 a = _vertices[_triangles[i]];
                Vector3 b = _vertices[_triangles[i + 1]];
                Vector3 c = _vertices[_triangles[i + 2]];
                if (!IntersectTriangle(origin, direction, a, b, c, out float distance) || distance >= nearest)
                    continue;
                nearest = distance;
                point = ray.GetPoint(distance);
            }

            return !float.IsPositiveInfinity(nearest);
        }

        private static bool IntersectTriangle(Vector3 origin, Vector3 direction,
            Vector3 a, Vector3 b, Vector3 c, out float distance)
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

        /// <summary>笔刷圆环用当前层的底色，换层时颜色即时改变。</summary>
        private Color LayerColor(LayeredGroundSurface surface)
        {
            IReadOnlyList<LayeredGroundLayerSettings> layers = surface.Layers;
            if (layers == null || layers.Count == 0)
                return new Color(0.45f, 1f, 0.45f, 0.9f);

            int index = Mathf.Clamp(_layerIndex, 0, layers.Count - 1);
            LayeredGroundLayerSettings layer = layers[index];
            Color color = layer?.BaseColor ?? Color.white;
            color.a = 0.9f;
            return color;
        }
    }
}
