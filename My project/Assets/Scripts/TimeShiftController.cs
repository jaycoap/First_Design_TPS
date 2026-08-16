using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 시간 능력 컨트롤러(타임포스 소모). 두 능력 모두 별도 선택 창 없이 키 한 번으로 즉시 발동한다.
/// - T(시간역행): 지나온 길을 거꾸로 되감기며 5초 전 상태(위치·자세·체력·탄약)로 돌아간다.
///   월드의 TimeRewindable(보스 패턴 등)도 함께 역재생된다. 쿨다운 10초.
/// - G(협공): 크로스헤어가 겨누고 있는 그 대상만 과거의 나가 함께 쏜다. 쿨다운 8초.
///   적을 스스로 찾아다니지 않으므로, 여러 적 중 '어느 놈을 칠지'는 전적으로 플레이어의 조준이 정한다.
///
/// 사용 가능 여부와 남은 쿨다운은 HudUI가 좌하단 슬롯에 표시하고,
/// 발동에 실패하면 그 이유를 토스트로 알린다(LastDenyReason).
/// </summary>
[RequireComponent(typeof(PlayerTimeGhost))]
public class TimeShiftController : MonoBehaviour
{
    [Header("타임포스 비용")]
    [Tooltip("시간역행(T) 비용")]
    [SerializeField] private float rewindCost = 30f;
    [Tooltip("협공(G) 비용")]
    [SerializeField] private float supportCost = 30f;

    [Header("쿨다운")]
    [Tooltip("시간역행(T) 재사용 대기(초). 발동한 순간부터 잰다.")]
    [SerializeField] private float rewindCooldown = 10f;
    [Tooltip("협공(G) 재사용 대기(초). 발동한 순간부터 재므로 사격 시간(Support Duration)도 이 안에 포함된다.")]
    [SerializeField] private float supportCooldown = 8f;

    [Header("시간역행 연출")]
    [Tooltip("과거로 되감기는 모습이 보이는 시간(초). 플레이어와 월드(TimeRewindable)가 함께 역재생된다.\n길수록 잔상이 길게 늘어져 연출이 극적이다.")]
    [SerializeField] private float rewindDuration = 2.5f;

    [Header("지원 사격")]
    [Tooltip("고스트 레이저 색(플레이어와 구분되도록 다른 색을 권장)")]
    [SerializeField] private Color ghostLaserColor = new Color(0.7f, 0.5f, 1f);
    [SerializeField] private float supportDuration = 3f;
    [SerializeField] private float supportFireRate = 8f;
    [SerializeField] private float supportDamage = 10f;
    [SerializeField] private float supportRange = 200f;
    [Tooltip("협공 발동 키. 사격 중에도 그대로 누를 수 있다.")]
    [SerializeField] private Key supportKey = Key.G;
    [Tooltip("과거의 나가 쏠 때의 탄 퍼짐·반동 = 플레이어 수치 × 이 값.\n" +
             "1.5면 플레이어보다 반동이 세고 더 흩어져, 협공만으로는 표적을 계속 맞히지 못한다.\n" +
             "0이면 예전처럼 무조건 명중.")]
    [SerializeField] private float ghostSpreadScale = 1.5f;

    /// <summary>시간역행 역재생 중인가(전역). 재생 동안 조준/발사 등 입력을 잠근다.</summary>
    public static bool RewindActive { get; private set; }

    /// <summary>
    /// 지금 들어가는 피해가 '과거의 나(고스트)'의 협공인가(전역, 한 발 단위).
    /// 보스의 분신 처형 패턴은 이 피해만 파훼로 인정한다 — 일반 사격과 구분하기 위한 표식.
    /// </summary>
    public static bool GhostDamageActive { get; private set; }

    /// <summary>
    /// 협공 세션 번호(발동할 때마다 1 증가). 진행 중이던 협공과 새로 시작한 협공을 구분한다.
    /// 보스는 패턴이 시작되기 전에 이미 날아오던 협공으로 패턴이 즉시 파훼되는 것을 이 번호로 막는다.
    /// </summary>
    public static int SupportSession { get; private set; }

    /// <summary>협공 사격이 진행 중인가(전역).</summary>
    public static bool SupportActive { get; private set; }

    /// <summary>
    /// 능력을 눌렀는데 발동하지 않은 이유(HUD 토스트용). 예전에는 조건이 안 맞으면
    /// 아무 반응 없이 무시돼, 왜 안 되는지 알 방법이 없었다.
    /// </summary>
    public static string LastDenyReason { get; private set; }
    /// <summary>위 사유가 기록된 시각(Time.unscaledTime). HUD가 잠깐만 띄우는 데 쓴다.</summary>
    public static float LastDenyTime { get; private set; }

    // ---- HUD 표시용 ----
    /// <summary>시간역행 비용(HUD 게이지 눈금).</summary>
    public float RewindCost => rewindCost;
    /// <summary>협공 비용(HUD 게이지 눈금).</summary>
    public float SupportCost => supportCost;
    /// <summary>과거의 나가 준비됐는가(히스토리 축적 완료).</summary>
    public bool GhostReady => _ghost != null && _ghost.GhostReady;
    /// <summary>협공이 끝나기까지 남은 비율 1→0(진행 중이 아니면 0).</summary>
    public float SupportRemain01 => SupportActive && supportDuration > 0.01f
        ? Mathf.Clamp01((_supportEndTime - Time.time) / supportDuration)
        : 0f;

    /// <summary>시간역행 쿨다운 진행도 0→1(1 = 대기 완료).</summary>
    public float RewindCooldown01 => Progress01(_nextRewindTime, rewindCooldown);
    /// <summary>시간역행 남은 쿨다운(초). 0이면 대기 완료.</summary>
    public float RewindRemain => Mathf.Max(0f, _nextRewindTime - Time.time);
    /// <summary>협공 쿨다운 진행도 0→1(1 = 대기 완료).</summary>
    public float SupportCooldown01 => Progress01(_nextSupportTime, supportCooldown);
    /// <summary>협공 남은 쿨다운(초). 0이면 대기 완료.</summary>
    public float SupportRemain => Mathf.Max(0f, _nextSupportTime - Time.time);

    private static float Progress01(float readyTime, float cooldown)
        => cooldown <= 0.01f ? 1f : Mathf.Clamp01(1f - (readyTime - Time.time) / cooldown);

    private Transform _supportTarget; // 협공이 겨누고 있는 대상(매 프레임 조준 갱신용)
    private float _supportEndTime;
    private float _nextRewindTime;    // 이 시각 이후 시간역행 재사용 가능
    private float _nextSupportTime;   // 이 시각 이후 협공 재사용 가능
    private string _msgRecharge = "과거의 나 재충전 중";
    private string _msgNoTarget = "겨누는 대상이 없다";
    private string _msgNoForce = "타임포스 부족";
    private string _msgCooldown = "재사용 대기";

    /// <summary>발동 실패 사유 기록(HUD가 1.6초간 표시).</summary>
    private void Deny(string reason)
    {
        LastDenyReason = reason;
        LastDenyTime = Time.unscaledTime;
    }

    /// <summary>진행 중인 협공을 즉시 중단시킨다(보스 패턴 진입 등 외부 개입).</summary>
    public static void CancelSupport()
    {
        if (_instance != null) _instance.StopSupport();
    }

    /// <summary>
    /// 씬을 다시 불러오기 전에 전역 상태를 초기화한다.
    /// 이 값들은 static이라 씬 로드를 넘어 그대로 남는다 — 예컨대 시간역행 도중에 죽으면
    /// RewindActive가 true인 채로 새 씬이 시작돼 조준·발사가 영영 잠긴다.
    /// </summary>
    public static void ResetGlobalState()
    {
        RewindActive = false;
        SupportActive = false;
        GhostDamageActive = false;
        SupportSession = 0;
        LastDenyReason = null;
        LastDenyTime = 0f;
    }

    private static TimeShiftController _instance;

    private PlayerTimeGhost _ghost;
    private PlayerController _pc;
    private PlayerStats _stats;
    private PlayerShooter _shooter;   // 총구 끝 계산을 공유(분신도 총구에서 발사)
    private CharacterController _cc;
    private Camera _cam;
    private ThirdPersonCamera _tpsCam;
    private int _mask;
    private float _fxScale = 1f;
    private float _decisionEndRealtime;
    private Coroutine _support;
    private LineRenderer _tracer;
    private float _tracerHide;
    private GunFx.MuzzleFx _ghostMuzzleFx;
    private GunFx.ImpactFx _ghostImpactFx;
    private GunFx.ImpactFx _ghostCritImpactFx;   // 약점(머리) 명중 전용(첫 명중 때 생성)

    // 고스트의 탄 퍼짐/반동(플레이어 수치 × ghostSpreadScale). 협공 한 세션 동안 누적된다.
    private float _ghostSpread;
    private float _ghostSpreadHold;   // 이 시각까지는 퍼짐 회복 보류
    private float _ghostPitch;        // 누적 반동 — 총구가 들린 각도(도)
    private float _ghostYaw;          // 누적 반동 — 좌우로 밀린 각도(도)

    /// <summary>반동 누적 상한 = 한 발 반동 × 이 값(계속 쏴도 조준선에서 이만큼 이상은 벗어나지 않는다).</summary>
    private const float GhostRecoilCap = 1.5f;
    /// <summary>반동 회복 속도 = 초당 누적량 × 이 값(1보다 작으므로 연사하면 서서히 밀려 올라간다).</summary>
    private const float GhostRecoilRecovery = 0.7f;


    private void Awake()
    {
        _instance = this;
        _ghost = GetComponent<PlayerTimeGhost>();
        _pc = GetComponent<PlayerController>();
        _stats = GetComponent<PlayerStats>();
        _shooter = GetComponent<PlayerShooter>();
        _cc = GetComponent<CharacterController>();
        _cam = Camera.main;
        _tpsCam = _cam != null ? _cam.GetComponent<ThirdPersonCamera>() : null;
        _mask = ~(1 << gameObject.layer); // 자기(플레이어/고스트) 레이어 제외
        // 보이지 않는 낙하 방지 벽은 협공 사선을 막으면 안 된다(맵 밖의 분신을 겨눌 수 있어야 한다)
        int wallLayer = LayerMask.NameToLayer("ArenaWall");
        if (wallLayer >= 0) _mask &= ~(1 << wallLayer);
        // 보스 본체(CharacterController) 캡슐도 제외 — 협공도 부위 히트박스에만 맞아야
        // 부위별 배율이 적용된다(몸통 캡슐이 앞을 막으면 머리 판정이 오지 않는다)
        int bossBodyLayer = LayerMask.NameToLayer(BossHitbox.BodyLayer);
        if (bossBodyLayer >= 0) _mask &= ~(1 << bossBodyLayer);
        if (_cc != null) _fxScale = _cc.height / 1.8f;

        _tracer = CreateGhostTracer();
        // 고스트는 플레이어와 구분되는 보라빛 레이저
        _ghostMuzzleFx = GunFx.BuildMuzzleFlash(transform, _fxScale, ghostLaserColor);
        _ghostImpactFx = GunFx.BuildImpact(_fxScale, ghostLaserColor);
        LocalizeMessages();
    }

    private void OnDestroy()
    {
        if (_ghostImpactFx != null && _ghostImpactFx.Root != null) Destroy(_ghostImpactFx.Root);
        if (_ghostCritImpactFx != null && _ghostCritImpactFx.Root != null) Destroy(_ghostCritImpactFx.Root);
    }

    private void OnDisable()
    {
        StopSupport();
        if (_instance == this) _instance = null;
    }

    /// <summary>
    /// 협공 코루틴을 중단하고 고스트를 원래 상태로 돌린다(중복 호출 안전).
    /// 정상 종료와 달리 히스토리는 비우지 않는다 — 외부 사정으로 끊긴 것이므로
    /// 5초 재충전 페널티까지 물리면 곧바로 다시 협공해야 하는 패턴을 넘길 수 없다.
    /// </summary>
    private void StopSupport()
    {
        if (_support == null) { SupportActive = false; return; }

        StopCoroutine(_support);
        _support = null;
        _supportTarget = null;
        SupportActive = false;
        _ghost.SetGhostAnimating(false);
        _ghost.SetFrozen(false);
    }

    private void Update()
    {
        if (_tracer != null && _tracer.enabled && Time.unscaledTime >= _tracerHide)
            _tracer.enabled = false;

        if (_support != null)
        {
            RecoverGhostAim(); // 협공 중 고스트의 반동/퍼짐 회복

            // 조준은 매 프레임 갱신한다 — 발사할 때만 돌리면(초당 8회) 목표가 움직일 때
            // 몸이 뚝뚝 끊겨 돌아가고, 발사 사이에는 엉뚱한 데를 보고 서 있게 된다.
            if (_supportTarget != null && _supportTarget.gameObject.activeInHierarchy)
                AimGhostAtTarget(AimPointOf(_supportTarget));
        }

        // 컷신 중·사망 후에는 시간 능력도 잠근다(사망 후 R은 재시작 전용)
        // 컷신 중과 사망 후에는 시간 능력을 잠근다(사망 후 R은 재시작 전용이다)
        if (Keyboard.current == null || BossController.CutsceneActive) return;
        if (_stats != null && _stats.IsDead) return;
        if (_stats != null && _stats.IsDead) return;

        // 협공(G): 사격 중에도 끊김 없이 즉시 발동
        if (Keyboard.current[supportKey].wasPressedThisFrame)
            TryStartSupport();

        // 시간역행(T): 선택 창 없이 곧바로 발동
        if (Keyboard.current.tKey.wasPressedThisFrame)
            TryRewind();
    }

    private bool HasForce(float amount) => _stats == null || _stats.TimeForce >= amount;
    private bool TryUseForce(float amount) => _stats == null || _stats.TryUseTimeForce(amount);

    /// <summary>
    /// T 입력 처리: 조건이 맞으면 즉시 시간역행. 안 되면 왜 안 되는지 토스트로 알린다.
    /// </summary>
    private void TryRewind()
    {
        if (RewindActive || _support != null) return;
        if (RewindRemain > 0.01f) { Deny($"{_msgCooldown} {RewindRemain:0.0}s"); return; }
        if (!_ghost.GhostReady) { Deny(_msgRecharge); return; }
        if (!TryUseForce(rewindCost)) { Deny(_msgNoForce); return; }

        _nextRewindTime = Time.time + rewindCooldown;
        DoRewind();
    }

    /// <summary>시간역행: 지나온 길을 거꾸로 되감기며 5초 전 상태로 — 월드도 함께 역행.</summary>
    private void DoRewind()
    {
        // 시간역행: 텔레포트가 아니라 지나온 길을 거꾸로 되감기며 5초 전 상태
        // (위치·자세·체력·탄약)로 돌아간다. 월드의 TimeRewindable(보스/적)도 함께 역재생.
        if (RewindActive) return;

        _ghostImpactFx.Spawn(transform.position + Vector3.up * (_fxScale * 0.9f), Vector3.up); // 발동 이펙트

        bool started = _ghost.StartRewindPlayback(rewindDuration, () =>
        {
            _ghostImpactFx.Spawn(transform.position + Vector3.up * (_fxScale * 0.9f), Vector3.up); // 도착 이펙트
            RewindActive = false;
            if (_tpsCam != null)
            {
                _tpsCam.SetRewindCinematic(false);
                _tpsCam.AddShake(0.5f, 0.35f);   // 착지하듯 툭 떨어지는 마무리
                _tpsCam.AddFovKick(-6f);
            }
        });
        if (!started) return;

        RewindActive = true;
        if (_tpsCam != null)
        {
            _tpsCam.SetRewindCinematic(true); // 물러나며 넓어지고 기울어짐
            _tpsCam.AddShake(0.6f, 0.4f);     // 발동 순간의 충격
        }
        TimeRewindable.RewindAll(_ghost.Delay, rewindDuration); // 월드(보스 패턴 등)도 함께 역행
    }

    /// <summary>
    /// 협공 발동(G / 선택 모드 우클릭).
    /// 크로스헤어가 지금 겨누고 있는 대상 하나만 목표로 삼는다 — 스스로 적을 찾지 않으므로
    /// 여러 적(보스 분신 등) 중 무엇을 칠지는 플레이어의 조준이 결정한다.
    /// 겨누는 대상이 없으면 발동하지 않고 타임포스도 소모하지 않는다.
    /// </summary>
    private void TryStartSupport()
    {
        if (_support != null || RewindActive) return;   // 이미 쓰는 중 — 알릴 것도 없다
        if (SupportRemain > 0.01f) { Deny($"{_msgCooldown} {SupportRemain:0.0}s"); return; }
        if (!_ghost.GhostReady) { Deny(_msgRecharge); return; }

        Transform target = FindAimedTarget();
        if (target == null) { Deny(_msgNoTarget); return; }   // 허공을 겨눈 협공은 발동하지 않는다
        if (!TryUseForce(supportCost)) { Deny(_msgNoForce); return; }

        _nextSupportTime = Time.time + supportCooldown;
        SupportSession++;
        SupportActive = true;
        GameSfx.Play(Sfx.TimeSupport);
        _support = StartCoroutine(SupportFireRoutine(target));
    }

    /// <summary>고스트를 그 자리에 고정하고, 지정한 대상만 계속 쏜다.</summary>
    private IEnumerator SupportFireRoutine(Transform target)
    {
        _supportTarget = target;        // Update가 매 프레임 이 대상을 겨눈다
        _ghost.SetFrozen(true);
        _ghost.SetGhostAnimating(true); // 과거 자세와 무관하게 총을 겨눈 모습으로
        ResetGhostAim();                // 반동/퍼짐은 세션마다 처음부터 쌓인다

        // 발사 전에 먼저 몸을 돌려 둔다 — 첫 발이 등을 진 채로 나가지 않도록
        AimGhostAtTarget(AimPointOf(target));

        float end = Time.time + supportDuration;
        _supportEndTime = end;          // HUD 진행 바
        var wait = new WaitForSeconds(1f / Mathf.Max(1f, supportFireRate));
        while (Time.time < end)
        {
            // 목표가 사라지면(격파/소멸) 협공도 거기서 끝난다 — 다른 적으로 옮겨가지 않는다
            if (target == null || !target.gameObject.activeInHierarchy) break;

            // 매 발 다시 겨눈다 — 목표가 움직여도 총구가 따라간다
            AimGhostAtTarget(AimPointOf(target));

            FireFromGhost(target);
            yield return wait;
        }

        // 지원 사격을 마친 과거의 나는 시간 속으로 사라진다.
        // 히스토리를 비워야 (a) 흘러간 시간만큼 다른 자리로 순간이동하는 현상이 없고
        // (b) 시간역행과 똑같이 5초간 재충전되는 쿨다운이 생긴다.
        if (_ghost.TryGetGhostState(out Vector3 ghostPos, out _))
            _ghostImpactFx.Spawn(ghostPos + Vector3.up * (_fxScale * 0.9f), Vector3.up);

        _ghost.SetGhostAnimating(false);
        _ghost.SetFrozen(false);
        _ghost.ClearHistory();
        _support = null;
        _supportTarget = null;
        SupportActive = false;
    }

    /// <summary>
    /// 협공 목표 = 지금 크로스헤어가 겨누고 있는 대상. 오직 이것뿐이며 주변 탐색은 하지 않는다.
    /// (겨눈 놈만 친다 — 보스 분신들 사이에서 '진짜'를 직접 짚어내야 하는 이유)
    /// </summary>
    private Transform FindAimedTarget()
    {
        if (_cam == null) return null;

        Ray center = _cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Physics.Raycast(center, out RaycastHit hit, supportRange, _mask, QueryTriggerInteraction.Ignore))
            return null;

        var aimed = hit.collider.GetComponentInParent<IDamageable>() as Component;
        if (aimed == null || aimed.transform == transform) return null;
        return aimed.transform;
    }

    /// <summary>
    /// 고스트가 목표를 '겨누는' 자세가 되도록 수평 회전시킨다.
    /// 루트를 목표로 바로 돌리면 얼어붙은 포즈가 비스듬해서 몸이 딴 데를 보게 되므로,
    /// 총열 방향이 목표를 향하도록 그 차이만큼만 돌린다(플레이어의 조준 정렬과 같은 방식).
    /// 회전하면 총 위치도 함께 움직이므로 몇 번 반복해 수렴시킨다.
    ///
    /// 총열의 앞뒤 기준(forwardRef)은 반드시 <b>고스트 자신의 정면</b>이어야 한다.
    /// 목표 방향을 기준으로 주면 TryResolveMuzzle이 총열을 목표 쪽 반구로 뒤집어 버려서,
    /// 실제로 등을 돌리고 있어도 "거의 다 겨눴다"고 계산된다(회전각이 ±90°를 못 넘는다).
    /// 과거의 나가 엉뚱한 데를 보면서 쏘던 원인이 이것이다.
    /// </summary>
    private void AimGhostAtTarget(Vector3 aim)
    {
        Transform gun = _ghost.GhostGun;
        if (gun == null || _shooter == null)
        {
            _ghost.AimGhostAt(aim); // 총을 못 찾으면 루트 정렬로 폴백
            return;
        }

        for (int i = 0; i < 3; i++)
        {
            if (!_shooter.TryResolveMuzzle(gun, _ghost.GhostForward, out Vector3 tip, out Vector3 barrel))
            {
                _ghost.AimGhostAt(aim);
                return;
            }

            Vector3 cur = new Vector3(barrel.x, 0f, barrel.z);
            Vector3 want = aim - tip;
            want.y = 0f;
            if (cur.sqrMagnitude < 1e-6f || want.sqrMagnitude < 1e-6f) return;

            float delta = Vector3.SignedAngle(cur, want, Vector3.up);
            if (Mathf.Abs(delta) < 0.05f) break; // 이미 겨누고 있음
            _ghost.RotateGhostYaw(delta);
        }
    }

    /// <summary>조준점: 콜라이더 중심(피벗이 발밑인 모델에서도 몸통을 맞히도록).</summary>
    private static Vector3 AimPointOf(Transform t)
    {
        if (t.TryGetComponent(out Collider col)) return col.bounds.center;
        var child = t.GetComponentInChildren<Collider>();
        return child != null ? child.bounds.center : t.position;
    }

    private void FireFromGhost(Transform target)
    {
        Transform gun = _ghost.GhostGun;
        if (gun == null || _cam == null) return;

        // 목표: 잡아둔 적 → 없으면 플레이어 크로스헤어 지점
        Vector3 aim;
        if (target != null) aim = AimPointOf(target);
        else
        {
            Ray center = _cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            aim = Physics.Raycast(center, out RaycastHit ch, supportRange, _mask, QueryTriggerInteraction.Ignore)
                ? ch.point
                : center.origin + center.direction * supportRange;
        }

        // 발사 원점 = 고스트 총의 총구 끝(플레이어와 동일한 계산).
        // 앞뒤 기준은 조준 정렬과 마찬가지로 고스트 자신의 정면이어야 한다 —
        // 목표 방향을 주면 총이 등을 지고 있을 때 개머리판 쪽이 '총구'로 잡힌다.
        // 못 구하면 총 오브젝트 원점(그립)으로 폴백.
        Vector3 origin = gun.position;
        if (_shooter != null && _shooter.TryResolveMuzzle(gun, _ghost.GhostForward, out Vector3 tip, out _))
            origin = tip;

        // 겨눈 방향에 반동과 탄 퍼짐을 얹는다 — 빗나가면 그대로 빗나간다.
        // (예전에는 레이가 빗나가도 겨눈 적에게 무조건 명중시켰지만, 그러면 반동·퍼짐이 무의미하다)
        Vector3 dir = ApplyGhostAim((aim - origin).normalized);
        Vector3 endPoint = origin + dir * supportRange;

        if (Physics.Raycast(origin, dir, out RaycastHit hit, supportRange, _mask, QueryTriggerInteraction.Ignore))
        {
            endPoint = hit.point;
            var dmg = hit.collider.GetComponentInParent<IDamageable>();
            if (dmg != null) DealGhostDamage(dmg, supportDamage, hit.point, hit.normal);

            // 약점(머리)에 맞으면 협공도 플레이어와 같은 규칙으로 다른 탄착을 터뜨린다
            bool crit = dmg is BossHitbox part && part.IsWeakPoint;
            GameSfx.PlayAt(crit ? Sfx.CritImpact : Sfx.Impact, hit.point);
            if (crit)
            {
                // 고스트 색을 유지한 채 뜨거운 쪽으로 밀어 '약점 명중'으로 읽히게 한다
                _ghostCritImpactFx ??= GunFx.BuildImpact(
                    _fxScale, Color.Lerp(ghostLaserColor, new Color(1f, 0.82f, 0.25f), 0.75f), critical: true);
                _ghostCritImpactFx.Spawn(hit.point, hit.normal);
            }
            else _ghostImpactFx.Spawn(hit.point, hit.normal);
        }
        AddGhostRecoil();

        _ghost.TriggerGhostFire(); // 발사 모션(상체)
        GameSfx.PlayAt(Sfx.GhostFire, origin, pitch: Random.Range(0.92f, 1.02f));
        _ghostMuzzleFx.Fire(origin, dir);
        _tracer.enabled = true;
        _tracer.positionCount = 2;
        _tracer.SetPosition(0, origin);
        _tracer.SetPosition(1, endPoint);
        _tracerHide = Time.unscaledTime + 0.04f;
    }

    // ---------- 고스트의 반동 / 탄 퍼짐 ----------
    // 수치는 플레이어의 총 그대로에 ghostSpreadScale(기본 1.5)을 곱한 값이다.
    // 과거의 나는 반동을 잡아 주지 못하므로, 길게 쏠수록 총구가 들리고 탄이 벌어진다
    // → 협공만으로는 표적을 계속 맞히지 못하고, 플레이어의 사격이 함께 필요해진다.

    /// <summary>협공 세션 시작 시 반동/퍼짐을 초기 상태로 되돌린다.</summary>
    private void ResetGhostAim()
    {
        _ghostPitch = 0f;
        _ghostYaw = 0f;
        _ghostSpread = _shooter != null ? _shooter.SpreadBase * ghostSpreadScale : 0f;
        _ghostSpreadHold = 0f;
    }

    /// <summary>겨눈 방향에 지금까지 쌓인 반동(위로 들림 + 좌우 밀림)과 퍼짐을 얹는다.</summary>
    private Vector3 ApplyGhostAim(Vector3 dir)
    {
        if (ghostSpreadScale <= 0.001f || _shooter == null) return dir;

        Vector3 right = Vector3.Cross(Vector3.up, dir);
        right = right.sqrMagnitude > 1e-6f ? right.normalized : Vector3.right;

        // 반동: 조준선이 통째로 어긋난다(총구가 들리는 쪽이 -pitch)
        dir = Quaternion.AngleAxis(-_ghostPitch, right) * dir;
        dir = Quaternion.AngleAxis(_ghostYaw, Vector3.up) * dir;

        // 퍼짐: 현재 반각의 원뿔 안에서 균일하게 흩뜨린다(플레이어와 같은 방식)
        if (_ghostSpread > 0.001f)
        {
            Vector3 up = Vector3.Cross(dir, right).normalized;
            Vector2 r = Random.insideUnitCircle * Mathf.Tan(_ghostSpread * Mathf.Deg2Rad);
            dir = (dir + right * r.x + up * r.y).normalized;
        }
        return dir;
    }

    /// <summary>한 발 쏜 뒤: 반동을 상한까지 누적하고 퍼짐을 벌린다.</summary>
    private void AddGhostRecoil()
    {
        if (ghostSpreadScale <= 0.001f || _shooter == null) return;

        float pitchStep = _shooter.RecoilPitch * ghostSpreadScale;
        float yawStep = _shooter.RecoilYaw * ghostSpreadScale;
        _ghostPitch = Mathf.Min(_ghostPitch + pitchStep, pitchStep * GhostRecoilCap);
        _ghostYaw = Mathf.Clamp(_ghostYaw + Random.Range(-yawStep, yawStep),
                                -yawStep * GhostRecoilCap, yawStep * GhostRecoilCap);

        _ghostSpread = Mathf.Min(_shooter.SpreadMax * ghostSpreadScale,
                                 _ghostSpread + _shooter.SpreadPerShot * ghostSpreadScale);
        _ghostSpreadHold = Time.time + _shooter.SpreadRecoveryDelay * ghostSpreadScale;
    }

    /// <summary>협공 중 매 프레임 회복. 퍼짐은 플레이어와 같은 규칙, 반동은 누적 속도의 일부만.</summary>
    private void RecoverGhostAim()
    {
        if (ghostSpreadScale <= 0.001f || _shooter == null) return;

        float baseSpread = _shooter.SpreadBase * ghostSpreadScale;
        if (_ghostSpread > baseSpread && Time.time >= _ghostSpreadHold)
            _ghostSpread = Mathf.Max(baseSpread,
                _ghostSpread - _shooter.SpreadRecovery * ghostSpreadScale * Time.deltaTime);

        float recover = _shooter.RecoilPitch * ghostSpreadScale * supportFireRate
                        * GhostRecoilRecovery * Time.deltaTime;
        _ghostPitch = Mathf.MoveTowards(_ghostPitch, 0f, recover);
        _ghostYaw = Mathf.MoveTowards(_ghostYaw, 0f, recover);
    }

    /// <summary>
    /// 협공 피해 전달. 전달 동안 GhostDamageActive를 세워, 받는 쪽이
    /// "이건 과거의 나와의 협공"임을 구분할 수 있게 한다(보스 분신 처형 파훼 판정).
    /// </summary>
    private static void DealGhostDamage(IDamageable target, float amount, Vector3 point, Vector3 normal)
    {
        GhostDamageActive = true;
        try { target.TakeDamage(amount, point, normal); }
        finally { GhostDamageActive = false; }
    }

    /// <summary>고스트 지원 사격용 탄도선(시안 빛, 캐릭터 스케일 반영).</summary>
    private LineRenderer CreateGhostTracer()
    {
        var go = new GameObject("GhostTracerFX");
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = GunFx.MakeTracerMaterial();
        // 굵기·색이 끝까지 일정해야 '광선'으로 보인다(탄도선처럼 가늘어지지 않게)
        Color core = Color.Lerp(ghostLaserColor, Color.white, 0.7f);
        lr.startColor = core;
        lr.endColor = core;
        lr.startWidth = 0.045f * _fxScale;
        lr.endWidth = 0.045f * _fxScale;
        lr.positionCount = 2;
        lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.enabled = false;
        return lr;
    }

    /// <summary>
    /// 발동 실패 토스트 문구를 준비한다(HudUI가 이 문자열을 그대로 띄운다).
    /// 내장 폰트에 한글 글리프가 없으므로, OS 폰트(맑은 고딕)를 쓸 수 있을 때만 한글로 쓴다.
    /// </summary>
    private void LocalizeMessages()
    {
        bool korean;
        try { korean = Font.CreateDynamicFontFromOSFont("Malgun Gothic", 20) != null; }
        catch { korean = false; }

        _msgRecharge = korean ? "과거의 나 재충전 중" : "PAST SELF RECHARGING";
        _msgNoTarget = korean ? "겨누는 대상이 없다" : "NO TARGET";
        _msgNoForce = korean ? $"타임포스 부족 — {supportCost:0} 필요"
                             : $"NOT ENOUGH TIME FORCE ({supportCost:0})";
        _msgCooldown = korean ? "재사용 대기" : "ON COOLDOWN";
    }
}
