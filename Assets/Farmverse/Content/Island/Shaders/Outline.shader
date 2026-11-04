// Farmverse outline: the glowing rim round a building the farmer is standing in (or a field they're
// working). Drawn on a copy of the building's meshes: the back faces pushed out along their normals
// and filled with one colour, so only a band shows around the silhouette. A gentle pulse makes it
// read as "active". Unlit, one pass, no textures.
Shader "Farmverse/Outline"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 0.86, 0.15, 1)
        _Width ("Width (m)", Float) = 0.12
        _Pulse ("Pulse", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+20" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Width;
                half _Pulse;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 w = TransformObjectToWorld(i.positionOS.xyz);
                float3 n = normalize(TransformObjectToWorldNormal(i.normalOS));
                float width = _Width * (1.0 + 0.25 * sin(_Time.y * 6.0));
                o.positionCS = TransformWorldToHClip(w + n * width);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half glow = 1.0 - _Pulse * 0.5 * (1.0 + sin(_Time.y * 6.0));
                return half4(_Color.rgb * (0.75 + 0.5 * glow), 1);
            }
            ENDHLSL
        }
    }
}
