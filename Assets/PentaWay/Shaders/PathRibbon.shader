Shader "PentaWay/PathRibbon"
{
    Properties
    {
        _BaseColor ("Ribbon colour", Color) = (1, 1, 1, 0.92)
        _AccentColor ("Chevron colour", Color) = (0.55, 0.75, 1, 1)
        _FlowSpeed ("Flow speed", Float) = 0.6
        _FadeStart ("Fade start (m)", Float) = 14
        _FadeEnd ("Fade end (m)", Float) = 28
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "PathRibbon"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _AccentColor;
                float _FlowSpeed;
                float _FadeStart;
                float _FadeEnd;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                half4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float edge = smoothstep(0.0, 0.12, input.uv.x) * smoothstep(1.0, 0.88, input.uv.x);
                float chevron = frac(input.uv.y * 0.7 + abs(input.uv.x - 0.5) * 0.6 - _Time.y * _FlowSpeed);
                float band = smoothstep(0.0, 0.04, chevron) * (1.0 - smoothstep(0.16, 0.22, chevron));
                half3 colour = lerp(_BaseColor.rgb, _AccentColor.rgb, band);
                float d = distance(input.positionWS, GetCameraPositionWS());
                float fade = 1.0 - saturate((d - _FadeStart) / max(_FadeEnd - _FadeStart, 0.01));
                return half4(colour, _BaseColor.a * edge * fade * input.color.a);
            }
            ENDHLSL
        }
    }
}
