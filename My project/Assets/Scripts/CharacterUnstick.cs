using UnityEngine;

/// <summary>
/// CharacterController가 지오메트리에 파묻혔을 때 밖으로 밀어내는 공용 처리.
///
/// CharacterController는 <b>스스로 겹침을 풀지 못한다</b>. Move()는 "이동을 막을" 뿐이라
/// 한 번 벽 안으로 들어가면 어느 방향으로도 나오지 못하고 그 자리에 갇힌다 — 맵에 끼는 현상.
/// 파묻히는 경로는 여러 가지다:
///   - 물리 접촉 오프셋만큼 부풀어 있는 콜라이더에 밀려 들어감
///   - 맵 메시의 얇은 구석이나 벽 이음매에 쐐기처럼 박힘
///   - 텔레포트/워프로 겹친 자리에 놓임(설 자리 검사는 캡슐을 조금 줄여서 보므로 완벽하지 않다)
///
/// 원인을 하나씩 막는 대신 매 프레임 겹친 깊이를 재서 최소 거리로 빼낸다. 겹친 만큼만
/// 옮기므로 벽을 통과하지는 않는다.
/// </summary>
public static class CharacterUnstick
{
    private static readonly Collider[] Buffer = new Collider[16];

    /// <summary>레이어별 '실제로 부딪히는 상대' 마스크 캐시(레이어 충돌 행렬에서 뽑는다).</summary>
    private static readonly int[] MaskCache = new int[32];
    private static readonly bool[] MaskReady = new bool[32];

    /// <summary>
    /// 이 레이어가 물리적으로 부딪히는 모든 레이어.
    ///
    /// 여기에 스크립트의 시야/지형 마스크(obstacleMask 같은 것)를 쓰면 안 된다.
    /// 그런 마스크는 "레이캐스트로 볼 대상"이라 실제 충돌 대상과 다르다 —
    /// 예컨대 보스의 obstacleMask에는 아레나 벽 레이어가 빠져 있어서, 그걸로 겹침을 풀면
    /// 정작 벽에 낀 경우를 못 푼다. 캡슐을 실제로 막는 것은 레이어 충돌 행렬이 정한다.
    /// </summary>
    private static int CollisionMaskFor(int layer)
    {
        if (layer < 0 || layer > 31) return ~0;
        if (MaskReady[layer]) return MaskCache[layer];

        int mask = 0;
        for (int i = 0; i < 32; i++)
            if (!Physics.GetIgnoreLayerCollision(layer, i)) mask |= 1 << i;

        MaskCache[layer] = mask;
        MaskReady[layer] = true;
        return mask;
    }

    /// <summary>
    /// 파묻힌 만큼 밀어낸다. 매 프레임 Move() 직후에 부르면 된다.
    /// </summary>
    /// <returns>
    /// 이번 프레임에 가장 깊이 박혀 있던 깊이(월드 미터). 0이면 정상 접촉이다.
    /// 0보다 크면 <b>지오메트리 안에 들어가 있었다</b>는 뜻이라, 부르는 쪽에서
    /// 끼임 판정에 쓸 수 있다(벽에 붙어 걷는 것과 구분하는 유일한 단서).
    /// </returns>
    public static float Resolve(CharacterController cc) => Resolve(cc, out _);

    /// <summary>겹친 상대까지 알아야 할 때(진단 로그 등).</summary>
    public static float Resolve(CharacterController cc, out Collider deepestCollider)
    {
        deepestCollider = null;
        if (cc == null || !cc.enabled) return 0f;

        Transform t = cc.transform;
        int mask = CollisionMaskFor(t.gameObject.layer);
        float sx = Mathf.Abs(t.lossyScale.x);
        float sy = Mathf.Abs(t.lossyScale.y);
        float radius = cc.radius * sx;
        if (radius <= 1e-6f) return 0f;

        // 캡슐 양 끝의 구(球) 중심. height가 지름보다 작은 구성이면 구 하나로 취급된다.
        float height = Mathf.Max(cc.height * sy, radius * 2f);
        Vector3 center = t.TransformPoint(cc.center);
        Vector3 half = t.up * (height * 0.5f - radius);

        int n = Physics.OverlapCapsuleNonAlloc(center - half, center + half, radius,
                                               Buffer, mask, QueryTriggerInteraction.Ignore);
        if (n <= 0) return 0f;

        // 겹침이 skinWidth 안쪽이면 CharacterController가 알아서 흡수하는 정상 접촉이다.
        // 이 문턱이 없으면 벽에 붙어 서 있거나 바닥을 딛고 있는 것만으로 매 프레임
        // 미세하게 밀려 떨린다.
        float slack = cc.skinWidth * sy;
        Vector3 push = Vector3.zero;
        Vector3 deepestPush = Vector3.zero;
        float deepest = 0f;

        for (int i = 0; i < n; i++)
        {
            Collider col = Buffer[i];
            if (col == null || col == cc) continue;
            if (col.transform.IsChildOf(t)) continue;   // 자기 몸(부위 히트박스 등)

            if (!Physics.ComputePenetration(cc, t.position, t.rotation,
                                            col, col.transform.position, col.transform.rotation,
                                            out Vector3 dir, out float dist))
                continue;

            if (dist <= slack) continue;

            float depth = dist - slack;
            Vector3 p = dir * depth;
            push += p;
            if (depth > deepest) { deepest = depth; deepestPush = p; deepestCollider = col; }
        }

        // 마주 보는 두 면(계단 옆면과 벽, 벽 이음매 등) 사이에 쐐기처럼 박히면
        // 밀어내는 방향이 서로 상쇄돼 합이 0에 가까워진다. 그대로 두면
        // "겹쳐 있는데 어디로도 못 빠지는" 상태가 그대로 유지된다 — 이게 맵에 끼는 모습이다.
        // 이럴 때는 합 대신 가장 깊이 박힌 쪽으로만 뺀다(그쪽이 가장 얕게 빠져나오는 길이다).
        if (deepest > 0f && push.magnitude < deepest * 0.5f) push = deepestPush;

        // Move()가 아니라 위치를 직접 옮긴다 — Move는 같은 벽에 다시 막혀서
        // 이미 갇힌 상태를 풀지 못한다.
        if (push.sqrMagnitude > 1e-12f) t.position += push;
        return deepest;
    }

    /// <summary>
    /// 수평 어느 쪽으로도 캡슐을 뺄 수 없는가.
    ///
    /// "못 움직인다"만으로는 끼임을 판정할 수 없다 — 벽에 대고 걸어도 안 움직이기 때문이다.
    /// 여기서는 8방향으로 캡슐을 쓸어 보고 <b>전부 막혔을 때만</b> 갇힌 것으로 본다.
    /// 매 프레임 부르지 말고, 못 움직인 시간이 쌓였을 때 한 번만 확인하는 용도다.
    /// </summary>
    public static bool IsBoxedIn(CharacterController cc)
    {
        if (cc == null || !cc.enabled) return false;

        Transform t = cc.transform;
        int mask = CollisionMaskFor(t.gameObject.layer);
        GetCapsule(cc, t.position, out Vector3 p0, out Vector3 p1, out float radius);
        if (radius <= 1e-6f) return false;

        // 반지름의 절반이면 "한 걸음 옆으로 비킬 수 있는가"를 보기에 충분하다.
        float probe = radius * 0.5f;

        for (int i = 0; i < 8; i++)
        {
            float a = i * (Mathf.PI * 2f / 8f);
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            if (!Physics.CapsuleCast(p0, p1, radius * 0.95f, dir, probe, mask,
                                     QueryTriggerInteraction.Ignore))
                return false;   // 한 방향이라도 열려 있으면 갇힌 게 아니다
        }
        return true;
    }

    /// <summary>
    /// 낀 캐릭터를 가장 가까운 빈 자리로 옮긴다.
    ///
    /// 위로 먼저 시도한다 — 계단이나 바닥 이음매에 박히는 경우가 가장 흔하고,
    /// 살짝 들어 올리는 것이 가장 짧고 안전한 탈출이기 때문이다. 그다음 수평 8방향을
    /// hint에 가까운 순서로 훑는다. 어느 후보든 <b>발밑에 바닥이 있어야</b> 인정한다 —
    /// 안 그러면 끼임을 푸는 대신 허공에 떨어뜨리게 된다.
    /// </summary>
    /// <param name="hint">되도록 이 방향으로 빼낸다(예: 아레나 안쪽). 0이면 방향을 가리지 않는다.</param>
    public static bool TryEscape(CharacterController cc, Vector3 hint)
    {
        if (cc == null || !cc.enabled) return false;

        Transform t = cc.transform;
        int mask = CollisionMaskFor(t.gameObject.layer);
        GetCapsule(cc, t.position, out _, out _, out float radius);
        if (radius <= 1e-6f) return false;

        hint.y = 0f;
        if (hint.sqrMagnitude > 1e-8f) hint.Normalize();

        // 가까운 곳부터 넓혀 간다 — 필요 이상으로 멀리 옮기면 그것대로 순간이동처럼 보인다
        for (int ring = 1; ring <= 3; ring++)
        {
            float step = radius * ring;

            if (TryPlace(cc, t.position + Vector3.up * step, mask)) return true;

            // 수평은 hint에 가까운 방향부터 — 각도 차가 작은 순으로 훑는다
            for (int i = 0; i < 12; i++)
            {
                float a = ((i + 1) / 2) * (Mathf.PI * 2f / 12f) * (i % 2 == 0 ? 1f : -1f);
                Vector3 dir = hint.sqrMagnitude > 1e-8f
                            ? Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.up) * hint
                            : new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));

                // 살짝 띄운 채로 옆으로 뺀다 — 바닥 턱에 다시 걸리는 것을 막는다
                if (TryPlace(cc, t.position + dir * step + Vector3.up * (radius * 0.5f), mask)) return true;
            }
        }
        return false;
    }

    /// <summary>후보 자리가 비어 있고 발밑에 바닥이 있으면 그리로 옮긴다.</summary>
    private static bool TryPlace(CharacterController cc, Vector3 at, int mask)
    {
        Transform t = cc.transform;
        GetCapsule(cc, at, out Vector3 p0, out Vector3 p1, out float radius);

        // 캡슐을 조금 줄여서 본다 — 정확히 딱 맞는 자리는 다음 프레임에 도로 낀다
        if (Physics.CheckCapsule(p0, p1, radius * 0.95f, mask, QueryTriggerInteraction.Ignore))
            return false;

        // 발밑 확인. 캐릭터 키의 세 배 안에 바닥이 없으면 허공이므로 쓰지 않는다.
        float height = Mathf.Max(cc.height * Mathf.Abs(t.lossyScale.y), radius * 2f);
        if (!Physics.Raycast(p0, Vector3.down, height * 3f, mask, QueryTriggerInteraction.Ignore))
            return false;

        t.position = at;
        return true;
    }

    /// <summary>지정한 위치에 놓았을 때의 캡슐(양 끝 구 중심 + 반지름). 전부 월드 단위.</summary>
    private static void GetCapsule(CharacterController cc, Vector3 at,
                                   out Vector3 p0, out Vector3 p1, out float radius)
    {
        Transform t = cc.transform;
        float sx = Mathf.Abs(t.lossyScale.x);
        float sy = Mathf.Abs(t.lossyScale.y);
        radius = cc.radius * sx;
        float height = Mathf.Max(cc.height * sy, radius * 2f);

        // TransformPoint는 현재 위치 기준이라, 옮겨 놓았을 때의 중심으로 보정한다
        Vector3 center = t.TransformPoint(cc.center) + (at - t.position);
        Vector3 half = t.up * (height * 0.5f - radius);
        p0 = center - half;
        p1 = center + half;
    }
}
