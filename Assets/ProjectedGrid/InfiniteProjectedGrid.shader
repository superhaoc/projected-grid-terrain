//
// Created by haoc on 2023/3/5.
//
Shader "Custom/InfiniteProjectedGridWithHeight"
{
    Properties
    {
        _GridColor ("Grid Color", Color) = (1,1,1,1)
        _GridSize ("Grid Size", Float) = 1.0
        _GridThickness ("Grid Thickness", Float) = 0.02
        _FadeStart ("Fade Start", Float) = 50
        _FadeEnd ("Fade End", Float) = 100
        _BasePlaneHeight ("Base Plane Height", Float) = 0
        _ProjectorElevation ("Projector Elevation", Float) = 10
        _GridResolution ("Grid Resolution", Vector) = (64, 64, 0, 0)
        _DisplacementRange ("Displacement Range", Vector) = (-1, 1, 0, 0)
        
        // 高度场属性
        _HeightMap ("Height Map", 2D) = "white" {}
        _HeightScale ("Height Scale", Float) = 1.0
        _NoiseScale ("Noise Scale", Float) = 1.0
        _NoiseSpeed ("Noise Speed", Float) = 0.1
        
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalStrength ("Normal Strength", Float) = 1.0
        _LightDirection ("Light Direction", Vector) = (0.5, 0.5, 0.5, 0)
        _LightColor ("Light Color", Color) = (1,1,1,1)
        _AmbientColor ("Ambient Color", Color) = (0.2,0.2,0.2,1)
        _SpecularPower ("Specular Power", Float) = 32
        _SpecularIntensity ("Specular Intensity", Float) = 1.0
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderType" = "Transparent" 
            "Queue" = "Transparent" 
            "RenderPipeline" = "UniversalPipeline"
        }
        
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        
        Pass
        {
            Name "ProjectedGrid"
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "ProjectedGrid.hlsl"
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD1;  // 世界空间位置（已应用高度场）
                float2 uv : TEXCOORD2;
                float fade : TEXCOORD3;
                float3 normalWS : TEXCOORD5;    // 世界空间法线
        
            };
            
            CBUFFER_START(UnityPerMaterial)
                float4 _GridColor;
                float _GridSize;
                float _GridThickness;
                float _FadeStart;
                float _FadeEnd;
                float _BasePlaneHeight;
                float _ProjectorElevation;
                float2 _GridResolution;
                float2 _DisplacementRange;
                
                // 高度场属性
                float _HeightScale;
                float _NoiseScale;
                float _NoiseSpeed;
                
                // 法线属性
                float _NormalStrength;
                
                float4 _LightDirection;
                float4 _LightColor;
                float4 _AmbientColor;
                float _SpecularPower;
                float _SpecularIntensity;
            CBUFFER_END
            

            TEXTURE2D(_HeightMap);
            SAMPLER(sampler_HeightMap);
            
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);
            
            // 投影网格数据
            float4x4 _ProjectorMatrix;
            float4x4 _RangeConversionMatrix;
            float3 _CameraPositionWS;
            float _GridScale;
            
            // 计算网格衰减
            float CalculateGridFade(float3 worldPos)
            {
                float distanceToCamera = distance(worldPos, _CameraPositionWS);
                return 1.0 - smoothstep(_FadeStart, _FadeEnd, distanceToCamera);
            }
            
            float DrawGrid(float2 uv, float2 gridSize, float thickness)
            {
                float2 gridUV = uv * gridSize;
                float2 grid = abs(frac(gridUV - 0.5) - 0.5) / fwidth(gridUV);
                float lines = min(grid.x, grid.y);
                return 1.0 - smoothstep(thickness - 0.5, thickness + 0.5, lines);
            }
            
            float SampleHeightFieldWorld(float3 worldPos)
            {
                float2 heightUV = worldPos.xz ;
                float height =  SAMPLE_TEXTURE2D_LOD(_HeightMap, sampler_HeightMap, heightUV, 0).r;
                return height * _HeightScale;
            }

            float ProceduralHeightFieldWorld(float3 worldPos, float time)
            {
                // 使用世界空间坐标生成噪声，而不是投影仪空间UV
                float2 noiseUV = worldPos.xz * _NoiseScale / 100.0;
                
                // 多频噪声（Perlin噪声风格）
                float noise = 0.0;
                float amplitude = 2.5;
                float frequency = 1.0;
                

                for (int i = 0; i < 4; i++)
                {
                    float2 p = noiseUV * frequency + time * _NoiseSpeed;
                    float octaveNoise = sin(p.x) * 0.5 + 0.5;
                    octaveNoise += sin(p.y * 1.7 + 1.3) * 0.3;
                    octaveNoise = saturate(octaveNoise);
                    
                    noise += octaveNoise * amplitude;
                    
                    amplitude *= 0.5;
                    frequency *= 2.0;
                }
                
                return noise * _HeightScale;
            }
            
            // 组合高度场（可切换使用纹理或程序化噪声）
            float CalculateWorldHeight(float3 worldPos, float time)
            {
                #if defined(USE_TEXTURE_HEIGHT)
                    return SampleHeightFieldWorld(worldPos);
                #else
                    return ProceduralHeightFieldWorld(worldPos, time);
                #endif
            }

            // 计算法线（基于高度场）
            float3 CalculateNormalFromHeight(float3 worldPos, float delta = 50)
            {
                float heightX1 = CalculateWorldHeight(worldPos + float3(delta,0,0), 0);
                float heightX2 = CalculateWorldHeight(worldPos - float3(delta,0,0), 0);
                float heightZ1 = CalculateWorldHeight(worldPos + float3(0,0,delta), 0);
                float heightZ2 = CalculateWorldHeight(worldPos - float3(0,0,delta), 0);
                
                // 计算梯度
                float gradientX = (heightX1 - heightX2) / (2.0 * delta);
                float gradientZ = (heightZ1 - heightZ2) / (2.0 * delta);
                
                // 计算法线（梯度垂直于表面）,推导见知乎恐龙蛋
                //formula: noraml = -grad.xz + up

                float3 normal = normalize(float3(-gradientX, 1.0, -gradientZ));
                
                return normal;
            }

            
            Varyings vert(Attributes input)
            {
                Varyings output;
                
                ProjectedGridData gridData;
                gridData.ProjectorMatrix = _ProjectorMatrix;
                gridData.RangeConversionMatrix = _RangeConversionMatrix;
                gridData.CameraPosition = _CameraPositionWS;
                gridData.BasePlaneHeight = _BasePlaneHeight;
                gridData.GridScale = _GridScale;
                gridData.ProjectorElevation = _ProjectorElevation;
                gridData.GridResolution = _GridResolution;
                
                // 使用完整的投影网格算法创建基平面顶点
                float3 baseWorldPos;
                float2 finalUV;
                CreateProjectedGridVertex(input.uv, gridData, baseWorldPos, finalUV);
                
              
                // swiming artifacts,need hacking
                // 动态世界坐标采样height field 需要特殊处理,todo
                // 采样高度场

                float height = CalculateWorldHeight(baseWorldPos, _Time.y);
                
                // 应用高度场位移
                float3 displacedWorldPos = baseWorldPos;
                displacedWorldPos.y += height;

                float3 normal = CalculateNormalFromHeight(baseWorldPos);

                output.positionWS = displacedWorldPos;
                output.positionCS = TransformWorldToHClip(displacedWorldPos);
                output.uv = finalUV;
                output.fade = CalculateGridFade(displacedWorldPos);
                output.normalWS = normal;
                
                return output;
            }


            // 简单光照shade 
            float4 CalculateLighting(float3 position, float3 normal, float3 viewDir)
            {
                float4 ambient = _AmbientColor;
                float3 lightDir = normalize(_LightDirection.xyz);
                float NdotL = max(0, dot(normal, lightDir));
                float4 diffuse = _LightColor * NdotL;
                float3 halfDir = normalize(viewDir + lightDir);
                float NdotH = max(0, dot(normal, halfDir));
                float specular = pow(NdotH, _SpecularPower) * _SpecularIntensity;
                float4 lighting = ambient + diffuse + float4(specular, specular, specular, 0);
                return lighting;
            }

            
            half4 frag(Varyings input) : SV_Target
            {
                float3 viewDir = normalize(_CameraPositionWS - input.positionWS);
                float4 lighting = CalculateLighting(input.positionWS, input.normalWS, viewDir);
                float grid = DrawGrid(input.uv, _GridSize, _GridThickness);
                half4 color = _GridColor;
                color.a *= grid * input.fade;
                
                // 调试之用根据世界坐标位置着色
                float3 worldPos = input.positionWS;
                
                // 基于世界坐标的XZ位置添加颜色变化
                float2 gridCell = floor(worldPos.xz / 30);
                float cellHash = frac(sin(dot(gridCell, float2(12.9898, 78.233))) * 43758.5453);
                
                // 使用哈希值为不同网格区域着色
                color.rgb = lerp(color.rgb, 
                    float3(cellHash, frac(cellHash * 1.618), frac(cellHash * 2.718)), 
                    1.0);
                color.a = 1.0;
                return color * lighting;
            }
            ENDHLSL
        }
        
    }
}