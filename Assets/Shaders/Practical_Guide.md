# 实战：从零开始模仿GitHub架构

## 目标
创建一个简化版的多Pass shader，学习GitHub架构的精髓。

---

## Step 1: 创建文件结构

```
Assets/Shaders/Practice/
├── MyToonShader.shader              # 主shader文件
├── MyToonShader_Shared.hlsl         # 共享代码
├── MyToonShader_Lighting.hlsl       # 光照逻辑
└── Utils/
    └── MyOutlineUtil.hlsl           # 工具函数
```

---

## Step 2: 编写工具函数（学习独立封装）

### MyOutlineUtil.hlsl
```hlsl
#pragma once

// 描边宽度随距离和FOV调整
float GetOutlineDistanceMultiplier(float positionVS_Z)
{
    // 近处变细，远处变粗
    float distanceFix = abs(positionVS_Z);
    
    // FOV补偿（FOV越大，物体看起来越小，描边需要变粗）
    float fov = atan(1.0f / unity_CameraProjection._m11) * 2.0 * 57.3; // Rad2Deg
    float fovFix = fov;
    
    return distanceFix * fovFix * 0.00005;
}

// 沿法线推出顶点（描边的核心）
float3 ExpandVertexAlongNormal(float3 positionWS, float3 normalWS, float width, float distanceMultiplier)
{
    return positionWS + normalWS * width * distanceMultiplier;
}
```

**学习点:**
- `#pragma once` 代替传统的 `#ifndef/#define/#endif`
- 函数职责单一，命名清晰
- 数学逻辑封装在独立函数中

---

## Step 3: 定义数据结构（数据驱动）

### MyToonShader_Shared.hlsl（开头部分）
```hlsl
#pragma once

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "MyOutlineUtil.hlsl"

// 顶点输入
struct Attributes
{
    float3 positionOS   : POSITION;
    float3 normalOS     : NORMAL;
    float2 uv           : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

// 顶点到片元
struct Varyings
{
    float4 positionCS   : SV_POSITION;
    float2 uv           : TEXCOORD0;
    float3 positionWS   : TEXCOORD1;
    float3 normalWS     : TEXCOORD2;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

// 表面数据（材质属性）
struct MySurfaceData
{
    half3 albedo;
    half3 emission;
    half alpha;
};

// 光照数据（场景信息）
struct MyLightingData
{
    half3 normalWS;
    half3 viewDirWS;
    float3 positionWS;
};
```

**学习点:**
- 结构体命名清晰（Attributes, Varyings, SurfaceData, LightingData）
- VR支持的宏（UNITY_VERTEX_INPUT_INSTANCE_ID等）
- 数据和逻辑分离

---

## Step 4: CBUFFER（SRP Batching）

### MyToonShader_Shared.hlsl（继续）
```hlsl
// 纹理（不在CBUFFER中）
TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

// 材质属性（必须在CBUFFER中，实现SRP Batching）
CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half _Cutoff;
    float _OutlineWidth;
    half3 _OutlineColor;
    half _CelShadeMidPoint;
    half _CelShadeSoftness;
CBUFFER_END

// 特殊uniform（不属于材质，在CBUFFER外）
float3 _LightDirection;
```

**学习点:**
- 纹理和采样器用 `TEXTURE2D` + `SAMPLER` 宏
- 所有材质参数放在 `UnityPerMaterial` CBUFFER中
- 非材质参数（如光源方向）放在外面

---

## Step 5: 共享顶点着色器（关键！）

### MyToonShader_Shared.hlsl（继续）
```hlsl
// 这个函数会被多个Pass使用！
Varyings VertexShaderWork(Attributes input)
{
    Varyings output;
    
    // GPU Instancing和VR支持
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    
    // 获取基础变换数据
    VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS);
    VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);
    
    float3 positionWS = vertexInput.positionWS;
    
    // ====================================================
    // 关键：通过宏控制不同Pass的行为
    // ====================================================
#ifdef MY_SHADER_IS_OUTLINE_PASS
    // 只有Outline Pass会执行这段代码
    float distMul = GetOutlineDistanceMultiplier(vertexInput.positionVS.z);
    positionWS = ExpandVertexAlongNormal(
        positionWS, 
        normalInput.normalWS, 
        _OutlineWidth, 
        distMul
    );
#endif
    
    // 公共代码
    output.positionCS = TransformWorldToHClip(positionWS);
    output.positionWS = positionWS;
    output.normalWS = normalInput.normalWS;
    output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
    
#ifdef MY_SHADER_APPLY_SHADOW_BIAS
    // 只有ShadowCaster Pass会执行
    float4 positionCS = TransformWorldToHClip(
        ApplyShadowBias(positionWS, output.normalWS, _LightDirection)
    );
    #if UNITY_REVERSED_Z
        positionCS.z = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
    #else
        positionCS.z = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
    #endif
    output.positionCS = positionCS;
#endif
    
    return output;
}
```

**学习点:**
- **一个函数，多种行为**：通过 `#ifdef` 实现
- `MY_SHADER_IS_OUTLINE_PASS` 在Outline Pass中定义
- `MY_SHADER_APPLY_SHADOW_BIAS` 在ShadowCaster Pass中定义
- 代码复用率极高

---

## Step 6: 数据初始化函数

### MyToonShader_Shared.hlsl（继续）
```hlsl
// 初始化表面数据
MySurfaceData InitializeSurfaceData(Varyings input)
{
    MySurfaceData data;
    
    half4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
    data.albedo = baseMap.rgb * _BaseColor.rgb;
    data.alpha = baseMap.a * _BaseColor.a;
    data.emission = half3(0, 0, 0);
    
    // Alpha裁剪
#ifdef _USE_ALPHA_CLIPPING
    clip(data.alpha - _Cutoff);
#endif
    
    return data;
}

// 初始化光照数据
MyLightingData InitializeLightingData(Varyings input)
{
    MyLightingData data;
    data.positionWS = input.positionWS;
    data.normalWS = normalize(input.normalWS);
    data.viewDirWS = SafeNormalize(GetCameraPositionWS() - input.positionWS);
    return data;
}
```

---

## Step 7: 光照计算（独立文件）

### MyToonShader_Lighting.hlsl
```hlsl
#pragma once

// 卡通光照的核心算法
half3 CalculateToonLighting(MySurfaceData surface, MyLightingData lighting)
{
    // 获取主光源
    Light mainLight = GetMainLight();
    
    // Lambert + 卡通阶梯化
    half NdotL = dot(lighting.normalWS, mainLight.direction);
    half celShade = smoothstep(
        _CelShadeMidPoint - _CelShadeSoftness,
        _CelShadeMidPoint + _CelShadeSoftness,
        NdotL
    );
    
    // 漫反射
    half3 diffuse = surface.albedo * mainLight.color * celShade;
    
    // 环境光
    half3 ambient = surface.albedo * SampleSH(lighting.normalWS) * 0.3;
    
    return diffuse + ambient + surface.emission;
}
```

**学习点:**
- 光照逻辑完全独立
- 只依赖数据结构，不依赖具体实现
- 修改光照算法只需编辑这个文件

---

## Step 8: 主片元着色器

### MyToonShader_Shared.hlsl（继续）
```hlsl
#include "MyToonShader_Lighting.hlsl"

// 主片元着色器（会被ForwardLit和Outline Pass使用）
half4 FragmentShaderWork(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    
    // Step 1: 初始化数据
    MySurfaceData surface = InitializeSurfaceData(input);
    MyLightingData lighting = InitializeLightingData(input);
    
    // Step 2: 计算光照
    half3 color = CalculateToonLighting(surface, lighting);
    
    // Step 3: 特殊处理（通过宏）
#ifdef MY_SHADER_IS_OUTLINE_PASS
    // Outline Pass：使用描边颜色
    color *= _OutlineColor;
#endif
    
    return half4(color, surface.alpha);
}
```

---

## Step 9: 主Shader文件（组装Pass）

### MyToonShader.shader
```hlsl
Shader "Practice/MyToonShader"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        
        [Header(Toon Shading)]
        _CelShadeMidPoint ("Cel Shade MidPoint", Range(-1,1)) = 0
        _CelShadeSoftness ("Cel Shade Softness", Range(0,1)) = 0.05
        
        [Header(Outline)]
        _OutlineWidth ("Outline Width", Range(0,4)) = 1
        _OutlineColor ("Outline Color", Color) = (0,0,0,1)
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }
        
        // 共享的编译指令
        HLSLINCLUDE
        #pragma shader_feature_local _USE_ALPHA_CLIPPING
        ENDHLSL
        
        // ========================================
        // Pass #0: 主渲染Pass
        // ========================================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            
            Cull Back
            
            HLSLPROGRAM
            #pragma vertex VertexShaderWork
            #pragma fragment FragmentShaderWork
            
            // URP关键字
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog
            
            // GPU Instancing
            #pragma multi_compile_instancing
            
            // 不定义任何特殊宏，使用默认行为
            #include "MyToonShader_Shared.hlsl"
            
            ENDHLSL
        }
        
        // ========================================
        // Pass #1: 描边Pass
        // ========================================
        Pass
        {
            Name "Outline"
            
            Cull Front  // ✅ 关键：只渲染背面
            
            HLSLPROGRAM
            #pragma vertex VertexShaderWork
            #pragma fragment FragmentShaderWork
            
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            
            // ✅ 定义宏，激活描边代码
            #define MY_SHADER_IS_OUTLINE_PASS
            
            #include "MyToonShader_Shared.hlsl"
            
            ENDHLSL
        }
        
        // ========================================
        // Pass #2: 阴影投射Pass
        // ========================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            
            ZWrite On
            ZTest LEqual
            ColorMask 0
            
            HLSLPROGRAM
            #pragma vertex VertexShaderWork
            #pragma fragment ShadowFragment
            
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            
            // ✅ 定义宏，激活阴影偏移代码
            #define MY_SHADER_APPLY_SHADOW_BIAS
            
            #include "MyToonShader_Shared.hlsl"
            
            // 简单的阴影片元着色器
            half4 ShadowFragment(Varyings input) : SV_Target
            {
                MySurfaceData surface = InitializeSurfaceData(input);
                return 0;
            }
            
            ENDHLSL
        }
    }
    
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
```

**学习点:**
- 3个Pass，但只写了1份顶点着色器代码
- 通过 `#define` 控制不同Pass的行为
- ForwardLit: 无宏定义，正常渲染
- Outline: 定义 `MY_SHADER_IS_OUTLINE_PASS`，沿法线推出
- ShadowCaster: 定义 `MY_SHADER_APPLY_SHADOW_BIAS`，修复阴影偏移

---

## 架构对比图

### 传统方式
```
ForwardLit Pass {
    vertex: 100行代码
    fragment: 150行代码
}

Outline Pass {
    vertex: 105行代码 (95行重复)
    fragment: 160行代码 (140行重复)
}

ShadowCaster Pass {
    vertex: 80行代码 (70行重复)
    fragment: 30行代码
}

总计：625行代码，80%重复
```

### GitHub架构方式
```
Shared.hlsl {
    VertexShaderWork: 50行 (被3个Pass复用)
    FragmentShaderWork: 80行 (被2个Pass复用)
    
    #ifdef MY_SHADER_IS_OUTLINE_PASS
        // +10行描边特有代码
    #endif
    
    #ifdef MY_SHADER_APPLY_SHADOW_BIAS
        // +15行阴影特有代码
    #endif
}

Main.shader {
    Pass "ForwardLit" { #include "Shared.hlsl" }
    Pass "Outline" { #define OUTLINE; #include "Shared.hlsl" }
    Pass "ShadowCaster" { #define SHADOW; #include "Shared.hlsl" }
}

总计：155行代码，0%重复 ✨
```

---

## 工作流程图

```
开发流程：

1. 修改光照算法
   ↓
   编辑 MyToonShader_Lighting.hlsl
   ↓
   所有Pass自动更新 ✅

2. 添加新的材质属性
   ↓
   在CBUFFER中添加
   ↓
   在InitializeSurfaceData中使用
   ↓
   完成 ✅

3. 调整描边效果
   ↓
   编辑 MyOutlineUtil.hlsl
   ↓
   或者调整宏内的代码
   ↓
   只影响Outline Pass ✅

4. 添加新的Pass
   ↓
   在.shader中添加Pass{}
   ↓
   定义新的宏
   ↓
   在Shared.hlsl中添加#ifdef块
   ↓
   完成 ✅
```

---

## 关键要点总结

### 1. 单一数据源
所有Pass包含同一个 `Shared.hlsl`，修改一处，所有Pass更新。

### 2. 宏控制行为
```hlsl
#ifdef MY_SHADER_IS_OUTLINE_PASS
    // 描边特有代码
#endif
```

### 3. 数据结构驱动
```hlsl
SurfaceData → LightingData → Lighting → FinalColor
```

### 4. 函数复用
```hlsl
VertexShaderWork()  // 被3个Pass使用
FragmentShaderWork() // 被2个Pass使用
```

### 5. 职责分离
- `.shader` → 定义Pass和属性
- `_Shared.hlsl` → 结构体和主函数
- `_Lighting.hlsl` → 光照算法
- `Utils/` → 工具函数

---

## 下一步实践

1. **复制这个结构**，创建你自己的多Pass shader
2. **添加新功能**，比如边缘光、高光等
3. **优化性能**，测试SRP Batching是否生效
4. **阅读GitHub源码**，学习更多高级技巧

这就是工业级shader的开发方式！🚀
