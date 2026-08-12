using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 시간 능력 컨트롤러(타임포스 소모).
/// - G(협공): 전투 중 언제든 즉시 발동. 크로스헤어가 겨누고 있는 그 대상만 과거의 나가 함께 쏜다.
///   적을 스스로 찾아다니지 않으므로, 여러 적 중 '어느 놈을 칠지'는 전적으로 플레이어의 조준이 정한다.
/// - T: 슬로우 모션 + 선택 모드 진입(고스트가 준비되고 타임포스가 충분할 때)
///   - 좌클릭: 5초 전 위치로 되감기(텔레포트). 히스토리 초기화 → 고스트가 다시 차오를 때까지 자연 쿨다운
///   - 우클릭: 협공(G와 동일 — 겨누고 있는 대상)
///   - T 재입력 / 시간 초과: 취소(타임포스 소모 없음)
/// 선택 모드 동안 일반 발사/조준 입력은 잠긴다(PlayerShooter/ThirdPersonCamera가 DecisionActive 확인).
/// 협공(G)은 선택 모드를 거치지 않으므로 사격 중에도 끊김 없이 쓸 수 있다.
/// </summary>
[RequireComponent(typeof(PlayerTimeGhost))]
public class TimeShiftController : MonoBehaviour
{
    [Header("슬로우/선택")]
    [SerializeField] private float slowTimeScale = 0.2f;
    [Tooltip("선택 제한 시간(실시간 초). 초과 시 취소")]
    [SerializeField] private float decisionTimeout = 4f;

    [Header("타임포스 비용")]
    [Tooltip("좌클릭(시간역행) 비용")]
    [SerializeField] private float rewindCost = 30f;
    [Tooltip("우클릭(시간공명) 비용")]
    [SerializeField] private float supportCost = 30f;

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

    /// <summary>선택 모드 중인가(전역). PlayerShooter/카메라가 참조해 입력 충돌을 막는다.</summary>
    public static bool DecisionActive { get; private set; }

    /// <summary>시간역행 역재생 중인가(전역). 재생 동안 조준 등 입력을 잠근다.</summary>
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

    /// <summary>진행 중인 협공을 즉시 중단시킨다(보스 패턴 진입 등 외부 개입).</summary>
    public static void CancelSupport()
    {
        if (_instance != null) _instance.StopSupport();
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

    // 선택 UI(코드 생성 uGUI)
    private GameObject _panel;
    private Image _timeoutBar;
    private CanvasGroup _rewindCard;
    private CanvasGroup _supportCard;
    private Font _uiFont;

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
        if (_cc != null) _fxScale = _cc.height / 1.8f;

        _tracer = CreateGhostTracer();
        // 고스트는 플레이어와 구분되는 보라빛 레이저
        _ghostMuzzleFx = GunFx.BuildMuzzleFlash(transform, _fxScale, ghostLaserColor);
        _ghostImpactFx = GunFx.BuildImpact(_fxScale, ghostLaserColor);
        BuildDecisionUI();
    }

    private void OnDestroy()
    {
        if (_ghostImpactFx != null && _ghostImpactFx.Root != null) Destroy(_ghostImpactFx.Root);
    }

    private void OnDisable()
    {
        if (DecisionActive) EndDecision();
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
        SupportActive = false;
        _ghost.SetGhostAnimating(false);
        _ghost.SetFrozen(false);
    }

    private void Update()
    {
        if (_tracer != null && _tracer.enabled && Time.unscaledTime >= _tracerHide)
            _tracer.enabled = false;

        if (Keyboard.current == null || Mouse.current == null) return;

        if (!DecisionActive)
        {
            // 협공(G): 선택 모드를 거치지 않고 사격 중에도 즉시 발동
            if (Keyboard.current[supportKey].wasPressedThisFrame)
                TryStartSupport();

            if (Keyboard.current.tKey.wasPressedThisFrame && _support == null && !RewindActive
                && _ghost.GhostReady && HasForce(Mathf.Min(rewindCost, supportCost)))
                BeginDecision();
            return;
        }

        // --- 선택 모드 ---
        UpdateDecisionUI();
        if (Mouse.current.leftButton.wasPressedThisFrame && TryUseForce(rewindCost)) { DoRewind(); return; }
        if (Mouse.current.rightButton.wasPressedThisFrame) { EndDecision(); TryStartSupport(); return; }
        if (Keyboard.current.tKey.wasPressedThisFrame || Time.realtimeSinceStartup >= _decisionEndRealtime)
            EndDecision();
    }

    private bool HasForce(float amount) => _stats == null || _stats.TimeForce >= amount;
    private bool TryUseForce(float amount) => _stats == null || _stats.TryUseTimeForce(amount);

    private void BeginDecision()
    {
        DecisionActive = true;
        Time.timeScale = slowTimeScale;
        Time.fixedDeltaTime = 0.02f * slowTimeScale;
        _decisionEndRealtime = Time.realtimeSinceStartup + decisionTimeout;
        if (_panel != null) _panel.SetActive(true);
        UpdateDecisionUI();
    }

    private void EndDecision()
    {
        DecisionActive = false;
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;
        if (_panel != null) _panel.SetActive(false);
    }

    /// <summary>좌클릭(시간역행): 지나온 길을 거꾸로 되감기며 5초 전 상태로 — 월드도 함께 역행.</summary>
    private void DoRewind()
    {
        // 시간역행: 텔레포트가 아니라 지나온 길을 거꾸로 되감기며 5초 전 상태
        // (위치·자세·체력·탄약)로 돌아간다. 월드의 TimeRewindable(보스/적)도 함께 역재생.
        EndDecision();
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
        if (_support != null || RewindActive) return;
        if (!_ghost.GhostReady) return;

        Transform target = FindAimedTarget();
        if (target == null) return;          // 허공을 겨눈 협공은 발동하지 않는다
        if (!TryUseForce(supportCost)) return;

        SupportSession++;
        SupportActive = true;
        _support = StartCoroutine(SupportFireRoutine(target));
    }

    /// <summary>고스트를 그 자리에 고정하고, 지정한 대상만 계속 쏜다.</summary>
    private IEnumerator SupportFireRoutine(Transform target)
    {
        _ghost.SetFrozen(true);
        _ghost.SetGhostAnimating(true); // 과거 자세와 무관하게 총을 겨눈 모습으로

        float end = Time.time + supportDuration;
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
    /// 회전하면 총 위치도 함께 움직이므로 두 번 반복해 수렴시킨다.
    /// </summary>
    private void AimGhostAtTarget(Vector3 aim)
    {
        Transform gun = _ghost.GhostGun;
        if (gun == null || _shooter == null)
        {
            _ghost.AimGhostAt(aim); // 총을 못 찾으면 루트 정렬로 폴백
            return;
        }

        for (int i = 0; i < 2; i++)
        {
            if (!_shooter.TryResolveMuzzle(gun, aim - gun.position, out Vector3 tip, out Vector3 barrel))
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
        // 못 구하면 총 오브젝트 원점(그립)으로 폴백.
        Vector3 origin = gun.position;
        if (_shooter != null && _shooter.TryResolveMuzzle(gun, aim - gun.position, out Vector3 tip, out _))
            origin = tip;

        Vector3 dir = (aim - origin).normalized;
        Vector3 endPoint = origin + dir * supportRange;

        if (Physics.Raycast(origin, dir, out RaycastHit hit, supportRange, _mask, QueryTriggerInteraction.Ignore))
        {
            endPoint = hit.point;
            var dmg = hit.collider.GetComponentInParent<IDamageable>();
            if (dmg != null) DealGhostDamage(dmg, supportDamage, hit.point, hit.normal);
            _ghostImpactFx.Spawn(hit.point, hit.normal);
        }
        else if (target != null)
        {
            // 레이가 빗나가도(얇은 콜라이더 등) 조준한 적에겐 확실히 명중시킨다
            endPoint = aim;
            var dmg2 = target.GetComponentInParent<IDamageable>();
            if (dmg2 != null) DealGhostDamage(dmg2, supportDamage, aim, -dir);
            _ghostImpactFx.Spawn(aim, -dir);
        }

        _ghost.TriggerGhostFire(); // 발사 모션(상체)
        _ghostMuzzleFx.Fire(origin, dir);
        _tracer.enabled = true;
        _tracer.positionCount = 2;
        _tracer.SetPosition(0, origin);
        _tracer.SetPosition(1, endPoint);
        _tracerHide = Time.unscaledTime + 0.04f;
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

    // ---------- 선택 UI(코드 생성 uGUI) ----------
    // 내장 폰트에 한글 글리프가 없어 OS 폰트(맑은 고딕)를 동적 로드한다. 실패 시 영문 폴백.

    private void BuildDecisionUI()
    {
        bool korean = true;
        try { _uiFont = Font.CreateDynamicFontFromOSFont("Malgun Gothic", 20); }
        catch { _uiFont = null; }
        if (_uiFont == null)
        {
            _uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            korean = false; // 내장 폰트는 한글 미지원 → 영문 표기
        }

        var canvasGO = new GameObject("TimeShiftUI");
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10; // HUD 위에 표시
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // 어두운 오버레이(슬로우 모션 분위기)
        var dim = MakeImage(canvasGO.transform, "Dim", new Color(0.02f, 0.06f, 0.12f, 0.45f));
        Stretch(dim.rectTransform);

        // 타이틀 + 제한시간 바
        var title = MakeText(canvasGO.transform, "Title", 42, FontStyle.Bold, TextAnchor.MiddleCenter);
        title.text = "TIME SHIFT";
        title.color = new Color(0.7f, 0.95f, 1f);
        Place(title.rectTransform, new Vector2(0.5f, 0.8f), new Vector2(600f, 60f));

        var barBg = MakeImage(canvasGO.transform, "TimeoutBg", new Color(0f, 0f, 0f, 0.55f));
        Place(barBg.rectTransform, new Vector2(0.5f, 0.75f), new Vector2(320f, 8f));
        _timeoutBar = MakeImage(barBg.transform, "Fill", new Color(0.5f, 0.9f, 1f, 0.9f));
        Stretch(_timeoutBar.rectTransform);
        _timeoutBar.rectTransform.pivot = new Vector2(0f, 0.5f); // 좌측 기준으로 줄어듦(scale.x)

        // 선택 카드 2장(크로스헤어 아래, 좌/우): 시간역행 / 시간공명
        _rewindCard = MakeCard(canvasGO.transform, -230f, new Color(0.35f, 0.8f, 1f),
            korean ? "좌클릭" : "LMB",
            korean ? "시간역행" : "TIME REVERSE",
            korean ? $"5초 전의 나로 돌아간다  ·  TF {rewindCost:0}" : $"Return to 5s ago  ·  TF {rewindCost:0}");
        _supportCard = MakeCard(canvasGO.transform, 230f, new Color(1f, 0.65f, 0.3f),
            korean ? $"우클릭 / {supportKey}" : $"RMB / {supportKey}",
            korean ? "협공" : "CO-ATTACK",
            korean ? $"겨누는 대상을 과거의 나와 함께 사격  ·  TF {supportCost:0}"
                   : $"Past self fires at your target  ·  TF {supportCost:0}");

        // 취소 힌트
        var cancel = MakeText(canvasGO.transform, "Cancel", 20, FontStyle.Normal, TextAnchor.MiddleCenter);
        cancel.text = korean ? "T  취소" : "T  CANCEL";
        cancel.color = new Color(1f, 1f, 1f, 0.6f);
        Place(cancel.rectTransform, new Vector2(0.5f, 0.27f), new Vector2(300f, 30f));

        _panel = canvasGO;
        _panel.SetActive(false);
    }

    /// <summary>선택 모드 동안 매 프레임: 제한시간 바 + 타임포스 부족 시 카드 흐리게.</summary>
    private void UpdateDecisionUI()
    {
        if (_panel == null) return;
        if (_timeoutBar != null)
        {
            float remain = Mathf.Clamp01((_decisionEndRealtime - Time.realtimeSinceStartup) / Mathf.Max(0.01f, decisionTimeout));
            var s = _timeoutBar.rectTransform.localScale;
            s.x = remain;
            _timeoutBar.rectTransform.localScale = s;
        }
        if (_rewindCard != null) _rewindCard.alpha = HasForce(rewindCost) ? 1f : 0.35f;
        if (_supportCard != null) _supportCard.alpha = HasForce(supportCost) ? 1f : 0.35f;
    }

    /// <summary>키 배지 + 제목 + 설명이 있는 선택 카드 한 장.</summary>
    private CanvasGroup MakeCard(Transform parent, float offsetX, Color accent, string key, string titleText, string descText)
    {
        var card = MakeImage(parent, "Card", new Color(0f, 0f, 0f, 0.62f));
        var rt = card.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(offsetX, -150f);
        rt.sizeDelta = new Vector2(400f, 130f);

        // 좌측 강조선
        var stripe = MakeImage(card.transform, "Stripe", accent);
        var srt = stripe.rectTransform;
        srt.anchorMin = new Vector2(0f, 0f);
        srt.anchorMax = new Vector2(0f, 1f);
        srt.pivot = new Vector2(0f, 0.5f);
        srt.anchoredPosition = Vector2.zero;
        srt.sizeDelta = new Vector2(6f, 0f);

        // 마우스 키 배지
        var badge = MakeImage(card.transform, "Badge", accent);
        var brt = badge.rectTransform;
        brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0f, 1f);
        brt.anchoredPosition = new Vector2(18f, -14f);
        brt.sizeDelta = new Vector2(92f, 30f);
        var keyText = MakeText(badge.transform, "Key", 17, FontStyle.Bold, TextAnchor.MiddleCenter);
        keyText.text = key;
        keyText.color = Color.black;
        Stretch(keyText.rectTransform);

        // 제목
        var t = MakeText(card.transform, "Title", 26, FontStyle.Bold, TextAnchor.UpperLeft);
        t.text = titleText;
        var trt = t.rectTransform;
        trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0f, 1f);
        trt.anchoredPosition = new Vector2(18f, -52f);
        trt.sizeDelta = new Vector2(370f, 34f);

        // 설명
        var d = MakeText(card.transform, "Desc", 17, FontStyle.Normal, TextAnchor.UpperLeft);
        d.text = descText;
        d.color = new Color(1f, 1f, 1f, 0.75f);
        var drt = d.rectTransform;
        drt.anchorMin = drt.anchorMax = drt.pivot = new Vector2(0f, 1f);
        drt.anchoredPosition = new Vector2(18f, -92f);
        drt.sizeDelta = new Vector2(370f, 26f);

        return card.gameObject.AddComponent<CanvasGroup>();
    }

    private Image MakeImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private Text MakeText(Transform parent, string name, int size, FontStyle style, TextAnchor anchor)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var txt = go.AddComponent<Text>();
        txt.font = _uiFont;
        txt.fontSize = size;
        txt.fontStyle = style;
        txt.alignment = anchor;
        txt.color = Color.white;
        txt.raycastTarget = false;
        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        txt.verticalOverflow = VerticalWrapMode.Overflow;
        return txt;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void Place(RectTransform rt, Vector2 anchor, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
    }
}
