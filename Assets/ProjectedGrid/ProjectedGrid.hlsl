//
// Created by haoc on 2024/6/12.
//
#ifndef PROJECTED_GRID_INCLUDED
#define PROJECTED_GRID_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

struct ProjectedGridData
{
    float4x4 ProjectorMatrix;
    float4x4 RangeConversionMatrix;
    float3 CameraPosition;
    float3 BasePlaneNormal;
    float3 BasePlaneOrigin;
    float BasePlaneHeight;
    float GridScale;
    float ProjectorElevation;
    float2 GridResolution;
    float MinDisplacement;
    float MaxDisplacement;
};

// 线与平面相交（齐次坐标）- 论文附录A
float4 LinePlaneIntersectionHomogeneous(float4 lineStart, float4 lineEnd, float planeHeight, out float t)
{
    float4 delta = lineEnd - lineStart;
    
    // 论文附录A公式：t = (w·h - y) / (dy - dw·h)
   t = (lineStart.w * planeHeight - lineStart.y) / (delta.y - delta.w * planeHeight);
    
    return lineStart + delta * t;
}


float4 LinePlaneIntersectionHomogeneous(
    float4 lineStart, 
    float4 lineEnd, 
    float4 plane, 
    out float t,
    out bool isValid)
{
    float4 delta = lineEnd - lineStart;

    float denominator = 
        plane.x * delta.x + 
        plane.y * delta.y + 
        plane.z * delta.z + 
        plane.w * delta.w;
    

    float numerator = -(
        plane.x * lineStart.x + 
        plane.y * lineStart.y + 
        plane.z * lineStart.z + 
        plane.w * lineStart.w);
    
    const float epsilon = 1e-6;
    isValid = true;
    
    if (abs(denominator) < epsilon)
    {
        // 检查分子是否也接近零（直线在平面内）
        if (abs(numerator) < epsilon)
        {
            // 直线在平面内，返回起点
            t = 0;
            isValid = true;
            return lineStart;
        }
        else
        {
            // 直线平行但不在平面内，无交点
            t = 0;
            isValid = false;
            return float4(0, 0, 0, 0);
        }
    }
    
    // 计算交点参数 t
    t = numerator / denominator;
    return lineStart + delta * t;
}


void CreateProjectedGridVertex(
    float2 uv, // [0,1] 范围输入
    ProjectedGridData gridData,
    out float3 worldPosition,
    out float2 finalUV)
{

    float4 projectorSpacePos = float4(uv.x, uv.y, 0, 1);
    
    // P_world = M_projector · P_projector
    // ndc 位置
    float4 worldLineStart = mul(gridData.ProjectorMatrix, 
                               float4(projectorSpacePos.xy, -1, 1));
    float4 worldLineEnd = mul(gridData.ProjectorMatrix, 
                             float4(projectorSpacePos.xy, 1, 1));
    

    float t = 0;
    bool isValid = false;
    //float4 intersection = LinePlaneIntersectionHomogeneous(worldLineStart, worldLineEnd, gridData.BasePlaneHeight, t);
    float4 intersection = LinePlaneIntersectionHomogeneous(worldLineStart, worldLineEnd, float4(0, 1, 0, -gridData.BasePlaneHeight), t,isValid);
        
    if( t >= 0.0)
    {
        worldPosition = intersection.xyz / intersection.w;
        finalUV = uv;
    }
       
    else
        finalUV = float2(0.0,0.0);      //用于调试没有交点的时候
}

#endif