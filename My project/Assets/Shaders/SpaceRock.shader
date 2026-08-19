// 절차적 우주 암석 - 부유물(Asteroids)용.
// 밋밋한 회색 덩어리로 보이는 걸 막으려고, 그늘을 깊게 깔고 태양 쪽만 밝히며
// 성운 색 테두리광으로 실루엣을 살린다. 기본값은 어둡게 - 배경에 가라앉아야 한다.
//
// 행성과 같은 방식으로 태양 방향을 직접 받는다(_SunDir, 도구가 채운다).
// HLSL 블록 안은 전부 ASCII로만 쓴다.
Shader "TPS/Space Rock"
{
    Properties
    {
        _RockDark    ("암석(그늘)", Color) = (0.030, 0.028, 0.036, 1)
        _RockLight   ("암석(양지)", Color) = (0.150, 0.140, 0.155, 1)
        _RimColor    ("테두리광(성운색)", Color) = (0.45, 0.18, 0.52, 1)

        _SunDir      ("태양 방향(도구가 채운다)", Vector) = (0.5, 0.4, -0.75, 0)
        _SunLevel    ("햇빛 세기", Range(0, 3)) = 0.9
        _AmbientLevel("그늘 밝기", Range(0, 1)) = 0.06
        _Terminator  ("낮밤 경계 부드럽기", Range(0.01, 0.8)) = 0.25

        _NoiseScale  ("표면 무늬 크기", Range(0.05, 20)) = 4.0
        _RimPower    ("테두리 좁기", Range(1, 8)) = 3.0
        _RimStrength ("테두리 세기", Range(0, 3)) = 0.55
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

            float4 _RockDark, _RockLight, _RimColor;
            float4 _SunDir;
            float _SunLevel, _AmbientLevel, _Terminator;
            float _NoiseScale, _RimPower, _RimStrength, _Exposure;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float3 objPos   : TEXCOORD0;   // per-rock variation
                float3 worldNrm : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.objPos = v.vertex.xyz;
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
                for (int k = 0; k < 4; k++)
                {
                    sum += amp * vnoise(p);
                    p *= 2.11;
                    amp *= 0.5;
                }
                return sum;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 N = normalize(i.worldNrm);
                float3 V = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 L = normalize(_SunDir.xyz);

                // surface tone varies per rock and across each rock
                float n = fbm(i.objPos * _NoiseScale);
                float3 base = lerp(_RockDark.rgb, _RockLight.rgb, saturate(n * 1.5));

                // hard-ish terminator: deep shadow, only the sun side reads
                float ndl = dot(N, L);
                float day = smoothstep(-_Terminator, _Terminator, ndl);
                float3 col = base * (_AmbientLevel + day * _SunLevel);

                // nebula rim keeps the silhouette readable against the dark sky
                float rim = pow(saturate(1.0 - saturate(dot(N, V))), _RimPower);
                col += _RimColor.rgb * rim * _RimStrength;

                return float4(col * _Exposure, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
