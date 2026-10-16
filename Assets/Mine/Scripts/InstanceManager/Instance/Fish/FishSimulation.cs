using System.Runtime.InteropServices;
using UnityEngine;
using Mine.SceneGizmo;

/// <summary>
/// Fish particle simulation — particles are distributed along and inside a
/// CurveAsset tube, advancing purely by path sampling. No emission plane, no forces.
/// </summary>
public class FishSimulation : MonoBehaviour, IUniversalInstanceSimulator, IGizmoSource
{
    [Header("Compute")]
    public ComputeShader fishShader;

    [Header("Path")]
    public CurveAsset curveAsset;
    public float curveSpeed = 8f;
    public float tubeRadius = 2f;

    [Header("Behaviour")]
    [Range(0f, 1f)] public float speedVariation = 0.5f;
    [Range(0f, 1f)] public float positionSmooth = 0.3f;

    [Header("Debug")]
    [SerializeField] private string _gizmoMember = "Instance";

    private const int MaxCurveSamples = 1024;

    [StructLayout(LayoutKind.Sequential)]
    private struct Particle
    {
        public Vector3 position;
        public float curveT;
        public float radialAngle;
        public float radialDist;

        public static int Stride() => sizeof(float) * 6;
    }

    private ComputeBuffer particleBuffer;
    private ComputeBuffer curveBuffer;
    private int instanceCount;
    private int updateKernel;
    private int curveSampleCount;

    private static readonly int ParticleBufferId   = Shader.PropertyToID("_ParticleBuffer");
    private static readonly int CurveBufferId      = Shader.PropertyToID("_CurveBuffer");
    private static readonly int CurveSampleCountId = Shader.PropertyToID("_CurveSampleCount");
    private static readonly int InstanceCountId    = Shader.PropertyToID("_InstanceCount");
    private static readonly int DeltaTimeId        = Shader.PropertyToID("_DeltaTime");
    private static readonly int CurveSpeedId       = Shader.PropertyToID("_CurveSpeed");
    private static readonly int TubeRadiusId       = Shader.PropertyToID("_TubeRadius");
    private static readonly int TotalLengthId      = Shader.PropertyToID("_TotalLength");
    private static readonly int CurveLoopId        = Shader.PropertyToID("_CurveLoop");
    private static readonly int SpeedVariationId   = Shader.PropertyToID("_SpeedVariation");
    private static readonly int PositionSmoothId   = Shader.PropertyToID("_PositionSmooth");

    public ComputeBuffer VisibleCountBuffer => null;

    // ════════════════════════════════════════════════════════════
    //  Gizmo declaration — curve path, tube radius and travel direction
    // ════════════════════════════════════════════════════════════
    public string Member
    {
        get => _gizmoMember;
        set => _gizmoMember = value;
    }

    public GizmoForm Form => GizmoForm.Path;

    private void OnDrawGizmos() => SceneGizmos.Draw(this, false);

    private void OnDrawGizmosSelected() => SceneGizmos.Draw(this, true);

    public void DrawGizmos(GizmoCanvas canvas)
    {
        if (curveAsset == null || curveAsset.positions == null || curveAsset.positions.Length < 2) return;

        Vector3[] points = curveAsset.positions;
        Vector3[] tangents = curveAsset.tangents;
        if (tangents == null || tangents.Length != points.Length) return;

        // ── Curve path ─────────────────────────────────────────────
        canvas.Polyline(points, curveAsset.loop);

        // ── Tube cross sections ────────────────────────────────────
        canvas.Color = GizmoTint.Fade(canvas.BaseColor);
        canvas.TubeFrames(points, tangents, tubeRadius, TubeFrameCount, TubeRingSegments);

        // ── Travel direction at the path start ─────────────────────
        canvas.Color = GizmoTint.Emphasis(canvas.BaseColor);
        canvas.Arrow(points[0], tangents[0], ArrowLength, ArrowHeadLength, ArrowHeadWidth);
    }

    private const int TubeFrameCount = 32;
    private const int TubeRingSegments = 16;
    private const float ArrowLength = 0.5f;
    private const float ArrowHeadLength = 0.1f;
    private const float ArrowHeadWidth = 0.075f;

    // ════════════════════════════════════════════════════════════
    //  Debug — read back particle positions from GPU
    // ════════════════════════════════════════════════════════════
    public string DebugReadback(int maxParticles)
    {
        if (particleBuffer == null || curveAsset == null) return "buffer or curve null";

        int n = Mathf.Min(maxParticles, instanceCount);
        var data = new Particle[n];
        particleBuffer.GetData(data, 0, 0, n);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"--- {n} particles (curve samples={curveSampleCount}, totalLength={curveAsset.totalLength:F1}) ---");

        int badCount = 0;
        for (int i = 0; i < n; i++)
        {
            float bestDist = float.MaxValue;
            float bestT = 0;
            var positions = curveAsset.positions;
            for (int j = 0; j < positions.Length; j++)
            {
                float d = Vector3.Distance(data[i].position, positions[j]);
                if (d < bestDist) { bestDist = d; bestT = (float)j / positions.Length; }
            }

            if (bestDist > tubeRadius * 2f)
            {
                badCount++;
                if (badCount <= 5)
                    sb.AppendLine($"  BAD  [{i}] pos={data[i].position:F2} curveT={data[i].curveT:F3} nearestDist={bestDist:F2} nearestT={bestT:F3}");
            }
            else if (i < 3)
            {
                sb.AppendLine($"  OK   [{i}] pos={data[i].position:F2} curveT={data[i].curveT:F3} nearestDist={bestDist:F2}");
            }
        }

        sb.AppendLine($"Result: {n - badCount} OK / {badCount} BAD (dist > {tubeRadius * 2f})");
        return sb.ToString();
    }

    // ════════════════════════════════════════════════════════════
    //  Initialize
    // ════════════════════════════════════════════════════════════
    public void Initialize(int count)
    {
        instanceCount = count;
        updateKernel  = fishShader.FindKernel("CS_FishUpdate");
        UploadCurveData();
        SpawnParticles();
    }

    // ════════════════════════════════════════════════════════════
    //  Dispatch
    // ════════════════════════════════════════════════════════════
    public void Dispatch(float deltaTime)
    {
        if (curveAsset == null) return;

        fishShader.SetFloat(DeltaTimeId, deltaTime);
        fishShader.SetFloat(CurveSpeedId, curveSpeed);
        fishShader.SetFloat(TubeRadiusId, tubeRadius);
        fishShader.SetFloat(TotalLengthId, curveAsset.totalLength);
        fishShader.SetInt(InstanceCountId, instanceCount);
        fishShader.SetInt(CurveSampleCountId, curveSampleCount);
        fishShader.SetBool(CurveLoopId, curveAsset.loop);
        fishShader.SetFloat(SpeedVariationId, speedVariation);
        fishShader.SetFloat(PositionSmoothId, positionSmooth);

        int threadGroups = Mathf.CeilToInt((float)instanceCount / 64f);
        fishShader.Dispatch(updateKernel, threadGroups, 1, 1);
    }

    public void BindMaterial(Material material)
    {
        material.SetBuffer(ParticleBufferId, particleBuffer);
    }

    public void Release()
    {
        if (particleBuffer != null) { particleBuffer.Release(); particleBuffer = null; }
        if (curveBuffer != null) { curveBuffer.Release(); curveBuffer = null; }
    }

    // ════════════════════════════════════════════════════════════
    //  Curve upload
    // ════════════════════════════════════════════════════════════
    private void UploadCurveData()
    {
        if (curveAsset == null || curveAsset.positions == null) return;

        curveSampleCount = Mathf.Min(curveAsset.sampleCount, MaxCurveSamples);
        var data = new Vector4[curveSampleCount];
        for (int i = 0; i < curveSampleCount; i++)
        {
            Vector3 p = curveAsset.positions[i];
            data[i] = new Vector4(p.x, p.y, p.z, (float)i / Mathf.Max(1, curveSampleCount - 1));
        }

        if (curveBuffer != null) curveBuffer.Release();
        curveBuffer = new ComputeBuffer(curveSampleCount, sizeof(float) * 4);
        curveBuffer.SetData(data);

        fishShader.SetBuffer(updateKernel, CurveBufferId, curveBuffer);
    }

    // ════════════════════════════════════════════════════════════
    //  Spawn — particles distributed along the curve, inside tube
    // ════════════════════════════════════════════════════════════
    private void SpawnParticles()
    {
        Particle[] particles = new Particle[instanceCount];
        bool hasCurve = curveAsset != null && curveAsset.positions != null && curveAsset.positions.Length >= 2;

        for (int i = 0; i < instanceCount; i++)
        {
            float ct = (float)i / instanceCount;

            if (hasCurve)
            {
                curveAsset.Sample(ct, out Vector3 curvePos, out Vector3 tangent);

                Vector3 up = Vector3.up;
                if (Mathf.Abs(Vector3.Dot(tangent, up)) > 0.999f) up = Vector3.right;
                Vector3 right = Vector3.Cross(tangent, up).normalized;
                Vector3 curveUp = Vector3.Cross(right, tangent).normalized;

                float angle = Random.value * Mathf.PI * 2f;
                float dist  = Mathf.Sqrt(Random.value) * tubeRadius;

                particles[i].position    = curvePos + right * Mathf.Cos(angle) * dist + curveUp * Mathf.Sin(angle) * dist;
                particles[i].curveT      = ct;
            }
            else
            {
                particles[i].position    = Vector3.zero;
                particles[i].curveT      = 0f;
            }

            particles[i].radialAngle = Random.value * Mathf.PI * 2f;
            particles[i].radialDist  = Mathf.Sqrt(Random.value) * tubeRadius;
        }

        if (particleBuffer != null) particleBuffer.Release();
        particleBuffer = new ComputeBuffer(instanceCount, Particle.Stride());
        particleBuffer.SetData(particles);

        fishShader.SetBuffer(updateKernel, ParticleBufferId, particleBuffer);
    }
}
