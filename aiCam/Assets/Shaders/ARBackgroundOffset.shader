Shader "Hidden/URP/ARBackgroundOffset"
{
    Properties {
        _UvOffset("UV Offset", Vector) = (0,0,0,0)
        _UvScale ("UV Scale" , Vector) = (1,1,0,0)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always

        Pass
        {
            Name "ARBackgroundOffset"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            // ---- ARFoundation がセットしてくれるテクスチャ ----
            TEXTURE2D(_TextureY); SAMPLER(sampler_TextureY);
            TEXTURE2D(_TextureCbCr); SAMPLER(sampler_TextureCbCr);
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            float4x4 _UnityDisplayTransform;
            float4 _UvOffset;
            float4 _UvScale;

            struct Attributes {
                float3 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            Varyings Vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS);
                OUT.uv = IN.uv;
                return OUT;
            }

            float2 TransformDisplayUv(float2 uv)
            {
                float4 u = float4(uv, 0, 1);
                float2 t = mul(_UnityDisplayTransform, u).xy;
                t = t * _UvScale.xy + _UvOffset.xy;
                return t;
            }

            float3 SampleCameraRGB(float2 uv)
            {
                float y = SAMPLE_TEXTURE2D(_TextureY, sampler_TextureY, uv).r;
                float2 cbcr = SAMPLE_TEXTURE2D(_TextureCbCr, sampler_TextureCbCr, uv).rg;
                float cb = cbcr.x - 0.5;
                float cr = cbcr.y - 0.5;

                float3 rgb;
                rgb.r = y + 1.402 * cr;
                rgb.g = y - 0.344136 * cb - 0.714136 * cr;
                rgb.b = y + 1.772 * cb;

                float3 fallback = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
                float sumYUV = (y + cbcr.x + cbcr.y);
                float sumRGB = (fallback.r + fallback.g + fallback.b);
                return (sumYUV > 0.001) ? rgb : fallback;
            }

            float4 Frag(Varyings IN) : SV_Target
            {
                float2 uv = TransformDisplayUv(IN.uv);
                float3 col = SampleCameraRGB(uv);
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}