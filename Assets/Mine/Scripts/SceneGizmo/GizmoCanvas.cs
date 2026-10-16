using System;
using UnityEngine;

namespace Mine.SceneGizmo
{
    /// <summary>
    /// Drawing surface shared by every gizmo source. Owns the current space and the reusable
    /// shape primitives, so sources describe geometry instead of open-coding line loops.
    /// </summary>
    /// <remarks>
    /// Primitives draw in the space currently set by <see cref="LocalSpace"/>. Sources are created
    /// and reused by <see cref="SceneGizmos"/>; a source never constructs its own canvas, and never
    /// saves or restores color and matrix itself — the dispatcher wraps the whole call.
    ///
    /// Secondary parts of one visualization derive their color from <see cref="BaseColor"/> through
    /// <see cref="GizmoTint"/>, so a member has a single color that its whole roster shares.
    ///
    /// Shape primitives transferred from the pre-framework per-file implementations:
    /// ring tessellation and the hemisphere grid came from the interior baker volume, tube frames
    /// from the fish simulation, and the arrow from three separate arrow builders whose only
    /// differences were head size and orientation reference.
    /// </remarks>
    public sealed class GizmoCanvas
    {
        private const int RingSegments = 24;
        private const int HemisphereRingCount = 6;
        private const int HemisphereSegmentCount = 24;
        private const float DefaultHeadScale = 0.2f;
        private const float DefaultHeadWidthScale = 0.5f;
        private const float ParallelGuard = 0.999f;

        // ════════════════════════════════════════════════════════════
        //  State — the shared Gizmos state this canvas drives
        // ════════════════════════════════════════════════════════════

        /// <summary>Current stroke color.</summary>
        public Color Color
        {
            get => Gizmos.color;
            set => Gizmos.color = value;
        }

        /// <summary>Active drawing matrix. Prefer <see cref="LocalSpace"/> over setting this directly.</summary>
        public Matrix4x4 Matrix
        {
            get => Gizmos.matrix;
            set => Gizmos.matrix = value;
        }

        /// <summary>
        /// Color of the member that owns the drawing source. Pre-assigned by <see cref="SceneGizmos"/>;
        /// use it to derive subordinate tints instead of hardcoding a second color.
        /// </summary>
        public Color BaseColor { get; internal set; }

        // ════════════════════════════════════════════════════════════
        //  Space scope — local-space passes restore the previous matrix
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Draws subsequent primitives in a local space described by <paramref name="localToWorld"/>.
        /// Dispose the returned scope to restore the previous matrix.
        /// </summary>
        /// <example>
        /// <code>
        /// using (canvas.LocalSpace(transform.localToWorldMatrix))
        /// {
        ///     canvas.WireBox(Vector3.zero, _size);
        /// }
        /// </code>
        /// </example>
        public MatrixScope LocalSpace(Matrix4x4 localToWorld)
        {
            return new MatrixScope(localToWorld);
        }

        /// <summary>Restores the matrix captured when a local space was entered.</summary>
        public readonly struct MatrixScope : IDisposable
        {
            private readonly Matrix4x4 _previous;

            internal MatrixScope(Matrix4x4 localToWorld)
            {
                _previous = Gizmos.matrix;
                Gizmos.matrix = localToWorld;
            }

            /// <summary>Puts the previous matrix back.</summary>
            public void Dispose()
            {
                Gizmos.matrix = _previous;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  框架形制 — volume, area and range outlines
        // ════════════════════════════════════════════════════════════

        /// <summary>Wire cube centered on <paramref name="center"/> with full extents <paramref name="size"/>.</summary>
        public void WireBox(Vector3 center, Vector3 size)
        {
            Gizmos.DrawWireCube(center, size);
        }

        /// <summary>Wire sphere centered on <paramref name="center"/>.</summary>
        public void WireSphere(Vector3 center, float radius)
        {
            if (radius <= 0f) return;
            Gizmos.DrawWireSphere(center, radius);
        }

        /// <summary>
        /// Flat rectangle spanned by two orthonormal directions, each axis given as a full extent.
        /// Used for ground areas and emission planes that carry no thickness.
        /// </summary>
        public void PlaneRect(Vector3 center, Vector3 axisA, Vector3 axisB, float sizeA, float sizeB)
        {
            if (sizeA <= 0f || sizeB <= 0f) return;

            Vector3 halfA = axisA.normalized * (sizeA * 0.5f);
            Vector3 halfB = axisB.normalized * (sizeB * 0.5f);
            Vector3 corner0 = center - halfA - halfB;
            Vector3 corner1 = center + halfA - halfB;
            Vector3 corner2 = center + halfA + halfB;
            Vector3 corner3 = center - halfA + halfB;

            Gizmos.DrawLine(corner0, corner1);
            Gizmos.DrawLine(corner1, corner2);
            Gizmos.DrawLine(corner2, corner3);
            Gizmos.DrawLine(corner3, corner0);
        }

        /// <summary>
        /// Half-sphere whose apex points along <paramref name="poleAxis"/> and whose equator passes
        /// through <paramref name="center"/>. Draws the ring grid plus meridian lines to the apex.
        /// </summary>
        public void WireHemisphere(Vector3 center, float radius, Vector3 poleAxis)
        {
            if (radius <= 0f) return;

            Vector3 pole = Normalize(poleAxis, Vector3.up);
            OrthonormalBasis(pole, out Vector3 side, out Vector3 up);
            Vector3 apex = center + pole * radius;

            for (int ringIndex = 1; ringIndex <= HemisphereRingCount; ringIndex++)
            {
                float angle = ringIndex / (float)HemisphereRingCount * Mathf.PI * 0.5f;
                Vector3 ringCenter = center + pole * (Mathf.Cos(angle) * radius);
                DrawRing(ringCenter, side, up, Mathf.Sin(angle) * radius, RingSegments);
            }

            for (int segmentIndex = 0; segmentIndex < HemisphereSegmentCount; segmentIndex++)
            {
                float angle = segmentIndex / (float)HemisphereSegmentCount * Mathf.PI * 2f;
                Vector3 equatorPoint = center + (side * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius;
                Gizmos.DrawLine(apex, equatorPoint);
            }
        }

        // ════════════════════════════════════════════════════════════
        //  方向形制 — direction arrows
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Shaft plus four diagonal head edges from <paramref name="origin"/> along
        /// <paramref name="direction"/>. Zero head arguments derive the head from the shaft length.
        /// </summary>
        public void Arrow(Vector3 origin, Vector3 direction, float length, float headLength = 0f, float headWidth = 0f)
        {
            if (length <= 0f) return;

            Vector3 axis = Normalize(direction, Vector3.zero);
            if (axis == Vector3.zero) return;

            Vector3 endPoint = origin + axis * length;
            Gizmos.DrawLine(origin, endPoint);

            float head = headLength > 0f ? Mathf.Min(headLength, length * 0.5f) : length * DefaultHeadScale;
            float width = headWidth > 0f ? headWidth : head * DefaultHeadWidthScale;
            if (head <= 0f) return;

            OrthonormalBasis(axis, out Vector3 side, out Vector3 up);
            Vector3 headBase = endPoint - axis * head;

            for (int sideSign = -1; sideSign <= 1; sideSign += 2)
            {
                for (int upSign = -1; upSign <= 1; upSign += 2)
                {
                    Gizmos.DrawLine(endPoint, headBase + side * (width * sideSign) + up * (width * upSign));
                }
            }
        }

        // ════════════════════════════════════════════════════════════
        //  路径与散点形制 — polylines, tube cross sections, tick lines
        // ════════════════════════════════════════════════════════════

        /// <summary>Straight segment. Kept public so per-sample loops stay readable.</summary>
        public void Line(Vector3 from, Vector3 to)
        {
            Gizmos.DrawLine(from, to);
        }

        /// <summary>Connects <paramref name="points"/> in order, optionally closing the loop.</summary>
        public void Polyline(Vector3[] points, bool loop)
        {
            if (points == null || points.Length < 2) return;

            for (int index = 0; index < points.Length - 1; index++)
            {
                Gizmos.DrawLine(points[index], points[index + 1]);
            }

            if (loop)
            {
                Gizmos.DrawLine(points[points.Length - 1], points[0]);
            }
        }

        /// <summary>
        /// Cross-section rings of a tube swept along a path, sampled at <paramref name="frameCount"/>
        /// evenly spaced positions and framed from each tangent.
        /// </summary>
        /// <remarks>
        /// Frames come from a fixed reference axis rather than parallel transport, so a tangent close
        /// to vertical can roll the ring. This matches the pre-framework behavior of the fish path.
        /// </remarks>
        public void TubeFrames(Vector3[] points, Vector3[] tangents, float radius, int frameCount = 32, int segments = 16)
        {
            if (points == null || tangents == null || points.Length < 2) return;
            if (tangents.Length != points.Length || radius <= 0f || frameCount < 1 || segments < 3) return;

            int lastIndex = points.Length - 1;
            for (int frameIndex = 0; frameIndex <= frameCount; frameIndex++)
            {
                float t = frameIndex / (float)frameCount;
                int index = Mathf.Min((int)(t * lastIndex), lastIndex);
                OrthonormalBasis(Normalize(tangents[index], Vector3.forward), out Vector3 side, out Vector3 up);
                DrawRing(points[index], side, up, radius, segments);
            }
        }

        // ════════════════════════════════════════════════════════════
        //  Geometry helpers — shared by the primitives above
        // ════════════════════════════════════════════════════════════

        private void DrawRing(Vector3 center, Vector3 axisA, Vector3 axisB, float radius, int segments)
        {
            if (radius <= 0f) return;

            Vector3 previous = center + axisA * radius;
            for (int index = 1; index <= segments; index++)
            {
                float angle = index / (float)segments * Mathf.PI * 2f;
                Vector3 current = center + (axisA * Mathf.Cos(angle) + axisB * Mathf.Sin(angle)) * radius;
                Gizmos.DrawLine(previous, current);
                previous = current;
            }
        }

        /// <summary>
        /// Builds two unit directions perpendicular to <paramref name="axis"/> and to each other,
        /// falling back to a different reference when the axis is nearly parallel to world up.
        /// </summary>
        private static void OrthonormalBasis(Vector3 axis, out Vector3 side, out Vector3 up)
        {
            Vector3 reference = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > ParallelGuard ? Vector3.right : Vector3.up;
            side = Vector3.Cross(axis, reference).normalized;
            up = Vector3.Cross(side, axis).normalized;
        }

        private static Vector3 Normalize(Vector3 value, Vector3 fallback)
        {
            return value.sqrMagnitude > 1e-8f ? value.normalized : fallback;
        }
    }
}
