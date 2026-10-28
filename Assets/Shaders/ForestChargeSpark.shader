Shader "Forest/UI/ChargeSpark"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        [HDR] _Color ("Emission Tint", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15

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

        // RGB의 빛을 더하고, 화면의 기존 알파는 유지한다.
        Blend One One, Zero One
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 localPosition : TEXCOORD1;
            };

            float4 _Color;
            float4 _ClipRect;

            v2f vert(appdata input)
            {
                v2f output;

                output.localPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.color = input.color;
                output.uv = input.uv;

                return output;
            }

            float4 frag(v2f input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float distanceSquared = dot(p, p);
                float distanceFromCenter = sqrt(distanceSquared);

                // 선명한 발광 중심.
                float core = exp2(-60.0 * distanceSquared);

                // 중심 주변의 부드러운 빛.
                float halo = exp2(-6.0 * distanceSquared) * 0.25;

                // 가로·세로로 뻗는 반짝임.
                float vertical =
                    pow(saturate(1.0 - abs(p.x) * 12.0), 3.0) *
                    pow(saturate(1.0 - abs(p.y)), 2.0);

                float horizontal =
                    pow(saturate(1.0 - abs(p.y) * 12.0), 3.0) *
                    pow(saturate(1.0 - abs(p.x)), 2.0);

                // 이미지 사각형의 경계가 드러나지 않게 한다.
                float edgeFade =
                    1.0 - smoothstep(0.7, 1.0, distanceFromCenter);

                float shape =
                    (core + halo + (vertical + horizontal) * 0.6) *
                    edgeFade;

                float opacity = input.color.a * _Color.a;

                #ifdef UNITY_UI_CLIP_RECT
                opacity *= UnityGet2DClipping(
                    input.localPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(shape * opacity - 0.001);
                #endif

                // 중심은 흰색에 가까워지고 주변은 입자 색을 유지한다.
                float3 tint = lerp(
                    input.color.rgb,
                    float3(1.0, 1.0, 1.0),
                    core * 0.7);

                // 마지막 출력 단계에서 HDR 밝기를 적용한다.
                float3 light =
                    tint * _Color.rgb * shape * opacity;

                return float4(light, 0.0);
            }

            ENDCG
        }
    }
}