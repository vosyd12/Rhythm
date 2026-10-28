Shader "Forest/UI/SlashGlow"
{
    Properties
    {
        [PerRendererData]
        _MainTex ("Texture", 2D) = "white" {}

        _EmissionStrength ("Emission Strength", Range(1, 20)) = 4
        _CoreStrength ("Core Strength", Range(0, 3)) = 1
        _HaloStrength ("Halo Strength", Range(0, 1)) = 0.18

        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)]
        _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]

        // 밝기는 더하고, 그려지는 화면의 알파는 유지한다.
        Blend One One, Zero One
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 2.0

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct VertexInput
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct VertexOutput
            {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 localPosition : TEXCOORD1;
            };

            float4 _ClipRect;

            float _EmissionStrength;
            float _CoreStrength;
            float _HaloStrength;

            VertexOutput Vert(VertexInput input)
            {
                VertexOutput output;

                output.vertex = UnityObjectToClipPos(input.vertex);
                output.localPosition = input.vertex.xy;
                output.color = input.color;
                output.uv = input.uv;

                return output;
            }

            float4 Frag(VertexOutput input) : SV_Target
            {
                // 중심은 0, 양쪽 가장자리는 1이다.
                float distanceFromCenter =
                    abs(input.uv.y * 2.0 - 1.0);

                float distanceSquared =
                    distanceFromCenter * distanceFromCenter;

                // 가는 중심선, 색상 띠, 넓은 외곽광.
                float core = exp2(-80.0 * distanceSquared);
                float body = exp2(-12.0 * distanceSquared);
                float halo = exp2(-3.0 * distanceSquared);

                // 메시 끝에서 빛이 잘린 것처럼 보이지 않게 한다.
                float edgeFade = 1.0 -
                    smoothstep(0.75, 1.0, distanceFromCenter);

                float tipFade =
                    smoothstep(0.0, 0.06, input.uv.x) *
                    smoothstep(0.0, 0.06, 1.0 - input.uv.x);

                float opacity =
                    input.color.a * edgeFade * tipFade;

                #ifdef UNITY_UI_CLIP_RECT
                opacity *= UnityGet2DClipping(
                    input.localPosition, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(opacity - 0.001);
                #endif

                float3 tint = input.color.rgb;

                // 중심은 흰색에 가깝게, 외곽은 판정 색상을 유지한다.
                float3 coreColor = lerp(
                    tint, float3(1.0, 1.0, 1.0), 0.85);

                float3 light =
                    coreColor * core * _CoreStrength +
                    tint * body * 0.65 +
                    tint * halo * _HaloStrength;

                // 기존 연출기의 알파 감소가 발광에도 적용된다.
                light *= _EmissionStrength * opacity;

                return float4(light, 0.0);
            }

            ENDCG
        }
    }
}