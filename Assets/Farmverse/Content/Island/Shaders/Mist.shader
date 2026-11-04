// Farmverse mist: soft banks of sea fog that drift slowly across the water and creep onto the beach.
//  - Each layer is one big flat quad; two noise octaves scroll with the wind to make the banks.
//  - Thick over open sea, thinner near the coast; fades out where the ground rises up to the
//    layer (G channel of the island mask holds ground height) so hills never cut a hard line.
//  - Fades near the camera so zooming in never puts a grey sheet in front of the player.
// Cheap: no depth texture, one pass, two texture samples plus the mask.
Shader "Farmverse/Mist"
{
    Properties
    {
        _Noise ("Noise", 2D) = "gray" {}
        _Mask ("Island mask (R sea depth, G ground height/32)", 2D) = "white" {}
        _MaskRect ("Mask rect", Vector) = (-320, -320, 640, 640)
        _Color ("Mist colour", Color) = (0.93, 0.96, 1.0, 1)
        _Shade ("Shaded colour", Color) = (0.70, 0.79, 0.88, 1)
        _Density ("Density", Range(0, 1)) = 0.45
        _Cover ("Coverage (lower = more mist)", Range(0, 1)) = 0.48
        _Scale ("Bank size (m)", Float) = 260
        _Wind ("Wind (x, z) m/s", Vector) = (2.2, 0.9, 0, 0)
        _OverLand ("Amount over land", Range(0, 1)) = 0.2
        _NearFade ("Near fade start (m)", Float) = 25
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+5" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);
            TEXTURE2D(_Mask); SAMPLER(sampler_Mask);
            CBUFFER_START(UnityPerMaterial)
                float4 _Noise_ST, _Mask_ST, _MaskRect, _Wind;
                half4 _Color, _Shade;
                half _Density, _Cover, _OverLand;
                float _Scale, _NearFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half fog : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 xz = i.positionWS.xz;
                float t = _Time.y;
                // seed each layer by its height so stacked layers don't line up
                float seed = i.positionWS.y * 0.137;
                float2 w = _Wind.xy;
                float n1 = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, (xz - w * t) / _Scale + seed).r;
                float n2 = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, (xz - w * t * 1.6) / (_Scale * 0.37) - seed * 1.7 + 0.31).r;
                float n = n1 * 0.68 + n2 * 0.32;
                // slow breathing so banks swell and thin over time
                float breathe = 0.04 * sin(t * 0.11 + seed * 6.0);
                half dens = smoothstep(_Cover + breathe, _Cover + breathe + 0.3, n);

                float2 uv = (xz - _MaskRect.xy) / _MaskRect.zw;
                half sea = 1, ground = 0;
                if (all(uv > 0) && all(uv < 1))
                {
                    half4 m = SAMPLE_TEXTURE2D(_Mask, sampler_Mask, uv);
                    sea = m.r; ground = m.g * 32.0;
                }
                half place = lerp(_OverLand, 1.0, smoothstep(0.02, 0.5, sea));
                half soft = smoothstep(0.0, 3.0, i.positionWS.y - ground);   // ground rising into the layer

                float dist = distance(_WorldSpaceCameraPos, i.positionWS);
                half nearFade = smoothstep(_NearFade, _NearFade * 3.0, dist);
                // looking straight down through a thin sheet should read thinner than at a glancing angle
                float3 v = normalize(_WorldSpaceCameraPos - i.positionWS);
                half graze = lerp(1.0, 0.55, abs(v.y));

                half a = dens * place * soft * nearFade * _Density * graze;
                Light sun = GetMainLight();
                half3 col = lerp(_Shade.rgb, _Color.rgb, saturate(0.35 + n2 * 0.9)) * lerp(0.85, 1.1, saturate(sun.direction.y));
                // fog per pixel: each layer is one huge quad, so a per-vertex fog value would smear
                // across the whole triangle and show its diagonal as a hard edge
                col = MixFog(col, ComputeFogFactor(TransformWorldToHClip(i.positionWS).z));
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
