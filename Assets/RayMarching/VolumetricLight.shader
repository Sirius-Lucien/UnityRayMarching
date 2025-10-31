Shader "Hidden/VolumetricLight"
{
    Properties
    {
        // === Ray Marching ===
        _Steps          ("Steps (Raymarch steps)", Int) = 32  // 游戏标准：16-32步
        _MaxDistance    ("Max Distance", Range(0.0,500.0)) = 50.0
        // === Phase Function ===
        _PhaseG         ("Phase G (Henyey-Greenstein)", Range(-0.99,0.99)) = 0.5
        // === Scattering Properties ===
        _Scattering     ("Scattering Intensity", Range(0,50)) = 10.0
        _Density        ("Density", Range(0,1)) = 0.1
        _Extinction     ("Extinction", Range(0,1)) = 0.1
        // === Noise & Dithering ===
        _BlueNoiseTex   ("Blue Noise Texture", 2D) = "white" {}
        _BlueNoiseSize  ("Blue Noise Texture Size", Float) = 64.0
        _JitterStrength ("Jitter Strength", Range(0,1)) = 1.0
        _DitheringScale ("Dithering Scale", Float) = 1.0
        // === 游戏级优化 ===
        _NoiseIntensity ("Noise Intensity", Range(0,2)) = 1.0
        _TemporalStrength ("Temporal Jitter Strength", Range(0,1)) = 0.3  // 控制闪烁强度
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline"}
        Pass
        {
            ZWrite Off
            ZTest Always
            Blend One One
            HLSLPROGRAM
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            #pragma vertex Vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            
            float4x4 _CameraInverseProjection;
            float4x4 _CameraToWorld;
            sampler2D _BlueNoiseTex;

            // 参数
            int _Steps;
            float _MaxDistance;
            float _PhaseG;
            float _Scattering;
            float _Density;
            float _Extinction;
            float _BlueNoiseSize;
            float _JitterStrength;
            float _DitheringScale;
            float _NoiseIntensity;
            float _TemporalStrength;

            // 3D噪声函数 - 用于给密度添加variation（定义在前面）
            float Hash13(float3 p3)
            {
                p3  = frac(p3 * 0.1031);
                p3 += dot(p3, p3.zyx + 31.32);
                return frac((p3.x + p3.y) * p3.z);
            }

            // Henyey-Greenstein相位函数
            float PhaseHG(float3 viewDir, float3 lightDir)
            {
                float cosTheta = dot(-viewDir, lightDir);
                float g2 = _PhaseG * _PhaseG;
                return (1.0 - g2) / (4.0 * PI * pow(abs(1.0 + g2 - 2.0 * _PhaseG * cosTheta), 1.5));
            }

            // 体积密度函数（优化版，减少闪烁）
            float GetDensity(float3 worldPos)
            {
                // 基础密度
                float baseDensity = _Density;
                
                // ★ 改进：使用更大尺度的噪声，避免高频闪烁
                // 之前用0.1太细了，改成0.05让变化更缓慢
                float noise = Hash13(worldPos * 0.05) * 0.2 + 0.8; // 0.8-1.0范围（变化更小）
                
                return baseDensity * noise;
            }
            
            // 阴影采样
            float GetShadow(float3 worldPos)
            {
                float4 shadowCoord = TransformWorldToShadowCoord(worldPos);
                Light mainLight = GetMainLight(shadowCoord);
                return mainLight.shadowAttenuation;
            }

            // 改进的Blue Noise采样（减少闪烁版本）
            float SampleBlueNoise(float2 screenPos)
            {
                // 使用屏幕像素坐标对纹理尺寸取模，确保平铺
                float2 noiseUV = fmod(screenPos, _BlueNoiseSize) / _BlueNoiseSize;
                
                // ★ 关键改进：使用更慢、更平滑的时间变化
                // 之前用60fps太快了，现在改成更慢的变化
                float slowTime = _Time.y * 0.5;  // 减慢2倍
                float frameIndex = floor(slowTime);
                
                float goldenRatio = 1.61803398875;
                float goldenAngle = goldenRatio * PI * 2.0;
                
                // R2序列，但应用强度可控
                float2 frameOffset = frac(frameIndex * float2(goldenRatio, goldenAngle)) * _TemporalStrength;
                noiseUV = frac(noiseUV + frameOffset);
                
                return tex2D(_BlueNoiseTex, noiseUV).r;
            }
            
            // Interleaved Gradient Noise - 改进版（减少闪烁）
            // 参考：http://www.iryoku.com/next-generation-post-processing-in-call-of-duty-advanced-warfare
            float InterleavedGradientNoise(float2 screenPos)
            {
                // ★ 关键改进：减慢时间变化速度
                float slowTime = _Time.y * 0.5;  // 减慢2倍
                float frameOffset = frac(slowTime) * _TemporalStrength;
                screenPos += frameOffset * 5.4251;
                
                float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
                return frac(magic.z * frac(dot(screenPos, magic.xy)));
            }

            // 游戏级体积光散射（优化版）
            float3 VolumetricScattering(float3 rayStart, float3 rayDir, float rayLength, float2 noise)
            {
                Light mainLight = GetMainLight();
                float3 lightDir = normalize(mainLight.direction);
                float3 lightColor = mainLight.color;
                
                rayLength = min(rayLength, _MaxDistance);
                float stepSize = rayLength / float(_Steps);
                
                // ★ 关键技巧1：更激进的Jittering
                // 游戏中通常使用0.5-1.5范围的jitter，而不是0-1
                // 这样可以让采样点更分散，减少条纹
                float jitter = noise.x * _JitterStrength;
                // 改进：让jitter在[-0.5, 0.5]范围，这样采样更均匀
                jitter = jitter - 0.5 + 0.5; // 转换到[0,1]
                
                // ★ 关键技巧2：每个像素使用不同的起始偏移
                // IGN提供per-pixel variation
                float pixelJitter = noise.y;
                float combinedJitter = frac(jitter + pixelJitter);
                
                float3 currentPos = rayStart + rayDir * stepSize * combinedJitter;
                
                float3 step = rayDir * stepSize;
                float3 scatteredLight = 0.0;
                float transmittance = 1.0;
                
                // ★ 关键技巧3：更强的采样dithering
                // 每个采样点也添加一点扰动
                float sampleJitter = (pixelJitter - 0.5) * stepSize * 0.1 * _NoiseIntensity;
                
                for (int i = 0; i < _Steps; i++)
                {
                    // 检查是否超出边界
                    if (distance(currentPos, rayStart) >= rayLength) break;
                    
                    // 对当前采样位置添加微小扰动（帮助打破规律性）
                    float3 samplePos = currentPos + rayDir * sampleJitter * float(i & 1); // 交替扰动
                    
                    float density = GetDensity(samplePos);
                    if (density > 0.0001)
                    {
                        float shadow = GetShadow(samplePos);
                        float phase = PhaseHG(rayDir, lightDir);
                        float scatter = density * phase * shadow * _Scattering;
                        
                        // 累积散射光
                        scatteredLight += lightColor * scatter * transmittance * stepSize;
                        
                        // Beer-Lambert衰减
                        transmittance *= exp(-_Extinction * density * stepSize);
                        
                        // 早期退出优化
                        if (transmittance < 0.01) break;
                    }
                    currentPos += step;
                }
                
                return scatteredLight;
            }

            float4 frag(Varyings i) : SV_Target 
            {
                float2 uv = i.texcoord;
                real depth = SampleSceneDepth(uv);
                
                // 重建世界坐标
                float3 worldPos = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                
                // 计算射线方向和长度
                float3 rayDir = normalize(worldPos - _WorldSpaceCameraPos);
                float rayLength = distance(worldPos, _WorldSpaceCameraPos);
                
                // ★ 游戏技巧：使用像素坐标而不是UV来采样噪声
                // 这样在不同分辨率下噪声模式保持一致
                float2 screenPos = i.texcoord * _ScreenParams.xy;
                
                // 采样两种噪声
                float blueNoise = SampleBlueNoise(screenPos);
                float ignNoise = InterleavedGradientNoise(screenPos);
                
                // 组合噪声：Blue Noise用于temporal，IGN用于spatial
                float2 combinedNoise = float2(blueNoise, ignNoise);
                
                // 计算体积散射光照
                float3 volumetricLight = VolumetricScattering(_WorldSpaceCameraPos, rayDir, rayLength, combinedNoise);
                
                // ★ 更强的Dithering
                // 游戏中通常会用更明显的dithering来掩盖banding
                float dither = (ignNoise - 0.5) * _DitheringScale * 0.02 * _NoiseIntensity;
                volumetricLight = max(0, volumetricLight + dither);
                
                return float4(volumetricLight, 1.0);
            }
            ENDHLSL
        }
    }
}