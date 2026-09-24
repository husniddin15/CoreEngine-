// The Body Studio's see-through shapes and handles (docs/08 §7): a colour with simple shading from a fixed light
// and a bright rim, so a translucent hole or the selected shape still reads as a 3D form. ZTest is a property:
// LessEqual for shapes (with a small depth offset so a shape wins over the body faces it coincides with) and
// Always for the move, turn and size handles, which must stay visible inside the body.
Shader "CoreEngine/StudioOverlay"
{
    Properties
    {
        _BaseColor ("Colour", Color) = (1, 1, 1, 0.35)
        _Shade ("Shading (0 flat, 1 lit)", Range(0, 1)) = 0.7
        _Rim ("Rim", Range(0, 1)) = 0.35
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        _OffsetFactor ("Depth offset factor", Float) = -1
        _OffsetUnits ("Depth offset units", Float) = -1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "StudioOverlay"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off
            Offset [_OffsetFactor], [_OffsetUnits]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Shade;
                half _Rim;
                float _ZTest;
                float _OffsetFactor;
                float _OffsetUnits;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewWS = GetWorldSpaceViewDir(positionWS);
                return output;
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float3 n = normalize(input.normalWS) * IS_FRONT_VFACE(face, 1.0, -1.0);
                float3 v = normalize(input.viewWS);
                half light = saturate(dot(n, normalize(float3(0.35, 0.85, 0.40))));
                half shade = lerp(1.0h, 0.55h + 0.45h * light, _Shade);
                half rim = pow(1.0h - saturate(abs(dot(n, v))), 2.0h) * _Rim;
                half4 colour = _BaseColor;
                colour.rgb = colour.rgb * shade + rim;
                colour.a = saturate(colour.a + rim * 0.5h);
                return colour;
            }
            ENDHLSL
        }
    }
}
