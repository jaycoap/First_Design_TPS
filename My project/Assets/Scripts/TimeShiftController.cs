using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 시간 능력 컨트롤러(타임포스 소모).
/// - T: 슬로우 모션 + 선택 모드 진입(고스트가 준비되고 타임포스가 충분할 때)
///   - 좌클릭: 5초 전 위치로 되감기(텔레포트). 히스토리 초기화 → 고스트가 다시 차오를 때까지 자연 쿨다운
///   - 우클릭: 5초 전 고스트가 그 자리에 고정된 채 플레이어의 조준점을 향해 지원 사격
///   - T 재입력 / 시간 초과: 취소(타임포스 소모 없음)
/// 선택 모드 동안 일반 발사/조준 입력은 잠긴다(PlayerShooter/ThirdPersonCamera가 DecisionActive 확인).
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
    [SerializeField] private float supportDuration = 3f;
    [SerializeField] private float supportFireRate = 8f;
    [SerializeField] private float supportDamage = 10f;
    [SerializeField] private float supportRange = 200f;

    /// <summary>선택 모드 중인가(전역). PlayerShooter/카메라가 참조해 입력 충돌을 막는다.</summary>
    public static bool DecisionActive { get; private set; }

    /// <summary>시간역행 역재생 중인가(전역). 재생 동안 조준 등 입력을 잠근다.</summary>
    public static bool RewindActive { get; private set; }

    private PlayerTimeGhost _ghost;
    private PlayerController _pc;
    private PlayerStats _stats;
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
        _ghost = GetComponent<PlayerTimeGhost>();
        _pc = GetComponent<PlayerController>();
        _stats = GetComponent<PlayerStats>();
        _cc = GetComponent<CharacterController>();
        _cam = Camera.main;
        _tpsCam = _cam != null ? _cam.GetComponent<ThirdPersonCamera>() : null;
        _mask = ~(1 << gameObject.layer); // 자기(플레이어/고스트) 레이어 제외
        if (_cc != null) _fxScale = _cc.height / 1.8f;

        _tracer = CreateGhostTracer();
        _ghostMuzzleFx = GunFx.BuildMuzzleFlash(transform, _fxScale);
        _ghostImpactFx = GunFx.BuildImpact(_fxScale);
        BuildDecisionUI();
    }

    private void OnDestroy()
    {
        if (_ghostImpactFx != null && _ghostImpactFx.Root != null) Destroy(_ghostImpactFx.Root);
    }

    private void OnDisable()
    {
        if (DecisionActive) EndDecision();
    }

    private void Update()
    {
        if (_tracer != null && _tracer.enabled && Time.unscaledTime >= _tracerHide)
            _tracer.enabled = false;

        if (Keyboard.current == null || Mouse.current == null) return;

        if (!DecisionActive)
        {
            if (Keyboard.current.tKey.wasPressedThisFrame && _support == null && !RewindActive
                && _ghost.GhostReady && HasForce(Mathf.Min(rewindCost, supportCost)))
                BeginDecision();
            return;
        }

        // --- 선택 모드 ---
        UpdateDecisionUI();
        if (Mouse.current.leftButton.wasPressedThisFrame && TryUseForce(rewindCost)) { DoRewind(); return; }
        if (Mouse.current.rightButton.wasPressedThisFrame && TryUseForce(supportCost)) { DoSupportFire(); return; }
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

    /// <summary>우클릭: 고스트를 고정하고 플레이어 조준점을 향해 일정 시간 지원 사격.</summary>
    private void DoSupportFire()
    {
        EndDecision();
        if (_support == null) _support = StartCoroutine(SupportFireRoutine());
    }

    private IEnumerator SupportFireRoutine()
    {
        _ghost.SetFrozen(true);
        float end = Time.time + supportDuration;
        var wait = new WaitForSeconds(1f / Mathf.Max(1f, supportFireRate));
        while (Time.time < end)
        {
            FireFromGhost();
            yield return wait;
        }
        _ghost.SetFrozen(false);
        _support = null;
    }

    private void FireFromGhost()
    {
        Transform gun = _ghost.GhostGun;
        if (gun == null || _cam == null) return;

        // 목표 = 플레이어의 크로스헤어(화면 중앙) 지점
        Ray center = _cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 aim = Physics.Raycast(center, out RaycastHit ch, supportRange, _mask, QueryTriggerInteraction.Ignore)
            ? ch.point
            : center.origin + center.direction * supportRange;

        Vector3 origin = gun.position;
        Vector3 dir = (aim - origin).normalized;
        Vector3 target = origin + dir * supportRange;
        if (Physics.Raycast(origin, dir, out RaycastHit hit, supportRange, _mask, QueryTriggerInteraction.Ignore))
        {
            target = hit.point;
            hit.collider.GetComponentInParent<IDamageable>()?.TakeDamage(supportDamage, hit.point, hit.normal);
            _ghostImpactFx.Spawn(hit.point, hit.normal);
        }

        _ghostMuzzleFx.Fire(origin, dir);
        _tracer.enabled = true;
        _tracer.positionCount = 2;
        _tracer.SetPosition(0, origin);
        _tracer.SetPosition(1, target);
        _tracerHide = Time.unscaledTime + 0.04f;
    }

    /// <summary>고스트 지원 사격용 탄도선(시안 빛, 캐릭터 스케일 반영).</summary>
    private LineRenderer CreateGhostTracer()
    {
        var go = new GameObject("GhostTracerFX");
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = GunFx.MakeTracerMaterial();
        lr.startColor = new Color(0.5f, 0.9f, 1f, 0.9f);
        lr.endColor = new Color(0.3f, 0.7f, 1f, 0.25f);
        lr.startWidth = 0.05f * _fxScale;
        lr.endWidth = 0.02f * _fxScale;
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
            korean ? "우클릭" : "RMB",
            korean ? "시간공명" : "TIME RESONANCE",
            korean ? $"과거의 나와 공명해 함께 사격  ·  TF {supportCost:0}" : $"Past self fires with you  ·  TF {supportCost:0}");

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
