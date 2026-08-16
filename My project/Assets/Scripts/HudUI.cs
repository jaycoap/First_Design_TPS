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

    // 시간 능력 슬롯(T 시간역행 / G 협공)
    private CanvasGroup _rewindChip, _supportChip;
    private Image _rewindFill, _supportFill;
    private Text _rewindState, _supportState;
    private Text _toast;

    private Image _hpFill, _spFill, _tfFill;
    private Text _hpText, _spText, _tfText, _ammoText, _reloadText;
    private GameObject _bossRoot;
    private Image _bossFill;
    private Text _bossText;
    // 게임 오버(사망 → R로 재시작)
    private CanvasGroup _gameOver;
    private bool _restarting;

    private GameObject _judgmentRoot;
    private Image _judgmentFill;
    private Image _judgmentBreakFill;
    private Text _judgmentBreakText;
    private bool _korean;

    private static Sprite _whiteSprite;
    private Font _font;

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
            SetBar(_hpFill, _hpText, _stats.Health, _stats.MaxHealth);
            SetBar(_spFill, _spText, _stats.Stamina, _stats.MaxStamina);
            SetBar(_tfFill, _tfText, _stats.TimeForce, _stats.MaxTimeForce);
        }
        if (_shooter != null)
        {
            _ammoText.text = $"{_shooter.CurrentAmmo} / {_shooter.MagazineSize}";
            _reloadText.enabled = _shooter.IsReloading;
        }

        UpdateAbilities();
        UpdateGameOver();

        // 보스 체력: 살아있는 보스가 씬에 있을 때만 상단 중앙에 표시
        var boss = BossController.Active;
        bool showBoss = boss != null && !boss.IsDead && !boss.IntroPlaying; // 등장 컷신 전엔 숨긴다
        if (_bossRoot != null && _bossRoot.activeSelf != showBoss) _bossRoot.SetActive(showBoss);
        if (showBoss) SetBar(_bossFill, _bossText, boss.Health, boss.MaxHealth);

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

    private static void SetBar(Image fill, Text label, float value, float max)
    {
        if (fill != null) fill.fillAmount = max > 0f ? value / max : 0f;
        if (label != null) label.text = $"{Mathf.CeilToInt(value)} / {Mathf.CeilToInt(max)}";
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

        // 좌하단 스탯 바 3종
        _hpFill = MakeBar(canvas.transform, "HP", 0, new Color(0.85f, 0.25f, 0.25f), out _hpText);
        _spFill = MakeBar(canvas.transform, "SP", 1, new Color(0.35f, 0.8f, 0.35f), out _spText);
        _tfFill = MakeBar(canvas.transform, "TF", 2, new Color(0.35f, 0.7f, 0.95f), out _tfText);

        // 우하단 탄약 표시
        _ammoText = MakeText(canvas.transform, "Ammo", 44, FontStyle.Bold, TextAnchor.LowerRight);
        var ammoRt = _ammoText.rectTransform;
        ammoRt.anchorMin = ammoRt.anchorMax = ammoRt.pivot = new Vector2(1f, 0f);
        ammoRt.anchoredPosition = new Vector2(-40f, 40f);
        ammoRt.sizeDelta = new Vector2(300f, 52f);

        // 재장전 표시(탄약 위)
        _reloadText = MakeText(canvas.transform, "Reloading", 24, FontStyle.Bold, TextAnchor.LowerRight);
        _reloadText.text = "RELOADING...";
        _reloadText.color = new Color(1f, 0.7f, 0.2f);
        var rlRt = _reloadText.rectTransform;
        rlRt.anchorMin = rlRt.anchorMax = rlRt.pivot = new Vector2(1f, 0f);
        rlRt.anchoredPosition = new Vector2(-40f, 96f);
        rlRt.sizeDelta = new Vector2(300f, 30f);
        _reloadText.enabled = false;

        BuildAbilityChips(canvas.transform);
        BuildBossBar(canvas.transform);
        BuildJudgmentWarning(canvas.transform);
        BuildGameOver(canvas.transform);
    }

    /// <summary>
    /// 시간 능력 슬롯 2칸(T 시간역행 / G 협공)과 실패 사유 토스트.
    /// 타임포스 바에는 발동에 필요한 지점을 눈금으로 그어, 언제 쓸 수 있는지 한눈에 보이게 한다.
    /// (이게 없으면 능력이 조건 미달로 조용히 무시될 때 플레이어가 이유를 알 수 없다)
    /// </summary>
    private void BuildAbilityChips(Transform parent)
    {
        if (_shift == null) return; // 시간 능력이 없는 구성이면 슬롯도 만들지 않는다

        const float x = 40f, y = 136f, w = 165f, gap = 10f;
        var rewindColor = new Color(0.35f, 0.8f, 1f);
        var supportColor = new Color(1f, 0.65f, 0.3f);

        _rewindChip = MakeAbilityChip(parent, x, y, w, rewindColor, "T",
            _korean ? "시간역행" : "REVERSE", out _rewindFill, out _rewindState);
        _supportChip = MakeAbilityChip(parent, x + w + gap, y, w, supportColor, "G",
            _korean ? "협공" : "CO-ATK", out _supportFill, out _supportState);

        // 타임포스 바 위의 비용 눈금(둘 다 같은 값이면 하나만 보인다)
        if (_stats != null && _stats.MaxTimeForce > 0f)
        {
            AddCostTick(_tfFill, _shift.RewindCost / _stats.MaxTimeForce, rewindColor);
            AddCostTick(_tfFill, _shift.SupportCost / _stats.MaxTimeForce, supportColor);
        }

        _toast = MakeText(parent, "AbilityToast", 20, FontStyle.Bold, TextAnchor.LowerLeft);
        _toast.color = new Color(1f, 0.75f, 0.3f);
        var trt = _toast.rectTransform;
        trt.anchorMin = trt.anchorMax = trt.pivot = Vector2.zero;
        trt.anchoredPosition = new Vector2(x, y + 36f);
        trt.sizeDelta = new Vector2(520f, 28f);
        _toast.enabled = false;
    }

    /// <summary>능력 슬롯 한 칸: 키 배지 + 이름 + 상태, 배경에 충전/진행 채움.</summary>
    private CanvasGroup MakeAbilityChip(Transform parent, float x, float y, float width, Color accent,
                                        string key, string label, out Image fill, out Text state)
    {
        const float h = 30f;

        var bg = MakeImage(parent, "Ability_" + key, new Color(0f, 0f, 0f, 0.55f));
        var rt = bg.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(width, h);

        // 채움: 평소엔 타임포스 충전 정도, 협공 중에는 남은 시간
        fill = MakeImage(bg.transform, "Fill", new Color(accent.r, accent.g, accent.b, 0.3f));
        var frt = fill.rectTransform;
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(2f, 2f);
        frt.offsetMax = new Vector2(-2f, -2f);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;

        var badge = MakeImage(bg.transform, "Badge", accent);
        var brt = badge.rectTransform;
        brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0f, 0.5f);
        brt.anchoredPosition = new Vector2(6f, 0f);
        brt.sizeDelta = new Vector2(24f, 22f);

        var keyText = MakeText(badge.transform, "Key", 14, FontStyle.Bold, TextAnchor.MiddleCenter);
        keyText.text = key;
        keyText.color = Color.black;
        var krt = keyText.rectTransform;
        krt.anchorMin = Vector2.zero;
        krt.anchorMax = Vector2.one;
        krt.offsetMin = Vector2.zero;
        krt.offsetMax = Vector2.zero;

        var name = MakeText(bg.transform, "Name", 14, FontStyle.Bold, TextAnchor.MiddleLeft);
        name.text = label;
        var nrt = name.rectTransform;
        nrt.anchorMin = Vector2.zero;
        nrt.anchorMax = Vector2.one;
        nrt.offsetMin = new Vector2(36f, 0f);
        nrt.offsetMax = new Vector2(-8f, 0f);

        state = MakeText(bg.transform, "State", 12, FontStyle.Normal, TextAnchor.MiddleRight);
        state.color = new Color(1f, 1f, 1f, 0.85f);
        var srt = state.rectTransform;
        srt.anchorMin = Vector2.zero;
        srt.anchorMax = Vector2.one;
        srt.offsetMin = new Vector2(8f, 0f);
        srt.offsetMax = new Vector2(-8f, 0f);

        return bg.gameObject.AddComponent<CanvasGroup>();
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

        var title = MakeText(dim.transform, "Title", 96, FontStyle.Bold, TextAnchor.MiddleCenter);
        title.text = "GAME OVER";
        title.color = new Color(0.9f, 0.2f, 0.25f);
        PlaceCentered(title.rectTransform, 90f, new Vector2(1200f, 120f));

        var restart = MakeText(dim.transform, "Restart", 46, FontStyle.Bold, TextAnchor.MiddleCenter);
        restart.text = "RESTART?";
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

        var title = MakeText(root.transform, "Title", 34, FontStyle.Bold, TextAnchor.UpperCenter);
        title.text = _korean ? "분신 처형" : "EXECUTION";
        title.color = new Color(1f, 0.42f, 0.08f);
        var trt = title.rectTransform;
        trt.anchorMin = new Vector2(0f, 1f);
        trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.anchoredPosition = Vector2.zero;
        trt.sizeDelta = new Vector2(0f, 40f);

        var desc = MakeText(root.transform, "Desc", 20, FontStyle.Bold, TextAnchor.UpperCenter);
        desc.text = _korean
            ? "충전 색이 다른 '진짜'에게  G(협공) + 사격  을 퍼부어라"
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
        const float width = 900f, height = 20f;

        var bg = MakeImage(parent, "BossBar", new Color(0f, 0f, 0f, 0.55f));
        _bossRoot = bg.gameObject;
        var bgRt = bg.rectTransform;
        bgRt.anchorMin = bgRt.anchorMax = bgRt.pivot = new Vector2(0.5f, 1f);
        bgRt.anchoredPosition = new Vector2(0f, -48f);
        bgRt.sizeDelta = new Vector2(width, height);

        _bossFill = MakeImage(bg.transform, "Fill", new Color(0.75f, 0.3f, 1f));
        var fillRt = _bossFill.rectTransform;
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = new Vector2(2f, 2f);
        fillRt.offsetMax = new Vector2(-2f, -2f);
        _bossFill.type = Image.Type.Filled;
        _bossFill.fillMethod = Image.FillMethod.Horizontal;
        _bossFill.fillOrigin = (int)Image.OriginHorizontal.Left;

        var label = MakeText(bg.transform, "Label", 18, FontStyle.Bold, TextAnchor.LowerLeft);
        label.text = "ALIEN MONSTER";
        var labRt = label.rectTransform;
        labRt.anchorMin = new Vector2(0f, 1f);
        labRt.anchorMax = new Vector2(1f, 1f);
        labRt.pivot = new Vector2(0.5f, 0f);
        labRt.anchoredPosition = new Vector2(0f, 4f);
        labRt.sizeDelta = new Vector2(0f, 24f);

        _bossText = MakeText(bg.transform, "Value", 14, FontStyle.Normal, TextAnchor.MiddleRight);
        var valRt = _bossText.rectTransform;
        valRt.anchorMin = Vector2.zero;
        valRt.anchorMax = Vector2.one;
        valRt.offsetMin = new Vector2(8f, 0f);
        valRt.offsetMax = new Vector2(-8f, 0f);

        _bossRoot.SetActive(false);
    }

    /// <summary>좌하단에 라벨 + 게이지 바 한 줄 생성. 인덱스 순서대로 위에서 아래로 쌓인다.</summary>
    private Image MakeBar(Transform parent, string label, int index, Color color, out Text valueText)
    {
        const float width = 340f, height = 22f, gap = 10f, x = 40f;
        float y = 40f + (2 - index) * (height + gap); // index 0(HP)이 맨 위

        // 배경
        var bg = MakeImage(parent, label + "Bar", new Color(0f, 0f, 0f, 0.5f));
        var bgRt = bg.rectTransform;
        bgRt.anchorMin = bgRt.anchorMax = bgRt.pivot = new Vector2(0f, 0f);
        bgRt.anchoredPosition = new Vector2(x, y);
        bgRt.sizeDelta = new Vector2(width, height);

        // 채움(fillAmount로 좌→우 채움)
        var fill = MakeImage(bg.transform, "Fill", color);
        var fillRt = fill.rectTransform;
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = new Vector2(2f, 2f);
        fillRt.offsetMax = new Vector2(-2f, -2f);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;

        // 라벨(바 왼쪽 안)
        var lab = MakeText(bg.transform, "Label", 15, FontStyle.Bold, TextAnchor.MiddleLeft);
        lab.text = label;
        var labRt = lab.rectTransform;
        labRt.anchorMin = Vector2.zero;
        labRt.anchorMax = Vector2.one;
        labRt.offsetMin = new Vector2(8f, 0f);
        labRt.offsetMax = new Vector2(-8f, 0f);

        // 수치(바 오른쪽 안)
        valueText = MakeText(bg.transform, "Value", 14, FontStyle.Normal, TextAnchor.MiddleRight);
        var valRt = valueText.rectTransform;
        valRt.anchorMin = Vector2.zero;
        valRt.anchorMax = Vector2.one;
        valRt.offsetMin = new Vector2(8f, 0f);
        valRt.offsetMax = new Vector2(-8f, 0f);

        return fill;
    }

    private Image MakeImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = WhiteSprite;
        img.color = color;
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
