namespace Mine.SceneGizmo
{
    /// <summary>
    /// Contract every scene visualization implements. A source names the <see cref="GizmoMember"/>
    /// it belongs to, states which shape family it draws, and paints that shape through the shared
    /// <see cref="GizmoCanvas"/>.
    /// </summary>
    /// <remarks>
    /// Display properties are not declared here on purpose: enablement, display scenario and color
    /// all live on the member, so one edit retunes every source in the group. A source that needs
    /// different properties belongs to a different member.
    ///
    /// Sources never touch <c>Gizmos.color</c> or <c>Gizmos.matrix</c> directly and never decide
    /// their own trigger; <see cref="SceneGizmos.Draw"/> owns both. Subordinate parts of one
    /// visualization derive their tint from <see cref="GizmoCanvas.BaseColor"/> via
    /// <see cref="GizmoTint"/>.
    /// </remarks>
    public interface IGizmoSource
    {
        /// <summary>
        /// Membership key: the label of the <see cref="GizmoMember"/> this source belongs to.
        /// Read and written so the manager window can regroup a source without touching code.
        /// </summary>
        string Member { get; set; }

        /// <summary>Shape family this source draws; shown in the manager window.</summary>
        GizmoForm Form { get; }

        /// <summary>
        /// Paints the visualization into the provided canvas. Called only when the member and the
        /// display policy already agreed the source is visible, so no visibility check belongs here.
        /// </summary>
        /// <param name="canvas">Shared canvas; its color is pre-set to the member color.</param>
        void DrawGizmos(GizmoCanvas canvas);
    }
}
