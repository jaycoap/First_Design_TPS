// =============================================================================
//  BossFX/Radial  —  충격파 링 / 폭발 섬광 / 탄환 글로우
//  하나의 Quad 에서 세 가지 모드를 처리합니다.
//    Mode 0 : Ring   (퍼져나가는 충격파)
//    Mode 1 : Orb    (탄환 / 코어 발광)
//    Mode 2 : Burst  (방사형 섬광, 스파이크)
// =============================================================================
Shader "BossFX/Radial"
{
    Properties
    {
        [Enum(Ring,0,Orb,1,Burst,2)]
        _Mode        ("모드", Float) = 0

        _ColorCore   ("코어 색", Color) = (1, 0.85, 1, 1)
        _ColorEdge   ("외곽 색", Color) = (0.6, 0.2, 1, 1)

        _Radius      ("링 반지름", Range(0,1)) = 0.5
        _Thickness   ("링 두께", Range(0.001,1)) = 0.12
        _Falloff     ("감쇠", Range(0.5,8)) = 2.0

        _BurstSpikes ("섬광 갈래 수", Range(2,32)) = 10
        _BurstSharp  ("섬광 날카로움", Range(1,16)) = 5
        _Spin        ("회전 속도", Range(-8,8)) = 0.4

        _Intensity   ("세기", Range(0,16)) = 3.0
        _Opacity     ("페이드", Range(0,1)) = 1.0

        _NoiseTex      ("노이즈", 2D) = "white" {}
        _NoiseStrength ("노이즈 세기", Range(0,1)) = 0.3
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Transparent+120"
        }

        Blend SrcAlpha One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "RadialForward"
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
                float4 _ColorEdge;
                float  _Mode;
                float  _Radius;
                float  _Thickness;
                float  _Falloff;
                float  _BurstSpikes;
                float  _BurstSharp;
                float  _Spin;
                float  _Intensity;
                float  _Opacity;
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
                float2 p = (IN.uv - 0.5) * 2.0;
                float  r = length(p);
                float  ang = atan2(p.y, p.x) + _TimeParameters.x * _Spin;

                // 원 밖은 전부 버림 (Quad 모서리 잘라내기)
                float clipMask = 1.0 - smoothstep(0.98, 1.0, r);

                float mask = 0.0;
                float coreMask = 0.0;

                if (_Mode < 0.5)
                {
                    // ---- Ring : 반지름 _Radius 에 두께 _Thickness 의 고리
                    float dr = abs(r - _Radius) / max(_Thickness, 1e-4);
                    mask = pow(saturate(1.0 - dr), _Falloff);
                    coreMask = pow(saturate(1.0 - dr * 2.2), _Falloff);
                }
                else if (_Mode < 1.5)
                {
                    // ---- Orb : 중심이 밝은 구형 글로우
                    mask = pow(saturate(1.0 - r), _Falloff);
                    coreMask = pow(saturate(1.0 - r / max(_Thickness, 1e-4)), 1.5);
                }
                else
                {
                    // ---- Burst : 방사형 섬광
                    float spikes = abs(cos(ang * max(_BurstSpikes, 1.0) * 0.5));
                    spikes = pow(spikes, _BurstSharp);
                    float radial = pow(saturate(1.0 - r), _Falloff);
                    mask = radial * lerp(0.25, 1.0, spikes);
                    coreMask = pow(saturate(1.0 - r * 3.0), 2.0);
                }

                float n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex,
                                           IN.uv * _NoiseTex_ST.xy
                                           + _TimeParameters.x * 0.05).r;
                n = lerp(1.0, n * 1.7, _NoiseStrength);

                half3 col = lerp(_ColorEdge.rgb, _ColorCore.rgb, saturate(coreMask));
                half a = (mask * n + coreMask * 1.2) * clipMask * _Intensity * _Opacity;

                return half4(col, max(a, 0.0));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
