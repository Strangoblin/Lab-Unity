namespace Mine.GizmoRendering
{
    /// <summary>
    /// Display scenario a member declares for its sources: when the scene view should draw it.
    /// This is the per-member half of the display decision; the global half is <see cref="GizmoPolicy"/>.
    /// </summary>
    public enum GizmoPresence
    {
        /// <summary>Never draw. Kept as a serialized state so an item can stay wired but silent.</summary>
        Hidden = 0,

        /// <summary>Draw only while the owning GameObject is selected in the hierarchy.</summary>
        OnSelected = 1,

        /// <summary>Draw while the owning GameObject is active, selected or not.</summary>
        Always = 2
    }

    /// <summary>
    /// Editor-wide display override applied on top of every source's declared <see cref="GizmoPresence"/>.
    /// Lets one switch silence or surface all gizmos without editing each component.
    /// </summary>
    public enum GizmoPolicy
    {
        /// <summary>Respect each source's own <see cref="GizmoPresence"/>.</summary>
        Default = 0,

        /// <summary>Force every source hidden, regardless of its declaration.</summary>
        Hidden = 1,

        /// <summary>Force every source into selection-only display.</summary>
        OnSelected = 2,

        /// <summary>Force every source to draw always, including items declared as selection-only.</summary>
        Always = 3
    }
}
