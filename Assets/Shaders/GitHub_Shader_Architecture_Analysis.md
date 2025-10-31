# GitHub开源Shader架构深度分析

## 项目来源
- **作者**: ColinLeung-NiloCat
- **项目**: UnityURPToonLitShaderExample
- **特点**: 工业级卡通渲染Shader，被广泛用于商业项目

---

## 🎯 核心设计理念

### 1. **单Shader多Pass架构**
传统做法是每个效果一个shader，这个架构是：
```
一个.shader文件 + 多个.hlsl模块 = 完整的渲染系统
```

**文件结构:**
```
SimpleURPToonLitOutlineExample.shader          主Shader文件（定义Pass和属性）
├── SimpleURPToonLitOutlineExample_Shared.hlsl  共享代码（结构体、顶点/片元函数）
├── SimpleURPToonLitOutlineExample_LightingEquation.hlsl  光照算法
└── 工具模块
    ├── NiloOutlineUtil.hlsl      描边工具
    ├── NiloZOffset.hlsl          深度偏移工具
    └── NiloInvLerpRemap.hlsl     数值重映射工具
```

---

## 📐 架构特点详解

### 特点1: Pass级别的代码复用

#### 传统做法（不好）
```hlsl
Pass "ForwardLit" {
    // 写200行代码
}
Pass "Outline" {
    // 又写200行代码，90%重复
}
Pass "ShadowCaster" {
    // 再写100行代码，70%重复
}
```

#### 这个架构的做法（优秀）
```hlsl
// Shader文件中
HLSLINCLUDE
    // 共享的编译指令
    #pragma shader_feature_local_fragment _UseAlphaClipping
ENDHLSL

Pass "ForwardLit" {
    #define ToonShaderIsOutline  // ❌ 不定义
    #include "Shared.hlsl"        // ✅ 包含共享代码
}

Pass "Outline" {
    #define ToonShaderIsOutline  // ✅ 定义这个宏
    #include "Shared.hlsl"        // ✅ 包含同样的共享代码
}
```

**工作原理:**
- 所有Pass都包含同一个`Shared.hlsl`
- 通过`#define`宏控制不同Pass的行为
- 在`Shared.hlsl`内部使用`#ifdef`来区分

```hlsl
// 在Shared.hlsl中
Varyings VertexShaderWork(Attributes input)
{
    // 公共代码
    float3 positionWS = vertexInput.positionWS;

#ifdef ToonShaderIsOutline
    // 只有Outline Pass会执行这段
    positionWS = TransformPositionWSToOutlinePositionWS(...);
#endif

    // 公共代码继续
    output.positionCS = TransformWorldToHClip(positionWS);
}
```

---

### 特点2: 数据驱动的设计

#### 自定义数据结构
```hlsl
// 表面数据（材质属性）
struct ToonSurfaceData
{
    half3   albedo;      // 反照率
    half    alpha;       // 透明度
    half3   emission;    // 自发光
    half    occlusion;   // 遮蔽
};

// 光照数据（场景信息）
struct ToonLightingData
{
    half3   normalWS;           // 世界空间法线
    float3  positionWS;         // 世界空间位置
    half3   viewDirectionWS;    // 视图方向
    float4  shadowCoord;        // 阴影坐标
};
```

**好处:**
- 数据和逻辑分离
- 函数签名清晰
- 易于扩展（加字段不影响函数签名）

#### 流程化的片元着色器
```hlsl
half4 ShadeFinalColor(Varyings input) : SV_TARGET
{
    // 步骤1: 准备数据
    ToonSurfaceData surfaceData = InitializeSurfaceData(input);
    ToonLightingData lightingData = InitializeLightingData(input);
    
    // 步骤2: 计算光照
    half3 color = ShadeAllLights(surfaceData, lightingData);
    
    // 步骤3: 后处理（描边、雾效等）
#ifdef ToonShaderIsOutline
    color = ConvertSurfaceColorToOutlineColor(color);
#endif
    color = ApplyFog(color, input);
    
    return half4(color, surfaceData.alpha);
}
```

**流程图:**
```
输入(Varyings) 
    ↓
[InitializeSurfaceData]  → ToonSurfaceData
    ↓
[InitializeLightingData] → ToonLightingData
    ↓
[ShadeAllLights]         → 光照计算
    ↓
[后处理]                  → 描边、雾效
    ↓
输出(half4 颜色)
```

---

### 特点3: 光照系统的模块化

```hlsl
// ShadeAllLights函数拆分成多个子函数
half3 ShadeAllLights(ToonSurfaceData surfaceData, ToonLightingData lightingData)
{
    // 1. 间接光（环境光）
    half3 indirectResult = ShadeGI(surfaceData, lightingData);
    
    // 2. 主光源（太阳光）
    Light mainLight = GetMainLight();
    half3 mainLightResult = ShadeSingleLight(surfaceData, lightingData, mainLight, false);
    
    // 3. 额外光源（点光源、聚光灯）
    half3 additionalLightSumResult = 0;
#ifdef _ADDITIONAL_LIGHTS
    for (int i = 0; i < additionalLightsCount; ++i)
    {
        Light light = GetAdditionalPerObjectLight(...);
        additionalLightSumResult += ShadeSingleLight(surfaceData, lightingData, light, true);
    }
#endif
    
    // 4. 自发光
    half3 emissionResult = ShadeEmission(surfaceData, lightingData);
    
    // 5. 合成所有光照
    return CompositeAllLightResults(indirectResult, mainLightResult, 
                                     additionalLightSumResult, emissionResult, 
                                     surfaceData, lightingData);
}
```

**为什么要这样做？**
- 每个函数职责单一
- 想修改某个光照效果？只需编辑对应函数
- 想添加新光源类型？只需加一个函数调用

---

### 特点4: 工具函数的独立封装

#### NiloOutlineUtil.hlsl - 描边工具
```hlsl
// 解决问题：如何让描边宽度在不同距离和FOV下保持一致？

float GetOutlineCameraFovAndDistanceFixMultiplier(float positionVS_Z)
{
    if(unity_OrthoParams.w == 0)  // 透视相机
    {
        cameraMulFix = abs(positionVS_Z);  // 距离补偿
        cameraMulFix *= GetCameraFOV();     // FOV补偿
    }
    else  // 正交相机
    {
        cameraMulFix = abs(unity_OrthoParams.y) * 50;
    }
    return cameraMulFix * 0.00005;
}
```

**实际效果:**
- 角色靠近相机：描边自动变细（不会太粗）
- 角色远离相机：描边自动变粗（不会消失）
- 切换FOV：描边宽度保持视觉一致

#### NiloZOffset.hlsl - 深度偏移工具
```hlsl
// 解决问题：如何让眉毛显示在头发上面？如何隐藏脸部描边？

float4 NiloGetNewClipPosWithZOffset(float4 originalPositionCS, float viewSpaceZOffsetAmount)
{
    // 透视相机的深度偏移计算
    float modifiedPositionVS_Z = -originalPositionCS.w + -viewSpaceZOffsetAmount;
    float modifiedPositionCS_Z = modifiedPositionVS_Z * ProjM_ZRow_ZW[0] + ProjM_ZRow_ZW[1];
    originalPositionCS.z = modifiedPositionCS_Z * originalPositionCS.w / (-modifiedPositionVS_Z);
    return originalPositionCS;
}
```

**应用场景:**
- 眉毛ZOffset = -0.001 → 永远在头发前面
- 脸部描边ZOffset = 0.001 → 被头发遮挡（隐藏）
- 解决Z-Fighting问题

#### NiloInvLerpRemap.hlsl - 数值重映射工具
```hlsl
// 解决问题：如何灵活控制各种参数的范围？

half invLerpClamp(half from, half to, half value)
{
    return saturate((value - from) / (to - from));
}
```

**实际应用:**
```hlsl
// 场景1: AO贴图重映射
// 原始AO: 0.3-0.9 → 重映射到 0-1，增强对比度
occlusion = invLerpClamp(0.3, 0.9, occlusion);

// 场景2: 描边mask重映射
// 原始mask: 0.2-0.8 → 重映射到 0-1，控制哪里显示描边
outlineMask = invLerpClamp(_RemapStart, _RemapEnd, outlineMask);
```

---

### 特点5: CBUFFER的正确使用（SRP Batching）

```hlsl
// ❌ 错误做法：纹理放在CBUFFER里
CBUFFER_START(UnityPerMaterial)
    sampler2D _BaseMap;  // ❌ 纹理不能放这里！
    float4 _BaseColor;
CBUFFER_END

// ✅ 正确做法：纹理和CBUFFER分开
sampler2D _BaseMap;      // ✅ 纹理在外面
sampler2D _EmissionMap;
sampler2D _OcclusionMap;

CBUFFER_START(UnityPerMaterial)
    // 只放uniform参数
    float4  _BaseMap_ST;     // 纹理的Tiling和Offset
    half4   _BaseColor;
    half    _Cutoff;
    float   _IsFace;
    // ... 所有材质参数
CBUFFER_END
```

**为什么这样做？**
- SRP Batching要求所有材质参数在一个CBUFFER中
- 纹理不是uniform，不能放CBUFFER
- 正确使用可以让100+个角色只需1个DrawCall！

---

## 🔥 5个Pass的职责分工

### Pass #0: ForwardLit（主渲染Pass）
```hlsl
Pass {
    Name "ForwardLit"
    Tags { "LightMode" = "UniversalForwardOnly" }
    Cull Off  // 双面渲染
    
    // 渲染目标: _CameraColorTexture + _CameraDepthTexture
    // 功能: 完整的光照计算 + 颜色输出
}
```

### Pass #1: Outline（描边Pass）
```hlsl
Pass {
    Name "Outline"
    Cull Front  // ✅ 关键：只渲染背面
    
    #define ToonShaderIsOutline  // ✅ 激活描边代码
    
    // 顶点着色器会把顶点沿法线推出
    // 片元着色器会使用描边颜色
}
```

**描边原理:**
```
1. ForwardLit Pass 画正常模型（Cull Off）
2. Outline Pass 把模型放大一圈（沿法线推出）
3. 但只画背面（Cull Front）
4. 结果：看起来有一圈描边
```

### Pass #2: ShadowCaster（阴影投射）
```hlsl
Pass {
    Name "ShadowCaster"
    Tags { "LightMode" = "ShadowCaster" }
    ColorMask 0  // 不需要颜色，只写深度
    
    #define ToonShaderApplyShadowBiasFix  // 修复阴影偏移
    
    // 渲染目标: _MainLightShadowmapTexture
}
```

### Pass #3: DepthOnly（深度预渲染）
```hlsl
Pass {
    Name "DepthOnly"
    Tags { "LightMode" = "DepthOnly" }
    ColorMask R  // 只写R通道（深度）
    
    #define ToonShaderIsOutline  // 描边也要写深度
    
    // 渲染目标: _CameraDepthTexture
    // 触发条件: 开启Depth Texture或SSAO等效果
}
```

### Pass #4: DepthNormalsOnly（深度+法线）
```hlsl
Pass {
    Name "DepthNormalsOnly"
    Tags { "LightMode" = "DepthNormalsOnly" }
    ColorMask RGBA  // RGB=法线，A=深度
    
    // 渲染目标: _CameraDepthTexture + _CameraNormalsTexture
    // 触发条件: 开启SSAO等需要法线的后处理
}
```

---

## 💡 高级技巧解析

### 技巧1: 使用#define控制Pass行为

```hlsl
// 在Shared.hlsl中
Varyings VertexShaderWork(Attributes input)
{
    // ... 公共代码 ...
    
#ifdef ToonShaderIsOutline
    // 描边特有代码：沿法线推出顶点
    positionWS = TransformPositionWSToOutlinePositionWS(...);
    
    // 应用深度偏移（隐藏脸部描边）
    output.positionCS = NiloGetNewClipPosWithZOffset(...);
#endif

#ifdef ToonShaderApplyShadowBiasFix
    // ShadowCaster特有代码：修复阴影偏移
    output.positionCS = ApplyShadowBiasFixToHClipPos(...);
#endif
    
    return output;
}
```

**一个函数，多种行为：**
- ForwardLit Pass: 正常顶点变换
- Outline Pass: 顶点变换 + 沿法线推出 + 深度偏移
- ShadowCaster Pass: 顶点变换 + 阴影偏移修复

### 技巧2: 在顶点着色器采样纹理

```hlsl
// 问题：顶点着色器没有UV导数，不能用tex2D
// 解决：使用tex2Dlod，手动指定mip level

#ifdef ToonShaderIsOutline
    // 读取描边ZOffset遮罩纹理
    float mipLevel = 0;  // 使用最高精度的mip
    float mask = tex2Dlod(_OutlineZOffsetMaskTex, float4(input.uv, 0, mipLevel)).r;
    
    // 重映射mask值
    mask = 1 - mask;  // 翻转（黑色=应用ZOffset）
    mask = invLerpClamp(_RemapStart, _RemapEnd, mask);
    
    // 应用到深度偏移
    output.positionCS = NiloGetNewClipPosWithZOffset(output.positionCS, _OutlineZOffset * mask);
#endif
```

### 技巧3: 性能优化的静态分支

```hlsl
// 使用uniform控制的if分支（性能友好）
if(_UseEmission)  // _UseEmission是uniform常量
{
    result = tex2D(_EmissionMap, input.uv).rgb * _EmissionColor;
}

// vs 使用shader_feature（会生成2个变体）
#ifdef _USE_EMISSION_ON
    result = tex2D(_EmissionMap, input.uv).rgb * _EmissionColor;
#endif
```

**作者的选择:**
- 有纹理采样 → 用`shader_feature`（跳过纹理读取）
- 只有计算 → 用静态`if`（避免变体爆炸）

**原因:**
- 现代GPU的ALU很快，但带宽有限
- 跳过纹理读取很重要，跳过几次计算不重要
- 避免变体过多（2^n增长）导致编译时间和内存爆炸

### 技巧4: 处理多种相机类型

```hlsl
float GetOutlineCameraFovAndDistanceFixMultiplier(float positionVS_Z)
{
    if(unity_OrthoParams.w == 0)  // 透视相机
    {
        // 透视相机的处理
        cameraMulFix = abs(positionVS_Z) * GetCameraFOV();
    }
    else  // 正交相机
    {
        // 正交相机的处理
        cameraMulFix = abs(unity_OrthoParams.y) * 50;
    }
    return cameraMulFix * 0.00005;
}
```

**支持:**
- 透视相机（游戏主相机）
- 正交相机（UI、小地图等）
- VR相机（通过宏处理）

---

## 🎨 与我之前给你写的架构对比

### 你的需求架构（教学向）
```
Assets/Shaders/
├── ShaderLibrary/          # 通用工具库
│   ├── Common/            # 基础功能
│   └── Lighting/          # 光照模块
└── XXX.shader             # 各种变体shader
```
**特点:**
- ✅ 适合学习和快速原型
- ✅ 结构清晰，易于理解
- ❌ 多个shader文件，难以批处理

### GitHub架构（生产向）
```
单个Shader包含:
├── Main.shader            # 定义5个Pass
├── Shared.hlsl           # 所有Pass共享代码
├── LightingEquation.hlsl # 光照逻辑（最常修改）
└── Utils/                # 工具函数
```
**特点:**
- ✅ 一个shader完成所有功能
- ✅ 极致的SRP Batching优化
- ✅ Pass之间复用代码
- ❌ 初学者理解难度较高

---

## 🚀 实战建议

### 对于学习阶段（你现在）
建议使用**模块化的多shader架构**（我之前给你写的）：
- 每个shader职责单一
- 容易理解和修改
- 适合实验不同效果

### 对于项目后期
可以参考**GitHub的单shader多Pass架构**：
- 合并相似shader
- 优化批处理
- 提升性能

### 混合方案（最佳实践）
```
Assets/Shaders/
├── ShaderLibrary/                  # 共享工具库
│   ├── Common/
│   │   ├── Structures.hlsl        # 数据结构
│   │   ├── Functions.hlsl         # 工具函数
│   │   └── SurfaceData.hlsl       # 表面数据处理
│   ├── Lighting/
│   │   ├── LightingCommon.hlsl    # 标准光照
│   │   ├── ToonLighting.hlsl      # 卡通光照
│   │   └── PBRLighting.hlsl       # PBR光照
│   └── Utils/
│       ├── OutlineUtil.hlsl       # 描边工具（学习GitHub）
│       ├── ZOffset.hlsl           # 深度偏移（学习GitHub）
│       └── Remap.hlsl             # 重映射（学习GitHub）
│
└── Characters/                     # 角色shader（学习GitHub架构）
    ├── CharacterToon.shader
    ├── CharacterToon_Shared.hlsl
    └── CharacterToon_Lighting.hlsl
```

---

## 📝 关键学习点总结

### 1. **代码复用的层次**
- Level 1: 函数复用（基础）
- Level 2: .hlsl文件复用（你之前学的）
- Level 3: Pass之间复用（GitHub的方法）✨

### 2. **使用宏控制行为**
```hlsl
#define ToonShaderIsOutline
#include "Shared.hlsl"
// 同样的代码，不同的行为
```

### 3. **数据结构驱动设计**
```hlsl
SurfaceData → LightingData → 光照计算 → 最终颜色
```

### 4. **工具函数的封装**
- 每个工具解决一个具体问题
- 独立的.hlsl文件
- 清晰的头文件保护

### 5. **性能优化的平衡**
- 纹理采样 → 用shader_feature
- 纯计算 → 用静态if分支
- 避免变体爆炸

### 6. **5 Pass的设计模式**
- ForwardLit: 主渲染
- Outline: 描边（复用VertexShaderWork）
- ShadowCaster: 阴影（复用AlphaClip）
- DepthOnly: 深度预渲染
- DepthNormalsOnly: 法线+深度

---

## 🎓 建议的学习路径

1. **理解单shader多Pass架构** ✅ 你现在在这
2. **学习工具函数的设计** → 复制OutlineUtil.hlsl等
3. **实践数据驱动设计** → 创建自己的SurfaceData
4. **掌握宏控制技巧** → 用#define区分Pass行为
5. **优化SRP Batching** → 正确使用CBUFFER
6. **整合到项目** → 结合你的需求改进架构

---

## 💬 总结

GitHub这个shader的精髓在于：
- **极致的代码复用**：5个Pass共享90%的代码
- **清晰的职责分离**：数据/光照/工具分离
- **工业级的性能优化**：SRP Batching + 静态分支
- **灵活的扩展性**：修改光照只需编辑一个文件

这是经过多年商业项目打磨的架构，非常值得学习！
