// Farmverse/Waterfall: falling water for the chapter 3 falls. Two layers of the river texture
// scrolling down at different speeds, white streaks from the foam texture, brighter and foamier
// towards the foot, transparent at the sheet's edges (vertex alpha). Unlit with a touch of the
// sun, so it reads in shade too. URP.
Shader "Farmverse/Waterfall"
{
    Properties
    {
        _MainTex ("Water", 2D) = "white" {}
        _FoamTex ("Foam", 2D) = "white" {}
        _Tint ("Tint", Color) = (0.72, 0.88, 0.98, 1)
        _Speed ("Fall speed", Float) = 1.6
        _Foam ("Foam strength", Range(0, 2)) = 1.0
        _Alpha ("Opacity", Range(0, 1)) = 0.85
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_FoamTex); SAMPLER(sampler_FoamTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST, _FoamTex_ST;
                half4 _Tint;
                float _Speed;
                half _Foam, _Alpha;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; half fog : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.color = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y * _Speed;
                half3 a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(i.uv.x, i.uv.y * 0.6 + t)).rgb;
                half3 b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(i.uv.x * 1.7 + 0.3, i.uv.y * 1.1 + t * 1.45)).rgb;
                half f1 = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, float2(i.uv.x * 2.0, i.uv.y * 0.5 + t * 1.2)).r;
                half f2 = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, float2(i.uv.x * 3.1 + 0.5, i.uv.y * 0.8 + t * 1.8)).r;
                // streaks: brighter where both foam layers agree; more foam towards the foot (uv.y grows down the falls)
                half foot = saturate(i.uv.y * 0.35);
                half foam = saturate((f1 * f2) * 2.2 * _Foam + foot * 0.45);
                half3 col = (a + b) * 0.5 * _Tint.rgb;
                col = lerp(col, half3(1, 1, 1), foam);
                Light sun = GetMainLight();
                col *= 0.8 + 0.3 * sun.color;
                col = MixFog(col, i.fog);
                return half4(col, saturate(_Alpha * i.color.a * (0.75 + foam * 0.4)));
            }
            ENDHLSL
        }
    }
}
