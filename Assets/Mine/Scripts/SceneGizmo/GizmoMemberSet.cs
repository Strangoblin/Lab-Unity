using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Mine.SceneGizmo
{
    /// <summary>
    /// The editable member list behind the whole framework, stored as a project asset so the
    /// configuration travels with the repository. Members can be added, removed, reordered and
    /// retuned at any time; each one owns its own roster array and display properties.
    /// </summary>
    /// <remarks>
    /// Membership is resolved by label, not by serialized object references, because the visualized
    /// data changes while the editor runs. A label that has no member still draws through a transient
    /// fallback member with the same seeded defaults and a stable label-derived color, so a new source
    /// is never silently invisible; the manager window registers such labels for real on its next scan.
    ///
    /// The implementation is editor-guarded rather than placed in an Editor folder on purpose: the
    /// dispatch entry point lives in the runtime assembly and must be able to resolve members, so this
    /// type has to compile into that assembly. Player builds keep the empty shell and resolve nothing.
    /// </remarks>
    public sealed class GizmoMemberSet : ScriptableObject
    {
        /// <summary>Project-relative path of the member configuration asset.</summary>
        public const string AssetPath = "Assets/Mine/Scripts/SceneGizmo/SceneGizmoMembers.asset";

#if UNITY_EDITOR
        private const string AssetFolder = "Assets/Mine/Scripts/SceneGizmo";

        [SerializeField] private List<GizmoMember> _members = new List<GizmoMember>();

        private static GizmoMemberSet _cached;
        private static readonly Dictionary<string, GizmoMember> Transients = new Dictionary<string, GizmoMember>();

        /// <summary>Editable member list in authoring order.</summary>
        public IReadOnlyList<GizmoMember> Members => _members;

        // ════════════════════════════════════════════════════════════
        //  Loading — cached asset access and the transient fallback
        // ════════════════════════════════════════════════════════════

        /// <summary>Existing asset, or null when it has not been created yet. Never writes.</summary>
        public static GizmoMemberSet Load()
        {
            if (_cached != null) return _cached;
            _cached = AssetDatabase.LoadAssetAtPath<GizmoMemberSet>(AssetPath);
            return _cached;
        }

        /// <summary>Existing asset or a newly created one. Safe from the editor UI, not from a draw pass.</summary>
        public static GizmoMemberSet Ensure()
        {
            GizmoMemberSet set = Load();
            if (set != null) return set;

            if (!AssetDatabase.IsValidFolder(AssetFolder))
            {
                AssetDatabase.CreateFolder("Assets/Mine/Scripts", "SceneGizmo");
            }

            set = CreateInstance<GizmoMemberSet>();
            set.SeedDefaults();
            AssetDatabase.CreateAsset(set, AssetPath);
            AssetDatabase.SaveAssetIfDirty(set);
            _cached = set;
            return set;
        }

        /// <summary>Drops the cached reference so the next access re-reads the asset.</summary>
        public static void Invalidate()
        {
            _cached = null;
        }

        // ════════════════════════════════════════════════════════════
        //  Resolution — the only call the draw pass makes
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Member for <paramref name="label"/>. Unregistered labels resolve to a transient fallback
        /// so drawing keeps working; transient members are never written to the asset.
        /// </summary>
        public static GizmoMember ResolveMember(string label)
        {
            GizmoMemberSet set = Load();
            if (set != null)
            {
                GizmoMember found = set.Find(label);
                if (found != null) return found;
            }

            return TransientFor(label);
        }

        /// <summary>True when the member belongs to the saved asset rather than the transient fallback.</summary>
        public static bool IsPersistent(GizmoMember member)
        {
            GizmoMemberSet set = Load();
            return set != null && member != null && set._members.Contains(member);
        }

        private static GizmoMember TransientFor(string label)
        {
            string key = string.IsNullOrEmpty(label) ? "Unassigned" : label;
            if (Transients.TryGetValue(key, out GizmoMember member)) return member;

            member = BuildMember(key);
            member.Label = key;
            Transients[key] = member;
            return member;
        }

        // ════════════════════════════════════════════════════════════
        //  Editing — add, remove, reorder and retune members
        // ════════════════════════════════════════════════════════════

        /// <summary>Finds a member by label, or null.</summary>
        public GizmoMember Find(string label)
        {
            if (string.IsNullOrEmpty(label)) return null;

            for (int index = 0; index < _members.Count; index++)
            {
                if (_members[index] != null && _members[index].Matches(label)) return _members[index];
            }

            return null;
        }

        /// <summary>Finds a member by label, creating and saving it when absent.</summary>
        public GizmoMember EnsureMember(string label)
        {
            GizmoMember existing = Find(label);
            if (existing != null) return existing;

            return AddMember(label);
        }

        /// <summary>Appends a new member.</summary>
        public GizmoMember AddMember(string label)
        {
            string key = string.IsNullOrEmpty(label) ? $"Member {_members.Count + 1}" : label;
            GizmoMember member = BuildMember(key);
            member.Label = key;
            _members.Add(member);
            Save();
            return member;
        }

        /// <summary>Removes a member. Sources naming it fall back to the transient member.</summary>
        public void RemoveMember(GizmoMember member)
        {
            if (member == null || !_members.Remove(member)) return;
            Save();
        }

        /// <summary>Moves a member by <paramref name="offset"/> positions in the authoring order.</summary>
        public void MoveMember(GizmoMember member, int offset)
        {
            int index = _members.IndexOf(member);
            int target = index + offset;
            if (index < 0 || target < 0 || target >= _members.Count) return;

            _members.RemoveAt(index);
            _members.Insert(target, member);
            Save();
        }

        /// <summary>Rebuilds every roster from a scan, then registers labels that had no member.</summary>
        public void RosterFrom(IEnumerable<IGizmoSource> sources)
        {
            for (int index = 0; index < _members.Count; index++)
            {
                _members[index]?.ClearRoster();
            }

            foreach (IGizmoSource source in sources)
            {
                if (source == null) continue;
                EnsureMember(source.Member).Attach(source);
            }
        }

        /// <summary>Writes pending edits back to the configuration asset.</summary>
        public void Save()
        {
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssetIfDirty(this);
        }

        // ════════════════════════════════════════════════════════════
        //  Defaults — seeded once when the asset is created
        // ════════════════════════════════════════════════════════════

        private void SeedDefaults()
        {
            _members.Clear();
            _members.Add(BuildMember("Wind"));
            _members.Add(BuildMember("Instance"));
            _members.Add(BuildMember("Interior"));
            _members.Add(BuildMember("Terrain"));
        }

        private static GizmoMember BuildMember(string label)
        {
            var member = new GizmoMember { Label = label };

            switch (label)
            {
                case "Wind":
                    member.Color = new Color(0.2f, 0.8f, 1f, 0.85f);
                    member.Presence = GizmoPresence.OnSelected;
                    break;
                case "Instance":
                    member.Color = new Color(0.3f, 0.7f, 1f, 0.8f);
                    member.Presence = GizmoPresence.OnSelected;
                    break;
                case "Interior":
                    member.Color = new Color(1f, 0.75f, 0.15f, 0.95f);
                    member.Presence = GizmoPresence.Always;
                    break;
                case "Terrain":
                    member.Color = new Color(0.6f, 1f, 0.6f, 0.9f);
                    member.Presence = GizmoPresence.OnSelected;
                    break;
                default:
                    member.Color = ColorFor(label);
                    member.Presence = GizmoPresence.OnSelected;
                    break;
            }

            return member;
        }

        /// <summary>Stable color for an arbitrary label; independent of the platform string hash.</summary>
        private static Color ColorFor(string label)
        {
            uint hash = 2166136261u;
            if (!string.IsNullOrEmpty(label))
            {
                for (int index = 0; index < label.Length; index++)
                {
                    hash ^= label[index];
                    hash *= 16777619u;
                }
            }

            return Color.HSVToRGB((hash % 360u) / 360f, 0.7f, 1f);
        }
#endif
    }
}
