namespace Mine.GizmoRendering
{
    /// <summary>
    /// Shape family a gizmo source draws. Reported by <see cref="IGizmoSource.Form"/> and shown in the
    /// manager window, so a member's roster reads as "which geometry does this source contribute".
    /// </summary>
    public enum GizmoForm
    {
        /// <summary>Framework drawing: volume, area or range a system occupies.</summary>
        Volume = 0,

        /// <summary>Direction drawing: an arrow or axis describing flow, view or fall direction.</summary>
        Direction = 1,

        /// <summary>Path drawing: a polyline or tube along which instances or probes are placed.</summary>
        Path = 2,

        /// <summary>Point drawing: per-sample ticks such as generated grass normals.</summary>
        Points = 3
    }
}
