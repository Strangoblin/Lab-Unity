var sb = new System.Text.StringBuilder();
UnityEngine.Camera main = UnityEngine.Camera.main;
if (main == null) { foreach (var c in UnityEngine.Object.FindObjectsByType<UnityEngine.Camera>(UnityEngine.FindObjectsSortMode.None)) if (c.CompareTag("MainCamera")) main = c; }
if (main == null) return "no main camera";

UnityEngine.Rendering.Universal.ScriptableRendererData data = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.ScriptableRendererData>("Assets/Settings/PC_Renderer.asset");
AOFeature ao = null;
foreach (var f in data.rendererFeatures) if (f is AOFeature) ao = (AOFeature)f;
if (ao == null) return "no AOFeature";
float oldTimeScale = UnityEngine.Time.timeScale;
UnityEngine.Time.timeScale = 0f;

System.Reflection.FieldInfo idxField = typeof(UnityEngine.Rendering.Universal.UniversalAdditionalCameraData).GetField("m_RendererIndex", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
int w = Mathf.RoundToInt(main.pixelRect.width); int h = Mathf.RoundToInt(main.pixelRect.height);
if (w < 8) w = 642; if (h < 8) h = 522;

UnityEngine.RenderTexture rt = new UnityEngine.RenderTexture(w, h, 24, UnityEngine.RenderTextureFormat.ARGB32);
rt.Create();
UnityEngine.GameObject go = new UnityEngine.GameObject("AOProbeCam");
UnityEngine.Camera cam = go.AddComponent<UnityEngine.Camera>();
cam.CopyFrom(main);
cam.targetTexture = rt;
UnityEngine.Rendering.Universal.UniversalAdditionalCameraData add = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
add.renderPostProcessing = false;
if (idxField != null) idxField.SetValue(add, -1);
sb.AppendLine("probe " + w + "x" + h + " postProcessing=False");

AOFeature.Mode oldMode = ao.settings.mode;
float oldIntensity = ao.settings.intensity;
AOFeature.DebugMode oldDebug = ao.settings.debug;
ao.SetActive(true);

float[] cIntensity = new float[] { 0.0001f, 1f, 1f, 1f, 0.0001f, 1f };
int[] cMode = new int[] { 1, 1, 1, 0, 0, 0 };
int[] cDebug = new int[] { 0, 0, 1, 1, 0, 0 };
string[] cName = new string[] { "raw_hb_base", "raw_hb_comp", "raw_hb_vis", "raw_ss_vis", "raw_ss_base", "raw_ss_comp" };
byte[][] img = new byte[6][];

for (int i = 0; i < 6; ++i)
{
    ao.settings.mode = (AOFeature.Mode)cMode[i];
    ao.settings.intensity = cIntensity[i];
    ao.settings.debug = (AOFeature.DebugMode)cDebug[i];
    await System.Threading.Tasks.Task.Delay(50);
    cam.Render();
    await System.Threading.Tasks.Task.Delay(80);
    var req = UnityEngine.Rendering.AsyncGPUReadback.Request(rt);
    for (int k = 0; k < 300 && !req.done; ++k) await System.Threading.Tasks.Task.Delay(10);
    if (!req.done || req.hasError) { sb.AppendLine(cName[i] + ": readback fail"); continue; }
    byte[] px = req.GetData<byte>().ToArray();
    img[i] = px;
    var t2 = new UnityEngine.Texture2D(w, h, UnityEngine.TextureFormat.RGBA32, false);
    t2.LoadRawTextureData(px); t2.Apply();
    
    UnityEngine.Object.Destroy(t2);
    long sumR = 0; long dark = 0; long minv = 255; long maxv = 0; long n = px.Length / 4;
    for (int p = 0; p < px.Length; p += 4) { sumR += px[p]; if (px[p] < minv) minv = px[p]; if (px[p] > maxv) maxv = px[p]; if (px[p] < 230) ++dark; }
    sb.AppendLine(cName[i] + " meanR=" + (sumR / (double)n).ToString("F2") + " minR=" + minv + " maxR=" + maxv + " below230=" + (100.0 * dark / n).ToString("F1") + "%");
}

// 1) 线性化 + 逐像素校验 composite = base * visibility
double[] lin = new double[256];
for (int v = 0; v < 256; ++v) { double c = v / 255.0; lin[v] = c <= 0.04045 ? c / 12.92 : System.Math.Pow((c + 0.055) / 1.055, 2.4); }
if (img[0] != null && img[1] != null && img[2] != null)
{
    double sumErr = 0; double maxErr = 0; long cnt = 0; long within = 0; long dark1 = 0;
    for (int p = 0; p < img[0].Length; p += 4)
    {
        int bs = img[0][p]; int cs = img[1][p]; int vs = img[2][p];
        if (bs < 16) continue;
        double b = lin[bs]; double c = lin[cs]; double v = lin[vs];
        double err = System.Math.Abs(c / b - v);
        sumErr += err; if (err > maxErr) maxErr = err; ++cnt;
        if (err <= 1.0 / 255.0) ++within;
    }
    double d = 0; long neg = 0;
    for (int p = 0; p < img[0].Length; p += 4) { int dd = img[1][p] - img[0][p]; if (dd < 0) { ++neg; d += dd; } }
    sb.AppendLine("HBAO check: composite==base*V on " + cnt + " px (base>=16) meanAbsErr=" + (sumErr / cnt).ToString("F5")
        + " maxAbsErr=" + maxErr.ToString("F4") + " within1_255=" + (100.0 * within / cnt).ToString("F1") + "%"
        + " | darkerPx=" + neg + " meanDeltaR=" + (d / neg).ToString("F3"));
}
if (img[4] != null && img[5] != null && img[3] != null)
{
    double sumErr = 0; double maxErr = 0; long cnt = 0; long within = 0;
    for (int p = 0; p < img[4].Length; p += 4)
    {
        int bs = img[4][p]; int cs = img[5][p]; int vs = img[3][p];
        if (bs < 16) continue;
        double err = System.Math.Abs(lin[cs] / lin[bs] - lin[vs]);
        sumErr += err; if (err > maxErr) maxErr = err; ++cnt;
        if (err <= 1.0 / 255.0) ++within;
    }
    double d = 0; long neg = 0;
    for (int p = 0; p < img[4].Length; p += 4) { int dd = img[5][p] - img[4][p]; if (dd < 0) { ++neg; d += dd; } }
    sb.AppendLine("SSAO check: composite==base*V on " + cnt + " px meanAbsErr=" + (sumErr / cnt).ToString("F5")
        + " maxAbsErr=" + maxErr.ToString("F4") + " within1_255=" + (100.0 * within / cnt).ToString("F1") + "%"
        + " | darkerPx=" + neg + " meanDeltaR=" + (d / neg).ToString("F3"));
}
if (img[2] != null && img[3] != null)
{
    long n = img[2].Length / 4; long s2 = 0; long s3 = 0; long d2 = 0; long d3 = 0; long m2 = 255; long m3 = 255; long x2 = 0; long x3 = 0;
    for (int p = 0; p < img[2].Length; p += 4)
    {
        s2 += img[2][p]; s3 += img[3][p];
        if (img[2][p] < m2) m2 = img[2][p]; if (img[2][p] > x2) x2 = img[2][p];
        if (img[3][p] < m3) m3 = img[3][p]; if (img[3][p] > x3) x3 = img[3][p];
        if (img[2][p] < 250) ++d2;
        if (img[3][p] < 250) ++d3;
    }
    sb.AppendLine("raw visibility: HBAO mean=" + (s2 / (double)n / 255.0).ToString("F4") + " min=" + m2 + " max=" + x2 + " occ(<250)=" + (100.0 * d2 / n).ToString("F1")
        + "% | SSAO mean=" + (s3 / (double)n / 255.0).ToString("F4") + " min=" + m3 + " max=" + x3 + " occ(<250)=" + (100.0 * d3 / n).ToString("F1") + "%");
    // 天空行（图像顶部 5%）是否全为 1.0
    long skyN = 0; long skyFull = 0;
    int topRows = Mathf.Max(1, h / 20);
    for (int y = 0; y < topRows; ++y) for (int x = 0; x < w; ++x) { int idx = (y * w + x) * 4; ++skyN; if (img[2][idx] == 255) ++skyFull; }
    sb.AppendLine("HBAO top 5% rows: full-visibility px = " + (100.0 * skyFull / skyN).ToString("F1") + "%");
}

ao.settings.mode = oldMode; ao.settings.intensity = oldIntensity; ao.settings.debug = oldDebug;
UnityEngine.Time.timeScale = oldTimeScale;
UnityEngine.Object.DestroyImmediate(cam);
UnityEngine.Object.DestroyImmediate(go);
rt.Release();
UnityEngine.Object.DestroyImmediate(rt);
sb.AppendLine("restored mode=" + ao.settings.mode + " intensity=" + ao.settings.intensity + " debug=" + ao.settings.debug);
return sb.ToString();
