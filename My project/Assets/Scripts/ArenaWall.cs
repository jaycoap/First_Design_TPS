using UnityEngine;

/// <summary>
/// 원형 아레나 낙하 방지용 "보이지 않는 벽".
/// 반지름을 따라 박스 콜라이더를 빙 둘러 배치한다(렌더러 없음 = 눈에 보이지 않음).
/// CharacterController가 콜라이더에 막히므로 플레이어와 보스 모두 밖으로 나가지 못한다.
///
/// - 값을 바꾼 뒤 인스펙터 우클릭 → "벽 다시 만들기"로 즉시 재생성된다.
/// - 배치/측정은 Tools/TPS/Build Arena Wall 메뉴가 자동으로 해준다.
/// - 카메라 충돌·사격·레이저 판정에서는 제외되도록 전용 레이어(ArenaWall)를 쓴다.
/// </summary>
public class ArenaWall : MonoBehaviour
{
    [Tooltip("아레나 반지름(m). 이 원 안쪽으로만 이동할 수 있다.")]
    [SerializeField] private float radius = 10f;
    [Tooltip("벽 높이(m). 점프로 넘지 못할 만큼 넉넉히.")]
    [SerializeField] private float height = 5f;
    [Tooltip("원을 몇 조각의 판으로 근사할지. 많을수록 매끄럽다.")]
    [SerializeField, Range(8, 128)] private int segments = 36;
    [Tooltip("벽 두께(m). 빠른 이동에 뚫리지 않을 만큼.")]
    [SerializeField] private float thickness = 0.5f;

    public float Radius
    {
        get => radius;
        set => radius = Mathf.Max(0.01f, value);
    }
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

        int n = Mathf.Clamp(segments, 8, 128);
        // 조각이 서로 살짝 겹치게 폭을 잡아야 이음매 사이로 끼이지 않는다
        float segWidth = 2f * Mathf.PI * radius / n * 1.2f;

        for (int i = 0; i < n; i++)
        {
            var seg = new GameObject($"Seg_{i:00}");
            seg.layer = gameObject.layer;
            seg.transform.SetParent(transform, false);

            Quaternion rot = Quaternion.Euler(0f, 360f / n * i, 0f);
            seg.transform.localRotation = rot;
            seg.transform.localPosition = rot * Vector3.forward * radius;

            // 로컬 +Z가 바깥쪽 → 안쪽 면이 정확히 radius에 오도록 두께의 절반만큼 바깥으로 민다
            var box = seg.AddComponent<BoxCollider>();
            box.size = new Vector3(segWidth, height, thickness);
            box.center = new Vector3(0f, height * 0.5f, thickness * 0.5f);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 1f, 0.6f, 0.9f);
        const int steps = 64;
        Vector3 prev = transform.position + Vector3.forward * radius;
        for (int i = 1; i <= steps; i++)
        {
            float a = 360f / steps * i * Mathf.Deg2Rad;
            Vector3 p = transform.position + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * radius;
            Gizmos.DrawLine(prev, p);
            Gizmos.DrawLine(prev, prev + Vector3.up * height);
            prev = p;
        }
    }
}
