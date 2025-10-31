Shader "Universal Render Pipeline/SimplePBR"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _Metallic ("Metallic", Range(0,1)) = 0
        _Smoothness ("Smoothness", Range(0,1)) = 0.5
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1.0
        
        // 描边属性
        _OutlineWidth ("Outline Width", Range(0.01, 0.1)) = 0.03
        _OutlineColor ("Outline Color", Color) = (0,0,0,1)
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        
        // 描边Pass
        Pass
        {
            Name "Outline"
            
            Cull Front
            ZWrite On
            ZTest LEqual
            
            HLSLPROGRAM
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            // Outline Pass 的结构体定义
            struct OutlineAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };
            
            struct OutlineVaryings
            {
                float4 positionCS : SV_POSITION;
            };
            
            // Outline材质属性
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _Metallic;
                float _Smoothness;
                float _BumpScale;
                float _OutlineWidth;
                float4 _OutlineColor;
            CBUFFER_END
            
            OutlineVaryings OutlineVert(OutlineAttributes input)
            {
                OutlineVaryings output;
                
                // 在世界空间中沿法线扩展
                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);
                float3 worldNormal = TransformObjectToWorldNormal(input.normalOS);
                worldPos += worldNormal * _OutlineWidth;
                
                output.positionCS = TransformWorldToHClip(worldPos);
                return output;
            }
            
            half4 OutlineFrag(OutlineVaryings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
        
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 tangentWS : TEXCOORD3;
            };
            
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);
            
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _Metallic;
                float _Smoothness;
                float _BumpScale;
                float _OutlineWidth;
                float4 _OutlineColor;
            CBUFFER_END
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = float4(normalInputs.tangentWS, input.tangentOS.w);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                
                return output;
            }
            
            half4 frag(Varyings input) : SV_Target
            {
                // 采样贴图
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                
                // 构建TBN矩阵并转换法线到世界空间
                float3 bitangentWS = cross(input.normalWS, input.tangentWS.xyz) * input.tangentWS.w;
                float3x3 tangentToWorld = float3x3(input.tangentWS.xyz, bitangentWS, input.normalWS);
                float3 normalWS = normalize(mul(normalTS, tangentToWorld));
                
                // 获取主光源
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                
                // 半兰伯特光照计算
                float3 lightDir = normalize(mainLight.direction);
                float NdotL = dot(normalWS, lightDir);
                float halfLambert = NdotL * 0.5 + 0.5; // 半兰伯特公式
                
                // 基础漫反射
                float3 diffuse = albedo.rgb * mainLight.color * halfLambert * mainLight.shadowAttenuation;
                
                // 简单的环境光
                float3 ambient = albedo.rgb * 0.2;
                
                // 简单的镜面反射（Blinn-Phong）
                float3 viewDir = normalize(_WorldSpaceCameraPos - input.positionWS);
                float3 halfDir = normalize(lightDir + viewDir);
                float NdotH = saturate(dot(normalWS, halfDir));
                float specular = pow(NdotH, lerp(1, 256, _Smoothness)) * _Metallic;
                float3 specularColor = lerp(0.04, albedo.rgb, _Metallic);
                
                float3 finalColor = ambient + diffuse + specularColor * specular * mainLight.color;
                
                return half4(finalColor, albedo.a);
            }
            ENDHLSL
        }
        
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            
            ZWrite On
            ZTest LEqual
            ColorMask 0
            
            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
    }
}
