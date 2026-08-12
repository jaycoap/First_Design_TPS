using UnityEngine;
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

    private Image _hpFill, _spFill, _tfFill;
    private Text _hpText, _spText, _tfText, _ammoText, _reloadText;
    private GameObject _bossRoot;
    private Image _bossFill;
    private Text _bossText;
    private GameObject _judgmentRoot;
    private Image _judgmentFill;
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

        // 보스 체력: 살아있는 보스가 씬에 있을 때만 상단 중앙에 표시
        var boss = BossController.Active;
        bool showBoss = boss != null && !boss.IsDead;
        if (_bossRoot != null && _bossRoot.activeSelf != showBoss) _bossRoot.SetActive(showBoss);
        if (showBoss) SetBar(_bossFill, _bossText, boss.Health, boss.MaxHealth);

        // 분신 처형 경고: 남은 시간이 줄어드는 동안 진짜를 찾아 협공해야 한다
        bool showJudgment = showBoss && boss.JudgmentActive;
        if (_judgmentRoot != null && _judgmentRoot.activeSelf != showJudgment)
            _judgmentRoot.SetActive(showJudgment);
        if (showJudgment && _judgmentFill != null)
        {
            var scale = _judgmentFill.rectTransform.localScale;
            scale.x = boss.JudgmentRemain01;
            _judgmentFill.rectTransform.localScale = scale;
        }
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

        BuildBossBar(canvas.transform);
        BuildJudgmentWarning(canvas.transform);
    }

    /// <summary>
    /// 보스 "분신 처형" 경고. 진짜를 찾아 협공(G)해야 한다는 것과 남은 시간을 알린다.
    /// (이 패턴은 일반 사격으로는 파훼되지 않으므로, 안내가 없으면 사실상 즉사 패턴이 된다)
    /// </summary>
    private void BuildJudgmentWarning(Transform parent)
    {
        var root = MakeImage(parent, "JudgmentWarning", new Color(0f, 0f, 0f, 0f));
        _judgmentRoot = root.gameObject;
        var rt = root.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -110f);
        rt.sizeDelta = new Vector2(900f, 108f);

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
            ? "충전 색이 다른 '진짜'를 겨누고  G  — 과거의 나와 협공하라"
            : "Aim at the one with a DIFFERENT charge color, press G to co-attack";
        desc.color = new Color(1f, 0.92f, 0.8f);
        var drt = desc.rectTransform;
        drt.anchorMin = new Vector2(0f, 1f);
        drt.anchorMax = new Vector2(1f, 1f);
        drt.pivot = new Vector2(0.5f, 1f);
        drt.anchoredPosition = new Vector2(0f, -44f);
        drt.sizeDelta = new Vector2(0f, 28f);

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
