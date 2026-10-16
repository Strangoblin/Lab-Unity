using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Mine.GizmoRendering
{
    /// <summary>
    /// Single dispatch point for scene gizmo drawing. Resolves the source's member, applies the
    /// member's enablement and display scenario plus the editor-wide policy, sets the member color,
    /// then hands the shared canvas to the source and restores the gizmo state afterwards.
    /// </summary>
    /// <remarks>
    /// Sources use the two Unity Gizmo callbacks. Always draws in the ordinary pass and
    /// OnSelected in the selection pass; each dispatch restores color, matrix and base color.
    /// </remarks>
    public static class GizmoRender
    {
        /// <summary>Legacy preference key retained so the GizmoRender rename preserves the user override.</summary>
        public const string PolicyKey = "Mine.SceneGizmo.Policy";

        private static readonly GizmoCanvas Canvas = new GizmoCanvas();

        // ════════════════════════════════════════════════════════════
        //  会话级情景 — override on top of every member declaration
        // ════════════════════════════════════════════════════════════

        /// <summary>Editor-wide override; <see cref="GizmoPolicy.Default"/> defers to each member.</summary>
        public static GizmoPolicy Policy
        {
            get
            {
#if UNITY_EDITOR
                return (GizmoPolicy)EditorPrefs.GetInt(PolicyKey, (int)GizmoPolicy.Default);
#else
                return GizmoPolicy.Default;
#endif
            }
            set
            {
#if UNITY_EDITOR
                EditorPrefs.SetInt(PolicyKey, (int)value);
#endif
            }
        }

        /// <summary>Applies the editor-wide override to a member's declared scenario.</summary>
        public static GizmoPresence Resolve(GizmoPresence declared)
        {
            switch (Policy)
            {
                case GizmoPolicy.Hidden: return GizmoPresence.Hidden;
                case GizmoPolicy.OnSelected: return GizmoPresence.OnSelected;
                case GizmoPolicy.Always: return GizmoPresence.Always;
                default: return declared;
            }
        }

        /// <summary>Restores per-member declarations.</summary>
        public static void ResetOverrides()
        {
            Policy = GizmoPolicy.Default;
        }

        /// <summary>Forces every member silent.</summary>
        public static void HideAll()
        {
            Policy = GizmoPolicy.Hidden;
        }

        /// <summary>Makes every member draw regardless of selection.</summary>
        public static void ShowAll()
        {
            Policy = GizmoPolicy.Always;
        }

        // ════════════════════════════════════════════════════════════
        //  成员解析 — which member owns this source
        // ════════════════════════════════════════════════════════════

        /// <summary>Member that owns <paramref name="source"/>, or null outside the editor.</summary>
        public static GizmoMember ResolveMember(IGizmoSource source)
        {
            if (!GizmoMember.IsAlive(source)) return null;
#if UNITY_EDITOR
            GizmoMember member = GizmoMemberSet.ResolveMember(source.Member);
            member.Attach(source);
            return member;
#else
            return null;
#endif
        }

        /// <summary>
        /// Whether <paramref name="source"/> should draw in the current pass.
        /// <paramref name="selected"/> is true when called from the selection-only hook.
        /// </summary>
        public static bool IsVisible(IGizmoSource source, bool selected)
        {
            return IsVisible(ResolveMember(source), selected);
        }

        private static bool IsVisible(GizmoMember member, bool selected)
        {
            if (member == null || !member.Enabled) return false;

            switch (Resolve(member.Presence))
            {
                case GizmoPresence.Always: return !selected;
                case GizmoPresence.OnSelected: return selected;
                default: return false;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  绘制入口 — the only method gizmo sources call
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Draws <paramref name="source"/> if its member and the display policy allow it in this pass.
        /// </summary>
        /// <param name="source">The declaring component.</param>
        /// <param name="selected">Pass true from OnDrawGizmosSelected, false from OnDrawGizmos.</param>
        public static void Draw(IGizmoSource source, bool selected)
        {
            GizmoMember member = ResolveMember(source);
            if (!IsVisible(member, selected)) return;

            Color previousBaseColor = Canvas.BaseColor;
            Color previousColor = Gizmos.color;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            try
            {
                Canvas.BaseColor = member.Color;
                Canvas.Color = member.Color;
                Canvas.Matrix = Matrix4x4.identity;
                source.DrawGizmos(Canvas);
            }
            finally
            {
                Canvas.BaseColor = previousBaseColor;
                Gizmos.color = previousColor;
                Gizmos.matrix = previousMatrix;
            }
        }
    }
}
