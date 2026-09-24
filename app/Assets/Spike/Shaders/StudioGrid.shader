// The Body Studio's work grid on the deck (docs/08 §3): thin lines every 10 mm, stronger ones every 50 mm, the
// chassis centre lines in the axis colours (x red, z blue), fading out toward the edge. Drawn on a flat quad
// in the chassis frame, in metres; the lines stay one pixel wide at any zoom.
Shader "CoreEngine/StudioGrid"
{
    Properties
    {
        _MinorColor ("Minor lines", Color) = (1, 1, 1, 0.10)
        _MajorColor ("Major lines", Color) = (1, 1, 1, 0.24)
        _AxisXColor ("X axis", Color) = (1.0, 0.36, 0.36, 0.7)
        _AxisZColor ("Z axis", Color) = (0.36, 0.58, 1.0, 0.7)
        _Minor ("Minor spacing, m", Float) = 0.01
        _Major ("Major spacing, m", Float) = 0.05
        _Radius ("Fade radius, m", Float) = 0.17
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "StudioGrid"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _MinorColor;
                half4 _MajorColor;
                half4 _AxisXColor;
                half4 _AxisZColor;
                float _Minor;
                float _Major;
                float _Radius;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 plane : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.plane = input.positionOS.xz;
                return output;
            }

            // 1 on a line of the given spacing, fading to 0 one pixel away.
            float Lines(float2 p, float spacing)
            {
                float2 q = p / spacing;
                float2 width = max(fwidth(q), 1e-5);
                float2 d = abs(frac(q - 0.5) - 0.5) / width;
                return 1.0 - saturate(min(d.x, d.y));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.plane;
                float2 width = max(fwidth(p), 1e-6);
                half4 colour = _MinorColor * Lines(p, _Minor);
                colour = lerp(colour, _MajorColor, Lines(p, _Major));
                float xAxis = 1.0 - saturate(abs(p.y) / width.y - 0.5);
                float zAxis = 1.0 - saturate(abs(p.x) / width.x - 0.5);
                colour = lerp(colour, _AxisXColor, xAxis);
                colour = lerp(colour, _AxisZColor, zAxis);
                float fade = saturate(1.0 - length(p) / _Radius);
                colour.a *= fade * fade * (3.0 - 2.0 * fade);
                return colour;
            }
            ENDHLSL
        }
    }
}
