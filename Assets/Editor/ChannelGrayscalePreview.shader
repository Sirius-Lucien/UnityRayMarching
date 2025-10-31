Shader "Hidden/Editor/ChannelGrayscalePreview"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _ChannelMask ("Channel Mask", Vector) = (1, 0, 0, 0)
        _UseSRGBSampling ("Use sRGB Sampling", Float) = 1
        _ShowAlphaAsMask ("Show Alpha As Mask", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        ZWrite Off
        Cull Off
        ZTest Always
        Blend Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 _ChannelMask;
            float _UseSRGBSampling;
            float _ShowAlphaAsMask;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 ApplyChannelMask(fixed4 color, float4 mask)
            {
                float3 rawRGB = color.rgb;
                float3 displayRGB = rawRGB;

                // Optional linear -> sRGB conversion so显示结果接近 Photoshop 的通道预览。
                if (_UseSRGBSampling > 0.5f)
                {
                    displayRGB = LinearToGammaSpace(rawRGB);
                }

                float4 data = float4(displayRGB, color.a);
                float channelValue = dot(data, mask);

                // If we treat alpha as a mask, keep alpha but force RGB to the grayscale value multiplied by mask.
                if (_ShowAlphaAsMask > 0.5f)
                {
                    return fixed4(channelValue.xxx * color.a, color.a);
                }

                return fixed4(channelValue.xxx, 1.0f);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, i.uv);
                return ApplyChannelMask(color, _ChannelMask);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
