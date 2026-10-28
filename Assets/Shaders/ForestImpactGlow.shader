Shader "Forest/UI/ImpactGlow"
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
              // 이미지 중심을 기준으로 둥근 빛을 만든다.
              float2 position = input.uv * 2.0 - 1.0;
          
              float radiusSquared = dot(position, position);
              float radius = sqrt(radiusSquared);
          
              // 작은 중심 섬광과 넓은 주변 빛.
              float core = exp2(-60.0 * radiusSquared);
              float body = exp2(-12.0 * radiusSquared);
              float halo = exp2(-4.0 * radiusSquared);
          
              // 이미지의 사각형 경계가 드러나지 않도록 한다.
              float edgeFade =
                  1.0 - smoothstep(0.55, 1.0, radius);
          
              float opacity = input.color.a * edgeFade;
          
              #ifdef UNITY_UI_CLIP_RECT
              opacity *= UnityGet2DClipping(
                  input.localPosition, _ClipRect);
              #endif
          
              #ifdef UNITY_UI_ALPHACLIP
              clip(opacity - 0.001);
              #endif
          
              float3 tint = input.color.rgb;
          
              // 중심은 밝게, 주변에는 판정 색상을 남긴다.
              float3 coreColor = lerp(
                  tint, float3(1.0, 1.0, 1.0), 0.8);
          
              float3 light =
                  coreColor * core * _CoreStrength +
                  tint * body * 0.35 +
                  tint * halo * _HaloStrength;
          
              light *= _EmissionStrength * opacity;
          
              return float4(light, 0.0);
          }

            ENDCG
        }
    }
}