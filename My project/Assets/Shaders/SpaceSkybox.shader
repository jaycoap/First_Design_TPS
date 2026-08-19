// 절차적 우주 스카이박스 - 텍스처 없이 별과 성운을 그린다.
// 창밖 우주는 이 스카이박스가 담당한다. 배경 FBX의 NebulaDome(반지름 1600m)은
// 맵 축척에 따라 카메라 far clip 안팎을 오가며 하늘을 통째로 가려 버리므로,
// SpaceLookSetup이 그 돔을 끄고 이 스카이박스를 대신 쓴다.
//
// HLSL 블록(CGPROGRAM ~ ENDCG) 안은 전부 ASCII로만 쓴다.
// 유니티 셰이더 전처리기가 비ASCII 주석에서 실패해 분홍색(에러) 셰이더가 되는 일이 있다.
Shader "TPS/Space Skybox"
{
    Properties
    {
        _SpaceColor    ("깊은 우주(위)", Color) = (0.014, 0.011, 0.028, 1)
        _HorizonColor  ("아래쪽", Color) = (0.045, 0.020, 0.055, 1)
        _NebulaColorA  ("성운 A (자홍)", Color) = (0.52, 0.14, 0.44, 1)
        _NebulaColorB  ("성운 B (보라)", Color) = (0.22, 0.09, 0.42, 1)
        _NebulaAmount  ("성운 세기", Range(0, 3)) = 1.0
        _NebulaScale   ("성운 크기", Range(0.5, 8)) = 1.9
        _NebulaCut     ("성운 문턱(높일수록 뭉침)", Range(0, 0.8)) = 0.38
        _StarDensity   ("별 밀도", Range(10, 400)) = 130
        _StarAmount    ("별 개수", Range(0, 1)) = 0.13
        _StarBrightness("별 밝기", Range(0, 8)) = 1.8
        _Exposure      ("노출", Range(0, 4)) = 1.0
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            float4 _SpaceColor, _HorizonColor, _NebulaColorA, _NebulaColorB;
            float _NebulaAmount, _NebulaScale, _NebulaCut;
            float _StarDensity, _StarAmount, _StarBrightness, _Exposure;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;   // skybox mesh object space = view direction
                return o;
            }

            float hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float3 hash33(float3 p)
            {
                float3 q;
                q.x = dot(p, float3(127.1, 311.7, 74.7));
                q.y = dot(p, float3(269.5, 183.3, 246.1));
                q.z = dot(p, float3(113.5, 271.9, 124.6));
                return frac(sin(q) * 43758.5453);
            }

            float vnoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = hash13(i + float3(0.0, 0.0, 0.0));
                float n100 = hash13(i + float3(1.0, 0.0, 0.0));
                float n010 = hash13(i + float3(0.0, 1.0, 0.0));
                float n110 = hash13(i + float3(1.0, 1.0, 0.0));
                float n001 = hash13(i + float3(0.0, 0.0, 1.0));
                float n101 = hash13(i + float3(1.0, 0.0, 1.0));
                float n011 = hash13(i + float3(0.0, 1.0, 1.0));
                float n111 = hash13(i + float3(1.0, 1.0, 1.0));

                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);
                float nxy0 = lerp(nx00, nx10, f.y);
                float nxy1 = lerp(nx01, nx11, f.y);
                return lerp(nxy0, nxy1, f.z);
            }

            float fbm(float3 p)
            {
                float sum = 0.0;
                float amp = 0.5;
                for (int k = 0; k < 5; k++)
                {
                    sum += amp * vnoise(p);
                    p *= 2.03;
                    amp *= 0.5;
                }
                return sum;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.dir);

                // vertical gradient
                float h = saturate(dir.y * 0.5 + 0.5);
                float3 col = lerp(_HorizonColor.rgb, _SpaceColor.rgb, h);

                // nebula: keep only the clumped part so the sky stays dark overall
                float n = fbm(dir * _NebulaScale + 11.3);
                n = saturate((n - _NebulaCut) * 2.4);
                float tint = saturate(fbm(dir * (_NebulaScale * 2.1) - 7.7) * 1.6);
                float3 neb = lerp(_NebulaColorA.rgb, _NebulaColorB.rgb, tint);
                col += neb * (n * n) * _NebulaAmount;

                // stars: split the direction into cells, one dot per cell
                float3 sp = dir * _StarDensity;
                float3 cell = floor(sp);
                float3 f = frac(sp);
                float3 r = hash33(cell);
                float d = length(f - r);
                float star = smoothstep(0.16, 0.0, d);
                float pick = step(hash13(cell + 5.1), _StarAmount);
                float tw = 0.55 + 0.45 * hash13(cell + 17.0);
                float3 starTint = lerp(float3(0.72, 0.82, 1.0), float3(1.0, 0.88, 0.72), hash13(cell + 3.0));
                col += star * pick * tw * _StarBrightness * starTint;

                return float4(col * _Exposure, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
