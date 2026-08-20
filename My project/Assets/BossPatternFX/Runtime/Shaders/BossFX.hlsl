#ifndef BOSSFX_INCLUDED
#define BOSSFX_INCLUDED

// =============================================================================
//  BossFX 공통 함수
//  좌표계: p = (uv - 0.5) * 2  →  [-1, 1] 범위의 평면 좌표
//  모든 SD 함수는 "부호 있는 거리"를 돌려줍니다 (내부 = 음수, 경계 = 0)
// =============================================================================

// ---- 도형 -------------------------------------------------------------------
float BossSdCircle(float2 p)
{
    return length(p) - 1.0;
}

// inner: 0..1 (안쪽 구멍 반지름)
float BossSdRing(float2 p, float inner)
{
    float l = length(p);
    return max(l - 1.0, inner - l);
}

// dirRad: 부채꼴 중심 방향, halfAngleRad: 벌어진 각의 절반
float BossSdCone(float2 p, float dirRad, float halfAngleRad)
{
    float l = length(p);
    float a = atan2(p.y, p.x) - dirRad;
    a = atan2(sin(a), cos(a));                 // [-PI, PI] 로 감기
    // 각도 오차를 길이로 환산해야 테두리 두께가 반지름에 상관없이 일정해집니다
    float angular = (abs(a) - halfAngleRad) * max(l, 1e-4);
    return max(l - 1.0, angular);
}

// h: 반폭 (x = 길이 방향, y = 두께 방향)
float BossSdBox(float2 p, float2 h)
{
    float2 d = abs(p) - h;
    return min(max(d.x, d.y), 0.0) + length(max(d, 0.0));
}

// shape : 0=원형 1=링 2=부채꼴 3=직선
float BossShapeSD(float2 p, float shape, float inner,
                  float dirRad, float halfAngleRad, float lineWidth)
{
    if (shape < 0.5) return BossSdCircle(p);
    if (shape < 1.5) return BossSdRing(p, inner);
    if (shape < 2.5) return BossSdCone(p, dirRad, halfAngleRad);
    return BossSdBox(p, float2(1.0, max(lineWidth, 0.001)));
}

// ---- 채워지는 방향 ----------------------------------------------------------
// mode : 0=방사형(중심→바깥) 1=각도(시계방향 스윕) 2=직선(왼→오른)
float BossFillCoord(float2 p, float mode, float dirRad)
{
    if (mode < 0.5)
    {
        return saturate(length(p));
    }
    if (mode < 1.5)
    {
        float a = atan2(p.y, p.x) - dirRad;
        a = atan2(sin(a), cos(a));
        return saturate(a / (2.0 * PI) + 0.5);
    }
    return saturate(p.x * 0.5 + 0.5);
}

// ---- 유틸 -------------------------------------------------------------------
// 부호 있는 거리 → 안티에일리어싱된 마스크 (내부 1, 외부 0)
float BossAA(float d, float w)
{
    return saturate(0.5 - d / max(w, 1e-5));
}

// 화면 공간 기준 AA 폭
float BossAAWidth(float d)
{
    return fwidth(d) * 1.5 + 1e-5;
}

// 값 노이즈 (텍스처 없이 쓸 때의 대체용)
float BossHash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float BossValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = BossHash21(i);
    float b = BossHash21(i + float2(1, 0));
    float c = BossHash21(i + float2(0, 1));
    float d = BossHash21(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

#endif // BOSSFX_INCLUDED
