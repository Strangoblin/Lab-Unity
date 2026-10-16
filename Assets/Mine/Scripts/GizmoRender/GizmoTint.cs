using UnityEngine;

namespace Mine.GizmoRendering
{
    /// <summary>
    /// Derives subordinate colors from a member color, so one member color can still express
    /// a primary shape, its secondary extent and an emphasized face without a second palette.
    /// </summary>
    /// <remarks>
    /// Replaces the former named color tokens. A source that needs two visually different colors
    /// for unrelated meanings — not a main/subordinate pair — belongs in two members instead.
    /// </remarks>
    public static class GizmoTint
    {
        /// <summary>Secondary geometry of the same system: same hue, quieter.</summary>
        public static Color Fade(Color baseColor, float alphaScale = 0.35f)
        {
            baseColor.a *= Mathf.Clamp01(alphaScale);
            return baseColor;
        }

        /// <summary>Terminal or clamped face that should stand out against the base color.</summary>
        public static Color Emphasis(Color baseColor, float lighten = 0.35f, float alphaScale = 1f)
        {
            Color lifted = Color.Lerp(baseColor, Color.white, Mathf.Clamp01(lighten));
            lifted.a = baseColor.a * Mathf.Clamp01(alphaScale);
            return lifted;
        }
    }
}
