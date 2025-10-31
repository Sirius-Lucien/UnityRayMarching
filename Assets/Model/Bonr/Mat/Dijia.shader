Shader "Unlit/DijiaDissolve"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _Specular ("Specular", Color) = (1,1,1,1)
        _Gloss ("Gloss", Range(8.0, 256)) = 20
        _BumpScale ("Bump Scale", Float) = 1.0
        
        // 消散效果属性
        _NoiseTex ("Noise Texture", 2D) = "white" {}
        _DissolveAmount ("Dissolve Amount", Range(0, 1)) = 0
        _EdgeWidth ("Edge Width", Range(0, 0.2)) = 0.05
        _EdgeColor ("Edge Color", Color) = (1, 0.5, 0, 1)
        _ScatterStrength ("Scatter Strength", Float) = 2.0
        _ScatterSpeed ("Scatter Speed", Float) = 1.0
        _UpwardForce ("Upward Force", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _NormalMap;
            float4 _Specular;
            float _Gloss;
            float _BumpScale;
            
            // 消散效果变量
            sampler2D _NoiseTex;
            float _DissolveAmount;
            float _EdgeWidth;
            float4 _EdgeColor;
            float _ScatterStrength;
            float _ScatterSpeed;
            float _UpwardForce;
    
            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct v2g
            {
                float4 pos : POSITION;
                float2 uv : TEXCOORD0;
                float4 TtoW0 : TEXCOORD1;
                float4 TtoW1 : TEXCOORD2;
                float4 TtoW2 : TEXCOORD3;
                float3 worldPos : TEXCOORD4;
            };

            struct g2f
            {
                float2 uv : TEXCOORD0;
                float4 pos : SV_POSITION;
                float4 TtoW0 : TEXCOORD1;
                float4 TtoW1 : TEXCOORD2;
                float4 TtoW2 : TEXCOORD3;
                float dissolveProgress : TEXCOORD4;
                SHADOW_COORDS(5)
            };

            v2g vert (appdata v)
            {
                v2g o;
                o.pos = v.vertex;
                o.uv = v.uv;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldPos = worldPos;
                
                float3 worldNormal = UnityObjectToWorldNormal(v.normal);
                float3 worldTangent = UnityObjectToWorldDir(v.tangent.xyz);
                float3 worldBinormal = cross(worldNormal, worldTangent) * v.tangent.w;
                
                o.TtoW0 = float4(worldTangent.x, worldBinormal.x, worldNormal.x, worldPos.x);
                o.TtoW1 = float4(worldTangent.y, worldBinormal.y, worldNormal.y, worldPos.y);
                o.TtoW2 = float4(worldTangent.z, worldBinormal.z, worldNormal.z, worldPos.z);
                
                return o;
            }

            [maxvertexcount(3)]
            void geom(triangle v2g input[3], inout TriangleStream<g2f> triStream)
            {
                // 计算三角面中心
                float3 center = (input[0].worldPos + input[1].worldPos + input[2].worldPos) / 3.0;
                
                // 基于中心位置采样噪声
                float2 noiseUV = center.xz * 0.1;
                float noise = tex2Dlod(_NoiseTex, float4(noiseUV, 0, 0)).r;
                
                // 计算消散进度
                float dissolveProgress = saturate((_DissolveAmount - noise) / _EdgeWidth);
                
                // 如果完全消散，不输出三角面
                if (noise < _DissolveAmount - _EdgeWidth)
                {
                    return;
                }
                
                // 计算飘散偏移
                float3 scatterDir = normalize(float3(
                    sin(noise * 10.0 + _Time.y * _ScatterSpeed),
                    _UpwardForce,
                    cos(noise * 15.0 + _Time.y * _ScatterSpeed)
                ));
                
                float3 offset = scatterDir * dissolveProgress * _ScatterStrength;
                
                // 输出变换后的三角面
                for(int i = 0; i < 3; i++)
                {
                    g2f o;
                    
                    // 应用飘散偏移
                    float4 worldPos = float4(input[i].worldPos + offset, 1.0);
                    float4 localPos = mul(unity_WorldToObject, worldPos);
                    
                    o.pos = UnityObjectToClipPos(localPos);
                    o.uv = input[i].uv;
                    o.TtoW0 = input[i].TtoW0;
                    o.TtoW1 = input[i].TtoW1;
                    o.TtoW2 = input[i].TtoW2;
                    o.dissolveProgress = dissolveProgress;
                    
                    // 更新世界坐标
                    o.TtoW0.w = worldPos.x;
                    o.TtoW1.w = worldPos.y;
                    o.TtoW2.w = worldPos.z;
                    
                    TRANSFER_SHADOW(o);
                    triStream.Append(o);
                }
            }

            fixed4 frag (g2f i) : SV_Target
            {
                // 获取世界坐标
                float3 worldPos = float3(i.TtoW0.w, i.TtoW1.w, i.TtoW2.w);
                
                // 计算光源方向
                fixed3 lightDir;
                if (_WorldSpaceLightPos0.w == 0.0) {
                    lightDir = normalize(_WorldSpaceLightPos0.xyz);
                } else {
                    lightDir = normalize(_WorldSpaceLightPos0.xyz - worldPos);
                }
                
                fixed3 viewDir = normalize(_WorldSpaceCameraPos - worldPos);
                
                // 法线贴图处理
                fixed3 bump = UnpackNormal(tex2D(_NormalMap, i.uv));
                bump.xy *= _BumpScale;
                bump.z = sqrt(1.0 - saturate(dot(bump.xy, bump.xy)));
                bump = normalize(half3(dot(i.TtoW0.xyz, bump), dot(i.TtoW1.xyz, bump), dot(i.TtoW2.xyz, bump)));
                
                // 漫反射计算
                float NdotL = max(0, dot(bump, lightDir));
                float4 albedo = tex2D(_MainTex, i.uv);
                float3 diffuse = albedo.rgb * _LightColor0.rgb * NdotL;

                // 镜面反射
                float3 halfDir = normalize(lightDir + viewDir);
                float NdotH = max(0, dot(bump, halfDir));
                float3 specular = _Specular.rgb * _LightColor0.rgb * pow(NdotH, _Gloss);
                
                // 环境光
                float3 ambient = UNITY_LIGHTMODEL_AMBIENT.rgb * albedo.rgb;
                
                // 边缘发光效果
                float3 edgeGlow = _EdgeColor.rgb * _EdgeColor.a * i.dissolveProgress;
                
                // 阴影
                fixed shadow = SHADOW_ATTENUATION(i);

                return fixed4(ambient + (diffuse + specular) * shadow + edgeGlow, 1.0);
            }
            ENDCG
        }
        
        // ForwardAdd通道也需要添加几何着色器
        Pass
        {
            Tags { "LightMode" = "ForwardAdd" }
            Blend One One
            
            CGPROGRAM
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag
            #pragma multi_compile_fwdadd_fullshadows
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _NormalMap;
            float4 _Specular;
            float _Gloss;
            float _BumpScale;
            
            sampler2D _NoiseTex;
            float _DissolveAmount;
            float _EdgeWidth;
            float4 _EdgeColor;
            float _ScatterStrength;
            float _ScatterSpeed;
            float _UpwardForce;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct v2g
            {
                float4 pos : POSITION;
                float2 uv : TEXCOORD0;
                float4 TtoW0 : TEXCOORD1;
                float4 TtoW1 : TEXCOORD2;
                float4 TtoW2 : TEXCOORD3;
                float3 worldPos : TEXCOORD4;
            };

            struct g2f
            {
                float2 uv : TEXCOORD0;
                float4 pos : SV_POSITION;
                float4 TtoW0 : TEXCOORD1;
                float4 TtoW1 : TEXCOORD2;
                float4 TtoW2 : TEXCOORD3;
                float dissolveProgress : TEXCOORD4;
                SHADOW_COORDS(5)
            };

            v2g vert (appdata v)
            {
                v2g o;
                o.pos = v.vertex;
                o.uv = v.uv;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldPos = worldPos;
                
                float3 worldNormal = UnityObjectToWorldNormal(v.normal);
                float3 worldTangent = UnityObjectToWorldDir(v.tangent.xyz);
                float3 worldBinormal = cross(worldNormal, worldTangent) * v.tangent.w;
                
                o.TtoW0 = float4(worldTangent.x, worldBinormal.x, worldNormal.x, worldPos.x);
                o.TtoW1 = float4(worldTangent.y, worldBinormal.y, worldNormal.y, worldPos.y);
                o.TtoW2 = float4(worldTangent.z, worldBinormal.z, worldNormal.z, worldPos.z);
                
                return o;
            }

            [maxvertexcount(3)]
            void geom(triangle v2g input[3], inout TriangleStream<g2f> triStream)
            {
                float3 center = (input[0].worldPos + input[1].worldPos + input[2].worldPos) / 3.0;
                float2 noiseUV = center.xz * 0.1;
                float noise = tex2Dlod(_NoiseTex, float4(noiseUV, 0, 0)).r;
                float dissolveProgress = saturate((_DissolveAmount - noise) / _EdgeWidth);
                
                if (noise < _DissolveAmount - _EdgeWidth)
                {
                    return;
                }
                
                float3 scatterDir = normalize(float3(
                    sin(noise * 10.0 + _Time.y * _ScatterSpeed),
                    _UpwardForce,
                    cos(noise * 15.0 + _Time.y * _ScatterSpeed)
                ));
                
                float3 offset = scatterDir * dissolveProgress * _ScatterStrength;
                
                for(int i = 0; i < 3; i++)
                {
                    g2f o;
                    float4 worldPos = float4(input[i].worldPos + offset, 1.0);
                    float4 localPos = mul(unity_WorldToObject, worldPos);
                    
                    o.pos = UnityObjectToClipPos(localPos);
                    o.uv = input[i].uv;
                    o.TtoW0 = input[i].TtoW0;
                    o.TtoW1 = input[i].TtoW1;
                    o.TtoW2 = input[i].TtoW2;
                    o.dissolveProgress = dissolveProgress;
                    o.TtoW0.w = worldPos.x;
                    o.TtoW1.w = worldPos.y;
                    o.TtoW2.w = worldPos.z;
                    TRANSFER_SHADOW(o);
                    triStream.Append(o);
                }
            }

            fixed4 frag (g2f i) : SV_Target
            {
                float3 worldPos = float3(i.TtoW0.w, i.TtoW1.w, i.TtoW2.w);
                
                fixed3 lightDir;
                fixed atten = 1.0;
                
                if (_WorldSpaceLightPos0.w == 0.0) {
                    lightDir = normalize(_WorldSpaceLightPos0.xyz);
                } else {
                    float3 lightVec = _WorldSpaceLightPos0.xyz - worldPos;
                    lightDir = normalize(lightVec);
                    atten = 1.0 / (1.0 + length(lightVec) * length(lightVec) * 0.01);
                }
                
                fixed3 viewDir = normalize(_WorldSpaceCameraPos - worldPos);
                
                fixed3 bump = UnpackNormal(tex2D(_NormalMap, i.uv));
                bump.xy *= _BumpScale;
                bump.z = sqrt(1.0 - saturate(dot(bump.xy, bump.xy)));
                bump = normalize(half3(dot(i.TtoW0.xyz, bump), dot(i.TtoW1.xyz, bump), dot(i.TtoW2.xyz, bump)));
                
                float NdotL = pow(0.5*dot(bump, lightDir)+0.5,0.5);
                float4 albedo = tex2D(_MainTex, i.uv);
                float3 diffuse = albedo.rgb * _LightColor0.rgb * NdotL * atten;

                float3 halfDir = normalize(lightDir + viewDir);
                float NdotH = max(0, dot(bump, halfDir));
                float3 specular = _Specular.rgb * _LightColor0.rgb * pow(NdotH, _Gloss) * atten;
                
                fixed shadow = SHADOW_ATTENUATION(i);
                
                return fixed4((diffuse + specular) * shadow, 1.0);
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}