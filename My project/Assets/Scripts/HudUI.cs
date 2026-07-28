using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 게임 HUD를 에셋 없이 코드로 생성하는 UI.
/// - 좌하단: 체력(HP) / 기력(SP) / 타임포스(TF) 바
/// - 우하단: 현재 탄약 / 최대 탄약, 재장전 중 표시
/// PlayerStats와 PlayerShooter를 찾아 매 프레임 값을 반영한다.
/// (한글 글리프가 내장 폰트에 없어 라벨은 영문 약어를 쓴다)
/// </summary>
public class HudUI : MonoBehaviour
{
    private PlayerStats _stats;
    private PlayerShooter _shooter;

    private Image _hpFill, _spFill, _tfFill;
    private Text _hpText, _spText, _tfText, _ammoText, _reloadText;

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
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
