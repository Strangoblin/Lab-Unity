using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mine.GizmoRendering
{
    /// <summary>
    /// One user-defined group of scene gizmos. It carries the display properties that used to be
    /// split across a channel enum and a global palette, plus the live array of sources that
    /// currently belong to it.
    /// </summary>
    /// <remarks>
    /// The array is a roster, not the membership key: a source joins a member by naming it, because
    /// the visualized data changes while the editor runs — sources are enabled, disabled, added and
    /// removed, so a serialized reference list would go stale. Editing a member's properties applies
    /// to everything in its array at once; two visualizations that need different colors or display
    /// scenarios become two members.
    /// </remarks>
    [Serializable]
    public sealed class GizmoMember
    {
        [SerializeField] private string _label = "New Member";
        [SerializeField] private bool _enabled = true;
        [SerializeField] private GizmoPresence _presence = GizmoPresence.OnSelected;
        [SerializeField] private Color _color = new Color(0.2f, 0.8f, 1f, 0.85f);

        private readonly List<IGizmoSource> _sources = new List<IGizmoSource>();

        /// <summary>Membership key. Sources name this label to join; renaming orphans them.</summary>
        public string Label
        {
            get => _label;
            internal set => _label = GizmoMemberSet.NormalizeLabel(value);
        }

        /// <summary>Family switch. A disabled member draws nothing, whatever the global policy says.</summary>
        public bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        /// <summary>Display scenario for every source in this member.</summary>
        public GizmoPresence Presence
        {
            get => _presence;
            set => _presence = value;
        }

        /// <summary>Base color for this member. Subordinate parts derive from it through <see cref="GizmoTint"/>.</summary>
        public Color Color
        {
            get => _color;
            set => _color = value;
        }

        /// <summary>Live roster of sources that named this member.</summary>
        public IReadOnlyList<IGizmoSource> Sources
        {
            get
            {
                Prune();
                return _sources;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  Roster — maintained by the manager window scan and by draw-time attachment
        // ════════════════════════════════════════════════════════════

        internal void Attach(IGizmoSource source)
        {
            Prune();
            if (!IsAlive(source) || !Matches(source.Member)) return;
            if (_sources.Contains(source)) return;
            _sources.Add(source);
        }

        internal void Prune()
        {
            for (int index = _sources.Count - 1; index >= 0; index--)
            {
                IGizmoSource source = _sources[index];
                if (!IsAlive(source) || !Matches(source.Member)) _sources.RemoveAt(index);
            }
        }

        internal static bool IsAlive(IGizmoSource source)
        {
            return source != null && (!(source is UnityEngine.Object target) || target != null);
        }

        internal void ClearRoster()
        {
            _sources.Clear();
        }

        internal bool Matches(string label)
        {
            return string.Equals(_label, GizmoMemberSet.NormalizeLabel(label), StringComparison.Ordinal);
        }
    }
}
