// =============================================================================
//  BossFX/Telegraph  —  바닥 경고 장판 (원형 / 링 / 부채꼴 / 직선)
//  URP (Universal Render Pipeline) 전용. SRP Batcher 호환.
//
//  XZ 평면에 눕힌 Quad 에 붙여서 씁니다. 크기는 오브젝트 스케일로 조절.
//  _Fill 을 0 → 1 로 올리면 경고가 차오르고, 1 에서 발동시키면 됩니다.
// =============================================================================
Shader "BossFX/Telegraph"
{
    Properties
    {
        [Header(Shape)]
        [Enum(Circle,0,Ring,1,Cone,2,Line,3)]
        _Shape           ("모양", Float) = 0
        _InnerRadius     ("링 안쪽 반지름", Range(0,0.99)) = 0.6
        _ConeAngle       ("부채꼴 각도(도)", Range(0,360)) = 90
        _ConeDirection   ("부채꼴 방향(도)", Range(-180,180)) = 0
        _LineWidth       ("직선 두께", Range(0.01,1)) = 0.15

        [Header(Fill)]
        [Enum(Radial,0,Angular,1,Linear,2)]
        _FillMode        ("채우기 방향", Float) = 0
        _Fill            ("채움 진행도", Range(0,1)) = 0.0
        _FillAlpha       ("채운 부분 알파", Range(0,3)) = 0.9

        [Header(Look)]
        _ColorBase       ("기본 색", Color) = (0.35, 0.12, 0.75, 1)
        _ColorHot        ("위험 색", Color) = (1.0, 0.18, 0.35, 1)
        _ColorEdge       ("테두리 색", Color) = (0.85, 0.55, 1.0, 1)
        _BaseAlpha       ("기본 알파", Range(0,2)) = 0.18
        _EdgeWidth       ("테두리 두께", Range(0.001,0.3)) = 0.035
        _EdgeIntensity   ("테두리 세기", Range(0,8)) = 2.2
        _Intensity       ("전체 세기", Range(0,8)) = 1.0
        _Opacity         ("페이드(등장/소멸)", Range(0,1)) = 1.0

        [Header(Motion)]
        _StripeScale     ("경고 줄무늬 밀도", Range(0,30)) = 7
        _StripeSpeed     ("줄무늬 흐름 속도", Range(-5,5)) = 0.6
        _StripeStrength  ("줄무늬 세기", Range(0,2)) = 0.35
        _PulseSpeed      ("맥동 속도", Range(0,10)) = 2.0
        _PulseStrength   ("맥동 세기", Range(0,2)) = 0.3

        [Header(Noise)]
        _NoiseTex        ("노이즈", 2D) = "white" {}
        _NoiseScroll     ("노이즈 흐름", Vector) = (0.03, 0.05, 0, 0)
        _NoiseStrength   ("노이즈 세기", Range(0,1)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent+100"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha One          // 가산 합성 — 어두운 바닥에서 발광하듯 보입니다
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            Name "TelegraphForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "BossFX.hlsl"

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseTex_ST;
                float4 _ColorBase;
                float4 _ColorHot;
                float4 _ColorEdge;
                float4 _NoiseScroll;
                float  _Shape;
                float  _InnerRadius;
                float  _ConeAngle;
                float  _ConeDirection;
                float  _LineWidth;
                float  _FillMode;
                float  _Fill;
                float  _FillAlpha;
                float  _BaseAlpha;
                float  _EdgeWidth;
                float  _EdgeIntensity;
                float  _Intensity;
                float  _Opacity;
                float  _StripeScale;
                float  _StripeSpeed;
                float  _StripeStrength;
                float  _PulseSpeed;
                float  _PulseStrength;
                float  _NoiseStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 p = (IN.uv - 0.5) * 2.0;

                float dirRad = radians(_ConeDirection);
                float halfA  = radians(_ConeAngle) * 0.5;

                // ---- 도형 마스크
                float d = BossShapeSD(p, _Shape, _InnerRadius, dirRad, halfA, _LineWidth);
                float w = BossAAWidth(d);
                float inside = BossAA(d, w);
                float edge   = BossAA(abs(d) - _EdgeWidth, w);

                // ---- 채움
                float t = BossFillCoord(p, _FillMode, dirRad);
                float filled = 1.0 - smoothstep(_Fill - 0.02, _Fill + 0.02, t);
                // 채워지는 최전선을 밝게
                float front = exp(-pow((t - _Fill) * 26.0, 2.0)) * step(0.001, _Fill);

                // ---- 경고 줄무늬 (대각선 스크롤)
                float s = frac((p.x + p.y) * _StripeScale - _TimeParameters.x * _StripeSpeed);
                float stripe = smoothstep(0.42, 0.5, abs(s - 0.5)) * _StripeStrength;

                // ---- 노이즈
                float2 nuv = IN.uv * _NoiseTex_ST.xy + _NoiseTex_ST.zw
                           + _TimeParameters.x * _NoiseScroll.xy;
                float n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, nuv).r;
                n = lerp(1.0, n * 1.6, _NoiseStrength);

                // ---- 맥동 (다 찰수록 강해짐)
                float pulse = 1.0 + sin(_TimeParameters.x * _PulseSpeed * 6.2831853)
                                  * _PulseStrength * _Fill;

                // ---- 합성
                half3 col = lerp(_ColorBase.rgb, _ColorHot.rgb, filled);
                col = lerp(col, _ColorEdge.rgb, saturate(edge));
                col = lerp(col, _ColorEdge.rgb, saturate(front));

                half a = inside * (_BaseAlpha + stripe + filled * _FillAlpha) * n;
                a += edge * _EdgeIntensity;
                a += front * inside * 1.5;
                a *= pulse * _Intensity * _Opacity;

                return half4(col, max(a, 0.0));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
