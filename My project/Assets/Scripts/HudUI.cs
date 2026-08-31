using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 게임 HUD를 에셋 없이 코드로 생성하는 UI.
/// - 좌하단: 체력(HP) / 기력(SP) / 타임포스(TF) 바
/// - 우하단: 현재 탄약 / 최대 탄약, 재장전 중 표시
/// - 상단 중앙: 보스 체력(보스가 살아있을 때만)
/// PlayerStats와 PlayerShooter를 찾아 매 프레임 값을 반영한다.
/// (한글 글리프가 내장 폰트에 없어 라벨은 영문 약어를 쓴다)
/// </summary>
public class HudUI : MonoBehaviour
{
    private PlayerStats _stats;
    private PlayerShooter _shooter;
    private TimeShiftController _shift;

    // 시간 능력 슬롯(T 크로노 브레이크 / G 타임 어소리티)
    private CanvasGroup _rewindChip, _supportChip;
    private Image _rewindFill, _supportFill;
    private Text _rewindState, _supportState;
    private Text _toast;

    private Bar _hp, _sp, _tf, _boss;
    private Text _ammoText, _reloadText;

    // 빠른 재장전 막대
    private GameObject _reloadBarRoot;
    private RectTransform _reloadZone, _reloadMarker;
    private Image _reloadZoneImg, _reloadMarkerImg, _reloadTipImg, _reloadBarBg, _reloadProgressImg;

    /// <summary>성공 구간 색(푸른색). 성공하면 밝게 터지고, 놓치면 회색으로 죽는다.</summary>
    private static readonly Color ReloadZoneColor = new Color(0.25f, 0.62f, 1f, 0.95f);
    private static readonly Color ReloadZoneHit = new Color(0.6f, 1f, 0.9f, 1f);
    private static readonly Color ReloadZoneMiss = new Color(0.45f, 0.45f, 0.5f, 0.6f);
    private GameObject _bossRoot;
    // 게임 오버(사망 → R로 재시작)
    private CanvasGroup _gameOver;
    private bool _restarting;

    private Image _panelScan;          // 스탯 패널을 훑고 지나가는 밝은 선
    private RectTransform _panelScanRect;

    private GameObject _judgmentRoot;
    private Image _judgmentFill;
    private Image _judgmentBreakFill;
    private Text _judgmentBreakText;
    private bool _korean;

    private static Sprite _whiteSprite;
    private Font _font;

    // ---- 좌하단 스탯 패널 배치(1920x1080 기준) ----
    private const float PanelX = 44f;   // 왼쪽 여백
    private const float BarY = 40f;     // 맨 아래 바(TF)의 바닥 높이
    private const float BarW = 500f;
    private const float BarH = 30f;
    private const float BarGap = 9f;
    private const float ChipY = BarY + 3f * BarH + 2f * BarGap + 14f; // 바 3줄 위
    private const float ChipH = 40f;

    /// <summary>
    /// 게이지 한 줄의 표시 상태.
    /// 값이 뚝 끊겨 보이지 않도록 표시값(Display)을 따로 두고 부드럽게 따라가게 하며,
    /// 줄어든 만큼은 잔상(Trail)이 천천히 뒤따라와 "방금 얼마를 깎였는지"가 눈에 남는다.
    /// </summary>
    private class Bar
    {
        public Image Fill;    // 실제 값
        public Image Trail;   // 뒤늦게 따라오는 잔상(감소분)
        public Image Edge;    // 채움 끝의 밝은 캡
        public Text Value;
        public Color Color;   // 기본 채움색(맥동 시 여기서 흰색 쪽으로 섞는다)
        public float Display, TrailValue;
        public bool Warn;     // 낮을 때 맥동시킬지(체력)
    }

    /// <summary>씬에 HUD가 없으면 자동 생성 — 에디터 툴 실행 여부와 무관하게 항상 표시된다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExists()
    {
        if (FindFirstObjectByType<HudUI>() == null)
            new GameObject("GameHUD").AddComponent<HudUI>();
    }

    private void Start()
    {
        _stats = FindFirstObjectByType<PlayerStats>();
        _shooter = FindFirstObjectByType<PlayerShooter>();
        _shift = FindFirstObjectByType<TimeShiftController>();
        // 경고 문구는 한글로 보여야 해서 OS 폰트를 먼저 시도한다(실패 시 영문 폴백)
        try { _font = Font.CreateDynamicFontFromOSFont("Malgun Gothic", 24); }
        catch { _font = null; }
        _korean = _font != null;
        if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Build();
    }

    private void Update()
    {
        if (_stats != null)
        {
            UpdateBar(_hp, _stats.Health, _stats.MaxHealth);
            UpdateBar(_sp, _stats.Stamina, _stats.MaxStamina);
            UpdateBar(_tf, _stats.TimeForce, _stats.MaxTimeForce);
        }
        if (_shooter != null)
        {
            _ammoText.text = $"{_shooter.CurrentAmmo} / {_shooter.MagazineSize}";
            _reloadText.enabled = _shooter.IsReloading;
        }

        // 패널 훑는 선: 6초에 한 번, 왼쪽에서 오른쪽으로 한 번 지나간다.
        // 주기를 길게 잡아야 '가끔 스캔한다'로 읽힌다 — 빠르면 눈이 계속 그쪽으로 끌린다.
        if (_panelScan != null && _panelScanRect != null)
        {
            float w = _panelScanRect.rect.width;
            float t = Mathf.Repeat(Time.unscaledTime / 6f, 1f);
            _panelScan.rectTransform.anchoredPosition = new Vector2(t * w, 0f);
            // 양 끝에서는 사라진다(판 밖으로 튀어나온 것처럼 보이지 않게)
            _panelScan.color = new Color(0.55f, 0.9f, 1f, 0.14f * Mathf.Sin(t * Mathf.PI));
        }

        UpdateReloadBar();
        UpdateAbilities();
        UpdateGameOver();

        // 보스 체력: 살아있는 보스가 씬에 있을 때만 상단 중앙에 표시
        var boss = BossController.Active;
        bool showBoss = boss != null && !boss.IsDead && !boss.IntroPlaying; // 등장 컷신 전엔 숨긴다
        if (_bossRoot != null && _bossRoot.activeSelf != showBoss) _bossRoot.SetActive(showBoss);
        if (showBoss) UpdateBar(_boss, boss.Health, boss.MaxHealth);

        // 분신 처형 경고: 남은 시간이 줄어드는 동안 진짜를 찾아 협공·사격을 퍼부어야 한다
        bool showJudgment = showBoss && boss.JudgmentActive;
        if (_judgmentRoot != null && _judgmentRoot.activeSelf != showJudgment)
            _judgmentRoot.SetActive(showJudgment);
        if (showJudgment)
        {
            if (_judgmentFill != null)
            {
                var scale = _judgmentFill.rectTransform.localScale;
                scale.x = boss.JudgmentRemain01;
                _judgmentFill.rectTransform.localScale = scale;
            }
            // 파훼 진행도: 진짜에게 명중시킨 횟수(협공 + 일반 사격)
            if (_judgmentBreakFill != null) _judgmentBreakFill.fillAmount = boss.JudgmentBreak01;
            if (_judgmentBreakText != null)
                _judgmentBreakText.text = $"{boss.JudgmentHits} / {boss.JudgmentBreakHits}";
        }
    }

    /// <summary>
    /// 빠른 재장전 막대 갱신 — 화살표를 진행도만큼 밀고, 성공 구간을 이번 재장전의 자리에 놓는다.
    /// 자리는 재장전마다 바뀌므로 매번 다시 계산한다.
    /// </summary>
    private void UpdateReloadBar()
    {
        if (_reloadBarRoot == null) return;

        bool show = _shooter != null && _shooter.ActiveReloadShown;
        if (_reloadBarRoot.activeSelf != show) _reloadBarRoot.SetActive(show);
        if (!show) return;

        float w = _reloadBarBg.rectTransform.rect.width;
        float s = Mathf.Clamp01(_shooter.ActiveReloadStart);
        float e = Mathf.Clamp01(_shooter.ActiveReloadEnd);
        float t = _shooter.ActiveReloadMarker01;   // 화살표(왕복) — 진행도와 다르다

        _reloadZone.anchoredPosition = new Vector2(s * w, 0f);
        _reloadZone.sizeDelta = new Vector2(Mathf.Max(2f, (e - s) * w), 0f);
        _reloadMarker.anchoredPosition = new Vector2(t * w, 0f);

        // 남은 시간은 바탕이 차오르는 것으로 읽는다(화살표는 왕복해서 알 수 없다)
        if (_reloadProgressImg != null)
            _reloadProgressImg.rectTransform.sizeDelta =
                new Vector2(_shooter.ReloadProgress01 * w, 0f);

        // 결과 연출. 성공하면 구간이 밝게 터지고, 놓치면 회색으로 죽어 "끝났다"가 바로 읽힌다.
        int fb = _shooter.ActiveReloadFeedback;
        _reloadZoneImg.color = fb > 0 ? ReloadZoneHit : fb < 0 ? ReloadZoneMiss : ReloadZoneColor;

        // 화살표는 구간 위에 있을 때만 밝다 — "지금 누르면 된다"는 신호.
        // 기회를 이미 썼으면 흐려진다(더 눌러도 소용없다).
        Color marker = !_shooter.ActiveReloadReady ? new Color(1f, 1f, 1f, 0.3f)
                     : (t >= s && t <= e) ? ReloadZoneHit
                     : Color.white;
        _reloadMarkerImg.color = marker;
        if (_reloadTipImg != null) _reloadTipImg.color = marker;
    }

    /// <summary>
    /// 시간 능력 슬롯 갱신. 각 슬롯은 "왜 못 쓰는지"까지 보여 준다 —
    /// 재충전 중인지, 타임포스가 얼마나 모자란지, 협공이 몇 초 남았는지.
    /// </summary>
    private void UpdateAbilities()
    {
        if (_shift == null || _stats == null || _rewindChip == null) return;

        bool ghost = _shift.GhostReady;
        float tf = _stats.TimeForce;

        // T — 시간역행. 쿨다운 중에는 채움이 쿨다운 진행을, 아니면 타임포스 충전을 보여 준다.
        float rewindNeed = _shift.RewindCost;
        float rewindCd = _shift.RewindRemain;
        bool rewindOk = rewindCd <= 0.01f && ghost && tf >= rewindNeed;
        _rewindChip.alpha = rewindOk ? 1f : 0.45f;
        _rewindFill.fillAmount = rewindCd > 0.01f
            ? _shift.RewindCooldown01
            : (rewindNeed > 0f ? Mathf.Clamp01(tf / rewindNeed) : 1f);
        _rewindState.text = rewindCd > 0.01f ? $"{rewindCd:0.0}s"
                          : !ghost ? (_korean ? "재충전" : "RECHARGE")
                          : rewindOk ? (_korean ? "준비" : "READY")
                          : $"TF -{Mathf.CeilToInt(rewindNeed - tf)}";

        // G — 협공(발동 중에는 남은 사격 시간을, 그 뒤에는 쿨다운을 채움으로 보여 준다)
        float supportNeed = _shift.SupportCost;
        float supportCd = _shift.SupportRemain;
        bool supporting = TimeShiftController.SupportActive;
        bool supportOk = supportCd <= 0.01f && ghost && tf >= supportNeed;
        _supportChip.alpha = supporting || supportOk ? 1f : 0.45f;
        _supportFill.fillAmount = supporting ? _shift.SupportRemain01
                                : supportCd > 0.01f ? _shift.SupportCooldown01
                                : (supportNeed > 0f ? Mathf.Clamp01(tf / supportNeed) : 1f);
        _supportState.text = supporting ? (_korean ? "발동 중" : "FIRING")
                           : supportCd > 0.01f ? $"{supportCd:0.0}s"
                           : !ghost ? (_korean ? "재충전" : "RECHARGE")
                           : supportOk ? (_korean ? "준비" : "READY")
                           : $"TF -{Mathf.CeilToInt(supportNeed - tf)}";

        // 발동 실패 사유를 잠깐 띄웠다가 사라지게 한다
        if (_toast == null) return;
        const float toastLife = 1.6f;
        float age = Time.unscaledTime - TimeShiftController.LastDenyTime;
        bool show = !string.IsNullOrEmpty(TimeShiftController.LastDenyReason) && age < toastLife;
        _toast.enabled = show;
        if (show)
        {
            _toast.text = TimeShiftController.LastDenyReason;
            var c = _toast.color;
            c.a = Mathf.Clamp01(toastLife - age); // 마지막 1초 동안 서서히 사라짐
            _toast.color = c;
        }
    }

    /// <summary>
    /// 사망하면 화면을 어둡게 덮고 GAME OVER / RESTART? 를 띄운 뒤 R 입력을 기다린다.
    /// 페이드는 unscaled 시간을 쓴다 — 나중에 사망 연출로 시간을 늦추더라도 UI는 제 속도로 뜬다.
    /// </summary>
    private void UpdateGameOver()
    {
        if (_gameOver == null || _stats == null) return;

        bool dead = _stats.IsDead;
        if (_gameOver.gameObject.activeSelf != dead) _gameOver.gameObject.SetActive(dead);
        if (!dead) return;

        const float fadeTime = 0.9f;
        _gameOver.alpha = Mathf.Clamp01((Time.unscaledTime - _stats.DeathTime) / fadeTime);

        // 커서를 돌려줘야 창 밖으로 뺄 수 있다(재시작하면 카메라가 다시 잠근다)
        if (Cursor.lockState != CursorLockMode.None)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // R — 재시작. 연타로 두 번 로드되지 않게 한 번만 받는다.
        if (!_restarting && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            _restarting = true;
            Restart();
        }
    }

    /// <summary>현재 씬을 다시 불러 처음부터 시작한다.</summary>
    private static void Restart()
    {
        // 씬을 넘어 살아남는 전역 상태를 먼저 정리한다.
        // 이걸 빼먹으면 (예: 시간역행 도중에 죽으면) 재시작한 씬에서 조작이 잠긴 채로 시작한다.
        TimeShiftController.ResetGlobalState();

        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    /// <summary>
    /// 게이지 한 줄 갱신. 시간은 unscaled로 잰다 —
    /// 시간역행처럼 timeScale을 건드리는 연출 중에도 HUD는 제 속도로 움직여야 한다.
    /// </summary>
    private static void UpdateBar(Bar bar, float value, float max)
    {
        if (bar == null || bar.Fill == null) return;

        float target = max > 0f ? Mathf.Clamp01(value / max) : 0f;
        float dt = Time.unscaledDeltaTime;

        // 본 게이지는 차이가 클수록 빠르게 붙고, 잔상은 일정한 속도로 천천히 따라온다
        bar.Display = Mathf.MoveTowards(bar.Display, target,
                                        (0.4f + Mathf.Abs(target - bar.Display) * 6f) * dt);
        if (bar.TrailValue < bar.Display) bar.TrailValue = bar.Display; // 회복은 잔상도 같이 올라간다
        else bar.TrailValue = Mathf.MoveTowards(bar.TrailValue, bar.Display, 0.5f * dt);

        bar.Fill.fillAmount = bar.Display;
        if (bar.Trail != null) bar.Trail.fillAmount = bar.TrailValue;

        // 채움 끝의 밝은 캡 — 게이지가 살아 움직이는 느낌을 준다
        if (bar.Edge != null)
        {
            bool show = bar.Display > 0.005f;
            if (bar.Edge.enabled != show) bar.Edge.enabled = show;
            if (show)
            {
                var ert = bar.Edge.rectTransform;
                ert.anchorMin = new Vector2(bar.Display, 0f);
                ert.anchorMax = new Vector2(bar.Display, 1f);
            }
        }

        // 위험 구간에서는 맥동시켜 시선을 끈다(체력 전용)
        float pulse = bar.Warn && target > 0f && target < 0.25f
            ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 9f)
            : 0f;
        bar.Fill.color = pulse > 0f ? Color.Lerp(bar.Color, Color.white, pulse * 0.5f) : bar.Color;

        if (bar.Value != null) bar.Value.text = $"{Mathf.CeilToInt(value)} / {Mathf.CeilToInt(max)}";
    }

    // ---------- UI 구성 ----------

    private void Build()
    {
        var canvasGO = new GameObject("HudCanvas");
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // 좌하단 스탯 패널: 어두운 판을 먼저 깔아 밝은 배경 위에서도 게이지가 읽히게 한다
        BuildStatBackdrop(canvas.transform);

        // 좌하단 스탯 바 3종
        _hp = MakeBar(canvas.transform, "HP", 0, new Color(0.95f, 0.22f, 0.3f), warn: true);
        _sp = MakeBar(canvas.transform, "SP", 1, new Color(0.35f, 0.9f, 0.45f));
        _tf = MakeBar(canvas.transform, "TF", 2, new Color(0.35f, 0.72f, 1f));

        // 우하단 탄약 판(글자만 떠 있으면 좌하단 패널과 짝이 맞지 않는다)
        var ammoPanel = MakeAngled(canvas.transform, "AmmoPanel", new Color(0.03f, 0.05f, 0.09f, 0.55f));
        var apRt = ammoPanel.rectTransform;
        apRt.anchorMin = apRt.anchorMax = apRt.pivot = new Vector2(1f, 0f);
        apRt.anchoredPosition = new Vector2(-30f, 30f);
        apRt.sizeDelta = new Vector2(316f, 108f);
        AddScanlines(ammoPanel.transform, 0.05f);
        var ammoEdge = MakeAngled(ammoPanel.transform, "Edge", new Color(0.4f, 0.72f, 1f, 0.45f), outline: true);
        StretchFull(ammoEdge.rectTransform, 0f);
        AddCornerBrackets(ammoPanel.transform, new Color(0.5f, 0.85f, 1f, 0.85f), 20f, 3f);

        var ammoTag = MakeText(ammoPanel.transform, "Tag", 13, FontStyle.Bold, TextAnchor.UpperLeft);
        ammoTag.text = Spaced("AMMO");
        ammoTag.color = new Color(0.45f, 0.8f, 1f, 0.75f);
        var atRt = ammoTag.rectTransform;
        atRt.anchorMin = atRt.anchorMax = atRt.pivot = new Vector2(0f, 1f);
        atRt.anchoredPosition = new Vector2(14f, -8f);
        atRt.sizeDelta = new Vector2(160f, 18f);

        // 우하단 탄약 표시
        _ammoText = MakeText(canvas.transform, "Ammo", 60, FontStyle.Bold, TextAnchor.LowerRight);
        var ammoRt = _ammoText.rectTransform;
        ammoRt.anchorMin = ammoRt.anchorMax = ammoRt.pivot = new Vector2(1f, 0f);
        ammoRt.anchoredPosition = new Vector2(-48f, 44f);
        ammoRt.sizeDelta = new Vector2(360f, 70f);

        // 재장전 표시(탄약 위)
        _reloadText = MakeText(canvas.transform, "Reloading", 30, FontStyle.Bold, TextAnchor.LowerRight);
        _reloadText.text = "RELOADING...";
        _reloadText.color = new Color(1f, 0.7f, 0.2f);
        var rlRt = _reloadText.rectTransform;
        rlRt.anchorMin = rlRt.anchorMax = rlRt.pivot = new Vector2(1f, 0f);
        rlRt.anchoredPosition = new Vector2(-48f, 120f);
        rlRt.sizeDelta = new Vector2(360f, 38f);
        _reloadText.enabled = false;

        BuildReloadBar(canvas.transform);
        BuildAbilityChips(canvas.transform);
        BuildBossBar(canvas.transform);
        BuildJudgmentWarning(canvas.transform);
        BuildGameOver(canvas.transform);
    }

    /// <summary>
    /// 스탯 패널 배경. 게이지·능력 슬롯 전체를 감싸는 어두운 판 + 아래쪽 액센트 선.
    /// (밝은 바닥이나 폭발 이펙트 위에서 게이지가 묻히는 것을 막는다)
    /// </summary>
    private void BuildStatBackdrop(Transform parent)
    {
        float top = (_shift != null ? ChipY + ChipH : BarY + 3f * BarH + 2f * BarGap) + 14f;

        var panel = MakeAngled(parent, "StatPanel", new Color(0.03f, 0.05f, 0.09f, 0.62f));
        var rt = panel.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(PanelX - 18f, BarY - 18f);
        rt.sizeDelta = new Vector2(BarW + 36f, top - BarY + 36f);

        AddScanlines(panel.transform, 0.05f);

        // 같은 모양의 테두리를 겹쳐 윤곽을 세운다(판은 반투명이라 경계가 흐리다)
        var edge = MakeAngled(panel.transform, "Edge", new Color(0.35f, 0.7f, 1f, 0.5f), outline: true);
        StretchFull(edge.rectTransform, 0f);

        AddCornerBrackets(panel.transform, new Color(0.5f, 0.85f, 1f, 0.9f), 22f, 3f);

        // 위쪽 액센트 선 + 좌측 태그. 패널에 '머리'가 생겨 정보가 아래로 흐르는 방향이 잡힌다.
        var line = MakeImage(panel.transform, "AccentLine", new Color(0.4f, 0.78f, 1f, 0.5f));
        var lrt = line.rectTransform;
        lrt.anchorMin = new Vector2(0f, 1f);
        lrt.anchorMax = new Vector2(1f, 1f);
        lrt.pivot = new Vector2(0.5f, 1f);
        lrt.anchoredPosition = new Vector2(0f, -2f);
        lrt.sizeDelta = new Vector2(-28f, 1f);

        var tag = MakeText(panel.transform, "Tag", 13, FontStyle.Bold, TextAnchor.UpperLeft);
        tag.text = Spaced("VITALS");
        tag.color = new Color(0.45f, 0.8f, 1f, 0.75f);
        var tgrt = tag.rectTransform;
        tgrt.anchorMin = tgrt.anchorMax = tgrt.pivot = new Vector2(0f, 1f);
        tgrt.anchoredPosition = new Vector2(14f, 16f);
        tgrt.sizeDelta = new Vector2(200f, 18f);

        // 패널을 천천히 훑고 지나가는 밝은 선. 정지 화면에서도 '켜져 있는 장비'로 보이게 한다.
        _panelScan = MakeImage(panel.transform, "ScanSweep", new Color(0.55f, 0.9f, 1f, 0.14f));
        var psrt = _panelScan.rectTransform;
        psrt.anchorMin = new Vector2(0f, 0f);
        psrt.anchorMax = new Vector2(0f, 1f);
        psrt.pivot = new Vector2(0.5f, 0.5f);
        psrt.sizeDelta = new Vector2(46f, 0f);
        _panelScanRect = rt;
    }

    /// <summary>
    /// 시간 능력 슬롯 2칸(T 시간역행 / G 협공)과 실패 사유 토스트.
    /// 타임포스 바에는 발동에 필요한 지점을 눈금으로 그어, 언제 쓸 수 있는지 한눈에 보이게 한다.
    /// (이게 없으면 능력이 조건 미달로 조용히 무시될 때 플레이어가 이유를 알 수 없다)
    /// </summary>
    private void BuildAbilityChips(Transform parent)
    {
        if (_shift == null) return; // 시간 능력이 없는 구성이면 슬롯도 만들지 않는다

        const float gap = 12f;
        const float w = (BarW - gap) * 0.5f;  // 두 칸이 게이지 폭에 정확히 맞아떨어진다
        var rewindColor = new Color(0.35f, 0.8f, 1f);
        var supportColor = new Color(1f, 0.65f, 0.3f);

        _rewindChip = MakeAbilityChip(parent, PanelX, ChipY, w, rewindColor, "T",
            _korean ? "크로노 브레이크" : "CHRONO BREAK", out _rewindFill, out _rewindState);
        _supportChip = MakeAbilityChip(parent, PanelX + w + gap, ChipY, w, supportColor, "G",
            _korean ? "타임 어소리티" : "TIME AUTHORITY", out _supportFill, out _supportState);

        // 타임포스 바 위의 비용 눈금(둘 다 같은 값이면 하나만 보인다)
        if (_stats != null && _stats.MaxTimeForce > 0f && _tf != null)
        {
            AddCostTick(_tf.Fill, _shift.RewindCost / _stats.MaxTimeForce, rewindColor);
            AddCostTick(_tf.Fill, _shift.SupportCost / _stats.MaxTimeForce, supportColor);
        }

        _toast = MakeText(parent, "AbilityToast", 24, FontStyle.Bold, TextAnchor.LowerLeft);
        _toast.color = new Color(1f, 0.75f, 0.3f);
        var trt = _toast.rectTransform;
        trt.anchorMin = trt.anchorMax = trt.pivot = Vector2.zero;
        trt.anchoredPosition = new Vector2(PanelX, ChipY + ChipH + 12f);
        trt.sizeDelta = new Vector2(600f, 32f);
        _toast.enabled = false;
    }

    /// <summary>능력 슬롯 한 칸: 키 배지 + 이름 + 상태, 배경에 충전/진행 채움.</summary>
    private CanvasGroup MakeAbilityChip(Transform parent, float x, float y, float width, Color accent,
                                        string key, string label, out Image fill, out Text state)
    {
        float h = ChipH;

        // 몸통을 슬롯의 뿌리로 삼는다 — CanvasGroup(흐림 처리)이 테두리까지 함께 먹는다
        var bg = MakeAngled(parent, "Ability_" + key, new Color(0.02f, 0.035f, 0.06f, 0.88f),
                            cornerScale: 1.5f);
        var frame = bg;
        var rt = bg.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(width, h);
        ClipToShape(bg);

        // 채움: 평소엔 타임포스 충전 정도, 협공 중에는 남은 시간
        fill = MakeFill(bg.transform, "Fill", new Color(accent.r, accent.g, accent.b, 0.32f));
        AddGloss(bg.transform);

        var edge = MakeAngled(bg.transform, "Edge", new Color(accent.r, accent.g, accent.b, 0.6f),
                              outline: true, cornerScale: 1.5f);
        StretchFull(edge.rectTransform, 0f);

        // 키 배지 — 육각형에 어두운 글자. 눌러야 할 키가 가장 먼저 눈에 들어와야 한다.
        var badge = MakeImage(bg.transform, "Badge", accent);
        badge.sprite = HexSprite;
        var bfrt = badge.rectTransform;
        bfrt.anchorMin = bfrt.anchorMax = bfrt.pivot = new Vector2(0f, 0.5f);
        bfrt.anchoredPosition = new Vector2(7f, 0f);
        bfrt.sizeDelta = new Vector2(34f, 30f);

        var keyText = MakeText(badge.transform, "Key", 18, FontStyle.Bold, TextAnchor.MiddleCenter);
        keyText.text = key;
        keyText.color = new Color(0.05f, 0.06f, 0.1f);
        var krt = keyText.rectTransform;
        krt.anchorMin = Vector2.zero;
        krt.anchorMax = Vector2.one;
        krt.offsetMin = Vector2.zero;
        krt.offsetMax = Vector2.zero;

        // 이름은 배지 오른쪽부터, 상태 글자가 들어갈 자리(오른쪽 72px)까지만 쓴다.
        // 예전에는 이름 영역이 칸 끝까지 뻗어 있어 상태 글자와 겹쳐 읽을 수 없었다.
        var name = MakeText(bg.transform, "Name", 16, FontStyle.Bold, TextAnchor.MiddleLeft);
        name.text = label;
        var nrt = name.rectTransform;
        nrt.anchorMin = Vector2.zero;
        nrt.anchorMax = Vector2.one;
        nrt.offsetMin = new Vector2(46f, 0f);
        nrt.offsetMax = new Vector2(-74f, 0f);

        state = MakeText(bg.transform, "State", 14, FontStyle.Bold, TextAnchor.MiddleRight);
        state.color = Color.Lerp(accent, Color.white, 0.5f);
        var srt = state.rectTransform;
        srt.anchorMin = new Vector2(1f, 0f);
        srt.anchorMax = new Vector2(1f, 1f);
        srt.pivot = new Vector2(1f, 0.5f);
        srt.anchoredPosition = new Vector2(-10f, 0f);
        srt.sizeDelta = new Vector2(66f, 0f);

        return frame.gameObject.AddComponent<CanvasGroup>();
    }

    /// <summary>게이지 바에 "여기부터 쓸 수 있다"는 세로 눈금을 긋는다.</summary>
    private void AddCostTick(Image bar, float fraction, Color color)
    {
        if (bar == null) return;
        var tick = MakeImage(bar.transform.parent, "CostTick", new Color(color.r, color.g, color.b, 0.9f));
        var rt = tick.rectTransform;
        rt.anchorMin = new Vector2(Mathf.Clamp01(fraction), 0f);
        rt.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(2f, -8f);
    }

    /// <summary>
    /// 보스 "분신 처형" 경고. 진짜를 찾아 협공(G)해야 한다는 것과 남은 시간을 알린다.
    /// (이 패턴은 일반 사격으로는 파훼되지 않으므로, 안내가 없으면 사실상 즉사 패턴이 된다)
    /// </summary>
    /// <summary>
    /// 사망 화면. 전체를 어둡게 덮고 GAME OVER / RESTART? / R 안내를 세로로 세운다.
    /// 다른 HUD보다 뒤늦게(맨 마지막에) 만들어야 캔버스 위에 얹혀 전부 가린다.
    /// </summary>
    private void BuildGameOver(Transform parent)
    {
        var dim = MakeImage(parent, "GameOver", new Color(0.03f, 0.01f, 0.02f, 0.78f));
        var rt = dim.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        AddScanlines(dim.transform, 0.05f);

        // 문구를 담는 판. 어둡게 덮기만 하면 글자가 허공에 떠 있어 화면이 비어 보인다.
        var plate = MakeAngled(dim.transform, "Plate", new Color(0.06f, 0.02f, 0.04f, 0.6f));
        PlaceCentered(plate.rectTransform, 10f, new Vector2(880f, 340f));
        var plateEdge = MakeAngled(plate.transform, "Edge", new Color(0.9f, 0.25f, 0.3f, 0.5f), outline: true);
        StretchFull(plateEdge.rectTransform, 0f);
        AddCornerBrackets(plate.transform, new Color(1f, 0.35f, 0.4f, 0.9f), 34f, 4f);

        var title = MakeText(dim.transform, "Title", 90, FontStyle.Bold, TextAnchor.MiddleCenter);
        title.text = Spaced("GAME OVER");
        title.color = new Color(0.9f, 0.2f, 0.25f);
        PlaceCentered(title.rectTransform, 90f, new Vector2(1200f, 120f));

        // 제목 아래 가로줄
        var rule = MakeImage(dim.transform, "Rule", new Color(0.9f, 0.25f, 0.3f, 0.6f));
        PlaceCentered(rule.rectTransform, 34f, new Vector2(560f, 1f));

        var restart = MakeText(dim.transform, "Restart", 46, FontStyle.Bold, TextAnchor.MiddleCenter);
        restart.text = Spaced("RESTART?");
        restart.color = new Color(1f, 1f, 1f, 0.9f);
        PlaceCentered(restart.rectTransform, -20f, new Vector2(800f, 60f));

        var hint = MakeText(dim.transform, "Hint", 30, FontStyle.Normal, TextAnchor.MiddleCenter);
        hint.text = _korean ? "R  키를 눌러 다시 시작" : "PRESS  R  TO RESTART";
        hint.color = new Color(0.75f, 0.9f, 1f, 0.85f);
        PlaceCentered(hint.rectTransform, -90f, new Vector2(800f, 50f));

        _gameOver = dim.gameObject.AddComponent<CanvasGroup>();
        _gameOver.alpha = 0f;
        dim.gameObject.SetActive(false);
    }

    /// <summary>
    /// 빠른 재장전 막대. 크로스헤어 <b>바로 아래</b>에 둔다 —
    /// 성공 구간은 0.2초쯤 지나가므로, 시선을 화면 구석까지 옮겨야 하면 반응할 수가 없다.
    /// (탄약 표시가 있는 우하단은 그래서 후보가 아니다)
    ///
    /// 구조는 그림 그대로다: 테두리 → 안쪽 바탕 → 성공 구간 → 그 위를 지나는 화살표.
    /// </summary>
    private void BuildReloadBar(Transform parent)
    {
        const float W = 264f, H = 16f;

        // 테두리(바깥 검은 판) — 밝은 배경 위에서도 막대의 시작·끝이 읽혀야 한다
        var frame = MakeAngled(parent, "ReloadBar", new Color(0.04f, 0.05f, 0.08f, 0.92f), cornerScale: 3.2f);
        _reloadBarRoot = frame.gameObject;
        PlaceCentered(frame.rectTransform, -118f, new Vector2(W + 4f, H + 4f));

        _reloadBarBg = MakeImage(frame.transform, "Track", new Color(0.13f, 0.15f, 0.2f, 0.95f));
        StretchFull(_reloadBarBg.rectTransform, 2f);
        AddHatch(_reloadBarBg.transform, new Color(0.45f, 0.7f, 1f, 0.13f));

        // 진행도 채움. 화살표가 왕복하게 되면서 화살표 위치로는 "얼마나 남았는지"를
        // 알 수 없게 됐다 — 남은 시간은 이 채움으로 따로 읽는다.
        _reloadProgressImg = MakeImage(_reloadBarBg.transform, "Progress", new Color(1f, 1f, 1f, 0.1f));
        var progRt = _reloadProgressImg.rectTransform;
        progRt.anchorMin = new Vector2(0f, 0f);
        progRt.anchorMax = new Vector2(0f, 1f);
        progRt.pivot = new Vector2(0f, 0.5f);
        progRt.offsetMin = progRt.offsetMax = Vector2.zero;

        // 성공 구간: 위치·폭이 매번 바뀌므로 앵커를 좌우 비율로 잡고 Update에서 옮긴다
        _reloadZoneImg = MakeImage(_reloadBarBg.transform, "Zone", ReloadZoneColor);
        _reloadZone = _reloadZoneImg.rectTransform;
        _reloadZone.anchorMin = new Vector2(0f, 0f);
        _reloadZone.anchorMax = new Vector2(0f, 1f);
        _reloadZone.pivot = new Vector2(0f, 0.5f);
        _reloadZone.offsetMin = Vector2.zero;
        _reloadZone.offsetMax = Vector2.zero;

        // 화살표(진행 표시). 삼각형 스프라이트를 만들지 않고 얇은 세로 막대 + 위쪽 촉으로 대신한다.
        _reloadMarkerImg = MakeImage(_reloadBarBg.transform, "Marker", Color.white);
        _reloadMarker = _reloadMarkerImg.rectTransform;
        _reloadMarker.anchorMin = new Vector2(0f, 0f);
        _reloadMarker.anchorMax = new Vector2(0f, 1f);
        _reloadMarker.pivot = new Vector2(0.5f, 0.5f);
        _reloadMarker.sizeDelta = new Vector2(3f, 6f);   // 막대보다 위아래로 조금 더 길게

        _reloadTipImg = MakeImage(_reloadMarker.transform, "Tip", Color.white);
        _reloadTipImg.sprite = ArrowSprite;
        var tipRt = _reloadTipImg.rectTransform;
        tipRt.anchorMin = tipRt.anchorMax = tipRt.pivot = new Vector2(0.5f, 1f);
        tipRt.anchoredPosition = new Vector2(0f, 11f);
        tipRt.sizeDelta = new Vector2(13f, 10f);

        // 막대 좌우의 꺾쇠 — 재장전 중임을 화면 한가운데에서 잡아 준다
        AddCornerBrackets(frame.transform, new Color(0.5f, 0.85f, 1f, 0.8f), 13f, -4f);

        _reloadBarRoot.SetActive(false);
    }

    /// <summary>부모를 가득 채우되 사방으로 padding만큼 안쪽에 둔다.</summary>
    private static void StretchFull(RectTransform rt, float padding)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padding, padding);
        rt.offsetMax = new Vector2(-padding, -padding);
    }

    /// <summary>화면 중앙 기준으로 y만큼 띄워 배치.</summary>
    private static void PlaceCentered(RectTransform rt, float y, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = size;
    }

    private void BuildJudgmentWarning(Transform parent)
    {
        var root = MakeImage(parent, "JudgmentWarning", new Color(0f, 0f, 0f, 0f));
        _judgmentRoot = root.gameObject;
        var rt = root.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -110f);
        rt.sizeDelta = new Vector2(900f, 140f);

        // 경고판. 글자만 띄우면 다른 HUD와 재질이 달라 겉돌아 보인다.
        var plate = MakeAngled(root.transform, "Plate", new Color(0.09f, 0.03f, 0.01f, 0.62f));
        StretchFull(plate.rectTransform, -8f);
        var plateEdge = MakeAngled(plate.transform, "Edge", new Color(1f, 0.45f, 0.1f, 0.55f), outline: true);
        StretchFull(plateEdge.rectTransform, 0f);
        AddCornerBrackets(plate.transform, new Color(1f, 0.55f, 0.15f, 0.95f), 24f, 3f);

        var title = MakeText(root.transform, "Title", 34, FontStyle.Bold, TextAnchor.UpperCenter);
        title.text = _korean ? "분신 처형" : Spaced("EXECUTION");
        title.color = new Color(1f, 0.42f, 0.08f);
        var trt = title.rectTransform;
        trt.anchorMin = new Vector2(0f, 1f);
        trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.anchoredPosition = Vector2.zero;
        trt.sizeDelta = new Vector2(0f, 40f);

        var desc = MakeText(root.transform, "Desc", 20, FontStyle.Bold, TextAnchor.UpperCenter);
        desc.text = _korean
            ? "충전 색이 다른 '진짜'에게  G(타임 어소리티) + 사격  을 퍼부어라"
            : "Pour fire into the one with a DIFFERENT charge color (G = co-attack)";
        desc.color = new Color(1f, 0.92f, 0.8f);
        var drt = desc.rectTransform;
        drt.anchorMin = new Vector2(0f, 1f);
        drt.anchorMax = new Vector2(1f, 1f);
        drt.pivot = new Vector2(0.5f, 1f);
        drt.anchoredPosition = new Vector2(0f, -44f);
        drt.sizeDelta = new Vector2(0f, 28f);

        // 파훼 진행도 바(명중시킬수록 좌→우로 찬다) — 사격이 통하고 있음을 보여 준다
        var breakBg = MakeImage(root.transform, "BreakBg", new Color(0f, 0f, 0f, 0.55f));
        var kbrt = breakBg.rectTransform;
        kbrt.anchorMin = kbrt.anchorMax = kbrt.pivot = new Vector2(0.5f, 0f);
        kbrt.anchoredPosition = new Vector2(0f, 20f);
        kbrt.sizeDelta = new Vector2(520f, 20f);

        _judgmentBreakFill = MakeImage(breakBg.transform, "Fill", new Color(0.45f, 0.9f, 1f, 0.95f));
        var kfrt = _judgmentBreakFill.rectTransform;
        kfrt.anchorMin = Vector2.zero;
        kfrt.anchorMax = Vector2.one;
        kfrt.offsetMin = new Vector2(2f, 2f);
        kfrt.offsetMax = new Vector2(-2f, -2f);
        _judgmentBreakFill.type = Image.Type.Filled;
        _judgmentBreakFill.fillMethod = Image.FillMethod.Horizontal;
        _judgmentBreakFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        _judgmentBreakFill.fillAmount = 0f;

        var breakLabel = MakeText(breakBg.transform, "Label", 14, FontStyle.Bold, TextAnchor.MiddleLeft);
        breakLabel.text = _korean ? "파훼" : "BREAK";
        var klrt = breakLabel.rectTransform;
        klrt.anchorMin = Vector2.zero;
        klrt.anchorMax = Vector2.one;
        klrt.offsetMin = new Vector2(8f, 0f);
        klrt.offsetMax = new Vector2(-8f, 0f);

        _judgmentBreakText = MakeText(breakBg.transform, "Value", 14, FontStyle.Bold, TextAnchor.MiddleRight);
        var kvrt = _judgmentBreakText.rectTransform;
        kvrt.anchorMin = Vector2.zero;
        kvrt.anchorMax = Vector2.one;
        kvrt.offsetMin = new Vector2(8f, 0f);
        kvrt.offsetMax = new Vector2(-8f, 0f);

        // 남은 시간 바(좌→우로 줄어든다)
        var barBg = MakeImage(root.transform, "TimeBg", new Color(0f, 0f, 0f, 0.55f));
        var brt = barBg.rectTransform;
        brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0f);
        brt.anchoredPosition = new Vector2(0f, 4f);
        brt.sizeDelta = new Vector2(520f, 10f);

        _judgmentFill = MakeImage(barBg.transform, "Fill", new Color(1f, 0.42f, 0.08f, 0.95f));
        var frt = _judgmentFill.rectTransform;
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(2f, 2f);
        frt.offsetMax = new Vector2(-2f, -2f);
        frt.pivot = new Vector2(0f, 0.5f); // 좌측 기준으로 줄어듦(scale.x)

        _judgmentRoot.SetActive(false);
    }

    /// <summary>상단 중앙 보스 체력 바(보스가 없으면 숨긴다).</summary>
    private void BuildBossBar(Transform parent)
    {
        const float width = 1040f, height = 28f;
        var color = new Color(0.8f, 0.28f, 1f);
        _boss = new Bar { Color = color };

        // 투명한 껍데기를 뿌리로 둔다(보스가 사라지면 통째로 숨긴다).
        // 몸통에 마스크를 씌우기 때문에, 막대 <b>바깥</b>에 놓이는 이름표는
        // 몸통이 아니라 이 껍데기에 달아야 잘리지 않는다.
        var root = MakeImage(parent, "BossBar", new Color(0f, 0f, 0f, 0f));
        _bossRoot = root.gameObject;
        var rootRt = root.rectTransform;
        rootRt.anchorMin = rootRt.anchorMax = rootRt.pivot = new Vector2(0.5f, 1f);
        rootRt.anchoredPosition = new Vector2(0f, -46f);
        rootRt.sizeDelta = new Vector2(width, height);

        var bg = MakeAngled(root.transform, "Body", new Color(0.04f, 0.015f, 0.06f, 0.88f), cornerScale: 1.7f);
        StretchFull(bg.rectTransform, 0f);
        ClipToShape(bg);

        AddHatch(bg.transform, new Color(color.r, color.g, color.b, 0.1f));

        _boss.Trail = MakeFill(bg.transform, "Trail", new Color(1f, 0.85f, 0.95f, 0.5f));
        _boss.Fill = MakeFill(bg.transform, "Fill", color);

        _boss.Edge = MakeImage(_boss.Fill.transform, "Edge", Color.Lerp(color, Color.white, 0.85f));
        var edRt = _boss.Edge.rectTransform;
        edRt.anchorMin = new Vector2(0f, 0f);
        edRt.anchorMax = new Vector2(0f, 1f);
        edRt.pivot = new Vector2(0.5f, 0.5f);
        edRt.anchoredPosition = Vector2.zero;
        edRt.sizeDelta = new Vector2(6f, 0f);

        AddSegments(bg.transform, 20); // 보스는 칸을 촘촘히 — 한 칸이 깎이는 게 보인다
        AddGloss(bg.transform);

        var edge = MakeAngled(bg.transform, "Edge", new Color(color.r, color.g, color.b, 0.6f),
                              outline: true, cornerScale: 1.7f);
        StretchFull(edge.rectTransform, 0f);
        // 여기에는 모서리 꺾쇠를 달지 않는다 — 판 바깥으로 나가는 장식이라 마스크에 잘린다.
        // 대신 이름 좌우의 가는 선(아래 Wing)이 같은 역할을 한다.

        // 이름표는 막대 위로 나가므로 껍데기(root)에 단다 — 몸통에 달면 마스크에 잘린다
        var label = MakeText(root.transform, "Label", 24, FontStyle.Bold, TextAnchor.LowerCenter);
        label.text = Spaced("ALIEN MONSTER");
        label.color = new Color(1f, 0.9f, 1f);
        var labRt = label.rectTransform;
        labRt.anchorMin = new Vector2(0f, 1f);
        labRt.anchorMax = new Vector2(1f, 1f);
        labRt.pivot = new Vector2(0.5f, 0f);
        labRt.anchoredPosition = new Vector2(0f, 8f);
        labRt.sizeDelta = new Vector2(0f, 32f);

        // 이름 좌우의 가는 선 — 이름을 가운데로 붙잡아 준다
        for (int i = 0; i < 2; i++)
        {
            var wing = MakeImage(label.transform, "Wing" + i, new Color(color.r, color.g, color.b, 0.7f));
            var wrt = wing.rectTransform;
            float side = i == 0 ? 0f : 1f;
            wrt.anchorMin = wrt.anchorMax = wrt.pivot = new Vector2(side, 0.4f);
            wrt.anchoredPosition = new Vector2(i == 0 ? 24f : -24f, 0f);
            wrt.sizeDelta = new Vector2(150f, 1f);
        }

        _boss.Value = MakeText(bg.transform, "Value", 16, FontStyle.Bold, TextAnchor.MiddleRight);
        StretchInside(_boss.Value.rectTransform, 16f);

        _bossRoot.SetActive(false);
    }

    /// <summary>
    /// 좌하단 게이지 한 줄 생성(인덱스 0=HP가 맨 위).
    /// 구성: 액센트 테두리 → 어두운 홈 → 감소 잔상 → 채움(+끝 캡) → 눈금 → 유리 광택 → 글자.
    /// </summary>
    private Bar MakeBar(Transform parent, string label, int index, Color color, bool warn = false)
    {
        float y = BarY + (2 - index) * (BarH + BarGap);
        var bar = new Bar { Color = color, Warn = warn };

        // 홈(배경). 모서리를 깎아 두면 바 하나하나가 '슬롯에 끼운 부품'처럼 보인다.
        var bg = MakeAngled(parent, label + "Bar", new Color(0.02f, 0.035f, 0.06f, 0.88f), cornerScale: 1.9f);
        var bgRt = bg.rectTransform;
        bgRt.anchorMin = bgRt.anchorMax = bgRt.pivot = Vector2.zero;
        bgRt.anchoredPosition = new Vector2(PanelX, y);
        bgRt.sizeDelta = new Vector2(BarW, BarH);
        ClipToShape(bg);   // 채움·빗금이 깎인 모서리로 삐져나오지 않게

        // 빈 구간의 빗금 — 채움이 없는 자리가 '비었다'로 읽힌다(검은 홈은 그냥 어둡기만 하다)
        AddHatch(bg.transform, new Color(color.r, color.g, color.b, 0.09f));

        // 같은 모양의 테두리
        var frame = MakeAngled(bg.transform, "Frame", new Color(color.r, color.g, color.b, 0.5f),
                               outline: true, cornerScale: 1.9f);
        StretchFull(frame.rectTransform, 0f);

        // 감소 잔상 → 본 채움 순서로 겹친다(잔상이 뒤에 남아 깎인 폭이 보인다)
        bar.Trail = MakeFill(bg.transform, "Trail", Color.Lerp(color, Color.white, 0.75f) * new Color(1f, 1f, 1f, 0.5f));
        bar.Fill = MakeFill(bg.transform, "Fill", color);

        // 채움 끝의 밝은 캡(UpdateBar가 매 프레임 위치를 옮긴다)
        bar.Edge = MakeImage(bar.Fill.transform, "Edge", Color.Lerp(color, Color.white, 0.85f));
        var edRt = bar.Edge.rectTransform;
        edRt.anchorMin = new Vector2(0f, 0f);
        edRt.anchorMax = new Vector2(0f, 1f);
        edRt.pivot = new Vector2(0.5f, 0.5f);
        edRt.anchoredPosition = Vector2.zero;
        edRt.sizeDelta = new Vector2(5f, 0f);

        AddSegments(bg.transform, 10);
        AddGloss(bg.transform);

        // 라벨(바 왼쪽 안) — 게이지 색을 옅게 입혀 어느 자원인지 색으로도 읽히게 한다.
        // 자간을 벌리면 계기판 각인처럼 읽힌다(레거시 Text에 자간이 없어 공백으로 흉내낸다).
        var lab = MakeText(bg.transform, "Label", 18, FontStyle.Bold, TextAnchor.MiddleLeft);
        lab.text = Spaced(label);
        lab.color = Color.Lerp(color, Color.white, 0.65f);
        StretchInside(lab.rectTransform, 14f);

        // 라벨 뒤의 짧은 세로 막대 — 글자에 '핀'을 꽂아 왼쪽 줄을 맞춘다
        var pin = MakeImage(bg.transform, "Pin", new Color(color.r, color.g, color.b, 0.85f));
        var pinRt = pin.rectTransform;
        pinRt.anchorMin = new Vector2(0f, 0.5f);
        pinRt.anchorMax = new Vector2(0f, 0.5f);
        pinRt.pivot = new Vector2(0f, 0.5f);
        pinRt.anchoredPosition = new Vector2(7f, 0f);
        pinRt.sizeDelta = new Vector2(3f, BarH - 12f);

        // 수치(바 오른쪽 안)
        bar.Value = MakeText(bg.transform, "Value", 17, FontStyle.Bold, TextAnchor.MiddleRight);
        StretchInside(bar.Value.rectTransform, 14f);

        // 테두리는 맨 위로 — 채움 위에 윤곽이 남아야 홈의 경계가 유지된다
        frame.transform.SetAsLastSibling();
        return bar;
    }

    /// <summary>좌→우로 차는 채움 이미지(바 안쪽에 2px 물려 넣는다).</summary>
    private Image MakeFill(Transform parent, string name, Color color)
    {
        var img = MakeImage(parent, name, color);
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(2f, 2f);
        rt.offsetMax = new Vector2(-2f, -2f);
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = (int)Image.OriginHorizontal.Left;
        img.fillAmount = 0f;
        return img;
    }

    /// <summary>게이지를 세로 눈금으로 잘라 계기판처럼 보이게 한다.</summary>
    private void AddSegments(Transform bar, int count)
    {
        for (int i = 1; i < count; i++)
        {
            var seg = MakeImage(bar, "Seg", new Color(0f, 0f, 0f, 0.35f));
            var rt = seg.rectTransform;
            float f = i / (float)count;
            rt.anchorMin = new Vector2(f, 0f);
            rt.anchorMax = new Vector2(f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(2f, -4f);
        }
    }

    /// <summary>바 위쪽 절반에 옅은 흰빛을 덮어 유리처럼 반들거리게 한다.</summary>
    private void AddGloss(Transform bar)
    {
        var gloss = MakeImage(bar, "Gloss", new Color(1f, 1f, 1f, 0.09f));
        var rt = gloss.rectTransform;
        rt.anchorMin = new Vector2(0f, 0.52f);
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(2f, 0f);
        rt.offsetMax = new Vector2(-2f, -2f);
    }

    /// <summary>부모 전체를 채우되 좌우로 padding만큼 물린다.</summary>
    private static void StretchInside(RectTransform rt, float padding)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padding, 0f);
        rt.offsetMax = new Vector2(-padding, 0f);
    }

    private Image MakeImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = WhiteSprite;
        img.color = color;
        img.raycastTarget = false;   // HUD는 클릭 대상이 아니다 — 마우스 판정에서 통째로 뺀다
        return img;
    }

    private Text MakeText(Transform parent, string name, int size, FontStyle style, TextAnchor anchor)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var txt = go.AddComponent<Text>();
        txt.font = _font;
        txt.fontSize = size;
        txt.fontStyle = style;
        txt.alignment = anchor;
        txt.color = Color.white;
        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        txt.verticalOverflow = VerticalWrapMode.Overflow;
        return txt;
    }

    // ================= SF 부품 =================
    //
    // 에셋을 쓰지 않는 HUD라 필요한 모양은 전부 코드로 굽는다.
    // "사각형만 있는 UI"가 심심해 보이는 가장 큰 이유는 <b>모서리가 직각</b>이기 때문이다.
    // 모서리를 깎은 판(chamfer) 하나만 넣어도 화면이 통째로 장비 패널처럼 읽힌다.
    //
    // 전부 static 캐시 — 씬을 다시 열어도 한 번만 굽는다.

    private static Sprite _angledFill, _angledFrame, _hexFill, _hatch, _scanline, _bracket;

    private const int AngN = 48;       // 텍스처 한 변
    private const int AngCut = 14;     // 깎아내는 모서리 크기(px)
    private const int AngEdge = 3;     // 테두리 두께(px)
    private const float AngBorder = 16f; // 9슬라이스 경계 — 모서리 크기보다 커야 컷이 안 늘어난다

    // ※ 캐시 확인에 ??= 를 쓰면 안 된다. ??= 는 진짜 null만 보는데, Unity 오브젝트는
    //    파괴된 뒤에도 참조가 진짜 null이 아니라 '가짜 null'로 남는다 — 그러면 파괴된
    //    스프라이트를 영영 돌려주게 된다. 반드시 Unity의 != 연산자로 확인해야 한다.

    /// <summary>모서리를 깎은 판(속을 채운 것).</summary>
    private static Sprite AngledFillSprite
    {
        get
        {
            if (_angledFill == null)
                _angledFill = Bake(AngN, (x, y) => InChamfer(x, y, AngN, AngCut), AngBorder);
            return _angledFill;
        }
    }

    /// <summary>모서리를 깎은 테두리(속은 비어 있다).</summary>
    private static Sprite AngledFrameSprite
    {
        get
        {
            if (_angledFrame == null)
                _angledFrame = Bake(AngN,
                    (x, y) => InChamfer(x, y, AngN, AngCut)
                           && !InChamfer(x - AngEdge, y - AngEdge, AngN - AngEdge * 2, AngCut - AngEdge),
                    AngBorder);
            return _angledFrame;
        }
    }

    /// <summary>육각형(좌우가 뾰족). 키 배지처럼 작은 조각에 쓴다.</summary>
    private static Sprite HexSprite
    {
        get
        {
            if (_hexFill == null)
                _hexFill = Bake(48, (x, y) =>
                {
                    float u = x / 24f - 1f, v = y / 24f - 1f;   // -1 ~ 1
                    return Mathf.Abs(v) <= 1f && Mathf.Abs(u) <= 1f - 0.5f * Mathf.Abs(v);
                }, 0f);
            return _hexFill;
        }
    }

    /// <summary>사선 빗금(타일). 게이지의 빈 홈에 깔아 '비어 있음'을 질감으로 보여 준다.</summary>
    private static Sprite HatchSprite
    {
        get
        {
            if (_hatch == null) _hatch = Bake(16, (x, y) => Mathf.Repeat(x + y, 8f) < 2.2f, 0f, tile: true);
            return _hatch;
        }
    }

    /// <summary>주사선(타일). 패널 위에 아주 옅게 깔면 화면이 '장비 디스플레이'로 읽힌다.</summary>
    private static Sprite ScanlineSprite
    {
        get
        {
            if (_scanline == null) _scanline = Bake(8, (x, y) => Mathf.Repeat(y, 4f) < 1.1f, 0f, tile: true);
            return _scanline;
        }
    }

    /// <summary>모서리 꺾쇠(ㄴ자). 좌하단 기준으로 굽고, 뒤집어서 네 귀퉁이에 붙인다.</summary>
    private static Sprite BracketSprite
    {
        get
        {
            if (_bracket == null)
                _bracket = Bake(32, (x, y) => (x < 4f && y < 22f) || (y < 4f && x < 22f), 0f);
            return _bracket;
        }
    }

    /// <summary>
    /// 흰색 텍스처를 구워 스프라이트로 만든다.
    /// 픽셀마다 3×3으로 훑어 경계를 부드럽게 한다 — 대각선을 쓰는 모양이라
    /// 계단이 그대로 남으면 저해상도 이미지를 붙여 놓은 것처럼 보인다.
    /// </summary>
    private static Sprite Bake(int n, System.Func<float, float, bool> inside, float border, bool tile = false)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
        tex.wrapMode = tile ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int hit = 0;
                for (int sy = 0; sy < 3; sy++)
                    for (int sx = 0; sx < 3; sx++)
                        if (inside(x + (sx + 0.5f) / 3f, y + (sy + 0.5f) / 3f)) hit++;
                px[y * n + x] = new Color32(255, 255, 255, (byte)(hit * 255 / 9));
            }
        tex.SetPixels32(px);
        tex.Apply();

        var rect = new Rect(0f, 0f, n, n);
        var pivot = new Vector2(0.5f, 0.5f);
        Sprite sp = border > 0f
            ? Sprite.Create(tex, rect, pivot, 100f, 0, SpriteMeshType.FullRect,
                            new Vector4(border, border, border, border))
            : Sprite.Create(tex, rect, pivot, 100f, 0, SpriteMeshType.FullRect);
        sp.hideFlags = HideFlags.DontSave;
        return sp;
    }

    /// <summary>네 모서리를 45도로 깎은 사각형 안인가.</summary>
    private static bool InChamfer(float x, float y, float n, float cut)
    {
        float rx = n - x, ry = n - y;
        if (x < 0f || y < 0f || rx < 0f || ry < 0f) return false;
        return x + y >= cut && rx + y >= cut && x + ry >= cut && rx + ry >= cut;
    }

    /// <summary>모서리를 깎은 SF 판. outline이면 테두리만 남는다.</summary>
    /// <param name="cornerScale">
    /// 1보다 크면 깎인 모서리가 그만큼 작아진다. 작은 부품(재장전 막대 등)에서
    /// 컷이 상대적으로 너무 커 보이는 것을 막는다.
    /// </param>
    private Image MakeAngled(Transform parent, string name, Color color,
                             bool outline = false, float cornerScale = 1f)
    {
        var img = MakeImage(parent, name, color);
        img.sprite = outline ? AngledFrameSprite : AngledFillSprite;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = Mathf.Max(0.1f, cornerScale);
        return img;
    }

    /// <summary>네 귀퉁이에 꺾쇠를 붙인다 — 조준 프레임 같은 인상을 준다.</summary>
    private void AddCornerBrackets(Transform parent, Color color, float size, float inset = 0f)
    {
        // 0=좌하 1=우하 2=우상 3=좌상.
        // 회전이 아니라 <b>스케일 뒤집기</b>로 방향을 맞춘다 — 앵커를 귀퉁이에 두고 회전시키면
        // 사각형이 판 밖으로 돌아 나가 버린다. 뒤집기는 자리를 그대로 두고 모양만 거울처럼 바꾼다.
        var corners = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        for (int i = 0; i < 4; i++)
        {
            var img = MakeImage(parent, "Bracket" + i, color);
            img.sprite = BracketSprite;

            var a = corners[i];
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = a;      // 피벗이 귀퉁이 → 사각형이 안쪽으로 뻗는다
            rt.anchoredPosition = new Vector2((a.x < 0.5f ? 1f : -1f) * inset,
                                              (a.y < 0.5f ? 1f : -1f) * inset);
            rt.sizeDelta = new Vector2(size, size);
            rt.localScale = new Vector3(a.x < 0.5f ? 1f : -1f, a.y < 0.5f ? 1f : -1f, 1f);
        }
    }

    /// <summary>
    /// 자식들을 이 판의 <b>모양대로</b> 잘라 낸다.
    ///
    /// 모서리를 깎아 놓아도 자식(채움·빗금·광택)은 여전히 직각 사각형이라, 깎아낸 자리로
    /// 삐져나와 모서리가 도로 각져 보인다. 스텐실 마스크로 판의 알파 밖을 잘라내야
    /// 채움이 끝까지 차도 윤곽이 유지된다.
    ///
    /// 주의: 판 <b>바깥</b>에 붙이는 장식(모서리 꺾쇠 등)도 함께 잘리므로,
    /// 마스크를 씌운 판에는 바깥 장식을 달지 않는다.
    /// </summary>
    private static void ClipToShape(Image panel)
    {
        var mask = panel.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = true;   // 판 자체는 계속 보여야 한다(모양을 잡는 배경이므로)
    }

    /// <summary>주사선을 아주 옅게 깐다(장비 디스플레이 질감).</summary>
    private void AddScanlines(Transform parent, float alpha)
    {
        var img = MakeImage(parent, "Scanlines", new Color(0.55f, 0.85f, 1f, alpha));
        img.sprite = ScanlineSprite;
        img.type = Image.Type.Tiled;
        // 깎인 모서리 안쪽으로 물려 넣는다 — 판에 마스크가 없어도 귀퉁이로 새지 않게
        StretchFull(img.rectTransform, 7f);
    }

    /// <summary>게이지 홈에 사선 빗금을 깐다 — 빈 구간이 '비었다'로 읽힌다.</summary>
    private void AddHatch(Transform parent, Color color)
    {
        var img = MakeImage(parent, "Hatch", color);
        img.sprite = HatchSprite;
        img.type = Image.Type.Tiled;
        StretchFull(img.rectTransform, 1f);
    }

    /// <summary>글자 사이를 벌린다(레거시 Text에는 자간이 없어 공백으로 흉내낸다).</summary>
    private static string Spaced(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new System.Text.StringBuilder(s.Length * 2);
        foreach (char c in s) { sb.Append(c); sb.Append(' '); }
        return sb.ToString(0, sb.Length - 1);
    }

    /// <summary>
    /// 아래를 가리키는 삼각형 스프라이트(빠른 재장전 화살표). 흰 사각형 위에 그린다.
    /// 텍스처 y=0이 아래쪽이므로, 아래로 갈수록 좁아지게 채우면 아래를 가리키는 촉이 된다.
    /// </summary>
    private static Sprite _arrowSprite;
    private static Sprite ArrowSprite
    {
        get
        {
            if (_arrowSprite != null) return _arrowSprite;

            const int N = 32;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            {
                float half = (y + 0.5f) * 0.5f;   // 위(y=N)에서 폭이 최대, 아래(y=0)에서 0
                for (int x = 0; x < N; x++)
                {
                    bool inside = Mathf.Abs(x + 0.5f - N * 0.5f) <= half;
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(inside ? 255 : 0));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();

            _arrowSprite = Sprite.Create(tex, new Rect(0f, 0f, N, N), new Vector2(0.5f, 0.5f), 100f);
            _arrowSprite.hideFlags = HideFlags.DontSave;
            return _arrowSprite;
        }
    }

    /// <summary>Image.Filled에 쓸 흰색 스프라이트(코드 생성).</summary>
    private static Sprite WhiteSprite
    {
        get
        {
            if (_whiteSprite != null) return _whiteSprite;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
            var px = new Color[16];
            for (int i = 0; i < px.Length; i++) px[i] = Color.white;
            tex.SetPixels(px);
            tex.Apply();
            _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            return _whiteSprite;
        }
    }
}
