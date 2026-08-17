using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// TPS 플레이어 이동 컨트롤러(CharacterController 기반).
/// - 카메라 기준 방향으로 WASD 이동
/// - 평상시: 이동 방향으로 몸을 회전
/// - 조준 중: 카메라(=조준) 방향으로 몸을 고정 회전
/// - 중력/점프 처리
/// Animator가 지정되어 있으면 Speed / IsAiming 파라미터를 갱신한다(없어도 동작).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("참조")]
    [Tooltip("TPS 카메라. 이동 방향/조준 상태의 기준이 된다.")]
    [SerializeField] private ThirdPersonCamera tpsCamera;
    [Tooltip("선택: 캐릭터 Animator. 없으면 애니메이션 없이 이동만 동작.")]
    [SerializeField] private Animator animator;

    [Header("이동")]
    [SerializeField] private float walkSpeed = 2.5f;
    [SerializeField] private float runSpeed = 5.5f;
    [SerializeField] private float aimSpeed = 2f;
    [SerializeField] private float rotationSpeed = 15f;
    [Tooltip("구르기 중 전방 이동 속도 = walkSpeed × 이 배율.\n달리기 속도로 미끄러져 과이동하는 것을 막고, 구르기 관성만 남긴다.")]
    [SerializeField] private float rollSpeedMultiplier = 1.2f;
    [Tooltip("구르기 전체 속도 배율. 애니메이션 재생 속도와 전방 이동 속도에 함께 곱해져\n" +
             "같은 궤적을 더 빠르게 지나간다(1 = 원래 속도). 회피를 민첩하게 만들 때 올린다.")]
    [SerializeField] private float rollSpeedUp = 1.6f;
    [Tooltip("구르기 클립의 이 지점(0~1)을 지나면 조작을 돌려준다.\n" +
             "일어서는 뒷부분은 애니메이션만 마저 블렌드되고 이동/회전/조준은 즉시 반응하므로,\n" +
             "'구르고 원래 자세로 돌아오는' 답답함이 사라진다. 1이면 예전처럼 끝까지 잠근다.")]
    [SerializeField, Range(0.4f, 1f)] private float rollControlReturn = 0.7f;

    [Header("중력/점프")]
    [SerializeField] private float gravity = -20f;
    [SerializeField] private float jumpHeight = 1.2f;

    [Header("접지 보정")]
    [Tooltip("애니메이션/리타게팅/레이어 블렌드 잔차로 발이 지면에서 뜨는 것을\n최종 포즈 기준으로 매 프레임 보정한다(지속 잔차만 제거, 순간 도약은 보존).")]
    [SerializeField] private bool groundFeet = true;
    [Tooltip("접지 보정 추적 속도. 높을수록 즉각 반응, 낮을수록 부드러움.")]
    [SerializeField] private float groundFeetSpeed = 8f;

    [Header("조준")]
    [Tooltip("크로스헤어 조준점 계산용 레이 마스크(자기 몸은 코드에서 제외)")]
    [SerializeField] private LayerMask aimMask = ~0;
    [Tooltip("머리/상체가 크로스헤어 지점을 바라보게 하는 LookAt IK 사용 여부")]
    [SerializeField] private bool lookAtAim = true;
    [Range(0f, 1f)]
    [Tooltip("상체(척추)를 조준점 쪽으로 트는 정도. 0=머리만, 1=상체 전체")]
    [SerializeField] private float lookAtBodyWeight = 0.35f;
    [Tooltip("총 메시의 실제 총열 방향을 측정해, 총구가 크로스헤어(화면 정면)를 향하도록\n수평은 몸 전체 회전, 수직은 가슴 기울임으로 보정한다(실제 TPS 표준 방식).")]
    [SerializeField] private bool gunForwardAlign = true;
    [Tooltip("가슴 상하 기울임 최대 각도(수직 보정 한계)")]
    [SerializeField] private float maxSpinePitch = 30f;
    [Tooltip("조준 보정 켜고 끌 때 블렌드 속도")]
    [SerializeField] private float aimBlendSpeed = 8f;
    [Tooltip("수평 미세 보정(도). 총구가 크로스헤어보다 오른쪽이면 -, 왼쪽이면 +")]
    [SerializeField] private float aimYawTrim = 0f;
    [Tooltip("수직 미세 보정(도). 총구가 크로스헤어보다 위면 +, 아래면 -")]
    [SerializeField] private float aimPitchTrim = 0f;

    private CharacterController _cc;
    private Transform _camTransform;
    private Camera _aimCam;
    private float _verticalVelocity;
    private bool _moving;              // 이번 프레임 이동 여부(조준 보정 판단용)
    private bool _rolling;             // 다이브 롤 재생 중(무적 프레임 / 애니메이션 가속)
    private bool _rollLocked;          // 롤 조작 잠금 구간(회복 구간에 들어서면 풀린다)
    private bool _rollStateActive;     // 직전 프레임 롤 상태(진입 순간에만 기력을 빼기 위한 판정)
    private bool _isRunning;           // 질주 중(외부 참조용)
    private float _aimBlend;           // 조준 보정 블렌드(0~1)
    private float _poseGunYawOffset;   // 포즈상 총열이 몸 정면에서 틀어진 요 각(자동 측정)
    private WeaponHolder _weaponHolder;
    private PlayerShooter _shooter;    // 재장전 상태 참조(UpperBody 레이어 가중치)
    private PlayerStats _stats;        // 기력(구르기) 참조
    private int _upperLayerIdx = -2;   // UpperBody 레이어 인덱스(-2=미조회, -1=없음)
    private Transform _gun;            // 현재 무기(총열 측정 대상)
    private Vector3 _barrelAxisAbs;    // 총 로컬 총열 축(부호 없는 기준, 최장 메시 축)
    private bool _barrelResolved;
    private float _feetOffset;         // 스무딩된 발-지면 갭(접지 보정량)
    private bool _feetOffsetInit;      // 첫 측정 여부(스폰 직후엔 스냅)
    private bool _wasRolling;          // 직전 프레임 구르기 여부(복귀 시 스냅)
    private float _groundContactY;     // CC가 실제로 밟은 접촉점 Y
    private float _groundContactTime;  // 접촉 갱신 시각
    private float _slowFactor = 1f;    // 외부에서 건 이동 둔화 배율
    private float _slowUntil;          // 둔화 만료 시각

    private const float AimRange = 500f;

    // Animator 파라미터 해시(있을 때만 사용)
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int IsRunningHash = Animator.StringToHash("IsRunning");
    private static readonly int IsAimingHash = Animator.StringToHash("IsAiming");
    private static readonly int RollHash = Animator.StringToHash("Roll");

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
        if (tpsCamera == null) tpsCamera = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
        if (tpsCamera != null) _camTransform = tpsCamera.transform;
        _aimCam = _camTransform != null ? _camTransform.GetComponent<Camera>() : Camera.main;
        if (animator == null) animator = GetComponentInChildren<Animator>();
        _weaponHolder = GetComponent<WeaponHolder>();
        _shooter = GetComponent<PlayerShooter>();
        // 스탯이 씬에 없으면 기본값으로 자동 추가(HUD/기력/타임포스가 항상 동작하도록)
        _stats = GetComponent<PlayerStats>();
        if (_stats == null) _stats = gameObject.AddComponent<PlayerStats>();
    }

    /// <summary>다이브 롤 재생 중 여부(회피 판정용 — 롤 중 피격은 회피로 처리).</summary>
    public bool IsRolling => _rolling;

    /// <summary>질주 중 여부(카메라 속도감 연출에 사용).</summary>
    public bool IsRunning => _isRunning;

    /// <summary>외부(시간 되감기 등)에서 텔레포트시킨 뒤 호출: 수직 속도 초기화.</summary>
    public void OnTeleported() => _verticalVelocity = 0f;

    /// <summary>
    /// 외부(보스 텔레포트 충격파 등)에서 이동 속도를 일정 시간 떨어뜨린다.
    /// factor 0.7 = 30% 감소. 이미 더 강한 둔화가 걸려 있으면 그것을 유지하고 지속시간만 늘린다.
    /// 구르기(회피)에는 적용하지 않는다 — 둔화 중에도 회피 수단은 남겨 둔다.
    /// </summary>
    public void ApplyMoveSlow(float factor, float duration)
    {
        factor = Mathf.Clamp(factor, 0.05f, 1f);
        if (Time.time >= _slowUntil || factor < _slowFactor) _slowFactor = factor;
        _slowUntil = Mathf.Max(_slowUntil, Time.time + duration);
    }

    /// <summary>이동 둔화가 걸려 있는가(HUD/연출용).</summary>
    public bool IsSlowed => Time.time < _slowUntil;

    /// <summary>현재 이동 속도 배율(둔화가 없으면 1).</summary>
    private float MoveSlowFactor => Time.time < _slowUntil ? _slowFactor : 1f;

    private void Update()
    {
        if (Keyboard.current == null) return;

        // 컷신 중(카메라가 보스를 비추는 동안)과 사망 후에는 조작을 잠근다.
        // 중력은 계속 적용해 공중에 뜬 채로 멈추지 않게 한다.
        if (BossController.CutsceneActive || (_stats != null && _stats.IsDead))
        {
            _moving = false;
            _isRunning = false;
            if (_cc.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
            _verticalVelocity += gravity * Time.deltaTime;
            _cc.Move(Vector3.up * _verticalVelocity * Time.deltaTime);

            if (animator != null && animator.runtimeAnimatorController != null)
            {
                animator.SetFloat(SpeedHash, 0f, 0.1f, Time.deltaTime);

                // 사망 중엔 상체 레이어(소총 파지)를 내려야 쓰러지는 모션이 상체까지 보인다 —
                // 남겨두면 몸은 넘어가는데 상체만 총을 든 채 서 있는 자세가 된다.
                if (_upperLayerIdx == -2) _upperLayerIdx = animator.GetLayerIndex("UpperBody");
                if (_upperLayerIdx >= 0)
                {
                    float w = Mathf.MoveTowards(animator.GetLayerWeight(_upperLayerIdx), 0f, 6f * Time.deltaTime);
                    animator.SetLayerWeight(_upperLayerIdx, w);
                }
            }
            return;
        }

        bool isAiming = tpsCamera != null && tpsCamera.IsAiming;

        // --- 입력 읽기 (New Input System) ---
        Vector2 input = ReadMoveInput();

        // 카메라 기준 이동 방향(수평 평면)
        Vector3 camForward = _camTransform != null ? _camTransform.forward : Vector3.forward;
        Vector3 camRight = _camTransform != null ? _camTransform.right : Vector3.right;
        camForward.y = 0f; camRight.y = 0f;
        camForward.Normalize(); camRight.Normalize();

        Vector3 moveDir = (camForward * input.y + camRight * input.x);
        float inputMag = Mathf.Clamp01(moveDir.magnitude);
        moveDir.Normalize();

        bool moving = inputMag > 0.01f;
        _moving = moving;

        // 다이브 롤 재생 중(전환 진입 포함) 여부.
        // 회전/이동을 잠그는 구간(_rollLocked)은 롤의 앞부분뿐이고,
        // 일어서는 뒷부분(rollControlReturn 이후)에서는 애니메이션만 마저 블렌드되고
        // 조작은 바로 돌아온다 → 구르고 나서 굳어 있는 답답함이 사라진다.
        _rolling = false;
        _rollLocked = false;
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            var cur = animator.GetCurrentAnimatorStateInfo(0);
            bool curRoll = cur.IsName("Running Dive Roll");
            bool nextRoll = animator.GetNextAnimatorStateInfo(0).IsName("Running Dive Roll");

            _rolling = curRoll || nextRoll;
            bool recovering = curRoll && !nextRoll && cur.normalizedTime >= rollControlReturn;
            _rollLocked = _rolling && !recovering;
        }

        // 기력은 '구르기가 실제로 시작된 순간'에만 뺀다.
        // 누른 시점에 빼면, 구를 수 없는 상태에서 연타했을 때 구르지도 않고 기력만 사라진다.
        if (_rolling && !_rollStateActive && _stats != null) _stats.TryUseRollStamina();
        _rollStateActive = _rolling;

        // 달리기는 이동하면 항상 켜진다(Shift는 이제 구르기 전용).
        // 조준 중에는 달리지 않는다 — 조준 자세로 걸어야 총구가 크로스헤어를 따라간다.
        bool isRunning = moving && !isAiming;
        _isRunning = isRunning;

        float speed = (isAiming ? aimSpeed : (isRunning ? runSpeed : walkSpeed)) * MoveSlowFactor;
        Vector3 horizontal = moveDir * speed * inputMag;

        // 구르기 중엔 입력/달리기 속도를 무시하고 몸 전방으로 일정 속도만 이동.
        // (달리기 속도 그대로 미끄러져 롤이 끝나기 전에 과이동하는 문제 방지 —
        //  롤이 끝나면 키를 계속 누르고 있을 경우 자연히 달리기로 복귀한다)
        if (_rollLocked)
            horizontal = transform.forward * (walkSpeed * rollSpeedMultiplier * rollSpeedUp);

        // --- 회전 ---
        if (_rollLocked)
        {
            // 구르기 중엔 회전 잠금: 구르기 시작 방향 그대로 유지
        }
        else if (isAiming || !moving)
        {
            // 조준/정지 시: 몸을 카메라 요에 맞추되, 포즈상 총이 틀어진 각도만큼 되돌려
            // 총이 화면 정면(크로스헤어)을 향하게 한다. (몸 전체 회전 → 포즈 자연 유지)
            if (tpsCamera != null)
            {
                float targetYaw = tpsCamera.Yaw;
                if (gunForwardAlign) targetYaw -= _poseGunYawOffset * _aimBlend;
                Quaternion targetRot = Quaternion.Euler(0f, targetYaw, 0f);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
            }
        }
        else
        {
            // 비조준 이동 중엔 이동 방향으로 회전
            Quaternion targetRot = Quaternion.LookRotation(moveDir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }

        // --- 중력/점프 ---
        if (_cc.isGrounded)
        {
            if (_verticalVelocity < 0f) _verticalVelocity = -2f;
            if (Keyboard.current.spaceKey.wasPressedThisFrame)
                _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        _verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = horizontal + Vector3.up * _verticalVelocity;
        _cc.Move(velocity * Time.deltaTime);

        // --- Animator 갱신(컨트롤러가 실제로 있을 때만) ---
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            // 구르는 동안만 애니메이션을 빠르게 재생 → 이동 속도(rollSpeedUp)와 궤적이 어긋나지 않는다
            animator.speed = _rolling ? rollSpeedUp : 1f;

            // Speed는 이동 여부(0=정지)로 Idle↔Walk 전환에 쓰이고,
            // IsRunning이 켜지면 Walk→(Idle To Running)→Rifle Run으로 이어진다.
            float animSpeed = speed * inputMag;
            animator.SetFloat(SpeedHash, animSpeed, 0.1f, Time.deltaTime);
            animator.SetBool(IsRunningHash, isRunning);
            animator.SetBool(IsAimingHash, isAiming);

            // 다이브 롤: 이동 중 Shift로 발동(기력 소모, 부족하면 불가).
            // 달리기가 상시가 되면서 Shift가 비었으므로 회피를 그쪽으로 옮겼다.
            // 컨트롤러의 Roll 전환은 AnyState에 걸려 있어, 누른 프레임에 곧바로 구르기가 시작된다.
            //
            // 구르는 동안의 입력은 전부 무시한다 — 트리거는 소비될 때까지 남아 있고
            // 롤 상태에서는 자기 자신으로 전환할 수 없어, 연타하면 롤이 끝나는 족족
            // 다시 발동(연속 구르기)했다. 기력도 여기서 빼지 않는다 — 실제로 롤 상태에
            // 들어간 프레임에서 뺀다(위) → 구른 횟수와 소모량이 정확히 1:1.
            if (_rolling)
            {
                animator.ResetTrigger(RollHash); // 구르는 중에 눌린 입력은 큐에 남기지 않는다
            }
            else if (isRunning && Keyboard.current.leftShiftKey.wasPressedThisFrame
                     && (_stats == null || _stats.CanRoll))
            {
                animator.SetTrigger(RollHash);
            }

            // 걷기 중 상체를 소총 파지 자세로 유지(UpperBody 레이어) → 총이 허공에 뜨는 문제 방지.
            // 달리기(Rifle Run)는 자체가 소총 애니메이션이라 제외, 롤 중에도 제외.
            // 재장전 중엔 이동 상태와 무관하게 상체 레이어를 켜서 재장전 모션을 재생한다.
            if (_upperLayerIdx == -2) _upperLayerIdx = animator.GetLayerIndex("UpperBody");
            if (_upperLayerIdx >= 0)
            {
                // 발사 직후엔 이동 여부와 무관하게 상체 레이어를 켜야 발사 모션이 보인다
                bool reloading = _shooter != null && _shooter.IsReloading;
                bool firingRecently = _shooter != null && _shooter.FiredRecently;
                float target = (!_rollLocked && (reloading || firingRecently || (!isRunning && moving))) ? 1f : 0f;
                float w = Mathf.MoveTowards(animator.GetLayerWeight(_upperLayerIdx), target, 8f * Time.deltaTime);
                animator.SetLayerWeight(_upperLayerIdx, w);
            }
        }
    }

    /// <summary>CC 이동 중 충돌 접촉점 기록. 위를 향한 면(바닥)만 지면으로 취급.</summary>
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.normal.y > 0.5f)
        {
            _groundContactY = hit.point.y;
            _groundContactTime = Time.time;
        }
    }

    private Vector2 ReadMoveInput()
    {
        var kb = Keyboard.current;
        float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
        float y = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
        return new Vector2(x, y);
    }

    /// <summary>
    /// 휴머노이드 LookAt IK: 머리(+상체 일부)가 크로스헤어 지점을 바라보게 한다.
    /// Animator 레이어의 IK Pass가 켜져 있어야 호출된다(PlayerAnimatorSetup이 켬).
    /// 조준(우클릭) 중엔 상체 비중을 높여 총도 조준점 쪽으로 따라가게 한다.
    /// </summary>
    private void OnAnimatorIK(int layerIndex)
    {
        if (!lookAtAim || animator == null || animator.runtimeAnimatorController == null) return;
        if (_rollLocked) return; // 구르기 중 머리 IK는 목이 꺾여 보임 → 끔(회복 구간부터 다시 켠다)
        if (_stats != null && _stats.IsDead) return; // 쓰러진 채로 조준점을 쳐다보면 목이 꺾인다

        // 수직/수평 조준은 LateUpdate의 총열 보정이 담당 → LookAt은 머리 위주로만(자연스러운 시선)
        animator.SetLookAtWeight(1f, lookAtBodyWeight, 0.9f, 0f, 0.5f); // (전체, 몸, 머리, 눈, 제한)
        animator.SetLookAtPosition(GetAimPoint());
    }

    /// <summary>
    /// 조준 보정(실제 TPS 표준 방식). 총 메시의 실제 총열 방향을 측정해:
    /// 1) 수평: 총열이 몸 정면에서 틀어진 요 각을 측정 → Update의 몸 회전이 이만큼 되돌림
    ///    (본을 꺾지 않고 몸 전체를 돌리므로 포즈가 완전히 자연스럽게 유지된다)
    /// 2) 수직: 총열 피치가 카메라 피치와 일치하도록 가슴을 제한된 각도 안에서 기울임
    /// → 총구가 실제로 크로스헤어 지점을 향한다. (롤 중/비조준 이동 중엔 블렌드 아웃)
    /// </summary>
    private void LateUpdate()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;

        // 사망 중에는 어떤 보정도 걸지 않는다 — 쓰러진 몸에 접지 보정을 걸면 다리가 땅으로
        // 끌려가고, 조준 보정을 걸면 시체의 가슴이 카메라를 따라 비틀린다.
        if (_stats != null && _stats.IsDead)
        {
            _aimBlend = Mathf.MoveTowards(_aimBlend, 0f, aimBlendSpeed * Time.deltaTime);
            return;
        }

        ApplyFootGrounding();

        bool aiming = tpsCamera != null && tpsCamera.IsAiming;
        bool wantAim = gunForwardAlign && !_rollLocked && (aiming || !_moving);
        _aimBlend = Mathf.MoveTowards(_aimBlend, wantAim ? 1f : 0f, aimBlendSpeed * Time.deltaTime);

        // 구르기 중엔 몸이 뒤집혀 총열 측정값이 엉터리가 되므로 측정/보정 모두 건너뛴다
        // (회복 구간에서는 이미 몸이 서 있으므로 바로 조준 보정을 재개한다)
        if (_rollLocked) return;

        // --- 총열 방향 측정(총 메시 최장축, 부호는 매 프레임 캐릭터 전방 반구로) ---
        if (!TryGetBarrelDirection(out Vector3 barrelWorld)) return;

        // 1) 요 오프셋: 몸 로컬 기준이라 몸 회전과 무관(피드백 없음). Update가 사용.
        Vector3 local = transform.InverseTransformDirection(barrelWorld);
        Vector3 flat = new Vector3(local.x, 0f, local.z);
        if (flat.sqrMagnitude > 1e-6f)
            _poseGunYawOffset = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg + aimYawTrim;

        // 2) 피치 보정: 총열 피치를 카메라 피치에 일치시키는 만큼만 가슴을 기울임
        if (_aimBlend > 0.001f && tpsCamera != null)
        {
            Transform chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Spine);
            if (chest != null)
            {
                // 피치 부호는 카메라와 동일 규약: +가 아래
                float barrelPitch = -Mathf.Atan2(barrelWorld.y,
                    new Vector2(barrelWorld.x, barrelWorld.z).magnitude) * Mathf.Rad2Deg;
                float delta = Mathf.Clamp(tpsCamera.Pitch - barrelPitch + aimPitchTrim,
                                          -maxSpinePitch, maxSpinePitch);
                chest.rotation = Quaternion.AngleAxis(delta * _aimBlend, transform.right) * chest.rotation;
            }
        }
    }

    /// <summary>
    /// 접지 보정: 애니메이터가 최종 포즈를 쓴 뒤(LateUpdate) 발바닥 추정 높이를 재서
    /// "레이캐스트로 찾은 실제 바닥" 대비 지속적인 갭만 힙 본을 내려 제거한다.
    /// - 애니메이션은 진단상 접지가 정상이므로, 남는 갭의 원인은 CharacterController가
    ///   물리 여유(skinWidth/접촉 오프셋)만큼 지면 위에 떠서 정지하는 것 →
    ///   루트가 아니라 실제 지면을 기준으로 삼아야 완전히 사라진다.
    /// - 갭을 천천히 추적(스무딩)하므로 달리기 도약 같은 순간적인 발 들림은 남는다.
    /// - 구르기 중(몸이 수평)과 공중(점프)에서는 추적을 멈추고 마지막 보정량만 유지.
    /// </summary>
    private void ApplyFootGrounding()
    {
        if (!groundFeet) return;

        Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        Transform lf = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        Transform rf = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        if (hips == null || lf == null || rf == null) return;

        // 지면 기준: CC가 실제로 밟은 접촉점(OnControllerColliderHit). 최근 값만 신뢰.
        bool hasGround = _cc.isGrounded && Time.time - _groundContactTime < 0.2f;

        // 구르기 중엔 복귀 스냅을 예약(실제 측정이 이뤄질 때까지 유지)
        if (_rolling) _wasRolling = true;

        if (!_rolling && hasGround)
        {
            // 발바닥 추정: 발 본에서 발바닥까지의 오프셋 + 발끝 본 중 최저값
            float sole = Mathf.Min(
                lf.position.y - animator.leftFeetBottomHeight,
                rf.position.y - animator.rightFeetBottomHeight);
            Transform lt = animator.GetBoneTransform(HumanBodyBones.LeftToes);
            Transform rt = animator.GetBoneTransform(HumanBodyBones.RightToes);
            if (lt != null) sole = Mathf.Min(sole, lt.position.y);
            if (rt != null) sole = Mathf.Min(sole, rt.position.y);

            float gap = sole - _groundContactY; // +: 뜸, -: 파묻힘
            // 폭주 방지: 보정 한계는 키의 절반
            gap = Mathf.Clamp(gap, -_cc.height * 0.5f, _cc.height * 0.5f);

            // 첫 측정(스폰 직후)과 구르기 복귀 직후엔 과도기 없이 즉시 스냅,
            // 이후엔 스무딩 추적(순간적인 발 들림 보존)
            if (!_feetOffsetInit || _wasRolling)
            {
                _feetOffset = gap;
                _feetOffsetInit = true;
                _wasRolling = false;
            }
            else
            {
                _feetOffset = Mathf.Lerp(_feetOffset, gap, groundFeetSpeed * Time.deltaTime);
            }
        }

        if (Mathf.Abs(_feetOffset) > 1e-5f)
            hips.position -= Vector3.up * _feetOffset;
    }

    /// <summary>현재 총의 실제 총열 방향(월드). 총 메시의 최장 로컬 축 기준, 부호는 캐릭터 전방 반구.</summary>
    private bool TryGetBarrelDirection(out Vector3 barrelWorld)
    {
        barrelWorld = Vector3.zero;
        if (_gun == null)
        {
            var w = _weaponHolder != null ? _weaponHolder.CurrentWeapon : null;
            if (w == null) return false;
            _gun = w.transform;
        }
        if (!_barrelResolved)
        {
            _barrelAxisAbs = GetLongestLocalAxis(_gun);
            _barrelResolved = true;
        }

        barrelWorld = _gun.rotation * _barrelAxisAbs;
        // 들고 있는 총은 항상 캐릭터 앞 반구를 향한다 → 부호를 매 프레임 자기교정
        if (Vector3.Dot(barrelWorld, transform.forward) < 0f) barrelWorld = -barrelWorld;
        return true;
    }

    /// <summary>총 하위 메시들의 로컬 바운즈에서 가장 긴 축(단위벡터, 부호 미정)을 구한다.</summary>
    private static Vector3 GetLongestLocalAxis(Transform gun)
    {
        bool has = false;
        Bounds acc = new Bounds();
        foreach (var r in gun.GetComponentsInChildren<Renderer>())
        {
            Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh
                      : r.TryGetComponent(out MeshFilter mf) ? mf.sharedMesh : null;
            if (mesh == null) continue;
            Vector3 c = mesh.bounds.center, e = mesh.bounds.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3(
                    (i & 1) == 0 ? -e.x : e.x,
                    (i & 2) == 0 ? -e.y : e.y,
                    (i & 4) == 0 ? -e.z : e.z);
                Vector3 p = gun.InverseTransformPoint(r.transform.TransformPoint(corner));
                if (!has) { acc = new Bounds(p, Vector3.zero); has = true; }
                else acc.Encapsulate(p);
            }
        }
        if (!has) return Vector3.forward;

        Vector3 s = acc.size;
        if (s.x >= s.y && s.x >= s.z) return Vector3.right;
        return s.y >= s.z ? Vector3.up : Vector3.forward;
    }

    /// <summary>화면 중앙(크로스헤어) 레이가 맞는 월드 지점. PlayerShooter의 조준 레이와 동일.</summary>
    private Vector3 GetAimPoint()
    {
        if (_aimCam == null) return transform.position + transform.forward * AimRange;

        Ray ray = _aimCam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Physics.Raycast(ray, out RaycastHit hit, AimRange, aimMask, QueryTriggerInteraction.Ignore)
            && !hit.collider.transform.IsChildOf(transform)) // 자기 몸은 무시
            return hit.point;
        return ray.origin + ray.direction * AimRange;
    }
}
