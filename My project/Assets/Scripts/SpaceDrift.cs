using UnityEngine;

/// <summary>
/// 배경 오브젝트를 천천히 움직인다 — 행성 자전, 부유물이 둥둥 떠다니는 느낌.
///
/// 세 가지를 겹쳐 쓴다.
///  - 자전(spin): 자기 축을 중심으로 회전. 행성용.
///  - 공전(orbit): 지정한 월드 지점을 축으로 회전. 부유물 무리를 통째로 돌릴 때 쓴다.
///    (Asteroids는 조각 수천 개가 메시 하나로 합쳐져 있어 개별 회전이 안 된다.
///     대신 무리의 중심을 피벗으로 잡아 전체가 천천히 흐르게 한다)
///  - 부유(bob): 위아래 사인 흔들림.
///
/// 전부 아주 느리게 도는 값이 기본이다. 배경은 눈에 띄면 오히려 방해가 된다.
/// 플레이 모드에서만 움직인다 — 에디터에서 씬 파일의 위치를 건드리지 않기 위해서다.
/// 배치는 Tools/TPS/Space Look 메뉴가 자동으로 해준다.
/// </summary>
public class SpaceDrift : MonoBehaviour
{
    [Header("자전")]
    [Tooltip("자전축(로컬). 살짝 기울여야 자연스럽다.")]
    [SerializeField] private Vector3 spinAxis = new Vector3(0.15f, 1f, 0.05f);
    [Tooltip("자전 속도(초당 각도). 1이면 한 바퀴에 6분.")]
    [SerializeField] private float spinSpeed = 0.8f;

    [Header("공전")]
    [Tooltip("공전축(월드).")]
    [SerializeField] private Vector3 orbitAxis = Vector3.up;
    [Tooltip("공전 중심(월드 좌표). 보통 무리 자신의 중심.")]
    [SerializeField] private Vector3 orbitPivot = Vector3.zero;
    [Tooltip("공전 속도(초당 각도). 0이면 공전하지 않는다.")]
    [SerializeField] private float orbitSpeed = 0f;

    [Header("부유")]
    [Tooltip("위아래로 흔들리는 폭(m). 0이면 흔들리지 않는다.")]
    [SerializeField] private float bobAmplitude = 0f;
    [Tooltip("한 번 오르내리는 데 걸리는 시간(초).")]
    [SerializeField] private float bobPeriod = 18f;

    private Vector3 _bobOffset;
    private float _phase;

    public float SpinSpeed { get => spinSpeed; set => spinSpeed = value; }
    public Vector3 SpinAxis { get => spinAxis; set => spinAxis = value; }
    public float OrbitSpeed { get => orbitSpeed; set => orbitSpeed = value; }
    public Vector3 OrbitAxis { get => orbitAxis; set => orbitAxis = value; }
    public Vector3 OrbitPivot { get => orbitPivot; set => orbitPivot = value; }
    public float BobAmplitude { get => bobAmplitude; set => bobAmplitude = value; }
    public float BobPeriod { get => bobPeriod; set => bobPeriod = value; }

    private void Awake()
    {
        // 여러 개가 같은 박자로 흔들리지 않도록 시작 위상을 흩어 놓는다
        _phase = Random.value * Mathf.PI * 2f;
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        // 흔들림은 매 프레임 덧붙이는 값이라, 회전 계산 전에 걷어내고 시작한다
        transform.position -= _bobOffset;

        if (Mathf.Abs(spinSpeed) > 1e-5f && spinAxis.sqrMagnitude > 1e-6f)
            transform.Rotate(spinAxis.normalized, spinSpeed * dt, Space.Self);

        if (Mathf.Abs(orbitSpeed) > 1e-5f && orbitAxis.sqrMagnitude > 1e-6f)
            transform.RotateAround(orbitPivot, orbitAxis.normalized, orbitSpeed * dt);

        _bobOffset = Vector3.zero;
        if (bobAmplitude > 1e-5f && bobPeriod > 1e-3f)
        {
            float w = Mathf.PI * 2f / bobPeriod;
            _bobOffset = Vector3.up * (Mathf.Sin(_phase + Time.time * w) * bobAmplitude);
        }

        transform.position += _bobOffset;
    }

    private void OnDrawGizmosSelected()
    {
        if (Mathf.Abs(orbitSpeed) <= 1e-5f) return;
        Gizmos.color = new Color(0.6f, 0.4f, 1f, 0.8f);
        Gizmos.DrawLine(orbitPivot - orbitAxis.normalized * 5f, orbitPivot + orbitAxis.normalized * 5f);
        Gizmos.DrawLine(orbitPivot, transform.position);
    }
}
