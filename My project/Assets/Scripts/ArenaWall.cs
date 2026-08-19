using UnityEngine;

/// <summary>
/// 아레나 낙하 방지용 "보이지 않는 벽".
/// 바닥 가장자리를 따라 박스 콜라이더를 빙 둘러 배치한다(렌더러 없음 = 눈에 보이지 않음).
/// CharacterController가 콜라이더에 막히므로 플레이어와 보스 모두 밖으로 나가지 못한다.
///
/// 모양은 두 가지다.
///  - 프로필 없음: 반지름 하나짜리 완전한 원(원형 아레나용)
///  - 프로필 있음: 방향별 반지름 배열 = 바닥 모양을 그대로 따라가는 다각형
///    (정사각형 격납고처럼 원이 아닌 맵에서 귀퉁이를 버리지 않기 위해)
///
/// - 값을 바꾼 뒤 인스펙터 우클릭 → "벽 다시 만들기"로 즉시 재생성된다.
/// - 원형 배치/측정은 Tools/TPS/Build Arena Wall 메뉴가,
///   실내 맵 모양 맞춤은 Tools/TPS/Change Map 쪽 도구가 자동으로 해준다.
/// - 카메라 충돌·사격·레이저 판정에서는 제외되도록 전용 레이어(ArenaWall)를 쓴다.
/// </summary>
public class ArenaWall : MonoBehaviour
{
    [Tooltip("기준 반지름(m). 프로필이 없으면 이 값으로 완전한 원을 만든다.")]
    [SerializeField] private float radius = 10f;
    [Tooltip("벽 높이(m). 점프로 넘지 못할 만큼 넉넉히.")]
    [SerializeField] private float height = 5f;
    [Tooltip("원을 몇 조각의 판으로 근사할지. 많을수록 매끄럽다(프로필이 있으면 그 길이를 쓴다).")]
    [SerializeField, Range(8, 128)] private int segments = 36;
    [Tooltip("벽 두께(m). 빠른 이동에 뚫리지 않을 만큼.")]
    [SerializeField] private float thickness = 0.5f;
    [Tooltip("바닥 아래로 더 내리는 길이(m). 아래층으로 떨어져도 벽 밑으로 빠져나가지 못하게 한다.")]
    [SerializeField] private float skirt = 0f;
    [Tooltip("방향별 반지름 = 바닥 모양. 비어 있으면 완전한 원. 0번이 +Z, 시계 방향으로 한 바퀴.")]
    [SerializeField] private float[] profile;

    /// <summary>
    /// 안쪽 반지름 — 이 원 안은 어느 방향으로든 확실히 아레나 안이다.
    /// 프로필이 있으면 가장 짧은 방향을 쓴다(보스 텔레포트·메테오 같은 '안전한 자리' 판정용).
    /// set은 프로필을 지우고 완전한 원으로 되돌린다.
    /// </summary>
    public float Radius
    {
        get
        {
            if (!HasProfile) return radius;
            float min = float.MaxValue;
            for (int i = 0; i < profile.Length; i++) min = Mathf.Min(min, profile[i]);
            return min;
        }
        set
        {
            radius = Mathf.Max(0.01f, value);
            profile = null;
        }
    }

    /// <summary>바깥 반지름 — 이 원 밖은 어느 방향으로든 확실히 아레나 밖이다(분신 배치 등).</summary>
    public float OuterRadius
    {
        get
        {
            if (!HasProfile) return radius;
            float max = 0f;
            for (int i = 0; i < profile.Length; i++) max = Mathf.Max(max, profile[i]);
            return max;
        }
    }

    /// <summary>인스펙터에 적어 둔 기준 반지름. 프로필을 씌워도 바뀌지 않는다(맵을 맞출 때의 목표값).</summary>
    public float BaseRadius => radius;

    public bool HasProfile => profile != null && profile.Length >= 8;

    public float Height
    {
        get => height;
        set => height = Mathf.Max(0.01f, value);
    }
    public float Thickness
    {
        get => thickness;
        set => thickness = Mathf.Max(0.001f, value);
    }

    /// <summary>바닥 아래로 내려간 길이. 떨어져서 벽 밑으로 빠져나가는 걸 막는다.</summary>
    public float Skirt
    {
        get => skirt;
        set => skirt = Mathf.Max(0f, value);
    }

    /// <summary>벽이 실제로 막는 높이 범위(월드).</summary>
    public float Bottom => transform.position.y - skirt;
    public float Top => transform.position.y + height;

    /// <summary>바닥 모양을 그대로 씌운다(방향별 반지름). 기준 반지름은 건드리지 않는다.</summary>
    public void SetProfile(float[] radii)
    {
        if (radii == null || radii.Length < 8) { profile = null; return; }

        profile = new float[radii.Length];
        for (int i = 0; i < radii.Length; i++) profile[i] = Mathf.Max(0.01f, radii[i]);
        segments = Mathf.Clamp(radii.Length, 8, 128);
    }

    /// <summary>현재 값으로 벽 조각들을 다시 만든다(기존 조각은 지운다).</summary>
    [ContextMenu("벽 다시 만들기")]
    public void Rebuild()
    {
        // 기존 조각 정리
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }

        int n = SegmentCount;
        for (int i = 0; i < n; i++)
        {
            Vector3 a = OutlinePoint(i);
            Vector3 b = OutlinePoint(i + 1);
            Vector3 edge = b - a;
            float len = edge.magnitude;
            if (len < 1e-5f) continue;

            var seg = new GameObject($"Seg_{i:00}");
            seg.layer = gameObject.layer;
            seg.transform.SetParent(transform, false);

            // 로컬 +Z가 바깥쪽을 보게 세운다(시계 방향 외곽선의 바깥 법선).
            Vector3 outward = Vector3.Cross(edge, Vector3.up).normalized;
            seg.transform.localPosition = (a + b) * 0.5f;
            seg.transform.localRotation = Quaternion.LookRotation(outward, Vector3.up);

            // 조각이 서로 살짝 겹치게 폭을 잡아야 이음매 사이로 끼이지 않는다.
            // 두께의 절반만큼 바깥으로 밀어 안쪽 면이 정확히 외곽선에 오게 한다.
            // 바닥 아래로 skirt만큼 내려 두면, 아래층으로 떨어져도 벽 밑으로 못 빠져나간다.
            float tall = height + skirt;
            var box = seg.AddComponent<BoxCollider>();
            box.size = new Vector3(len * 1.2f, tall, thickness);
            box.center = new Vector3(0f, tall * 0.5f - skirt, thickness * 0.5f);
        }
    }

    private int SegmentCount => HasProfile ? profile.Length : Mathf.Clamp(segments, 8, 128);

    /// <summary>외곽선의 i번째 점(로컬 좌표). 0번이 +Z, 인덱스가 늘면 시계 방향.</summary>
    private Vector3 OutlinePoint(int i)
    {
        int n = SegmentCount;
        int k = ((i % n) + n) % n;
        float a = 360f / n * k * Mathf.Deg2Rad;
        float r = HasProfile ? profile[k] : radius;
        return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * r;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 1f, 0.6f, 0.9f);
        int n = SegmentCount;
        for (int i = 0; i < n; i++)
        {
            Vector3 a = transform.position + OutlinePoint(i);
            Vector3 b = transform.position + OutlinePoint(i + 1);
            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(a - Vector3.up * skirt, a + Vector3.up * height);
        }
    }
}
