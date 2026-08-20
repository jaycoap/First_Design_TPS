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

    [Header("사격 반동")]
    [Tooltip("반동이 원래 조준점으로 돌아오는 속도(클수록 빨리 복귀)")]
    [SerializeField] private float recoilRecoverySpeed = 9f;

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

    /// <summary>연출 킥의 누적 상한(도). 여러 이펙트가 겹쳐도 화면이 요동치지 않게 한다.</summary>
    private const float MaxFovKick = 10f;
    private const float MaxRollKick = 8f;
    private float _rewindWeight;    // 역행 연출 가중치 0~1

    // 컷신(대상 주위를 도는 카메라)
    private Transform _focus;
    private float _focusWeight;     // 0=플레이어 시점, 1=컷신 시점
    private float _focusSize = 1.8f; // 컷신 대상의 월드 높이 — 프레이밍의 기준자
    private float _focusAngle;      // 대상 주위를 도는 현재 각도(도)

    [Header("컷신 카메라")]
    // 거리·높이를 월드 미터로 못 박아 두면 대상 크기가 바뀔 때 프레이밍이 통째로 무너진다.
    // (이 프로젝트는 캐릭터가 0.2유닛짜리 미니어처라, 예전 값 1.2m는 키의 5.6배 거리였다.)
    // 그래서 전부 '대상 높이의 몇 배'로 다룬다.
    [Tooltip("컷신 대상과의 거리 = 대상 높이 x 이 배수")]
    [SerializeField] private float cinematicDistanceScale = 2.8f;
    [Tooltip("카메라가 대상 발밑보다 얼마나 위에 있는가 = 대상 높이 x 이 배수")]
    [SerializeField] private float cinematicHeightScale = 1.1f;
    [Tooltip("어디를 바라보는가(대상 발밑 기준) = 대상 높이 x 이 배수. 0.6이면 가슴께.")]
    [SerializeField] private float cinematicAimScale = 0.6f;
    [Tooltip("대상 높이를 못 받았을 때 가정할 크기(월드 미터)")]
    [SerializeField] private float cinematicDefaultSubjectHeight = 1.8f;
    [Tooltip("대상 주위를 도는 속도(도/초). 느리게 돌아야 '보여주는' 느낌이 난다.")]
    [SerializeField] private float cinematicOrbitSpeed = 14f;
    [Tooltip("컷신 시점으로 들고 나는 블렌드 속도(1/초)")]
    [SerializeField] private float cinematicBlendSpeed = 1.6f;
    private bool _rewindActive;
    private float _noiseSeed;
    private float _recoilPitch;     // 사격 반동(위로 들림). 0으로 복귀
    private float _recoilYaw;       // 사격 반동(좌우 흔들림). 0으로 복귀

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
    /// <summary>
    /// 순간적으로 시야각을 넓힌다(도). 상한을 두는 이유: 운석·구체처럼 여러 발이 연달아
    /// 터지면 킥이 그대로 누적돼 화면이 20° 넘게 출렁이며 번쩍이는 것처럼 보인다.
    /// </summary>
    public void AddFovKick(float degrees)
        => _fovKick = Mathf.Clamp(_fovKick + degrees, -MaxFovKick, MaxFovKick);

    /// <summary>화면을 순간적으로 기울인다(도). FOV 킥과 같은 이유로 상한을 둔다.</summary>
    public void AddRoll(float degrees)
        => _rollKick = Mathf.Clamp(_rollKick + degrees, -MaxRollKick, MaxRollKick);

    /// <summary>시간역행 연출 on/off — 물러나며 넓어지고 기울어진다.</summary>
    public void SetRewindCinematic(bool active) => _rewindActive = active;

    /// <summary>
    /// 컷신 연출: 지정한 대상을 천천히 돌며 비춘다(null이면 플레이어 시점으로 복귀).
    /// 플레이어 시점(_yaw/_pitch)은 건드리지 않고 최종 포즈만 섞으므로,
    /// 컷신이 끝나면 원래 보던 방향 그대로 돌아온다.
    /// </summary>
    /// <param name="subjectHeight">
    /// 대상의 월드 높이. 거리·높이가 전부 여기에 비례하므로, 넘겨 주면 대상이 크든 작든
    /// 화면에서 같은 크기로 잡힌다. 0이면 사람 크기로 가정한다.
    /// </param>
    public void SetCinematicFocus(Transform focus, float subjectHeight = 0f)
    {
        _focus = focus;
        if (subjectHeight > 1e-4f) _focusSize = subjectHeight;
        else if (focus == null) _focusSize = cinematicDefaultSubjectHeight;
        if (focus != null) _focusAngle = _yaw + 150f; // 뒤쪽 비스듬한 각도에서 시작
    }

    /// <summary>컷신 카메라가 완전히 잡혔는가(연출 타이밍을 맞출 때 참고).</summary>
    public bool CinematicReady => _focusWeight > 0.98f;

    /// <summary>
    /// 사격 반동: 화면이 pitchUp(도)만큼 들리고 yaw(도)만큼 옆으로 밀렸다가 원래 조준점으로 복귀한다.
    /// 조준 레이도 카메라 기준이라 반동 중엔 실제 탄착점도 함께 밀린다(진짜 반동).
    /// </summary>
    public void AddRecoil(float pitchUp, float yaw = 0f)
    {
        _recoilPitch -= pitchUp; // 이 카메라는 pitch 양수가 '내려다봄' → 들어올리려면 빼기
        _recoilYaw += yaw;
    }

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
        if (BossController.CutsceneActive) return; // 컷신 중엔 시점 조작을 막는다

        Vector2 delta = Mouse.current.delta.ReadValue() * mouseSensitivity;
        _yaw += delta.x;
        _pitch -= delta.y;
        _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

        // 우클릭 조준 상태 갱신
        // 시간 선택 모드(우클릭=시간공명 선택)와 시간역행 재생 중엔 조준으로 처리하지 않는다
        _isAiming = Mouse.current.rightButton.isPressed
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

        // 롤(기울기): 역행 연출 + 임펄스 / 피치·요에는 사격 반동을 더한다
        float roll = rewindRoll * _rewindWeight + _rollKick;
        Quaternion rotation = Quaternion.Euler(_pitch + _recoilPitch, _yaw + _recoilYaw, roll);
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

        // 컷신: 계산이 끝난 플레이어 시점 포즈 위에 '대상을 도는 시점'을 섞는다.
        // _yaw/_pitch를 건드리지 않으므로 컷신이 끝나면 보던 방향 그대로 돌아온다.
        if (_focusWeight > 0.001f && _focus != null)
        {
            float subject = Mathf.Max(_focusSize, 1e-4f);
            Vector3 look = _focus.position + Vector3.up * (subject * cinematicAimScale);
            Vector3 orbit = Quaternion.Euler(0f, _focusAngle, 0f)
                          * Vector3.back * (subject * cinematicDistanceScale);
            Vector3 cinePos = _focus.position + orbit
                            + Vector3.up * (subject * cinematicHeightScale);

            // 컷신 시점도 벽·천장에 막히면 대상 쪽으로 당긴다.
            // 이 블록은 위쪽 벽 보정이 끝난 '뒤'에 위치를 덮어쓰므로, 여기서 따로 검사하지
            // 않으면 좁은 실내에서 카메라가 그대로 벽 밖으로 빠져나가 맵 바깥을 비춘다.
            Vector3 fromLook = cinePos - look;
            float reach = fromLook.magnitude;
            if (reach > 1e-4f)
            {
                Vector3 dir = fromLook / reach;
                if (Physics.SphereCast(look, collisionRadius, dir, out RaycastHit cineHit,
                                       reach, collisionMask, QueryTriggerInteraction.Ignore))
                    cinePos = look + dir * cineHit.distance;
            }

            Quaternion cineRot = Quaternion.LookRotation((look - cinePos).sqrMagnitude > 1e-6f
                ? (look - cinePos).normalized : rotation * Vector3.forward);

            desiredPos = Vector3.Lerp(desiredPos, cinePos, _focusWeight);
            rotation = Quaternion.Slerp(rotation, cineRot, _focusWeight);
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

        // 사격 반동도 원래 조준점으로 복귀
        float r = 1f - Mathf.Exp(-recoilRecoverySpeed * dt);
        _recoilPitch = Mathf.Lerp(_recoilPitch, 0f, r);
        _recoilYaw = Mathf.Lerp(_recoilYaw, 0f, r);

        _rewindWeight = Mathf.MoveTowards(_rewindWeight, _rewindActive ? 1f : 0f, rewindBlendSpeed * dt);

        _focusWeight = Mathf.MoveTowards(_focusWeight, _focus != null ? 1f : 0f, cinematicBlendSpeed * dt);
        if (_focusWeight > 0.001f) _focusAngle += cinematicOrbitSpeed * dt;
        else if (_focus == null) _focusWeight = 0f;
    }
}
