// ════════════════════════════════════════════════════════════
//  ExteriorMappingFunction — solid-cube ray mapping inside a quad
//  Samples the entry face of a six-sided cube, opposite to interior mapping.
// ════════════════════════════════════════════════════════════

TEXTURE2D(_FaceTop);
SAMPLER(sampler_FaceTop);
TEXTURE2D(_FaceBottom);
SAMPLER(sampler_FaceBottom);
TEXTURE2D(_FaceFront);
SAMPLER(sampler_FaceFront);
TEXTURE2D(_FaceBack);
SAMPLER(sampler_FaceBack);
TEXTURE2D(_FaceLeft);
SAMPLER(sampler_FaceLeft);
TEXTURE2D(_FaceRight);
SAMPLER(sampler_FaceRight);

CBUFFER_START(UnityPerMaterial)
    float4 _CubeRotation;
    float4 _CubeOffset;
    float4 _Tint;
    float4 _LightDirection;
    float _CubeSize;
    float _BillboardMode;
    float _CubeSpace;
    float _Ambient;
    float _ShadingStrength;
CBUFFER_END

// ════════════════════════════════════════════════════════════
//  Math helpers — normalize and Euler rotation
// ════════════════════════════════════════════════════════════
float3 ExteriorMappingSafeNormalize(float3 value)
{
    return value * rsqrt(max(dot(value, value), 1e-8));
}

float3x3 ExteriorMappingRotateX(float angle)
{
    float s;
    float c;
    sincos(radians(angle), s, c);
    return float3x3(
        1.0, 0.0, 0.0,
        0.0, c, -s,
        0.0, s, c);
}

float3x3 ExteriorMappingRotateY(float angle)
{
    float s;
    float c;
    sincos(radians(angle), s, c);
    return float3x3(
        c, 0.0, s,
        0.0, 1.0, 0.0,
        -s, 0.0, c);
}

float3x3 ExteriorMappingRotateZ(float angle)
{
    float s;
    float c;
    sincos(radians(angle), s, c);
    return float3x3(
        c, -s, 0.0,
        s, c, 0.0,
        0.0, 0.0, 1.0);
}

float3x3 ExteriorMappingCubeRotation(float3 eulerAngles)
{
    float3x3 rotateX = ExteriorMappingRotateX(eulerAngles.x);
    float3x3 rotateY = ExteriorMappingRotateY(eulerAngles.y);
    float3x3 rotateZ = ExteriorMappingRotateZ(eulerAngles.z);
    return mul(mul(rotateY, rotateX), rotateZ);
}

// ════════════════════════════════════════════════════════════
//  Billboard — keep the quad centered on its pivot and facing the camera
// ════════════════════════════════════════════════════════════
float3 ExteriorMappingBillboardPositionWS(float3 positionOS)
{
    float3 pivotWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
    float3 rightWS = ExteriorMappingSafeNormalize(UNITY_MATRIX_V[0].xyz);
    float3 upWS = ExteriorMappingSafeNormalize(UNITY_MATRIX_V[1].xyz);
    float3 scaleOS = float3(
        length(UNITY_MATRIX_M[0].xyz),
        length(UNITY_MATRIX_M[1].xyz),
        length(UNITY_MATRIX_M[2].xyz));
    return pivotWS + rightWS * positionOS.x * scaleOS.x + upWS * positionOS.y * scaleOS.y;
}

float3 ExteriorMappingBillboardNormalWS()
{
    float3 rightWS = ExteriorMappingSafeNormalize(UNITY_MATRIX_V[0].xyz);
    float3 upWS = ExteriorMappingSafeNormalize(UNITY_MATRIX_V[1].xyz);
    return ExteriorMappingSafeNormalize(cross(rightWS, upWS));
}

// ════════════════════════════════════════════════════════════
//  Surface basis — reconstruct the quad plane from screen derivatives
// ════════════════════════════════════════════════════════════
void ExteriorMappingSurfaceBasis(
    float2 meshUV,
    float3 surfacePositionWS,
    float3 surfaceNormalWS,
    float3 positionDerivativeX,
    float3 positionDerivativeY,
    float2 uvDerivativeX,
    float2 uvDerivativeY,
    out float3 quadCenterWS,
    out float3 tangentWS,
    out float3 bitangentWS,
    out float3 normalWS,
    out float2 quadSize)
{
    float determinant = uvDerivativeX.x * uvDerivativeY.y
        - uvDerivativeX.y * uvDerivativeY.x;
    float safeDeterminant = abs(determinant) > 1e-6
        ? determinant
        : (determinant < 0.0 ? -1e-6 : 1e-6);
    float3 positionDerivativeU =
        (positionDerivativeX * uvDerivativeY.y
        - positionDerivativeY * uvDerivativeX.y) / safeDeterminant;
    float3 positionDerivativeV =
        (-positionDerivativeX * uvDerivativeY.x
        + positionDerivativeY * uvDerivativeX.x) / safeDeterminant;

    float quadWidth = max(length(positionDerivativeU), 1e-4);
    float quadHeight = max(length(positionDerivativeV), 1e-4);
    tangentWS = positionDerivativeU / quadWidth;
    bitangentWS = positionDerivativeV / quadHeight;

    float3 derivedNormal = cross(tangentWS, bitangentWS);
    normalWS = length(derivedNormal) > 1e-6
        ? ExteriorMappingSafeNormalize(derivedNormal)
        : ExteriorMappingSafeNormalize(surfaceNormalWS);

    quadSize = float2(quadWidth, quadHeight);
    quadCenterWS = surfacePositionWS
        - tangentWS * ((meshUV.x - 0.5) * quadWidth)
        - bitangentWS * ((meshUV.y - 0.5) * quadHeight);
}

// ════════════════════════════════════════════════════════════
//  Ray-box intersection — slab method in normalized cube space
// ════════════════════════════════════════════════════════════
void ExteriorMappingIntersectBox(
    float3 rayOrigin,
    float3 rayDirection,
    out float tNear,
    out float tFar,
    out int nearAxis,
    out int farAxis,
    out bool hit)
{
    float3 directionSign = float3(
        rayDirection.x < 0.0 ? -1.0 : 1.0,
        rayDirection.y < 0.0 ? -1.0 : 1.0,
        rayDirection.z < 0.0 ? -1.0 : 1.0);
    float3 inverseDirection = directionSign / max(abs(rayDirection), 1e-6);
    float3 t0 = (-0.5 - rayOrigin) * inverseDirection;
    float3 t1 = (0.5 - rayOrigin) * inverseDirection;
    float3 tMinimum = min(t0, t1);
    float3 tMaximum = max(t0, t1);

    tNear = max(max(tMinimum.x, tMinimum.y), tMinimum.z);
    tFar = min(min(tMaximum.x, tMaximum.y), tMaximum.z);
    hit = tNear <= tFar && tFar > 0.0;

    nearAxis = tMinimum.x >= tMinimum.y && tMinimum.x >= tMinimum.z
        ? 0
        : (tMinimum.y >= tMinimum.z ? 1 : 2);
    farAxis = tMaximum.x <= tMaximum.y && tMaximum.x <= tMaximum.z
        ? 0
        : (tMaximum.y <= tMaximum.z ? 1 : 2);
}

// ════════════════════════════════════════════════════════════
//  Face mapping — cube face axes and per-face texture coordinates
// ════════════════════════════════════════════════════════════
float2 ExteriorMappingFaceUV(float3 hitPosition, int axis, float faceSign)
{
    float2 faceUV;
    if (axis == 0)
    {
        faceUV = faceSign > 0.0
            ? float2(-hitPosition.z, hitPosition.y)
            : float2(hitPosition.z, hitPosition.y);
    }
    else if (axis == 1)
    {
        faceUV = faceSign > 0.0
            ? float2(hitPosition.x, -hitPosition.z)
            : float2(hitPosition.x, hitPosition.z);
    }
    else
    {
        faceUV = faceSign > 0.0
            ? float2(hitPosition.x, hitPosition.y)
            : float2(-hitPosition.x, hitPosition.y);
    }
    return saturate(faceUV + 0.5);
}

float3 ExteriorMappingFaceNormal(int axis, float faceSign)
{
    if (axis == 0)
    {
        return float3(faceSign, 0.0, 0.0);
    }
    if (axis == 1)
    {
        return float3(0.0, faceSign, 0.0);
    }
    return float3(0.0, 0.0, faceSign);
}

float4 ExteriorMappingSampleFace(int axis, float faceSign, float2 faceUV)
{
    float4 faceColor = float4(0.0, 0.0, 0.0, 0.0);
    if (axis == 0)
    {
        if (faceSign > 0.0)
        {
            faceColor = SAMPLE_TEXTURE2D(_FaceRight, sampler_FaceRight, faceUV);
        }
        else
        {
            faceColor = SAMPLE_TEXTURE2D(_FaceLeft, sampler_FaceLeft, faceUV);
        }
    }
    else if (axis == 1)
    {
        if (faceSign > 0.0)
        {
            faceColor = SAMPLE_TEXTURE2D(_FaceTop, sampler_FaceTop, faceUV);
        }
        else
        {
            faceColor = SAMPLE_TEXTURE2D(_FaceBottom, sampler_FaceBottom, faceUV);
        }
    }
    else
    {
        if (faceSign > 0.0)
        {
            faceColor = SAMPLE_TEXTURE2D(_FaceFront, sampler_FaceFront, faceUV);
        }
        else
        {
            faceColor = SAMPLE_TEXTURE2D(_FaceBack, sampler_FaceBack, faceUV);
        }
    }
    return faceColor;
}

// ════════════════════════════════════════════════════════════
//  Render — cast the camera ray, hit the cube, shade the face
// ════════════════════════════════════════════════════════════
float3 ExteriorMappingRenderSurface(
    float2 meshUV,
    float3 surfacePositionWS,
    float3 cameraPositionWS,
    float3 surfaceNormalWS,
    float3 positionDerivativeX,
    float3 positionDerivativeY,
    float2 uvDerivativeX,
    float2 uvDerivativeY,
    out float alpha)
{
    float3 quadCenterWS;
    float3 tangentWS;
    float3 bitangentWS;
    float3 normalWS;
    float2 quadSize;
    ExteriorMappingSurfaceBasis(
        meshUV,
        surfacePositionWS,
        surfaceNormalWS,
        positionDerivativeX,
        positionDerivativeY,
        uvDerivativeX,
        uvDerivativeY,
        quadCenterWS,
        tangentWS,
        bitangentWS,
        normalWS,
        quadSize);

    bool objectTransform = _CubeSpace > 0.5;
    float3 cubeCenterWS;
    float3 cubeSizeWS;
    float3x3 cubeRotation;

    if (objectTransform)
    {
        float3 objectRightWS = UNITY_MATRIX_M._m00_m10_m20;
        float3 objectUpWS = UNITY_MATRIX_M._m01_m11_m21;
        float3 objectForwardWS = UNITY_MATRIX_M._m02_m12_m22;
        float3 objectScale = max(
            float3(
                length(objectRightWS),
                length(objectUpWS),
                length(objectForwardWS)),
            1e-4);
        float3x3 objectRotation = float3x3(
            objectRightWS.x / objectScale.x,
            objectUpWS.x / objectScale.y,
            objectForwardWS.x / objectScale.z,
            objectRightWS.y / objectScale.x,
            objectUpWS.y / objectScale.y,
            objectForwardWS.y / objectScale.z,
            objectRightWS.z / objectScale.x,
            objectUpWS.z / objectScale.y,
            objectForwardWS.z / objectScale.z);
        cubeRotation = mul(objectRotation, ExteriorMappingCubeRotation(_CubeRotation.xyz));
        cubeCenterWS = quadCenterWS + mul(objectRotation, _CubeOffset.xyz * objectScale);
        cubeSizeWS = max(_CubeSize, 0.001) * objectScale;
    }
    else
    {
        float minQuadSize = max(min(quadSize.x, quadSize.y), 1e-4);
        float cubeWorldSize = max(_CubeSize, 0.001) * minQuadSize;
        cubeRotation = ExteriorMappingCubeRotation(_CubeRotation.xyz);
        cubeCenterWS = quadCenterWS
            + tangentWS * (_CubeOffset.x * quadSize.x)
            + bitangentWS * (_CubeOffset.y * quadSize.y)
            + normalWS * (_CubeOffset.z * minQuadSize);
        cubeSizeWS = float3(cubeWorldSize, cubeWorldSize, cubeWorldSize);
    }

    float3 rayDirectionWS = ExteriorMappingSafeNormalize(surfacePositionWS - cameraPositionWS);
    float3 cameraOffsetWS = cameraPositionWS - cubeCenterWS;
    float3x3 inverseCubeRotation = transpose(cubeRotation);

    float3 rayOriginCube;
    float3 rayDirectionCube;
    if (objectTransform)
    {
        float3 inverseCubeSize = 1.0 / max(cubeSizeWS, 1e-4);
        rayOriginCube = mul(inverseCubeRotation, cameraOffsetWS) * inverseCubeSize;
        rayDirectionCube = ExteriorMappingSafeNormalize(
            mul(inverseCubeRotation, rayDirectionWS) * inverseCubeSize);
    }
    else
    {
        float3 cameraQuad = float3(
            dot(cameraOffsetWS, tangentWS),
            dot(cameraOffsetWS, bitangentWS),
            dot(cameraOffsetWS, normalWS)) / cubeSizeWS.x;
        float3 rayDirectionQuad = float3(
            dot(rayDirectionWS, tangentWS),
            dot(rayDirectionWS, bitangentWS),
            dot(rayDirectionWS, normalWS));
        rayOriginCube = mul(inverseCubeRotation, cameraQuad);
        rayDirectionCube = mul(inverseCubeRotation, rayDirectionQuad);
    }

    float tNear;
    float tFar;
    int nearAxis;
    int farAxis;
    bool hit;
    ExteriorMappingIntersectBox(
        rayOriginCube,
        rayDirectionCube,
        tNear,
        tFar,
        nearAxis,
        farAxis,
        hit);

    if (!hit)
    {
        alpha = 0.0;
        return float3(0.0, 0.0, 0.0);
    }

    bool insideCube = tNear < 0.0;
    float hitDistance = insideCube ? tFar : tNear;
    int hitAxis = insideCube ? farAxis : nearAxis;
    float axisDirection = hitAxis == 0
        ? rayDirectionCube.x
        : (hitAxis == 1 ? rayDirectionCube.y : rayDirectionCube.z);
    float faceSign = axisDirection > 0.0
        ? (insideCube ? 1.0 : -1.0)
        : (insideCube ? -1.0 : 1.0);
    float3 hitPosition = rayOriginCube + rayDirectionCube * hitDistance;

    float2 faceUV = ExteriorMappingFaceUV(hitPosition, hitAxis, faceSign);
    if (insideCube)
    {
        faceUV.x = 1.0 - faceUV.x;
    }

    float4 faceColor = ExteriorMappingSampleFace(hitAxis, faceSign, faceUV);
    float3 faceNormalLocal = ExteriorMappingFaceNormal(hitAxis, faceSign);
    if (insideCube)
    {
        faceNormalLocal = -faceNormalLocal;
    }

    float3 faceNormalSpace = mul(cubeRotation, faceNormalLocal);
    float3 lightDirection = ExteriorMappingSafeNormalize(_LightDirection.xyz);
    float diffuse = saturate(dot(faceNormalSpace, lightDirection));
    float light = _Ambient + diffuse * _ShadingStrength;

    alpha = faceColor.a * _Tint.a;
    return faceColor.rgb * _Tint.rgb * light;
}
