// 절차적 행성 - "망해가는 별" 쪽으로 맞춘 기본값.
// 표면은 식어 굳은 어두운 암반, 갈라진 틈에서만 식어가는 용암빛이 새어 나오고,
// 대기는 얇게 남아 성운빛을 받는다. 대부분 그림자에 잠기고 태양 쪽 가장자리만 밝다.
//
// 라이팅 파이프라인에 기대지 않고 태양 방향을 직접 받는다(_SunDir, 도구가 채운다).
// HLSL 블록 안은 전부 ASCII로만 쓴다(비ASCII 주석에서 셰이더가 깨지는 일이 있다).
Shader "TPS/Planet"
{
    Properties
    {
        _RockColor   ("암반(어두운 쪽)", Color) = (0.035, 0.030, 0.042, 1)
        _RockLight   ("암반(밝은 쪽)", Color) = (0.115, 0.105, 0.125, 1)
        _CrackColor  ("갈라진 틈(빛)", Color) = (0.85, 0.22, 0.06, 1)
        _AshColor    ("재/구름", Color) = (0.14, 0.12, 0.15, 1)
        _AtmoColor   ("대기(테두리)", Color) = (0.42, 0.20, 0.55, 1)

        _SunDir      ("태양 방향(도구가 채운다)", Vector) = (0.5, 0.4, -0.75, 0)
        _SunLevel    ("햇빛 세기", Range(0, 3)) = 0.85
        _AmbientLevel("그늘 밝기", Range(0, 1)) = 0.05
        _Terminator  ("낮밤 경계 부드럽기", Range(0.01, 0.6)) = 0.10

        _NoiseScale  ("지형 크기", Range(0.5, 8)) = 2.6
        _CrackWidth  ("틈 굵기", Range(0.005, 0.2)) = 0.045
        _CrackGlow   ("틈 밝기", Range(0, 6)) = 1.6
        _AshAmount   ("재 구름 양", Range(0, 1)) = 0.25

        _AtmoPower   ("대기 두께", Range(1, 8)) = 3.5
        _AtmoStrength("대기 세기", Range(0, 3)) = 0.5
        _Exposure    ("노출", Range(0, 4)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            float4 _RockColor, _RockLight, _CrackColor, _AshColor, _AtmoColor;
            float4 _SunDir;
            float _SunLevel, _AmbientLevel, _Terminator;
            float _NoiseScale, _CrackWidth, _CrackGlow, _AshAmount;
            float _AtmoPower, _AtmoStrength, _Exposure;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos       : SV_POSITION;
                float3 objNormal : TEXCOORD0;   // pattern sticks to the planet
                float3 worldNrm  : TEXCOORD1;
                float3 worldPos  : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.objNormal = normalize(v.normal);
                o.worldNrm = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
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
                    p *= 2.07;
                    amp *= 0.5;
                }
                return sum;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 N = normalize(i.worldNrm);
                float3 V = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 L = normalize(_SunDir.xyz);
                float3 sp = normalize(i.objNormal);

                // --- cooled rock surface ---
                float n = fbm(sp * _NoiseScale + 3.7);
                float3 surf = lerp(_RockColor.rgb, _RockLight.rgb, saturate(n * 1.4));

                // ash clouds, thin and dull
                float ash = fbm(sp * (_NoiseScale * 2.3) + 21.0);
                float ashMask = smoothstep(0.98 - _AshAmount, 1.14 - _AshAmount, ash);
                surf = lerp(surf, _AshColor.rgb, ashMask * 0.7);

                // --- fissures: ridged noise, thin bright seams ---
                float ridge = 1.0 - abs(fbm(sp * (_NoiseScale * 1.35) - 5.2) * 2.0 - 1.0);
                float crack = smoothstep(1.0 - _CrackWidth, 1.0, ridge);

                // --- day / night ---
                float ndl = dot(N, L);
                float day = smoothstep(-_Terminator, _Terminator, ndl);
                float3 col = surf * (_AmbientLevel + day * _SunLevel);

                // fissures glow on their own, and read strongest on the dark side
                col += _CrackColor.rgb * crack * _CrackGlow * (0.35 + 0.65 * (1.0 - day));

                // --- thin atmosphere rim, brighter toward the sun ---
                float rim = pow(saturate(1.0 - saturate(dot(N, V))), _AtmoPower);
                col += _AtmoColor.rgb * rim * _AtmoStrength * (0.25 + 0.75 * saturate(ndl + 0.35));

                return float4(col * _Exposure, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
