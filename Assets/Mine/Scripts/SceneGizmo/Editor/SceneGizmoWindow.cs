#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mine.SceneGizmo
{
    /// <summary>
    /// Manager window for the scene gizmo framework: one place to add, remove and retune members,
    /// switch the session-wide display policy, and inspect which sources each member currently holds.
    /// </summary>
    /// <remarks>
    /// The window is a GUI shell. Visibility resolution stays in <see cref="SceneGizmos"/> and the
    /// member data lives in the <see cref="GizmoMemberSet"/> asset, so nothing here owns state beyond
    /// the scroll position and which rows are expanded.
    ///
    /// Every field writes behind <see cref="EditorGUI.BeginChangeCheck"/> rather than comparing the
    /// control's return value: a popup or toggle can report a value during a layout or repaint pass
    /// that was never user intent, and writing session state from that corrupts the display policy.
    /// </remarks>
    public sealed class SceneGizmoWindow : EditorWindow
    {
        [SerializeField] private Vector2 _scrollPosition;

        private readonly List<IGizmoSource> _scanned = new List<IGizmoSource>();
        private readonly HashSet<GizmoMember> _expanded = new HashSet<GizmoMember>();

        // ════════════════════════════════════════════════════════════
        //  Menu entry and refresh hooks
        // ════════════════════════════════════════════════════════════

        [MenuItem("Tools/Scene Gizmo/Gizmo Manager")]
        private static void OpenWindow()
        {
            var window = GetWindow<SceneGizmoWindow>(false, "Gizmo Manager", true);
            window.minSize = new Vector2(420f, 480f);
        }

        private void OnEnable()
        {
            GizmoMemberSet.Ensure();
            Rescan();
            EditorApplication.hierarchyChanged += Rescan;
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= Rescan;
        }

        // ════════════════════════════════════════════════════════════
        //  GUI — policy → members → per-member roster
        // ════════════════════════════════════════════════════════════

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Scene Gizmo", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawPolicy();
            EditorGUILayout.Space(8f);
            DrawMembers();

            EditorGUILayout.EndScrollView();
        }

        private void DrawPolicy()
        {
            EditorGUILayout.LabelField("Display Policy", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            EditorGUI.BeginChangeCheck();
            GizmoPolicy policy = (GizmoPolicy)EditorGUILayout.EnumPopup("Global Override", SceneGizmos.Policy);
            if (EditorGUI.EndChangeCheck())
            {
                SceneGizmos.Policy = policy;
                SceneView.RepaintAll();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Hide All", GUILayout.Height(24f)))
            {
                SceneGizmos.HideAll();
                SceneView.RepaintAll();
            }

            if (GUILayout.Button("Show All", GUILayout.Height(24f)))
            {
                SceneGizmos.ShowAll();
                SceneView.RepaintAll();
            }

            if (GUILayout.Button("Reset", GUILayout.Height(24f)))
            {
                SceneGizmos.ResetOverrides();
                SceneView.RepaintAll();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawMembers()
        {
            GizmoMemberSet set = GizmoMemberSet.Ensure();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Members ({set.Members.Count})", EditorStyles.boldLabel);
            if (GUILayout.Button("Add Member", GUILayout.Width(96f)))
            {
                GizmoMember added = set.AddMember($"Member {set.Members.Count + 1}");
                _expanded.Add(added);
                SceneView.RepaintAll();
                Repaint();
            }

            if (GUILayout.Button("Rescan Scenes", GUILayout.Width(110f)))
            {
                Rescan();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginVertical("box");

            GizmoMember removeTarget = null;
            bool changed = false;

            for (int index = 0; index < set.Members.Count; index++)
            {
                GizmoMember member = set.Members[index];
                if (member == null) continue;

                changed |= DrawMemberRow(set, member, index, ref removeTarget);
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.HelpBox(
                "A member owns its display properties and its source array. A source names its member by " +
                "label, so renaming a member leaves the old sources unregistered until they name the new " +
                "label; two visualizations that need different colors belong in two members.",
                MessageType.Info);

            if (removeTarget != null &&
                EditorUtility.DisplayDialog("Remove Member",
                    $"Remove '{removeTarget.Label}'?\nSources naming it fall back to an unregistered color.",
                    "Remove", "Cancel"))
            {
                _expanded.Remove(removeTarget);
                set.RemoveMember(removeTarget);
                changed = true;
            }

            if (changed)
            {
                set.Save();
                SceneView.RepaintAll();
            }
        }

        private bool DrawMemberRow(GizmoMemberSet set, GizmoMember member, int index, ref GizmoMember removeTarget)
        {
            bool changed = false;

            EditorGUILayout.BeginHorizontal();

            bool expanded = _expanded.Contains(member);
            bool nowExpanded = EditorGUILayout.Foldout(expanded, member.Label, true);
            if (nowExpanded != expanded)
            {
                if (nowExpanded) _expanded.Add(member);
                else _expanded.Remove(member);
            }

            EditorGUILayout.LabelField($"({member.Sources.Count})", EditorStyles.miniLabel, GUILayout.Width(30f));

            EditorGUI.BeginDisabledGroup(index == 0);
            if (GUILayout.Button("▲", GUILayout.Width(24f)))
            {
                set.MoveMember(member, -1);
                changed = true;
            }
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(index == set.Members.Count - 1);
            if (GUILayout.Button("▼", GUILayout.Width(24f)))
            {
                set.MoveMember(member, 1);
                changed = true;
            }
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button("✕", GUILayout.Width(24f)))
            {
                removeTarget = member;
            }

            EditorGUILayout.EndHorizontal();

            if (!nowExpanded) return changed;

            EditorGUI.indentLevel++;

            EditorGUI.BeginChangeCheck();
            bool enabled = EditorGUILayout.Toggle("Enabled", member.Enabled);
            string label = EditorGUILayout.TextField("Label", member.Label);
            GizmoPresence presence = (GizmoPresence)EditorGUILayout.EnumPopup("Scenario", member.Presence);
            Color color = EditorGUILayout.ColorField("Color", member.Color);
            if (EditorGUI.EndChangeCheck())
            {
                member.Enabled = enabled;
                member.Label = label;
                member.Presence = presence;
                member.Color = color;
                changed = true;
            }

            DrawRoster(member);

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4f);
            return changed;
        }

        private static void DrawRoster(GizmoMember member)
        {
            EditorGUILayout.LabelField($"Sources ({member.Sources.Count})", EditorStyles.miniBoldLabel);

            if (member.Sources.Count == 0)
            {
                EditorGUILayout.LabelField("empty — nothing named this label in the open scenes",
                    EditorStyles.miniLabel);
                return;
            }

            for (int index = 0; index < member.Sources.Count; index++)
            {
                IGizmoSource source = member.Sources[index];
                if (source == null) continue;

                var target = source as Object;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(target != null ? target.name : "(missing)", GUILayout.MinWidth(90f));
                EditorGUILayout.LabelField(source.Form.ToString(), EditorStyles.miniLabel, GUILayout.Width(70f));

                EditorGUI.BeginDisabledGroup(target == null);
                if (GUILayout.Button("Ping", GUILayout.Width(48f)))
                {
                    EditorGUIUtility.PingObject(target);
                }
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.EndHorizontal();
            }
        }

        // ════════════════════════════════════════════════════════════
        //  Scanning — collect live sources and refresh every roster
        // ════════════════════════════════════════════════════════════

        private void Rescan()
        {
            _scanned.Clear();

            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.isLoaded) continue;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (behaviour is IGizmoSource source)
                        {
                            _scanned.Add(source);
                        }
                    }
                }
            }

            GizmoMemberSet set = GizmoMemberSet.Ensure();
            set.RosterFrom(_scanned);
            set.Save();
            Repaint();
        }
    }
}
#endif
