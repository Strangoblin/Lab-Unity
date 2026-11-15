using UnityEngine;

// ═══════════════════════════════════════════════════════════════════════════════
//  ColorfulWindowLight — 发布彩窗光平面与开关
//
//  SSL / 测试地面用它把 Cookie 染色限制在房间内侧；窗平面外侧保持普通阳光。
//  无此组件时 mask=0，Cookie 行为与未加平面限制时一致。
// ═══════════════════════════════════════════════════════════════════════════════
[ExecuteAlways]
[RequireComponent(typeof(Light))]
public sealed class ColorfulWindowLight : MonoBehaviour
{
    public Vector3 windowCenter = new Vector3(0f, 3f, 6f);
    public Vector3 roomSideNormal = new Vector3(0f, 0f, -1f);

    static readonly int PlaneID = Shader.PropertyToID("_ColorfulWindowPlane");
    static readonly int MaskID = Shader.PropertyToID("_ColorfulWindowMask");

    void OnEnable() => Apply();
    void OnValidate() => Apply();
    void Update() => Apply();

    void OnDisable()
    {
        Shader.SetGlobalFloat(MaskID, 0f);
    }

    void Apply()
    {
        Vector3 n = roomSideNormal.sqrMagnitude > 0.0001f
            ? roomSideNormal.normalized
            : Vector3.back;

        Shader.SetGlobalVector(PlaneID,
            new Vector4(n.x, n.y, n.z, -Vector3.Dot(n, windowCenter)));
        Shader.SetGlobalFloat(MaskID, 1f);
    }
}
