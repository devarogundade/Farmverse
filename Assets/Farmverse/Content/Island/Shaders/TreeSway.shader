// Farmverse/TreeSway: stylized lit shader for the Tripo trees with wind animation done on the GPU.
// The model's origin is the bottom centre of the trunk (tools/blender/tripo_bake.py), so the bend
// grows with height above it: the trunk base stays planted, the canopy leans and the top leaves
// flutter. Each tree gets its own phase from where it stands, and a slow gust wave rolls across
// the island along the wind direction. GPU-instanced and SRP-batcher friendly; no static batching
// (the origin has to survive for the bend to work).
Shader "Farmverse/TreeSway"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1,1,1,1)
        [Normal] _BumpMap ("Normal map", 2D) = "bump" {}
        _BumpScale ("Normal strength", Range(0, 2)) = 1
        _Wrap ("Soft light wrap", Range(0, 1)) = 0.35
        _Height ("Model height (m, object space)", Float) = 8
        _TrunkFrac ("Rigid trunk (fraction of height)", Range(0, 0.9)) = 0.25
        _Bend ("Canopy lean (fraction of height, at the top)", Range(0, 0.2)) = 0.045
        _BendSpeed ("Lean speed", Range(0.1, 4)) = 1.1
        _Flutter ("Leaf flutter (fraction of height)", Range(0, 0.04)) = 0.009
        _FlutterSpeed ("Flutter speed", Range(0.5, 12)) = 5
        _WindDir ("Wind direction (xz)", Vector) = (1, 0, 0.35, 0)
        _Gust ("Gust strength", Range(0, 2)) = 0.6
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        half4 _BaseColor;
        half _BumpScale;
        half _Wrap;
        float _Height, _TrunkFrac, _Bend, _BendSpeed, _Flutter, _FlutterSpeed, _Gust;
        float4 _WindDir;
    CBUFFER_END

    // Moves an object-space vertex in the wind and returns its world position.
    float3 Sway(float3 posOS)
    {
        float3 posWS = TransformObjectToWorld(posOS);
        float3 rootWS = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
        float h = saturate(posOS.y / max(_Height, 0.01));
        float bendMask = saturate((h - _TrunkFrac * 0.5) / max(1 - _TrunkFrac * 0.5, 0.01));
        bendMask *= bendMask;
        float leafMask = smoothstep(_TrunkFrac, saturate(_TrunkFrac + 0.25), h);
        float scale = length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21)) * _Height;   // tree height in world metres

        float2 dir = normalize(_WindDir.xz + 1e-4);
        float t = _Time.y;
        float phase = dot(rootWS.xz, float2(0.071, 0.053));
        // a broad gust wave travelling downwind, plus the tree's own two-sine rocking
        float gust = saturate(sin(dot(rootWS.xz, dir) * 0.018 - t * 0.55) * 0.5 + 0.5);
        gust = gust * gust * _Gust;
        float rock = sin(t * _BendSpeed + phase) * 0.65 + sin(t * _BendSpeed * 2.3 + phase * 1.7) * 0.35;
        float lean = (rock * 0.6 + 0.4 + gust) * _Bend * scale * bendMask;
        float side = sin(t * _BendSpeed * 0.8 + phase * 2.1) * 0.3 * _Bend * scale * bendMask;
        posWS.xz += dir * lean + float2(-dir.y, dir.x) * side;
        posWS.y -= abs(lean) * 0.12 * bendMask;          // keep the canopy roughly on its arc

        // leaves: small, fast, out of step with each other
        float3 f = posOS / max(_Height, 0.01) * 12 + phase;
        float flutter = (sin(t * _FlutterSpeed + f.x + f.y * 0.7) + sin(t * _FlutterSpeed * 1.37 + f.z * 1.3)) * 0.5;
        posWS += float3(dir.x, 0.35, dir.y) * flutter * _Flutter * scale * leafMask * (1 + gust);
        return posWS;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 tangentWS : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            V vert(A v)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 ws = Sway(v.positionOS.xyz);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                VertexNormalInputs n = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                o.normalWS = n.normalWS;
                o.tangentWS = half4(n.tangentWS, v.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            // the season (set globally by IslandWeatherFx): autumn turns the leaves, winter puts snow on top
            half _FvWinter, _FvAutumn;

            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _BumpScale);
                half3 bit = cross(i.normalWS, i.tangentWS.xyz) * i.tangentWS.w;
                half3 n = normalize(mul(nTS, half3x3(i.tangentWS.xyz, bit, i.normalWS)));
                half leafy = saturate((albedo.g - max(albedo.r, albedo.b)) * 5);
                albedo.rgb = lerp(albedo.rgb, half3(0.95, 0.52, 0.16) * (0.6 + Luminance(albedo.rgb)), _FvAutumn * leafy * 0.85);
                albedo.rgb = lerp(albedo.rgb, half3(0.94, 0.97, 1.0), _FvWinter * saturate(i.normalWS.y * 2.5 - 1.0) * 0.85);

                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS), i.positionWS, half4(1, 1, 1, 1));
                half ndl = dot(n, light.direction);
                half diff = saturate((ndl + _Wrap) / (1 + _Wrap));
                half3 col = albedo.rgb * (light.color * diff * light.shadowAttenuation * light.distanceAttenuation + SampleSH(n));
                col = MixFog(col, ComputeFogFactor(TransformWorldToHClip(i.positionWS).z));
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; };

            V vert(A v)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 ws = Sway(v.positionOS.xyz);
                float3 nws = TransformObjectToWorldNormal(v.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 ld = normalize(_LightPosition - ws);
            #else
                float3 ld = _LightDirection;
            #endif
                float4 p = TransformWorldToHClip(ApplyShadowBias(ws, nws, ld));
            #if UNITY_REVERSED_Z
                p.z = min(p.z, UNITY_NEAR_CLIP_VALUE);
            #else
                p.z = max(p.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = p;
                return o;
            }
            half4 frag(V i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; };
            V vert(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); o.positionCS = TransformWorldToHClip(Sway(v.positionOS.xyz)); return o; }
            half frag(V i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };
            V vert(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); o.positionCS = TransformWorldToHClip(Sway(v.positionOS.xyz)); o.normalWS = TransformObjectToWorldNormal(v.normalOS); return o; }
            half4 frag(V i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
    FallBack Off
}
