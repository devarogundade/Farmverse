// Farmverse water: a living, stylized sea for mobile.
//  - Rolling waves: three directional waves move the vertices; they calm down near the shore.
//  - Ripples: two normal maps scroll in different directions and scales.
//  - Colour by depth: turquoise in the shallows, deep blue offshore. Depth comes from a mask
//    texture baked from the island's height (no depth texture needed, cheap on phones).
//  - Shore foam: a foam band hugs the coast and lines of foam roll in towards the beach;
//    wave crests out at sea get flecks of foam too.
//  - Sky reflection (fresnel) and sun glitter.
// Transparent so the sand shows through the shallows.
Shader "Farmverse/Water"
{
    Properties
    {
        _NormalTex ("Ripple normals", 2D) = "bump" {}
        _FoamTex ("Foam pattern", 2D) = "black" {}
        _Mask ("Depth mask (R: 0 shore .. 1 deep)", 2D) = "white" {}
        _MaskRect ("Mask rect (minX, minZ, sizeX, sizeZ)", Vector) = (-320, -320, 640, 640)
        _Shallow ("Shallow colour", Color) = (0.30, 0.86, 0.82, 1)
        _Mid ("Mid colour", Color) = (0.10, 0.62, 0.78, 1)
        _Deep ("Deep colour", Color) = (0.05, 0.33, 0.58, 1)
        _Sky ("Sky reflection", Color) = (0.78, 0.92, 1.0, 1)
        _FoamColor ("Foam colour", Color) = (1, 1, 1, 1)
        _WaveAmp ("Wave height (m)", Float) = 0.35
        _WaveLength ("Wave length (m)", Float) = 16
        _WaveSpeed ("Wave speed", Float) = 1.0
        _RippleScale ("Ripple size (m)", Float) = 9
        _RippleSpeed ("Ripple speed", Float) = 0.04
        _RippleStrength ("Ripple strength", Range(0, 2)) = 0.7
        _ShoreWidth ("Shore foam width (mask units)", Range(0.01, 0.5)) = 0.1
        _ShoreLines ("Rolling foam lines", Range(0, 1)) = 0.8
        _ShallowAlpha ("Shallow transparency", Range(0, 1)) = 0.55
        _Glitter ("Sun glitter", Range(0, 6)) = 2.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_NormalTex); SAMPLER(sampler_NormalTex);
            TEXTURE2D(_FoamTex);
            TEXTURE2D(_Mask); SAMPLER(sampler_Mask);

            CBUFFER_START(UnityPerMaterial)
                float4 _NormalTex_ST, _FoamTex_ST, _Mask_ST;
                float4 _MaskRect;
                half4 _Shallow, _Mid, _Deep, _Sky, _FoamColor;
                float _WaveAmp, _WaveLength, _WaveSpeed;
                float _RippleScale, _RippleSpeed;
                half _RippleStrength, _ShoreWidth, _ShoreLines, _ShallowAlpha, _Glitter;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 waveNormal : TEXCOORD1;
                float crest : TEXCOORD2;
                half fog : TEXCOORD3;
            };

            float Depth01(float2 xz, bool vertexStage)
            {
                float2 uv = (xz - _MaskRect.xy) / _MaskRect.zw;
                if (any(uv < 0) || any(uv > 1)) return 1;
                return vertexStage ? SAMPLE_TEXTURE2D_LOD(_Mask, sampler_Mask, uv, 0).r
                                   : SAMPLE_TEXTURE2D(_Mask, sampler_Mask, uv).r;
            }

            // one directional wave: height and its slope along x/z
            void Wave(float2 xz, float2 dir, float len, float amp, float speed, inout float h, inout float2 slope)
            {
                float k = 6.28318 / len;
                float ph = dot(dir, xz) * k + _Time.y * speed * sqrt(9.8 * k);
                h += amp * sin(ph);
                slope += dir * (amp * k * cos(ph));
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float d = Depth01(ws.xz, true);
                float calm = saturate(d * 2.5 + 0.1);          // waves settle near the beach
                // and fade out towards the edge of the dense grid, so it meets the coarse far ring smoothly
                float edgeDist = max(abs(ws.x - (_MaskRect.x + _MaskRect.z * 0.5)), abs(ws.z - (_MaskRect.y + _MaskRect.w * 0.5)));
                calm *= 1 - smoothstep(_MaskRect.z * 0.36, _MaskRect.z * 0.49, edgeDist);
                float h = 0; float2 slope = 0;
                float A = _WaveAmp * calm, L = _WaveLength, S = _WaveSpeed;
                Wave(ws.xz, normalize(float2(1.0, 0.35)), L,        A,        S, h, slope);
                Wave(ws.xz, normalize(float2(-0.4, 1.0)), L * 0.61, A * 0.55, S, h, slope);
                Wave(ws.xz, normalize(float2(0.7, -0.8)), L * 0.37, A * 0.30, S, h, slope);
                ws.y += h;
                o.waveNormal = normalize(float3(-slope.x, 1, -slope.y));
                o.crest = saturate(h / max(_WaveAmp, 1e-3));
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float2 xz = i.positionWS.xz;
                float d = Depth01(xz, false);

                half3 r1 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, xz / _RippleScale + t * _RippleSpeed * float2(1.0, 0.6)), _RippleStrength);
                half3 r2 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, xz / (_RippleScale * 1.9) + t * _RippleSpeed * float2(-0.55, 0.9)), _RippleStrength);
                float3 n = normalize(i.waveNormal + float3(r1.x + r2.x, 0, r1.y + r2.y) * 0.5);

                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                Light light = GetMainLight();

                // colour by depth
                half3 col = lerp(_Shallow.rgb, _Mid.rgb, smoothstep(0.02, 0.25, d));
                col = lerp(col, _Deep.rgb, smoothstep(0.25, 0.85, d));
                // a little shading from the ripples and the sun
                half ndl = saturate(dot(n, light.direction));
                col *= 0.75 + 0.35 * ndl;
                col += SampleSH(n) * 0.08;
                // sky reflection
                half fres = pow(1 - saturate(dot(n, V)), 4);
                col = lerp(col, _Sky.rgb, fres * 0.55);
                // sun glitter
                float3 H = normalize(light.direction + V);
                half spec = pow(saturate(dot(n, H)), 220) * _Glitter;
                col += light.color * spec;

                // foam: a band at the coast, lines rolling in, flecks on crests
                half foamPat = SAMPLE_TEXTURE2D(_FoamTex, sampler_NormalTex, xz / 6 + t * 0.012).r;
                half foamPat2 = SAMPLE_TEXTURE2D(_FoamTex, sampler_NormalTex, xz / 11 - t * 0.008).r;
                half shore = 1 - smoothstep(0, _ShoreWidth, d);
                half lines = sin(d * 70 - t * 1.6) * 0.5 + 0.5;
                lines = smoothstep(0.82, 0.97, lines) * (1 - smoothstep(0.05, 0.3, d)) * _ShoreLines;
                // scattered whitecaps out at sea: slow-drifting noise, not tied to the mesh
                half caps = SAMPLE_TEXTURE2D(_FoamTex, sampler_NormalTex, xz / 37 + t * float2(0.004, 0.003)).r;
                half crest = smoothstep(0.8, 0.98, caps) * smoothstep(0.85, 1, i.crest + 0.2) * smoothstep(0.35, 0.7, d) * 0.45;
                half foam = saturate(shore * shore * 0.9 + (shore * 0.8 + lines + crest) * saturate(foamPat * 1.4 + foamPat2 * 0.4));
                col = lerp(col, _FoamColor.rgb * (0.85 + 0.15 * ndl), foam);

                half alpha = lerp(_ShallowAlpha, 1, smoothstep(0.0, 0.3, d));
                alpha = max(alpha, foam);
                col = MixFog(col, ComputeFogFactor(TransformWorldToHClip(i.positionWS).z));   // per pixel: the far sea has 80 m cells
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
