Shader "Unlit/Emission"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white"{}
        _EmissionColor("Emission Color", Color) = (1,1,1,1)
        _EmissionStrength("Emission Strength", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"
            struct appdata
            {
                float3 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos:SV_POSITION;
                float2 uv:TEXCOORD0;
            };
            sampler2D _MainTex;
            float4 _EmissionColor;
            float _EmissionStrength;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half4 col = tex2D(_MainTex, i.uv);
                float3 emission = _EmissionColor.rgb * _EmissionStrength * col.rgb;
                return float4(emission, 1.0); // 输出全亮透明度1.0
            }

            ENDCG
        }
    }
}