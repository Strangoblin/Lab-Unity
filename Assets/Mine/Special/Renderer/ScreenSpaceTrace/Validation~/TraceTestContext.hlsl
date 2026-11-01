// ═══════════════════════════════════════════════════════════════
//  TraceTestContext — synthetic-camera inputs for reusable GPU validation.
// ═══════════════════════════════════════════════════════════════
float4x4 _TestWorldToView;
float4x4 _TestViewToClip;
float4 _TestUvScaleBias;
float4 _TestDepthUvScaleBias;
float4 _TestZBufferParams;
float4 _TestClip;
float4 _TestSize;
float4 _TestTextureSize;
float4 _TestFlags;
int _TestMaxSteps;
float _TestThickness;

ScreenTraceContext TraceTest_Context()
{
    ScreenTraceContext context = (ScreenTraceContext)0;
    context.worldToView = _TestWorldToView;
    context.viewToClip = _TestViewToClip;
    context.uvScaleBias = _TestUvScaleBias;
    context.depthUvScaleBias = _TestDepthUvScaleBias;
    context.zBufferParams = _TestZBufferParams;
    context.clipPlanes = _TestClip.xy;
    context.depthSize = _TestSize.xy;
    context.depthTextureSize = _TestTextureSize.xy;
    context.depthIsLinear = _TestFlags.x;
    context.orthographic = _TestFlags.y;
    context.reversedZ = _TestFlags.z;
    context.pyramidMipCount = _TestFlags.w;
    return context;
}

ScreenTraceConfig TraceTest_Config()
{
    ScreenTraceConfig config;
    config.maxSteps = _TestMaxSteps;
    config.refinementSteps = 6;
    config.thickness = _TestThickness;
    return config;
}
