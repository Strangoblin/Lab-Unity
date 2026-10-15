using System;
using UnityEngine;
using CelToon.ProceduralController.Contracts;

namespace CelToon.ProceduralController.Fixtures
{
    public sealed class FakeClock
    {
        public double Now { get; private set; }
        public ulong Tick { get; private set; }
        public void Advance(float deltaTime) { if (deltaTime < 0f) throw new ArgumentOutOfRangeException(nameof(deltaTime)); Now += deltaTime; Tick++; }
        public TickContext Context(int actorId, uint epoch, float deltaTime) => new TickContext { ActorId = actorId, Epoch = epoch, TickId = Tick, Now = Now, DeltaTime = deltaTime };
    }

    public sealed class FlatGroundProbe : IGroundProbe
    {
        public bool TryProbe(in GroundQuery query, out GroundHit hit)
        {
            hit = default;
            if (!SnapshotValidation.IsFinite(query.OriginWS) || !SnapshotValidation.IsFinite(query.DirectionWS) || query.Distance <= 0f) return false;
            if (Mathf.Abs(query.DirectionWS.y) < 1e-5f || query.OriginWS.y < 0f || query.OriginWS.y > query.Distance * 1000f) return false;
            float distance = query.OriginWS.y / -query.DirectionWS.normalized.y;
            if (distance < 0f || distance > query.Distance) return false;
            hit = new GroundHit { Found = true, PointWS = query.OriginWS + query.DirectionWS.normalized * distance, NormalWS = Vector3.up, Distance = distance, SurfaceId = 1, IsStatic = true };
            return true;
        }
    }
}
