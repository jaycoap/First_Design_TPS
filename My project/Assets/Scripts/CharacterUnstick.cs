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

    /// <summary>파묻힌 만큼 밀어낸다. 매 프레임 Move() 직후에 부르면 된다.</summary>
    public static void Resolve(CharacterController cc)
    {
        if (cc == null || !cc.enabled) return;

        Transform t = cc.transform;
        int mask = CollisionMaskFor(t.gameObject.layer);
        float sx = Mathf.Abs(t.lossyScale.x);
        float sy = Mathf.Abs(t.lossyScale.y);
        float radius = cc.radius * sx;
        if (radius <= 1e-6f) return;

        // 캡슐 양 끝의 구(球) 중심. height가 지름보다 작은 구성이면 구 하나로 취급된다.
        float height = Mathf.Max(cc.height * sy, radius * 2f);
        Vector3 center = t.TransformPoint(cc.center);
        Vector3 half = t.up * (height * 0.5f - radius);

        int n = Physics.OverlapCapsuleNonAlloc(center - half, center + half, radius,
                                               Buffer, mask, QueryTriggerInteraction.Ignore);
        if (n <= 0) return;

        // 겹침이 skinWidth 안쪽이면 CharacterController가 알아서 흡수하는 정상 접촉이다.
        // 이 문턱이 없으면 벽에 붙어 서 있거나 바닥을 딛고 있는 것만으로 매 프레임
        // 미세하게 밀려 떨린다.
        float slack = cc.skinWidth * sy;
        Vector3 push = Vector3.zero;

        for (int i = 0; i < n; i++)
        {
            Collider col = Buffer[i];
            if (col == null || col == cc) continue;
            if (col.transform.IsChildOf(t)) continue;   // 자기 몸(부위 히트박스 등)

            if (!Physics.ComputePenetration(cc, t.position, t.rotation,
                                            col, col.transform.position, col.transform.rotation,
                                            out Vector3 dir, out float dist))
                continue;

            if (dist > slack) push += dir * (dist - slack);
        }

        // Move()가 아니라 위치를 직접 옮긴다 — Move는 같은 벽에 다시 막혀서
        // 이미 갇힌 상태를 풀지 못한다.
        if (push.sqrMagnitude > 1e-12f) t.position += push;
    }
}
