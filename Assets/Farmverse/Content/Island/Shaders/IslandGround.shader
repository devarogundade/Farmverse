// Farmverse island ground: five painted layers blended per vertex: grass, flowers, sand, rock from
// the vertex colour (R, G, B, A) and bare earth (tracks, road shoulders, worn patches) from UV0.x. To keep a big island from looking like one
// repeated image it:
//  - samples every layer twice (straight and rotated at another scale) and mixes the two with a
//    large-scale noise, so the repeat is broken up;
//  - blends layers by their own brightness ("height blend"), so sand and grass meet in natural ragged
//    edges instead of soft smears;
//  - tints the result with a very large noise (lighter and darker patches of meadow);
//  - projects rock from the sides on steep slopes (no stretching on cliffs).
// Lit by the main light with shadows, ambient (SH) and fog. Built for URP 17, mobile friendly.
Shader "Farmverse/IslandGround"
{
    Properties
    {
        _Grass ("Grass", 2D) = "white" {}
        _GrassN ("Grass normal", 2D) = "bump" {}
        _Flowers ("Flowers", 2D) = "white" {}
        _Sand ("Sand", 2D) = "white" {}
        _SandN ("Sand normal", 2D) = "bump" {}
        _SnowLine ("Snow line (m)", Float) = 34
        _Rock ("Rock", 2D) = "white" {}
        _RockN ("Rock normal", 2D) = "bump" {}
        _Dirt ("Dirt", 2D) = "white" {}
        _DirtN ("Dirt normal", 2D) = "bump" {}
        _DirtTiling ("Metres per dirt tile", Float) = 5
        _DirtTint ("Dirt tint", Color) = (0.86, 0.80, 0.72, 1)
        _DirtSat ("Dirt saturation", Range(0, 1.5)) = 0.55
        _Macro ("Macro noise (RGB)", 2D) = "gray" {}
        _Tiling ("Metres per tile: grass, flowers, sand, rock", Vector) = (7, 7, 6, 9)
        _MacroScale ("Macro noise size (m)", Float) = 140
        _MacroDark ("Macro tint dark", Color) = (0.80, 0.88, 0.72, 1)
        _MacroLight ("Macro tint light", Color) = (1.10, 1.06, 0.88, 1)
        _BlendSharp ("Layer blend sharpness", Range(1, 24)) = 8
        _NormalStrength ("Normal strength", Range(0, 2)) = 0.7
        _Saturation ("Saturation", Range(0, 2)) = 1.05
        _Flat ("Low-poly flat colours (0 photo textures, 1 flat)", Range(0, 1)) = 0
        _FlatGrass ("Flat grass", Color) = (0.49, 0.78, 0.30, 1)
        _FlatFlowers ("Flat meadow", Color) = (0.60, 0.82, 0.32, 1)
        _FlatSand ("Flat sand", Color) = (0.92, 0.78, 0.56, 1)
        _FlatRock ("Flat rock", Color) = (0.64, 0.52, 0.44, 1)
        _FlatDirt ("Flat dirt", Color) = (0.86, 0.66, 0.44, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_Grass);   SAMPLER(sampler_Grass);
            TEXTURE2D(_GrassN);
            TEXTURE2D(_Flowers);
            TEXTURE2D(_Sand);
            TEXTURE2D(_SandN);
            TEXTURE2D(_Rock);
            TEXTURE2D(_RockN);
            TEXTURE2D(_Dirt);
            TEXTURE2D(_DirtN);
            TEXTURE2D(_Macro);

            CBUFFER_START(UnityPerMaterial)
                float _SnowLine;
                float4 _Grass_ST, _GrassN_ST, _Flowers_ST, _Sand_ST, _SandN_ST, _Rock_ST, _RockN_ST, _Macro_ST, _Dirt_ST, _DirtN_ST;
                float _DirtTiling;
                half4 _DirtTint;
                half _DirtSat;
                float4 _Tiling;
                float _MacroScale;
                half4 _MacroDark;
                half4 _MacroLight;
                half _BlendSharp;
                half _NormalStrength;
                half _Saturation;
                half _Flat;
                half4 _FlatGrass, _FlatFlowers, _FlatSand, _FlatRock, _FlatDirt;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
                half fog : TEXCOORD3;
                half dirt : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.color = v.color;
                o.dirt = v.uv.x;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // two lookups (straight + rotated/rescaled) mixed by a noise value: hides the tile repeat
            half4 AntiTile(TEXTURE2D_PARAM(tex, smp), float2 uv, half sel)
            {
                half4 a = SAMPLE_TEXTURE2D(tex, smp, uv);
                float2 uv2 = float2(uv.x * 0.8660 - uv.y * 0.5, uv.x * 0.5 + uv.y * 0.8660) * 0.73 + 0.37;
                half4 b = SAMPLE_TEXTURE2D(tex, smp, uv2);
                return lerp(a, b, smoothstep(0.3, 0.7, sel));
            }

            half Lum(half3 c) { return dot(c, half3(0.3, 0.59, 0.11)); }

            // the season (set globally by IslandWeatherFx): 1 in winter (snow lies on flat ground), 1 in autumn (golden grass)
            half _FvWinter, _FvAutumn;

            half4 frag(Varyings i) : SV_Target
            {
                float3 n0 = normalize(i.normalWS);
                float2 xz = i.positionWS.xz;
                half3 macro = SAMPLE_TEXTURE2D(_Macro, sampler_Grass, xz / _MacroScale).rgb;
                half3 macro2 = SAMPLE_TEXTURE2D(_Macro, sampler_Grass, xz / (_MacroScale * 0.21) + 0.5).rgb;

                half wd = saturate(i.dirt);
                half4 w = max(i.color, 0);
                w /= max(w.r + w.g + w.b + w.a, 1e-3);
                w *= 1 - wd;

                half3 cG = AntiTile(TEXTURE2D_ARGS(_Grass, sampler_Grass), xz / _Tiling.x, macro2.g).rgb;
                half3 cF = AntiTile(TEXTURE2D_ARGS(_Flowers, sampler_Grass), xz / _Tiling.y, macro2.b).rgb;
                half3 cS = AntiTile(TEXTURE2D_ARGS(_Sand, sampler_Grass), xz / _Tiling.z, macro2.r).rgb;

                half3 cD = AntiTile(TEXTURE2D_ARGS(_Dirt, sampler_Grass), xz / _DirtTiling, macro2.g).rgb;
                cD = lerp(dot(cD, half3(0.3, 0.59, 0.11)).xxx, cD, _DirtSat) * _DirtTint.rgb;   // the photo dirt is far more orange than the low-poly models

                // rock: side projection on slopes (blend of the three axis projections)
                float3 bw = pow(abs(n0), 4); bw /= (bw.x + bw.y + bw.z);
                float3 p = i.positionWS / _Tiling.w;
                half3 cR = SAMPLE_TEXTURE2D(_Rock, sampler_Grass, p.zy).rgb * bw.x
                         + SAMPLE_TEXTURE2D(_Rock, sampler_Grass, p.xz).rgb * bw.y
                         + SAMPLE_TEXTURE2D(_Rock, sampler_Grass, p.xy).rgb * bw.z;

                // low-poly look: the photo layers give way to flat pack-palette colours (a touch of their grain stays)
                half gG = 0.85 + 0.3 * (Lum(cG) - 0.4), gF = 0.85 + 0.3 * (Lum(cF) - 0.4), gS = 0.9 + 0.2 * (Lum(cS) - 0.5), gR = 0.8 + 0.4 * (Lum(cR) - 0.4), gD = 0.9 + 0.2 * (Lum(cD) - 0.4);
                cG = lerp(cG, _FlatGrass.rgb * gG, _Flat);
                cF = lerp(cF, _FlatFlowers.rgb * gF, _Flat);
                cS = lerp(cS, _FlatSand.rgb * gS, _Flat);
                cR = lerp(cR, _FlatRock.rgb * gR, _Flat);
                cD = lerp(cD, _FlatDirt.rgb * gD, _Flat);

                // height blend: brighter texels of a layer win where layers overlap
                half4 hb = half4(Lum(cG), Lum(cF), Lum(cS), Lum(cR));
                half4 wb = w * (0.35 + hb);
                wb = pow(max(wb, 1e-4), _BlendSharp);
                half wdb = pow(max(wd * (0.45 + Lum(cD)), 1e-4), _BlendSharp);
                half sum = wb.r + wb.g + wb.b + wb.a + wdb;
                wb /= sum; wdb /= sum;

                half3 albedo = cG * wb.r + cF * wb.g + cS * wb.b + cR * wb.a + cD * wdb;
                // big soft light/dark patches; less on sand and rock
                half3 tint = lerp(_MacroDark.rgb, _MacroLight.rgb, macro.r);
                albedo *= lerp(half3(1, 1, 1), tint, saturate(wb.r + wb.g));
                albedo *= 0.94 + 0.12 * macro.b;
                half l = Lum(albedo);
                albedo = lerp(half3(l, l, l), albedo, _Saturation);
                // snow on the high peaks (chapter 3's mountains): above the snow line, thinner on steep faces
                half snow = saturate((i.positionWS.y - _SnowLine) / 6.0 + (macro.g - 0.5) * 0.6) * saturate(n0.y * 1.8 - 0.4);
                // autumn browns the grass; winter lays snow on everything flat that isn't water's edge or a road
                albedo = lerp(albedo, albedo * half3(1.18, 0.92, 0.62), _FvAutumn * saturate(wb.r + wb.g) * 0.75);
                half lying = _FvWinter * saturate(n0.y * 2.2 - 1.2) * saturate((i.positionWS.y - 0.6) * 1.5) * (0.78 + 0.22 * macro.g) * (1 - wdb * 0.6);
                snow = max(snow, lying);
                albedo = lerp(albedo, half3(0.93, 0.96, 1.0), snow);

                // normal detail (top projection is enough for grass/sand; rock adds its own)
                half3 tG = UnpackNormalScale(SAMPLE_TEXTURE2D(_GrassN, sampler_Grass, xz / _Tiling.x), _NormalStrength);
                half3 tS = UnpackNormalScale(SAMPLE_TEXTURE2D(_SandN, sampler_Grass, xz / _Tiling.z), _NormalStrength);
                half3 tR = UnpackNormalScale(SAMPLE_TEXTURE2D(_RockN, sampler_Grass, p.xz), _NormalStrength);
                half3 tD = UnpackNormalScale(SAMPLE_TEXTURE2D(_DirtN, sampler_Grass, xz / _DirtTiling), _NormalStrength);
                half2 d = tG.xy * (wb.r + wb.g) + tS.xy * wb.b + tR.xy * wb.a + tD.xy * wdb;
                float3 n = normalize(n0 + float3(d.x, 0, d.y) * (1 - 0.85 * _Flat));

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light light = GetMainLight(shadowCoord, i.positionWS, half4(1, 1, 1, 1));   // this overload fades shadows out at the shadow distance
                half ndl = saturate(dot(n, light.direction));
                half3 diffuse = light.color * (ndl * light.shadowAttenuation);
                half3 ambient = SampleSH(n);
                half3 col = albedo * (diffuse + ambient);
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
    FallBack "Universal Render Pipeline/Lit"
}
