using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// TPS 오버숄더 카메라. 마우스로 궤도 회전하고, 조준(우클릭) 시 어깨 너머로 붙으며 FOV가 좁아진다.
/// 카메라 오브젝트(Main Camera)에 붙이고 target에 플레이어를 지정한다.
/// 벽 뚫림 방지를 위한 충돌(clipping) 처리 포함.
/// </summary>
public class ThirdPersonCamera : MonoBehaviour
{
    [Header("추적 대상")]
    [Tooltip("따라다닐 플레이어 Transform")]
    [SerializeField] private Transform target;

    [Tooltip("타겟 기준 카메라가 바라보는 지점의 높이(어깨/머리 근처)")]
    [SerializeField] private float pivotHeight = 1.5f;

    [Header("궤도(Orbit) 설정")]
    [SerializeField] private float mouseSensitivity = 0.15f;
    [SerializeField] private float minPitch = -35f;
    [SerializeField] private float maxPitch = 70f;
    [Tooltip("시작 시 카메라 상하 각도(양수 = 살짝 내려다봄). 탑뷰 방지용 초기값.")]
    [SerializeField] private float initialPitch = 12f;

    [Header("일반 상태")]
    [Tooltip("타겟에서 카메라까지 거리")]
    [SerializeField] private float normalDistance = 4f;
    [Tooltip("어깨 오프셋(오른쪽/위). 화면에서 캐릭터가 왼쪽에 오도록.")]
    [SerializeField] private Vector2 normalShoulder = new Vector2(0.6f, 0f);
    [SerializeField] private float normalFov = 60f;

    [Header("조준(Aim) 상태")]
    [SerializeField] private float aimDistance = 2f;
    [SerializeField] private Vector2 aimShoulder = new Vector2(0.8f, 0.1f);
    [SerializeField] private float aimFov = 40f;

    [Header("전환 부드러움")]
    [SerializeField] private float transitionSpeed = 12f;

    [Header("충돌(벽 뚫림 방지)")]
    [SerializeField] private LayerMask collisionMask = ~0;
    [SerializeField] private float collisionRadius = 0.2f;

    [Header("역동 연출 - 시간역행")]
    [Tooltip("역행 중 넓어지는 FOV(도)")]
    [SerializeField] private float rewindFovBoost = 20f;
    [Tooltip("역행 중 카메라가 물러나는 배율")]
    [SerializeField] private float rewindDistanceMul = 1.6f;
    [Tooltip("역행 중 기울어지는 각도(도). 화면이 비스듬해지며 이질감을 준다.")]
    [SerializeField] private float rewindRoll = 7f;
    [Tooltip("역행 연출이 들어오고 빠지는 속도")]
    [SerializeField] private float rewindBlendSpeed = 4f;

    [Header("역동 연출 - 흔들림")]
    [Tooltip("흔들림 진폭 기준(카메라 거리 대비 비율)")]
    [SerializeField] private float shakeScale = 0.35f;
    [Tooltip("흔들림 주파수(클수록 빠르게 떨림)")]
    [SerializeField] private float shakeFrequency = 22f;

    private float _yaw;
    private float _pitch;
    private float _currentDistance;
    private Vector2 _currentShoulder;
    private Camera _cam;
    private bool _isAiming;

    // 역동 연출 상태(모두 unscaledDeltaTime으로 감쇠 — 슬로우모션에도 반응이 무뎌지지 않게)
    private float _shakeAmp;        // 현재 흔들림 진폭(0~1 정규화)
    private float _shakeDecay;      // 초당 감쇠량
    private float _fovKick;         // 추가 FOV(감쇠)
    private float _rollKick;        // 추가 롤(감쇠)
    private float _rewindWeight;    // 역행 연출 가중치 0~1
    private bool _rewindActive;
    private float _noiseSeed;

    /// <summary>다른 스크립트(발사/이동)에서 현재 카메라 조준 여부와 방향을 참조.</summary>
    public bool IsAiming => _isAiming;
    public float Yaw => _yaw;
    public float Pitch => _pitch;

    // ---------- 외부에서 쏘는 카메라 임펄스 ----------

    /// <summary>화면 흔들림 추가(strength 0~1 권장). 이미 흔들리는 중이면 더 강한 쪽을 유지.</summary>
    public void AddShake(float strength, float duration = 0.35f)
    {
        _shakeAmp = Mathf.Max(_shakeAmp, Mathf.Clamp01(strength));
        _shakeDecay = 1f / Mathf.Max(0.05f, duration);
    }

    /// <summary>FOV를 순간적으로 밀었다가 되돌린다(양수=넓어짐, 음수=좁아짐).</summary>
    public void AddFovKick(float degrees) => _fovKick += degrees;

    /// <summary>화면을 순간적으로 기울인다(도).</summary>
    public void AddRoll(float degrees) => _rollKick += degrees;

    /// <summary>시간역행 연출 on/off — 물러나며 넓어지고 기울어진다.</summary>
    public void SetRewindCinematic(bool active) => _rewindActive = active;

    private void Start()
    {
        _cam = GetComponent<Camera>();
        _currentDistance = normalDistance;
        _currentShoulder = normalShoulder;

        // 카메라의 현재 회전(탑뷰일 수 있음) 대신, 타겟 뒤·살짝 내려다보는 각도로 초기화
        _yaw = target != null ? target.eulerAngles.y : transform.eulerAngles.y;
        _pitch = initialPitch;

        _noiseSeed = Random.value * 100f;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        if (Mouse.current == null) return;

        Vector2 delta = Mouse.current.delta.ReadValue() * mouseSensitivity;
        _yaw += delta.x;
        _pitch -= delta.y;
        _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

        // 우클릭 조준 상태 갱신
        // 시간 선택 모드(우클릭=시간공명 선택)와 시간역행 재생 중엔 조준으로 처리하지 않는다
        _isAiming = Mouse.current.rightButton.isPressed
                    && !TimeShiftController.DecisionActive
                    && !TimeShiftController.RewindActive;
    }

    private void LateUpdate()
    {
        if (target == null) return;

        UpdateDynamics();

        // 상태별 목표값으로 부드럽게 보간
        float t = 1f - Mathf.Exp(-transitionSpeed * Time.deltaTime);
        float targetDist = _isAiming ? aimDistance : normalDistance;
        Vector2 targetShoulder = _isAiming ? aimShoulder : normalShoulder;
        float targetFov = _isAiming ? aimFov : normalFov;

        // 시간역행 연출: 물러나며 시야가 열린다(평상시엔 _rewindWeight=0이라 아무 영향 없음)
        targetDist *= Mathf.Lerp(1f, rewindDistanceMul, _rewindWeight);
        targetFov += rewindFovBoost * _rewindWeight;

        _currentDistance = Mathf.Lerp(_currentDistance, targetDist, t);
        _currentShoulder = Vector2.Lerp(_currentShoulder, targetShoulder, t);
        if (_cam != null)
            _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, targetFov + _fovKick, t);

        // 롤(기울기): 역행 연출 + 임펄스
        float roll = rewindRoll * _rewindWeight + _rollKick;
        Quaternion rotation = Quaternion.Euler(_pitch, _yaw, roll);
        Vector3 pivot = target.position + Vector3.up * pivotHeight;

        // 어깨 오프셋을 카메라 회전 기준으로 적용
        Vector3 shoulderOffset = rotation * new Vector3(_currentShoulder.x, _currentShoulder.y, 0f);
        Vector3 pivotWithShoulder = pivot + shoulderOffset;

        Vector3 desiredPos = pivotWithShoulder - rotation * Vector3.forward * _currentDistance;

        // 벽 충돌 시 카메라를 앞으로 당김
        if (Physics.SphereCast(pivotWithShoulder, collisionRadius,
                (desiredPos - pivotWithShoulder).normalized,
                out RaycastHit hit, _currentDistance, collisionMask, QueryTriggerInteraction.Ignore))
        {
            desiredPos = pivotWithShoulder + (desiredPos - pivotWithShoulder).normalized * (hit.distance);
        }

        // 흔들림은 화면 기준(카메라 로컬)으로 더한다 — 벽 보정 이후라 카메라가 벽을 뚫지 않는다
        if (_shakeAmp > 0.001f)
        {
            float amp = _shakeAmp * shakeScale;
            float time = Time.unscaledTime * shakeFrequency;
            float nx = (Mathf.PerlinNoise(_noiseSeed, time) - 0.5f) * 2f;
            float ny = (Mathf.PerlinNoise(_noiseSeed + 17.3f, time) - 0.5f) * 2f;
            desiredPos += rotation * new Vector3(nx, ny, 0f) * (amp * normalDistance);
        }

        transform.position = desiredPos;
        transform.rotation = rotation;
    }

    /// <summary>임펄스(흔들림/FOV/롤) 감쇠와 역행 연출 가중치 갱신. 슬로우모션에도 무뎌지지 않게 unscaled 사용.</summary>
    private void UpdateDynamics()
    {
        float dt = Time.unscaledDeltaTime;

        if (_shakeAmp > 0f)
            _shakeAmp = Mathf.Max(0f, _shakeAmp - _shakeDecay * dt);

        // 킥은 지수 감쇠로 부드럽게 0으로 복귀
        float k = 1f - Mathf.Exp(-8f * dt);
        _fovKick = Mathf.Lerp(_fovKick, 0f, k);
        _rollKick = Mathf.Lerp(_rollKick, 0f, k);

        _rewindWeight = Mathf.MoveTowards(_rewindWeight, _rewindActive ? 1f : 0f, rewindBlendSpeed * dt);
    }
}
