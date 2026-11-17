Shader "PentaWay/PathArrow"
{
    Properties
    {
        _BaseColor ("Color", Color) = (0.10, 0.62, 1.0, 0.95)
        _FadeStart ("Fade start (m)", Float) = 12
        _FadeEnd ("Fade end (m)", Float) = 25
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "PathArrow"
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
                float _FadeStart;
                float _FadeEnd;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float distanceToCamera = distance(input.positionWS, GetCameraPositionWS());
                half fade = 1.0h - (half)saturate((distanceToCamera - _FadeStart) / max(_FadeEnd - _FadeStart, 0.01));
                return half4(_BaseColor.rgb, _BaseColor.a * fade);
            }
            ENDHLSL
        }
    }
}
