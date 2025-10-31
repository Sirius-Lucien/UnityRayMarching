Shader "Hidden/Bloom"
{
    Properties
    {
        _MainTex ("Bloom(RGB)", 2D) = "white" {}
        _Bloom ("Bloom(RGB)", 2D) = "black" {}
        _LuminaceThreshold ("Luminance Threshold", Range(0, 1)) = 0.5
        _BlurSize ("Blur Size", Float) = 4
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        
        // Pass 0: Extract Bright Areas
        Pass
        {
            CGPROGRAM
            #pragma vertex vertExtractBright
            #pragma fragment fragExtractBright
            
            sampler2D _MainTex;
            half4 _MainTex_TexelSize;
            float _LuminaceThreshold;
            
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };
            
            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 pos : SV_POSITION;
            };
            
            v2f vertExtractBright(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            
            fixed Luminance(fixed4 color)
            {
                return 0.2125 * color.r + 0.7154 * color.g + 0.0721 * color.b;
            }
            
            fixed4 fragExtractBright(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                fixed val = clamp(Luminance(col) - _LuminaceThreshold, 0.0, 1.0);
                return col * val;
            }
            ENDCG
        }
        
        // Pass 1: Vertical Blur
        Pass
        {
            CGPROGRAM
            #pragma vertex vertBlurVertical
            #pragma fragment fragBlur
            
            sampler2D _MainTex;
            half4 _MainTex_TexelSize;
            float _BlurSize;
            
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };
            
            struct v2f
            {
                float2 uv[5] : TEXCOORD0;
                float4 pos : SV_POSITION;
            };
            
            v2f vertBlurVertical(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv[0] = v.uv;
                o.uv[1] = v.uv + float2(0.0, _MainTex_TexelSize.y * 1.0) * _BlurSize;
                o.uv[2] = v.uv - float2(0.0, _MainTex_TexelSize.y * 1.0) * _BlurSize;
                o.uv[3] = v.uv + float2(0.0, _MainTex_TexelSize.y * 2.0) * _BlurSize;
                o.uv[4] = v.uv - float2(0.0, _MainTex_TexelSize.y * 2.0) * _BlurSize;
                return o;
            }
            
            fixed4 fragBlur(v2f i) : SV_Target
            {
                float weight[3] = { 0.4026, 0.2442, 0.0545 };
                fixed3 sum = tex2D(_MainTex, i.uv[0]).rgb * weight[0];
                sum += tex2D(_MainTex, i.uv[1]).rgb * weight[1];
                sum += tex2D(_MainTex, i.uv[2]).rgb * weight[1];
                sum += tex2D(_MainTex, i.uv[3]).rgb * weight[2];
                sum += tex2D(_MainTex, i.uv[4]).rgb * weight[2];
                return fixed4(sum, 1.0);
            }
            ENDCG
        }
        
        // Pass 2: Horizontal Blur
        Pass
        {
            CGPROGRAM
            #pragma vertex vertBlurHorizontal
            #pragma fragment fragBlur
            
            sampler2D _MainTex;
            half4 _MainTex_TexelSize;
            float _BlurSize;
            
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };
            
            struct v2f
            {
                float2 uv[5] : TEXCOORD0;
                float4 pos : SV_POSITION;
            };
            
            v2f vertBlurHorizontal(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv[0] = v.uv;
                o.uv[1] = v.uv + float2(_MainTex_TexelSize.x * 1.0, 0.0) * _BlurSize;
                o.uv[2] = v.uv - float2(_MainTex_TexelSize.x * 1.0, 0.0) * _BlurSize;
                o.uv[3] = v.uv + float2(_MainTex_TexelSize.x * 2.0, 0.0) * _BlurSize;
                o.uv[4] = v.uv - float2(_MainTex_TexelSize.x * 2.0, 0.0) * _BlurSize;
                return o;
            }
            
            fixed4 fragBlur(v2f i) : SV_Target
            {
                float weight[3] = { 0.4026, 0.2442, 0.0545 };
                fixed3 sum = tex2D(_MainTex, i.uv[0]).rgb * weight[0];
                sum += tex2D(_MainTex, i.uv[1]).rgb * weight[1];
                sum += tex2D(_MainTex, i.uv[2]).rgb * weight[1];
                sum += tex2D(_MainTex, i.uv[3]).rgb * weight[2];
                sum += tex2D(_MainTex, i.uv[4]).rgb * weight[2];
                return fixed4(sum, 1.0);
            }
            ENDCG
        }
        
        // Pass 3: Combine Bloom
        Pass
        {
            CGPROGRAM
            #pragma vertex vertBloom
            #pragma fragment fragBloom
            
            sampler2D _MainTex;
            sampler2D _Bloom;
            half4 _MainTex_TexelSize;
            
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };
            
            struct v2fBloom
            {
                float4 pos : SV_POSITION;
                float4 uv : TEXCOORD0;
            };
            
            v2fBloom vertBloom(appdata v)
            {
                v2fBloom o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv.xy = v.uv;
                o.uv.zw = v.uv;
                #if UNITY_UV_STARTS_AT_TOP
                if (_MainTex_TexelSize.y < 0.0)
                    o.uv.w = 1.0 - o.uv.w;
                #endif
                return o;
            }
            
            fixed4 fragBloom(v2fBloom i) : SV_Target
            {
                return tex2D(_MainTex, i.uv.xy) + tex2D(_Bloom, i.uv.zw);
            }
            ENDCG
        }
    }
}