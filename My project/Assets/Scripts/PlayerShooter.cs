using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// TPS 발사 로직.
/// - 좌클릭으로 발사(연사 지원, fireRate로 속도 제어)
/// - 조준(우클릭) 여부는 ThirdPersonCamera에서 참조
/// - 화면 중앙에서 카메라 정면으로 Raycast → 명중 지점 계산
/// - 총구(muzzlePoint)에서 명중 지점을 향해 발사 이펙트/트레이서 원점 사용
/// - 명중 시 임팩트 이펙트 생성, IDamageable 대상엔 데미지 전달
/// Animator가 있으면 Fire 트리거를 발생시킨다(없어도 동작).
/// </summary>
public class PlayerShooter : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private ThirdPersonCamera tpsCamera;
    [SerializeField] private Camera aimCamera;
    [Tooltip("총구 위치. WeaponHolder가 붙인 총의 총구 빈 오브젝트를 지정.")]
    [SerializeField] private Transform muzzlePoint;
    [SerializeField] private Animator animator;

    [Header("발사 설정")]
    [SerializeField] private float fireRate = 10f;      // 초당 발사 수
    [SerializeField] private float range = 200f;
    [SerializeField] private float damage = 15f;

    [Header("탄약/재장전")]
    [SerializeField] private int magazineSize = 35;
    [Tooltip("재장전에 걸리는 시간(초). 재장전 모션의 재생 속도가 이 시간에 맞춰 자동으로 조절되므로\n" +
             "값을 바꾸면 애니메이션도 같이 빨라지거나 느려진다.\n" +
             "(애니메이터에 Reload 상태가 없는 구성에서는 이 값이 그대로 대기 시간이 된다)")]
    [SerializeField] private float reloadTime = 1.5f;
    [Tooltip("빠른 재장전(액티브 리로드) 사용 여부.\n" +
             "재장전 중 막대 위를 지나가는 화살표가 성공 구간에 들어왔을 때 R을 누르면 즉시 완료된다.")]
    [SerializeField] private bool activeReloadEnabled = true;
    [Tooltip("성공 구간의 폭(막대 길이에 대한 비율) — 보스가 만피일 때.")]
    [SerializeField, Range(0.03f, 0.4f)] private float activeReloadWindow = 0.12f;
    [Tooltip("성공 구간의 폭 — 보스가 빈사일 때. 보스 체력이 줄수록 이 값으로 좁아진다.")]
    [SerializeField, Range(0.02f, 0.4f)] private float activeReloadWindowMin = 0.09f;
    [Tooltip("재장전 한 번 동안 화살표가 막대를 가로지르는 횟수(편도 기준) — 보스가 만피일 때.\n" +
             "1이면 한 번 훑고 끝난다(막대를 건너는 데 재장전 시간 전부를 쓴다).\n" +
             "1을 넘으면 끝에 닿았을 때 되돌아온다.")]
    [SerializeField] private float activeReloadSweeps = 1f;
    [Tooltip("가로지르는 횟수 — 보스가 빈사일 때. 너무 올리면 눈이 못 따라가므로 조금씩만 올린다.\n" +
             "1.35 = 화살표가 35% 빨라진다.")]
    [SerializeField] private float activeReloadSweepsMax = 1.35f;
    [Tooltip("성공 구간이 놓이는 범위(재장전 진행도 0~1). 매번 이 안에서 무작위로 정해진다.\n" +
             "앞쪽을 비워 두는 이유: 재장전이 시작되자마자 지나가면 반응할 시간이 없다.\n" +
             "뒤쪽을 비워 두는 이유: 끝에 붙으면 성공해도 아낀 시간이 없어 의미가 없다.")]
    [SerializeField] private Vector2 activeReloadRange = new Vector2(0.28f, 0.86f);

    [Tooltip("조준(우클릭) 중일 때만 발사할지 여부")]
    [SerializeField] private bool requireAimToFire = true;
    [SerializeField] private LayerMask hitMask = ~0;

    [Header("무기 조준 정렬(총구를 크로스헤어로)")]
    [Tooltip("애니메이션 이후 총을 화면 중앙(크로스헤어) 지점으로 직접 겨눈다.\n" +
             "주의: 손에 맞춰둔 장착 회전을 덮어써 총이 손에서 이탈할 수 있어 기본 꺼짐.\n" +
             "시선/상체 정렬은 PlayerController의 LookAt IK가 담당한다.")]
    [SerializeField] private bool aimWeaponToCrosshair = false;
    [Range(0f, 1f)]
    [Tooltip("정렬 강도(1=완전히 크로스헤어로, 0=애니메이션 그대로).")]
    [SerializeField] private float aimWeight = 1f;
    [Tooltip("총열 방향(총 로컬 축) 수동 지정. (0,0,0)이면 메시로 자동 판별. 자동이 앞뒤/축이 틀리면 예: (0,0,1),(0,0,-1),(1,0,0) 등으로 지정.")]
    [SerializeField] private Vector3 barrelAxisOverride = Vector3.zero;

    [Header("반동")]
    [Tooltip("한 발당 화면이 들리는 각도(도). 조준점도 함께 밀렸다가 복귀한다.")]
    [SerializeField] private float recoilPitch = 1.1f;
    [Tooltip("한 발당 좌우로 밀리는 최대 각도(도). 매 발 랜덤 방향.")]
    [SerializeField] private float recoilYaw = 0.35f;
    [Tooltip("반동에 곁들이는 미세한 화면 흔들림(0이면 없음)")]
    [SerializeField] private float recoilShake = 0.12f;

    [Header("탄 퍼짐(Spread)")]
    [Tooltip("기본 퍼짐 반각(도). 쏘지 않을 때의 최소 퍼짐.")]
    [SerializeField] private float spreadBase = 0.12f;
    [Tooltip("한 발마다 늘어나는 퍼짐(도)")]
    [SerializeField] private float spreadPerShot = 0.28f;
    [Tooltip("퍼짐 상한(도)")]
    [SerializeField] private float spreadMax = 2.2f;
    [Tooltip("초당 회복량(도/초). 클수록 빨리 조여든다.")]
    [SerializeField] private float spreadRecovery = 5f;
    [Tooltip("마지막 발사 후 회복이 시작되기까지의 지연(초).\n0이면 연사 중 증가분과 회복이 상쇄돼 퍼짐이 늘지 않는다.")]
    [SerializeField] private float spreadRecoveryDelay = 0.12f;
    [Tooltip("조준(우클릭) 중 퍼짐 배율. 1이면 조준해도 동일.")]
    [SerializeField] private float aimSpreadMultiplier = 0.45f;

    [Header("레이저 이펙트")]
    [Tooltip("레이저 빔·총구 방출·탄착에 공통으로 쓰이는 색")]
    [SerializeField] private Color laserColor = new Color(0.35f, 0.8f, 1f);
    [Tooltip("빔이 화면에 남아있는 시간(초). 길수록 광선처럼 이어져 보인다.")]
    [SerializeField] private float beamDuration = 0.07f;
    [Tooltip("약점(머리) 명중 탄착 색. 일반 탄착과 확실히 구분되도록 다른 색을 쓴다.\n" +
             "이 색으로 더 크게 터지고 빛이 한 번 번쩍인다.")]
    [SerializeField] private Color critImpactColor = new Color(1f, 0.82f, 0.25f);

    [Header("이펙트(선택)")]
    [Tooltip("직접 만든 총구 FX. 비우면 GunFx가 레이저 방출 FX를 자동 생성.")]
    [SerializeField] private ParticleSystem muzzleFlash;
    [Tooltip("직접 만든 탄착 프리팹. 비우면 GunFx가 레이저 탄착 FX를 자동 생성.")]
    [SerializeField] private GameObject impactPrefab;
    [Tooltip("빔 코어용 LineRenderer. 비우면 자동 생성. 표시 시간은 위의 Beam Duration.")]
    [SerializeField] private LineRenderer tracer;

    [Tooltip("이펙트 크기 배율. 0이면 CharacterController 높이 기준으로 자동 계산(사람 1.8m 기준).")]
    [SerializeField] private float fxScale = 0f;

    private float _nextFireTime;
    private float _lastFireTime = -99f;                  // 발사 모션 유지 판정용
    private const float fireMotionHold = 0.35f;          // 마지막 발사 후 상체 레이어를 켜둘 시간(초)
    private float _tracerHideTime;
    private PlayerController _controller;                // 구르기 중 발사 차단 판정용
    private LineRenderer _beamGlow;                      // 빔 바깥 글로우 레이어
    private float _spread;                               // 현재 퍼짐 반각(도) — 발사로 누적, 시간으로 회복
    private float _spreadHoldUntil;                      // 이 시각까지는 회복 보류(연사 중 상쇄 방지)
    private Vector3 _localBarrelAxis = Vector3.forward; // 총 로컬 총열 축(자동 판별)
    private Vector3 _gunBoundsCenter;                    // 총 로컬 바운즈 중심(총구 끝 계산용)
    private float _gunExtentAlong;                       // 총열 축 방향 절반 길이
    private bool _barrelResolved;
    private GunFx.MuzzleFx _muzzleFx;                    // 재사용형 총구 화염(자동 생성)
    private GunFx.ImpactFx _impactFx;                    // 재사용형 탄착 FX
    private GunFx.ImpactFx _critImpactFx;                // 약점(머리) 전용 탄착 FX
    private float _fxScale;
    private int _ammo;                                   // 현재 탄약
    // 빠른 재장전(액티브 리로드) 상태. 전부 막대 위 0~1 좌표 기준이다.
    private float _arStart, _arEnd;   // 이번 재장전의 성공 구간
    private float _arSweeps = 1f;     // 이번 재장전의 왕복 횟수(시작할 때 정해 고정한다)
    private bool _arUsed;             // 이번 재장전에 이미 R을 눌렀다(기회는 한 번)
    private bool _arSuccess;          // 마지막 시도의 결과(연출용)
    private float _arFeedbackTime = -99f;

    private bool _reloading;
    private float _reloadStartTime;
    private bool _reloadStateSeen;                       // 애니메이터 Reload 상태 진입 확인 여부
    private PlayerStats _stats;                          // 명중 시 타임포스 획득용(지연 조회)
    private int _upperLayerIdx = -2;                     // UpperBody 레이어(-2=미조회, -1=없음)
    private static readonly int FireHash = Animator.StringToHash("Fire");
    private static readonly int ReloadHash = Animator.StringToHash("Reload");
    private static readonly int ReloadSpeedHash = Animator.StringToHash("ReloadSpeed");
    private bool _reloadSpeedChecked, _hasReloadSpeedParam;

    /// <summary>
    /// PlayerAnimatorSetup이 재장전 모션 속도를 구울 때 기준으로 삼는 시간(초).
    /// 런타임에는 reloadTime과의 비율만큼 ReloadSpeed 파라미터로 다시 보정하므로,
    /// Inspector에서 reloadTime을 바꿔도 애니메이션이 항상 그 시간에 맞춰 끝난다.
    /// </summary>
    public const float BakedReloadDuration = 1.5f;

    /// <summary>현재 탄약 수(HUD 표시용).</summary>
    public int CurrentAmmo => _ammo;
    /// <summary>탄창 크기(HUD 표시용).</summary>
    public int MagazineSize => magazineSize;
    /// <summary>재장전 중 여부. PlayerController가 UpperBody 레이어 가중치에 사용.</summary>
    public bool IsReloading => _reloading;

    // ---- 빠른 재장전(HUD가 막대를 그리는 데 쓰는 값들. 전부 진행도 0~1 기준) ----

    /// <summary>지금 막대를 띄워야 하는가.</summary>
    public bool ActiveReloadShown => _reloading && activeReloadEnabled;

    /// <summary>재장전 진행도(막대 바탕이 차오르는 정도).</summary>
    public float ReloadProgress01 => reloadTime <= 0.01f ? 1f
        : Mathf.Clamp01((Time.time - _reloadStartTime) / reloadTime);

    /// <summary>
    /// 화살표 위치 0~1. 진행도와 <b>다르다</b> — 왕복하기 때문이다.
    ///
    /// 보스가 약해질수록 화살표가 빨라져야 하는데, 재장전 시간은 그대로이므로
    /// 한 번 훑고 끝나면 남는 시간이 생긴다. 그래서 끝에 닿으면 되돌아온다.
    /// (오른쪽 끝에서 왼쪽으로 순간이동하는 방식은 그 순간을 눈이 놓친다)
    /// 진행도는 막대 바탕이 차오르는 것으로 따로 읽는다.
    /// </summary>
    public float ActiveReloadMarker01
    {
        get
        {
            // 왕복 한 번 = 편도 2회이므로 주기가 2다. 삼각파로 접어 0~1로 되돌린다.
            float f = Mathf.Repeat(ReloadProgress01 * Mathf.Max(0.25f, _arSweeps), 2f);
            return f <= 1f ? f : 2f - f;
        }
    }

    /// <summary>성공 구간의 시작·끝.</summary>
    public float ActiveReloadStart => _arStart;
    public float ActiveReloadEnd => _arEnd;

    /// <summary>아직 기회가 남아 있는가(한 번 누르면 성공이든 실패든 끝난다).</summary>
    public bool ActiveReloadReady => _reloading && activeReloadEnabled && !_arUsed;

    /// <summary>마지막 시도의 결과. 0=시도 없음, 1=성공, -1=실패(연출이 남아 있는 동안만).</summary>
    public int ActiveReloadFeedback =>
        Time.time - _arFeedbackTime > 0.45f ? 0 : (_arSuccess ? 1 : -1);

    /// <summary>
    /// 난이도 계수 0~1. 보스 체력이 줄수록 오른다 — 성공 구간이 좁아지고 화살표가 빨라진다.
    ///
    /// 보스가 없거나 죽었으면 0(가장 쉬움)이다. 전투가 아닐 때까지 어렵게 만들 이유가 없다.
    /// 단계(70%/50%)로 끊지 않고 연속으로 두는 이유: 한 대 때릴 때마다 아주 조금씩
    /// 조여드는 편이, 어느 순간 갑자기 어려워지는 것보다 압박이 자연스럽게 쌓인다.
    /// </summary>
    private float ActiveReloadDifficulty01
    {
        get
        {
            var boss = BossController.Active;
            if (boss == null || boss.IsDead || boss.MaxHealth <= 0.01f) return 0f;
            return Mathf.Clamp01(1f - boss.Health / boss.MaxHealth);
        }
    }

    /// <summary>최근에 발사했는가(발사 모션이 재생될 동안). UpperBody 레이어를 켜두는 데 쓴다.</summary>
    public bool FiredRecently => Time.time - _lastFireTime < fireMotionHold;

    /// <summary>
    /// 현재 탄 퍼짐 반각(도). 실제 발사 방향이 이 원뿔 안에서 흩어지며,
    /// Crosshair가 같은 값을 읽어 벌어짐을 그리므로 조준선과 탄착 범위가 정확히 일치한다.
    /// </summary>
    public float CurrentSpreadDegrees
    {
        get
        {
            float mul = (tpsCamera != null && tpsCamera.IsAiming) ? aimSpreadMultiplier : 1f;
            return _spread * mul;
        }
    }

    // ---- 퍼짐/반동 수치(협공 고스트가 배율을 곱해 그대로 쓴다 → 총이 같으면 감각도 같다) ----
    /// <summary>기본 퍼짐 반각(도).</summary>
    public float SpreadBase => spreadBase;
    /// <summary>한 발마다 늘어나는 퍼짐(도).</summary>
    public float SpreadPerShot => spreadPerShot;
    /// <summary>퍼짐 상한(도).</summary>
    public float SpreadMax => spreadMax;
    /// <summary>초당 퍼짐 회복량(도/초).</summary>
    public float SpreadRecovery => spreadRecovery;
    /// <summary>마지막 발사 후 회복이 시작되기까지의 지연(초).</summary>
    public float SpreadRecoveryDelay => spreadRecoveryDelay;
    /// <summary>한 발당 총구가 들리는 각도(도).</summary>
    public float RecoilPitch => recoilPitch;
    /// <summary>한 발당 좌우로 밀리는 최대 각도(도).</summary>
    public float RecoilYaw => recoilYaw;

    /// <summary>시간역행 적용: 탄약을 기록 시점 값으로 되돌린다(진행 중 재장전은 취소).</summary>
    public void RewindAmmo(int ammo)
    {
        _ammo = Mathf.Clamp(ammo, 0, magazineSize);
        _reloading = false;
    }

    private void Awake()
    {
        if (aimCamera == null) aimCamera = Camera.main;
        if (tpsCamera == null && aimCamera != null) tpsCamera = aimCamera.GetComponent<ThirdPersonCamera>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        _controller = GetComponent<PlayerController>();

        // 이펙트 배율: 캐릭터 키(CharacterController 높이)를 사람 1.8m에 대비해 환산
        var cc = GetComponent<CharacterController>();
        _fxScale = fxScale > 0f ? fxScale : (cc != null ? cc.height / 1.8f : 1f);

        _ammo = magazineSize;
        _spread = spreadBase;

        if (tracer == null) tracer = CreateTracer(); // 지정 안 하면 자동 생성
        tracer.enabled = false;
        _beamGlow = CreateBeamGlow();
    }

    private void OnDestroy()
    {
        // 탄착 FX 루트는 씬 최상위에 생성되므로 직접 정리
        if (_impactFx != null && _impactFx.Root != null) Destroy(_impactFx.Root);
        if (_critImpactFx != null && _critImpactFx.Root != null) Destroy(_critImpactFx.Root);
    }

    private void Update()
    {
        if (Mouse.current == null || aimCamera == null) return;

        // 구르기 모션이 완전히 끝날 때까지 발사 금지(다이브 중 사격 방지).
        // IsRolling은 롤에서 빠져나오는 블렌드가 끝날 때까지 true를 유지한다.
        bool rolling = _controller != null && _controller.IsRolling;

        if (_stats == null) _stats = GetComponent<PlayerStats>();

        bool canFire = !TimeShiftController.RewindActive   // 시간역행 역재생 중엔 조작이 잠긴다
                       && !BossController.CutsceneActive   // 등장 컷신 중에도 잠근다
                       && !(_stats != null && _stats.IsDead) // 사망 후 R은 재시작 전용이다
                       && !rolling
                       && !_reloading                      // 재장전 플래그가 살아 있는 동안
                       && !ReloadMotionPlaying()           // 플래그가 먼저 풀려도 모션이 끝날 때까지
                       && (!requireAimToFire || (tpsCamera != null && tpsCamera.IsAiming));
        if (canFire && Mouse.current.leftButton.isPressed && Time.time >= _nextFireTime)
        {
            _nextFireTime = Time.time + 1f / Mathf.Max(0.01f, fireRate);
            Fire();
        }

        // 수동 재장전(R). 자동 재장전은 탄 소진 시 Fire에서 발동.
        // 사망 후에는 R이 '재시작'이므로 여기서는 받지 않는다(HudUI가 처리).
        // 재장전 중이면 같은 R이 '빠른 재장전' 입력이 된다 — 키를 새로 만들지 않는 편이
        // 손에 익는다(어차피 재장전 중엔 R이 하는 일이 없었다).
        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame
            && !(_stats != null && _stats.IsDead))
        {
            if (_reloading) TryActiveReload();
            else StartReload();
        }

        UpdateReload();

        // 퍼짐 회복: 마지막 발사 후 잠깐 뒤부터 기본값으로 조여든다
        if (_spread > spreadBase && Time.time >= _spreadHoldUntil)
            _spread = Mathf.Max(spreadBase, _spread - spreadRecovery * Time.deltaTime);

        if (tracer != null && tracer.enabled && Time.time >= _tracerHideTime)
        {
            tracer.enabled = false;
            if (_beamGlow != null) _beamGlow.enabled = false;
        }
    }

    // 애니메이션(손 포즈) 이후에 총을 크로스헤어 지점으로 정렬 → 조준 포즈의 총구 어긋남 보정
    private void LateUpdate()
    {
        if (!aimWeaponToCrosshair || aimCamera == null || muzzlePoint == null) return;

        Transform gun = muzzlePoint.parent; // Muzzle의 부모 = 총 오브젝트
        if (gun == null) return;

        // 총열(로컬) 축 1회 결정: 오버라이드 우선, 없으면 메시 최장축 + 플레이어 정면 기준 부호
        if (!_barrelResolved)
        {
            _localBarrelAxis = ResolveLocalBarrelAxis(gun);
            _barrelResolved = true;
        }

        Vector3 barrelWorld = gun.rotation * _localBarrelAxis;         // 현재 총열 방향(월드)
        if (Vector3.Dot(barrelWorld, transform.forward) < 0f) barrelWorld = -barrelWorld; // 앞 반구로
        Vector3 desiredDir = (GetAimPoint() - muzzlePoint.position);   // 조준 방향
        if (desiredDir.sqrMagnitude < 1e-6f) return;
        desiredDir.Normalize();

        // 손 포즈(그립=손)를 유지한 채 총열만 조준방향으로 델타 회전.
        // 몸통도 크로스헤어를 향하므로 델타가 작아 롤/뒤집힘이 생기지 않는다.
        Quaternion delta = Quaternion.FromToRotation(barrelWorld, desiredDir);
        Quaternion target = delta * gun.rotation;
        gun.rotation = Quaternion.Slerp(gun.rotation, target, aimWeight);
    }

    /// <summary>
    /// 총 메시 최장축을 총열 축으로 판별(오버라이드 우선)하고,
    /// 총구 끝 계산용 로컬 바운즈 중심/절반 길이도 저장한다. 부호는 사용처에서 매 프레임 결정.
    /// </summary>
    private Vector3 ResolveLocalBarrelAxis(Transform gun)
    {
        bool has = false;
        Bounds local = new Bounds();
        foreach (var mf in gun.GetComponentsInChildren<MeshFilter>())
            if (mf.sharedMesh != null) EncapsulateLocal(gun, mf.transform, mf.sharedMesh.bounds, ref local, ref has);
        foreach (var smr in gun.GetComponentsInChildren<SkinnedMeshRenderer>())
            if (smr.sharedMesh != null) EncapsulateLocal(gun, smr.transform, smr.sharedMesh.bounds, ref local, ref has);

        Vector3 barrel;
        if (barrelAxisOverride.sqrMagnitude > 1e-6f) barrel = barrelAxisOverride.normalized;
        else if (has)
        {
            Vector3 s = local.size;
            int longest = s.x >= s.y && s.x >= s.z ? 0 : (s.y >= s.z ? 1 : 2);
            barrel = longest == 0 ? Vector3.right : longest == 1 ? Vector3.up : Vector3.forward;
        }
        else barrel = Vector3.forward;

        _gunBoundsCenter = has ? local.center : Vector3.zero;
        Vector3 abs = new Vector3(Mathf.Abs(barrel.x), Mathf.Abs(barrel.y), Mathf.Abs(barrel.z));
        _gunExtentAlong = has ? Vector3.Dot(local.extents, abs) : 0.3f;
        return barrel;
    }

    private static void EncapsulateLocal(Transform gun, Transform meshT, Bounds meshBounds, ref Bounds acc, ref bool has)
    {
        Vector3 c = meshBounds.center, e = meshBounds.extents;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = c + new Vector3(
                (i & 1) == 0 ? -e.x : e.x,
                (i & 2) == 0 ? -e.y : e.y,
                (i & 4) == 0 ? -e.z : e.z);
            Vector3 localPt = gun.InverseTransformPoint(meshT.TransformPoint(corner));
            if (!has) { acc = new Bounds(localPt, Vector3.zero); has = true; }
            else acc.Encapsulate(localPt);
        }
    }

    /// <summary>화면 중앙(크로스헤어) 레이가 맞는 월드 지점. 없으면 사거리 끝점.</summary>
    private Vector3 GetAimPoint()
    {
        Ray ray = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Physics.Raycast(ray, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore)
            && !hit.collider.transform.IsChildOf(transform)) // 자기 몸 제외
            return hit.point;
        return ray.origin + ray.direction * range;
    }

    private void Fire()
    {
        // 재장전 중엔 발사 불가, 탄이 없으면 자동 재장전
        if (_reloading) return;
        if (_ammo <= 0) { StartReload(); return; }
        _ammo--;

        // 화면 중앙에서 카메라 정면으로 레이 발사. 현재 퍼짐만큼 원뿔 안에서 방향이 흩어진다
        // (크로스헤어가 같은 값으로 벌어지므로 탄착 범위와 조준선이 일치)
        Ray ray = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        ray.direction = ApplySpread(ray.direction);
        Vector3 targetPoint = ray.origin + ray.direction * range;
        bool didHit = Physics.Raycast(ray, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore);
        if (didHit)
        {
            targetPoint = hit.point;

            // 데미지 전달 + 명중 시 타임포스 획득
            var damageable = hit.collider.GetComponentInParent<IDamageable>();
            bool weakPoint = damageable is BossHitbox part && part.IsWeakPoint;
            if (damageable != null)
            {
                damageable.TakeDamage(damage, hit.point, hit.normal);
                if (_stats == null) _stats = GetComponent<PlayerStats>();
                // 약점(머리)에 맞히면 타임포스를 더 준다 — 정확히 쏠수록 시간 능력이 빨리 돌아온다
                if (_stats != null && !ReferenceEquals(damageable, _stats))
                    _stats.GainTimeForceOnHit(weakPoint);
            }

            SpawnImpact(hit.point, hit.normal, weakPoint);
        }

        // 총구 위치(총 메시 실측 끝) / 발사 방향
        GetMuzzle(out Vector3 muzzlePos, out _);
        Vector3 fireDir = (targetPoint - muzzlePos).normalized;

        // 총구 화염
        if (muzzleFlash != null)
        {
            muzzleFlash.transform.SetPositionAndRotation(muzzlePos, Quaternion.LookRotation(fireDir));
            muzzleFlash.Play();
        }
        else
        {
            _muzzleFx ??= GunFx.BuildMuzzleFlash(transform, _fxScale, laserColor);
            _muzzleFx.Fire(muzzlePos, fireDir);
        }

        // 레이저 빔(총구 → 탄착점): 흰 코어 + 색 글로우 두 겹
        if (tracer != null)
        {
            tracer.enabled = true;
            tracer.positionCount = 2;
            tracer.SetPosition(0, muzzlePos);
            tracer.SetPosition(1, targetPoint);
            if (_beamGlow != null)
            {
                _beamGlow.enabled = true;
                _beamGlow.positionCount = 2;
                _beamGlow.SetPosition(0, muzzlePos);
                _beamGlow.SetPosition(1, targetPoint);
            }
            _tracerHideTime = Time.time + beamDuration;
        }

        GameSfx.Play(Sfx.PlayerFire, pitch: Random.Range(0.95f, 1.05f));

        _lastFireTime = Time.time;
        if (animator != null && animator.runtimeAnimatorController != null)
            animator.SetTrigger(FireHash);

        // 반동: 총구가 들리며 조준점이 살짝 밀렸다가 복귀
        if (tpsCamera != null)
        {
            tpsCamera.AddRecoil(recoilPitch, Random.Range(-recoilYaw, recoilYaw));
            if (recoilShake > 0f) tpsCamera.AddShake(recoilShake, 0.1f);
        }

        // 쏠수록 퍼짐 누적(상한까지). 멈추고 잠깐 뒤부터 Update에서 회복된다.
        _spread = Mathf.Min(spreadMax, _spread + spreadPerShot);
        _spreadHoldUntil = Time.time + spreadRecoveryDelay;

        // 마지막 발을 쏘면 즉시 자동 재장전
        if (_ammo <= 0) StartReload();
    }

    /// <summary>
    /// 상체 레이어가 지금 재장전 모션을 재생(또는 진입)하고 있는가.
    /// _reloading 플래그와 별개로 '화면에 재장전 동작이 보이는 동안'을 직접 판정한다.
    /// 아래 UpdateReload의 타이머 폴백이 모션보다 먼저 끝나더라도 이 조건이 발사를 막는다.
    /// </summary>
    private bool ReloadMotionPlaying()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return false;
        if (_upperLayerIdx == -2) _upperLayerIdx = animator.GetLayerIndex("UpperBody");
        if (_upperLayerIdx < 0) return false;

        return animator.GetCurrentAnimatorStateInfo(_upperLayerIdx).IsName("Reload")
            || animator.GetNextAnimatorStateInfo(_upperLayerIdx).IsName("Reload");
    }

    /// <summary>
    /// 컨트롤러에 ReloadSpeed(재생 속도 배율) 파라미터가 있는가.
    /// 애니메이터를 예전 구성 그대로 쓰고 있어도 오류 없이 넘어가도록 1회만 확인한다.
    /// </summary>
    private bool HasReloadSpeedParam()
    {
        if (_reloadSpeedChecked) return _hasReloadSpeedParam;
        _reloadSpeedChecked = true;

        foreach (var p in animator.parameters)
            if (p.nameHash == ReloadSpeedHash) { _hasReloadSpeedParam = true; break; }
        return _hasReloadSpeedParam;
    }

    /// <summary>재장전 시작. UpperBody 레이어의 Reload 상태(모션)를 발동시킨다.</summary>
    private void StartReload()
    {
        if (_reloading || _ammo >= magazineSize) return;
        if (_controller != null && _controller.IsRolling) return; // 구르는 중엔 시작하지 않음
        _reloading = true;
        _reloadStartTime = Time.time;
        _reloadStateSeen = false;

        // 난이도는 재장전을 시작할 때 한 번만 정해 고정한다.
        // 매 프레임 다시 재면 재장전 도중 보스를 맞히는 순간 화살표 속도가 튄다.
        float hard = ActiveReloadDifficulty01;
        _arSweeps = Mathf.Max(0.25f, Mathf.Lerp(activeReloadSweeps, activeReloadSweepsMax, hard));

        // 성공 구간은 매번 새로 뽑는다 — 자리가 고정되면 화면을 보지 않고
        // 박자만 외워서 누르게 되고, 그러면 미니게임이 아니라 그냥 단축키가 된다.
        float span = Mathf.Clamp(Mathf.Lerp(activeReloadWindow, activeReloadWindowMin, hard), 0.02f, 0.4f);
        float lo = Mathf.Clamp01(Mathf.Min(activeReloadRange.x, activeReloadRange.y));
        float hi = Mathf.Clamp01(Mathf.Max(activeReloadRange.x, activeReloadRange.y));
        _arStart = Random.Range(lo, Mathf.Max(lo, hi - span));
        _arEnd = Mathf.Min(1f, _arStart + span);
        _arUsed = false;

        GameSfx.Play(Sfx.Reload);
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            // 재장전 모션이 reloadTime 안에 끝나도록 재생 속도를 맞춘다
            // (컨트롤러는 BakedReloadDuration 기준으로 구워져 있으므로 그 비율만 넘긴다)
            if (HasReloadSpeedParam())
                animator.SetFloat(ReloadSpeedHash, BakedReloadDuration / Mathf.Max(0.05f, reloadTime));

            // 마지막 발에서 자동 재장전이 걸리면 소비되지 않은 Fire 트리거가 남아 있다.
            // 그대로 두면 재장전이 끝나는 순간 입력도 없이 발사 모션이 한 번 튄다.
            animator.ResetTrigger(FireHash);
            animator.SetTrigger(ReloadHash);
        }
    }

    /// <summary>
    /// 빠른 재장전 시도(재장전 중 R).
    ///
    /// 기회는 <b>재장전당 한 번</b>이다. 실패해도 벌칙은 없지만 기회가 사라지므로,
    /// 연타로 긁어 맞히는 것이 최선의 전략이 되지 않는다 — 화살표를 보고 눌러야 한다.
    /// </summary>
    private void TryActiveReload()
    {
        if (!activeReloadEnabled || !_reloading || _arUsed) return;

        _arUsed = true;
        float t = ActiveReloadMarker01;   // 진행도가 아니라 화살표 자리로 판정한다(왕복하므로 다르다)
        _arSuccess = t >= _arStart && t <= _arEnd;
        _arFeedbackTime = Time.time;

        if (_arSuccess)
        {
            FinishReload(cutMotion: true);
            GameSfx.Play(Sfx.Reload, 1f, 1.6f);   // 같은 소리를 높게 — "빨리 끝났다"는 신호
        }
        else GameSfx.Play(Sfx.Reload, 0.5f, 0.55f);
    }

    /// <summary>
    /// 재장전을 지금 끝낸다(탄창을 채우고 모션도 끊는다).
    ///
    /// 빠른 재장전으로 <b>중간에</b> 끝낼 때는 모션도 함께 끊어야 한다(cutMotion) —
    /// 발사 조건이 <see cref="ReloadMotionPlaying"/>도 보고 있어서, 탄만 채우고 모션을 두면
    /// <b>탄은 찼는데 쏘지 못하는</b> 상태가 되어 아낀 시간이 그대로 사라진다.
    /// 정상 완료는 이미 모션이 끝난 뒤라 끊을 필요가 없다(끊으면 뒷동작만 잘린다).
    /// </summary>
    private void FinishReload(bool cutMotion = false)
    {
        _reloading = false;
        _reloadStateSeen = false;
        _ammo = magazineSize;

        if (!cutMotion || animator == null || animator.runtimeAnimatorController == null) return;
        animator.ResetTrigger(ReloadHash);
        if (_upperLayerIdx == -2) _upperLayerIdx = animator.GetLayerIndex("UpperBody");
        if (_upperLayerIdx >= 0) animator.Play("Rifle Hold", _upperLayerIdx, 0f);
    }

    /// <summary>
    /// 재장전 취소(구르기로 중단). 탄약은 채워지지 않으며, 상체 레이어를 파지 자세로 되돌려
    /// 구르기가 끝난 뒤 재장전 모션의 뒷부분이 이어서 재생되지 않게 한다.
    /// </summary>
    private void CancelReload()
    {
        _reloading = false;
        _reloadStateSeen = false;
        if (animator == null || animator.runtimeAnimatorController == null) return;

        animator.ResetTrigger(ReloadHash);
        if (_upperLayerIdx == -2) _upperLayerIdx = animator.GetLayerIndex("UpperBody");
        if (_upperLayerIdx >= 0) animator.Play("Rifle Hold", _upperLayerIdx, 0f);
    }

    /// <summary>
    /// 재장전 완료 감시. 애니메이터의 Reload 상태가 끝나는 시점(모션 길이 그대로)에 완료하고,
    /// Reload 상태가 없는 구성이면 reloadTime 타이머로 대체한다.
    /// </summary>
    private void UpdateReload()
    {
        if (!_reloading) return;

        // 구르기로 재장전 중단 — 탄약은 채워지지 않는다.
        // (레이어 가중치만 0이 되면 상태머신은 계속 돌아 재장전이 완료돼 버린다)
        if (_controller != null && _controller.IsRolling) { CancelReload(); return; }

        float elapsed = Time.time - _reloadStartTime;
        bool hasAnim = animator != null && animator.runtimeAnimatorController != null;
        if (hasAnim && _upperLayerIdx == -2) _upperLayerIdx = animator.GetLayerIndex("UpperBody");

        bool done;
        if (hasAnim && _upperLayerIdx >= 0)
        {
            bool inState = ReloadMotionPlaying();
            if (inState) _reloadStateSeen = true;
            // 상태 진입을 확인했으면 상태 종료 = 완료 / 진입을 못 봤으면(상태 미구성) 타이머로 대체
            done = _reloadStateSeen ? !inState : elapsed >= reloadTime;
        }
        else
        {
            done = elapsed >= reloadTime;
        }

        if (done) FinishReload();
    }

    /// <summary>탄착 이펙트: 프리팹이 있으면 사용, 없으면 재사용형 스파크/먼지 FX를 코드로 생성.</summary>
    /// <summary>
    /// 발사 방향을 현재 퍼짐 반각의 원뿔 안으로 흩뜨린다.
    /// 원 안에서 균일하게 뽑아, 화면상 탄착이 크로스헤어가 그리는 원 안에 고르게 분포한다.
    /// </summary>
    private Vector3 ApplySpread(Vector3 dir)
    {
        float sp = CurrentSpreadDegrees;
        if (sp <= 0.001f) return dir;

        Vector2 r = Random.insideUnitCircle * Mathf.Tan(sp * Mathf.Deg2Rad);
        Transform camT = aimCamera.transform;
        return (dir + camT.right * r.x + camT.up * r.y).normalized;
    }

    /// <summary>탄착 이펙트. 약점(머리)에 맞으면 색·크기·소리가 다른 전용 FX가 터진다.</summary>
    private void SpawnImpact(Vector3 pos, Vector3 normal, bool weakPoint = false)
    {
        GameSfx.PlayAt(weakPoint ? Sfx.CritImpact : Sfx.Impact, pos);

        if (impactPrefab != null)
        {
            GameObject fx = Instantiate(impactPrefab, pos, Quaternion.LookRotation(normal));
            Destroy(fx, 3f);
            return;
        }

        if (weakPoint)
        {
            _critImpactFx ??= GunFx.BuildImpact(_fxScale, critImpactColor, critical: true);
            _critImpactFx.Spawn(pos, normal);
            return;
        }
        _impactFx ??= GunFx.BuildImpact(_fxScale, laserColor);
        _impactFx.Spawn(pos, normal);
    }

    /// <summary>플레이어 총의 총구 끝과 총열 방향(월드).</summary>
    private void GetMuzzle(out Vector3 pos, out Vector3 dir)
    {
        Transform gun = muzzlePoint != null ? muzzlePoint.parent : null;
        if (gun != null && TryResolveMuzzle(gun, transform.forward, out pos, out dir)) return;

        pos = muzzlePoint != null ? muzzlePoint.position : aimCamera.transform.position;
        dir = aimCamera.transform.forward;
    }

    /// <summary>
    /// 임의의 총 Transform에 대해 총구 끝(메시 바운즈의 총열 방향 끝)과 총열 방향을 구한다.
    /// 고스트의 복제 총도 같은 모델이라 로컬 기하가 동일하므로 그대로 적용된다
    /// → 플레이어와 분신이 똑같이 총구 끝에서 발사된다.
    /// forwardRef는 총열의 앞뒤를 판별하는 기준 방향(보통 쏘려는 방향).
    /// </summary>
    public bool TryResolveMuzzle(Transform gun, Vector3 forwardRef, out Vector3 pos, out Vector3 dir)
    {
        pos = default; dir = default;
        if (gun == null) return false;

        if (!_barrelResolved)
        {
            _localBarrelAxis = ResolveLocalBarrelAxis(gun);
            _barrelResolved = true;
        }
        Vector3 axisW = gun.rotation * _localBarrelAxis;
        float sign = Vector3.Dot(axisW, forwardRef) < 0f ? -1f : 1f; // 총구는 항상 쏘려는 쪽
        pos = gun.TransformPoint(_gunBoundsCenter + _localBarrelAxis * (_gunExtentAlong * sign));
        dir = axisW * sign;
        return true;
    }

    /// <summary>
    /// 레이저 빔용 LineRenderer 생성. 굵기가 일정하고 색이 끝까지 유지돼
    /// '날아가는 탄'이 아니라 '이어진 광선'으로 보인다. 바깥 글로우 레이어는 CreateBeamGlow가 담당.
    /// </summary>
    private LineRenderer CreateTracer()
    {
        var go = new GameObject("LaserBeamCore");
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = GunFx.MakeTracerMaterial();
        Color core = Color.Lerp(laserColor, Color.white, 0.75f); // 코어는 흰빛
        lr.startColor = core;
        lr.endColor = core;
        lr.startWidth = 0.035f * _fxScale;
        lr.endWidth = 0.035f * _fxScale;
        lr.positionCount = 2;
        lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.enabled = false;
        return lr;
    }

    /// <summary>코어 바깥을 감싸는 넓고 옅은 글로우 레이어(레이저의 발광 후광).</summary>
    private LineRenderer CreateBeamGlow()
    {
        var go = new GameObject("LaserBeamGlow");
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = GunFx.MakeTracerMaterial();
        Color glow = new Color(laserColor.r, laserColor.g, laserColor.b, 0.45f);
        lr.startColor = glow;
        lr.endColor = glow;
        lr.startWidth = 0.11f * _fxScale;
        lr.endWidth = 0.11f * _fxScale;
        lr.positionCount = 2;
        lr.numCapVertices = 4;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.enabled = false;
        return lr;
    }

    private void OnDrawGizmosSelected()
    {
        if (muzzlePoint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(muzzlePoint.position, muzzlePoint.forward * 2f);
        }
    }
}

/// <summary>피격 가능한 대상 인터페이스. 적/파괴 오브젝트가 구현한다.</summary>
public interface IDamageable
{
    void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitNormal);
}
