// =============================================================================
//  BossFX/Beam  —  레이저 빔 / 관통 광선
//  가로로 늘린 Quad 에 사용합니다. UV.x = 빔 진행 방향, UV.y = 두께 방향.
//  _Charge 0→1 로 가늘게 예열, _Fire 0→1 로 발사 폭 확장.
// =============================================================================
Shader "BossFX/Beam"
{
    Properties
    {
        _ColorCore     ("코어 색", Color) = (1, 0.9, 1, 1)
        _ColorGlow     ("외곽 색", Color) = (0.55, 0.2, 1, 1)
        _CoreWidth     ("코어 두께", Range(0.001,1)) = 0.10
        _GlowWidth     ("외곽 두께", Range(0.001,1)) = 0.55
        _GlowFalloff   ("외곽 감쇠", Range(0.5,8)) = 2.5
        _Charge        ("충전(0~1)", Range(0,1)) = 1.0
        _Fire          ("발사(0~1)", Range(0,1)) = 1.0
        _Intensity     ("세기", Range(0,12)) = 3.0
        _Opacity       ("페이드", Range(0,1)) = 1.0

        _HeadTaper     ("끝단 뾰족함", Range(0,1)) = 0.25
        _FlickerSpeed  ("깜빡임 속도", Range(0,60)) = 22
        _FlickerAmount ("깜빡임 세기", Range(0,1)) = 0.18

        _NoiseTex      ("노이즈", 2D) = "white" {}
        _NoiseScroll   ("노이즈 흐름", Vector) = (-2.5, 0.1, 0, 0)
        _NoiseStrength ("노이즈 세기", Range(0,1)) = 0.4
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Transparent+110"
        }

        Blend SrcAlpha One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "BeamForward"
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
                float4 _ColorCore;
                float4 _ColorGlow;
                float4 _NoiseScroll;
                float  _CoreWidth;
                float  _GlowWidth;
                float  _GlowFalloff;
                float  _Charge;
                float  _Fire;
                float  _Intensity;
                float  _Opacity;
                float  _HeadTaper;
                float  _FlickerSpeed;
                float  _FlickerAmount;
                float  _NoiseStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float along  = IN.uv.x;                 // 0 = 시작점, 1 = 끝
                float across = (IN.uv.y - 0.5) * 2.0;   // -1 .. 1

                // 진행도: _Fire 만큼만 그려서 빔이 뻗어나가게.
                // ※ smoothstep 은 edge0 < edge1 이어야 합니다. 거꾸로 넣으면
                //    HLSL 에서 동작이 정의되지 않아 빔이 반대로 그려지거나 사라집니다.
                //    반전이 필요하면 반드시 1 - smoothstep(...) 을 쓸 것.
                float reach = 1.0 - smoothstep(_Fire - 0.06, _Fire, along);

                // 끝으로 갈수록 가늘어짐
                float taper = lerp(1.0, saturate(1.0 - along), _HeadTaper);

                // 충전 중엔 얇게, 발사되면 굵게
                float widthScale = lerp(0.12, 1.0, _Charge) * taper;
                float core = 1.0 - smoothstep(0.0, max(_CoreWidth * widthScale, 1e-4), abs(across));
                float glow = pow(saturate(1.0 - abs(across) / max(_GlowWidth * widthScale, 1e-4)),
                                 _GlowFalloff);

                // 흐르는 노이즈
                float2 nuv = float2(along, across * 0.5 + 0.5) * _NoiseTex_ST.xy
                           + _TimeParameters.x * _NoiseScroll.xy;
                float n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, nuv).r;
                n = lerp(1.0, n * 1.8, _NoiseStrength);

                // 깜빡임
                float flicker = 1.0 + sin(_TimeParameters.x * _FlickerSpeed) * _FlickerAmount;

                half3 col = lerp(_ColorGlow.rgb, _ColorCore.rgb, saturate(core));
                half a = (core * 1.4 + glow * n) * reach * flicker * _Intensity * _Opacity;

                return half4(col, max(a, 0.0));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
