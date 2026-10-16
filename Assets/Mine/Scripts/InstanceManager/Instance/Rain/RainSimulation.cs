using UnityEngine;
using System.Runtime.InteropServices;
using Mine.Fields;
using Mine.GizmoRendering;

/// <summary>
/// Rain particle simulation — particles spawn throughout a rotated volume and
/// grow then shrink over independently staggered lifetimes.
/// </summary>
public class RainSimulation : MonoBehaviour, IUniversalInstanceSimulator, IGizmoSource
{
    [Header("Compute")]
    public ComputeShader rainShader;

    [Header("Physics")]
    public Vector3[] forceFields = new Vector3[]
    {
        new Vector3(0, -9.81f, 0),
    };

    [Header("Artistic · Shared wind")]
    [SerializeField, Min(0f)] private float _windDrag = 1f;

    [Header("Area (transform local space)")]
    [Tooltip("x=X width, y=spawn depth along -UP, z=Z width")]
    public Vector3 spawnArea = new Vector3(30, 55, 30);

    [Header("Lifecycle")]
    [Min(0.01f)]
    [Tooltip("Average lifetime in seconds; each particle varies by +/-25%.")]
    public float lifetime = 2f;

    [Header("Debug")]
    [SerializeField] private string _gizmoMember = "Instance";

    private const int MaxForceFields = 16;

    [StructLayout(LayoutKind.Sequential)]
    private struct RainParticle
    {
        public Vector3 position;
        public float age;
        public Vector3 velocity;
        public float lifetime;

        public static int Stride() => sizeof(float) * 8;
    }

    private ComputeBuffer rainBuffer;
    private int instanceCount;
    private int updateKernel;
    private float simulationTime;
    private readonly Vector4[] uploadedForceFields = new Vector4[MaxForceFields];

    private static readonly int RainBufferId       = Shader.PropertyToID("_RainBuffer");
    private static readonly int InstanceCountId    = Shader.PropertyToID("_InstanceCount");
    private static readonly int DeltaTimeId        = Shader.PropertyToID("_DeltaTime");
    private static readonly int ForceFieldsId      = Shader.PropertyToID("_ForceFields");
    private static readonly int ForceFieldCountId  = Shader.PropertyToID("_ForceFieldCount");
    private static readonly int SpawnCenterId      = Shader.PropertyToID("_SpawnCenter");
    private static readonly int SpawnRightId       = Shader.PropertyToID("_SpawnRight");
    private static readonly int SpawnUpId          = Shader.PropertyToID("_SpawnUp");
    private static readonly int SpawnForwardId     = Shader.PropertyToID("_SpawnForward");
    private static readonly int SpawnAreaId        = Shader.PropertyToID("_SpawnArea");

    private static readonly int LifetimeId = Shader.PropertyToID("_Lifetime");
    private static readonly int SimulationTimeId = Shader.PropertyToID("_SimulationTime");

    public ComputeBuffer VisibleCountBuffer => null;

    // ════════════════════════════════════════════════════════════
    //  Gizmo declaration — emission plane, fall volume and fall arrow
    // ════════════════════════════════════════════════════════════
    public string Member
    {
        get => _gizmoMember;
        set => _gizmoMember = value;
    }

    public GizmoForm Form => GizmoForm.Volume;

    private void OnDrawGizmos() => GizmoRender.Draw(this, false);

    private void OnDrawGizmosSelected() => GizmoRender.Draw(this, true);

    public void DrawGizmos(GizmoCanvas canvas)
    {
        using (canvas.LocalSpace(transform.localToWorldMatrix))
        {
            float halfWidth = spawnArea.x * 0.5f;
            float halfDepth = spawnArea.z * 0.5f;
            float floorY = -spawnArea.y;

            // ── Emission plane ─────────────────────────────────────
            canvas.PlaneRect(Vector3.zero, Vector3.right, Vector3.forward, spawnArea.x, spawnArea.z);

            // ── Fall volume — four vertical edges down to the floor ─
            canvas.Color = GizmoTint.Fade(canvas.BaseColor, 0.3f);
            for (int cornerIndex = 0; cornerIndex < FallCorners.Length; cornerIndex++)
            {
                Vector3 top = new Vector3(
                    FallCorners[cornerIndex].x * halfWidth,
                    0f,
                    FallCorners[cornerIndex].y * halfDepth);
                canvas.Line(top, new Vector3(top.x, floorY, top.z));
            }

            // ── Floor and fall direction ───────────────────────────
            canvas.Color = GizmoTint.Emphasis(canvas.BaseColor, 0.4f);
            canvas.PlaneRect(new Vector3(0f, floorY, 0f), Vector3.right, Vector3.forward, spawnArea.x, spawnArea.z);

            canvas.Color = canvas.BaseColor;
            canvas.Arrow(Vector3.zero, Vector3.down, spawnArea.y, ArrowHeadLength, ArrowHeadWidth);
        }
    }

    private const float ArrowHeadLength = 0.5f;
    private const float ArrowHeadWidth = 0.3f;

    private static readonly Vector2[] FallCorners =
    {
        new Vector2(-1f, -1f),
        new Vector2(1f, -1f),
        new Vector2(1f, 1f),
        new Vector2(-1f, 1f)
    };

    // ════════════════════════════════════════════════════════════
    //  Initialize
    // ════════════════════════════════════════════════════════════
    public void Initialize(int count)
    {
        instanceCount = count;
        updateKernel  = rainShader.FindKernel("CS_RainUpdate");

        Transform t = transform;
        simulationTime = 0f;
        Vector3 initialForce = Vector3.zero;
        int forceCount = forceFields == null ? 0 : Mathf.Min(forceFields.Length, MaxForceFields);
        for (int i = 0; i < forceCount; i++) initialForce += forceFields[i];

        RainParticle[] particles = new RainParticle[count];
        for (int i = 0; i < count; i++)
        {
            float rx = (Random.value * 2f - 1f) * spawnArea.x * 0.5f;
            float rz = (Random.value * 2f - 1f) * spawnArea.z * 0.5f;
            float ry = Random.Range(-spawnArea.y, 0f);

            particles[i].position = t.position
                + t.right   * rx
                + t.up      * ry
                + t.forward * rz;

            particles[i].lifetime = Mathf.Max(0.01f, lifetime) * Random.Range(0.75f, 1.25f);
            particles[i].age = Random.value * particles[i].lifetime;
            particles[i].velocity = new Vector3(initialForce.x, 0f, initialForce.z)
                + initialForce * particles[i].age;
        }

        rainBuffer = new ComputeBuffer(count, RainParticle.Stride());
        rainBuffer.SetData(particles);

        rainShader.SetBuffer(updateKernel, RainBufferId, rainBuffer);
        rainShader.SetInt(InstanceCountId, count);

        UploadForceFields();
    }

    // ════════════════════════════════════════════════════════════
    //  Dispatch
    // ════════════════════════════════════════════════════════════
    public void Dispatch(float deltaTime)
    {
        deltaTime = Mathf.Max(0f, deltaTime);
        simulationTime += deltaTime;
        rainShader.SetFloat(DeltaTimeId, deltaTime);
        rainShader.SetFloat(LifetimeId, Mathf.Max(0.01f, lifetime));
        rainShader.SetFloat(SimulationTimeId, simulationTime);

        Transform t = transform;
        rainShader.SetVector(SpawnCenterId,  t.position);
        rainShader.SetVector(SpawnRightId,   t.right);
        rainShader.SetVector(SpawnUpId,      t.up);
        rainShader.SetVector(SpawnForwardId, t.forward);
        rainShader.SetVector(SpawnAreaId,    spawnArea);

        UploadForceFields();
        FieldManager.BindWindCompute(rainShader, updateKernel);
        rainShader.SetFloat("_WindDrag", Mathf.Max(0f, _windDrag));

        int threadGroups = Mathf.CeilToInt((float)instanceCount / 64f);
        rainShader.Dispatch(updateKernel, threadGroups, 1, 1);
    }

    public void BindMaterial(Material material)
    {
        material.SetBuffer(RainBufferId, rainBuffer);
    }

    public void Release()
    {
        if (rainBuffer != null) { rainBuffer.Release(); rainBuffer = null; }
    }

    private void UploadForceFields()
    {
        int count = forceFields == null ? 0 : Mathf.Min(forceFields.Length, MaxForceFields);
        for (int i = 0; i < count; i++)
            uploadedForceFields[i] = new Vector4(forceFields[i].x, forceFields[i].y, forceFields[i].z, 0);
        rainShader.SetVectorArray(ForceFieldsId, uploadedForceFields);
        rainShader.SetInt(ForceFieldCountId, count);
    }
}
