using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// 맵이 어두울 때 밝기를 맞추는 도구.
///
/// 화면이 어두워지는 곳은 한 군데가 아니다. 환경광만 올리는 것으로는 한계가 있어서
/// (실제로 이 씬은 환경광을 기본값의 세 배 가까이 올려 둔 상태였는데도 어두웠다)
/// 어두움에 기여하는 <b>네 가지를 한꺼번에</b> 다룬다.
///
///  1. <b>비네트</b> — 화면 가장자리를 검게 덮는다. 이 씬은 세기가 <b>최대치 1.0</b>이었다.
///     환경광을 아무리 올려도 가장자리는 그대로 검으므로, 여기가 가장 큰 원인이었다.
///  2. <b>환경광</b>(Trilight) — 직사광이 닿지 않는 면의 밝기. 맵의 그늘진 쪽을 들어 올린다.
///  3. <b>Directional Light</b> — 전체 기본 밝기.
///  4. <b>안개</b> — 지수제곱 밀도 0.09는 먼 배경을 거의 안개색으로 덮는다.
///
/// 메뉴
///  - <b>권장값 적용</b>: 아래 값들을 한 번에 넣는다. 먼저 이걸 눌러 보고 판단하면 된다.
///  - <b>밝게(+) / 어둡게(−)</b>: 네 가지를 한 단계씩 같이 움직인다.
///  - <b>진단</b>: 지금 무엇이 얼마나 어둡게 만들고 있는지 찍어 본다.
///
/// 네온 간판과 블룸이 이 맵의 인상이므로, 권장값은 <b>바닥을 들어 올리되 네온을 죽이지 않는</b>
/// 선에서 잡았다. 더 밝히고 싶으면 밝게(+)를 몇 번 누르면 된다.
/// </summary>
public static class MapLightSetup
{
    /// <summary>밝게/어둡게 한 단계의 배율.</summary>
    private const float Step = 1.25f;

    // ---- 권장값 ----
    // 비네트는 '얼마나 어둡게 하느냐'라 다른 값과 방향이 반대다.
    // 1.0(최대)에서는 화면 가장자리가 통째로 검게 죽는다. 0.3이면 시선을 가운데로 모으는
    // 본래 역할은 하면서 맵을 가리지는 않는다. TPS는 화면 가장자리에서 적이 들어오므로
    // 이 값이 높으면 게임플레이에도 손해다.
    private const float RecommendedVignette = 0.3f;

    /// <summary>환경광(그늘의 밝기). 지금 씬 값의 약 1.6배.</summary>
    private static readonly Color RecSky = new Color(0.60f, 0.54f, 0.72f);
    private static readonly Color RecEquator = new Color(0.36f, 0.33f, 0.45f);
    private static readonly Color RecGround = new Color(0.19f, 0.17f, 0.23f);

    /// <summary>Directional Light 세기. 색(어두운 청색)은 분위기라 건드리지 않는다.</summary>
    private const float RecommendedSunIntensity = 2.6f;

    /// <summary>
    /// 안개 밀도(지수제곱). 0.09는 먼 배경을 거의 통째로 안개색으로 덮는다.
    /// 0.05면 거리감은 남기고 배경의 형태는 보인다.
    /// </summary>
    private const float RecommendedFogDensity = 0.05f;

    [MenuItem("Tools/TPS/맵 밝기/권장값 적용", priority = 0)]
    public static void ApplyRecommended()
    {
        Undo.SetCurrentGroupName("맵 밝기 권장값");
        int group = Undo.GetCurrentGroup();

        var sb = new StringBuilder();
        sb.AppendLine("<b>[맵 밝기] 권장값을 적용했습니다.</b>");

        // 1) 비네트 — 이 씬에서 가장 큰 원인
        if (TryGetVignette(out Vignette vig, out string profileName))
        {
            Undo.RecordObject(vig, "맵 밝기");
            sb.AppendLine($"· 비네트 {vig.intensity.value:F2} → {RecommendedVignette:F2}  ({profileName})");
            vig.intensity.overrideState = true;
            vig.intensity.value = RecommendedVignette;
            EditorUtility.SetDirty(vig);
        }
        else sb.AppendLine("· 비네트: 씬의 Volume에서 찾지 못했습니다(건너뜀)");

        // 2) 환경광
        RenderSettings.ambientMode = AmbientMode.Trilight;
        sb.AppendLine($"· 환경광 {RenderSettings.ambientSkyColor.r:F3} → {RecSky.r:F3} (하늘 기준)");
        RenderSettings.ambientSkyColor = RecSky;
        RenderSettings.ambientEquatorColor = RecEquator;
        RenderSettings.ambientGroundColor = RecGround;

        // 3) 태양
        Light sun = FindSun();
        if (sun != null)
        {
            Undo.RecordObject(sun, "맵 밝기");
            sb.AppendLine($"· '{sun.name}' 세기 {sun.intensity:F2} → {RecommendedSunIntensity:F2}");
            sun.intensity = RecommendedSunIntensity;
            EditorUtility.SetDirty(sun);
        }
        else sb.AppendLine("· Directional Light: 씬에 없습니다(건너뜀)");

        // 4) 안개
        if (RenderSettings.fog && RenderSettings.fogMode != FogMode.Linear)
        {
            sb.AppendLine($"· 안개 밀도 {RenderSettings.fogDensity:F3} → {RecommendedFogDensity:F3}");
            RenderSettings.fogDensity = RecommendedFogDensity;
        }

        Finish(group, sb, "더 밝히려면 Tools/TPS/맵 밝기/밝게 (+) 를 누르세요.");
    }

    [MenuItem("Tools/TPS/맵 밝기/밝게 (+)", priority = 20)]
    public static void Brighter() => StepBrightness(Step);

    [MenuItem("Tools/TPS/맵 밝기/어둡게 (−)", priority = 21)]
    public static void Darker() => StepBrightness(1f / Step);

    /// <summary>어두움에 관여하는 네 가지를 한 단계씩 같이 움직인다.</summary>
    private static void StepBrightness(float f)
    {
        Undo.SetCurrentGroupName("맵 밝기 조절");
        int group = Undo.GetCurrentGroup();

        var sb = new StringBuilder();
        sb.AppendLine($"<b>[맵 밝기] {(f > 1f ? "밝게" : "어둡게")} (×{f:F2})</b>");

        if (RenderSettings.ambientMode != AmbientMode.Trilight)
            RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Scale(RenderSettings.ambientSkyColor, f);
        RenderSettings.ambientEquatorColor = Scale(RenderSettings.ambientEquatorColor, f);
        RenderSettings.ambientGroundColor = Scale(RenderSettings.ambientGroundColor, f);
        sb.AppendLine($"· 환경광(하늘) = {RenderSettings.ambientSkyColor.r:F3}");

        Light sun = FindSun();
        if (sun != null)
        {
            Undo.RecordObject(sun, "맵 밝기");
            sun.intensity = Mathf.Clamp(sun.intensity * f, 0.05f, 12f);
            EditorUtility.SetDirty(sun);
            sb.AppendLine($"· '{sun.name}' 세기 = {sun.intensity:F2}");
        }

        // 비네트와 안개는 '어둡게 하는 양'이라 반대로 움직인다
        if (TryGetVignette(out Vignette vig, out _))
        {
            Undo.RecordObject(vig, "맵 밝기");
            vig.intensity.overrideState = true;
            vig.intensity.value = Mathf.Clamp01(vig.intensity.value / f);
            EditorUtility.SetDirty(vig);
            sb.AppendLine($"· 비네트 = {vig.intensity.value:F2}");
        }

        if (RenderSettings.fog && RenderSettings.fogMode != FogMode.Linear)
        {
            RenderSettings.fogDensity = Mathf.Clamp(RenderSettings.fogDensity / f, 0.001f, 0.5f);
            sb.AppendLine($"· 안개 밀도 = {RenderSettings.fogDensity:F3}");
        }

        Finish(group, sb, null);
    }

    [MenuItem("Tools/TPS/맵 밝기/진단", priority = 40)]
    public static void Diagnose()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<b>[맵 밝기] 현재 상태</b>");

        sb.AppendLine($"환경광 모드: {RenderSettings.ambientMode}");
        sb.AppendLine($"  하늘 {RenderSettings.ambientSkyColor.r:F3} / " +
                      $"수평 {RenderSettings.ambientEquatorColor.r:F3} / " +
                      $"땅 {RenderSettings.ambientGroundColor.r:F3}");

        Light sun = FindSun();
        sb.AppendLine(sun != null
            ? $"Directional Light '{sun.name}': 세기 {sun.intensity:F2}, 색 {ColorText(sun.color)}"
            : "<color=orange>Directional Light 없음 — 전체 기본 밝기를 낼 광원이 없습니다.</color>");

        if (RenderSettings.fog)
        {
            sb.Append($"안개: {RenderSettings.fogMode}");
            if (RenderSettings.fogMode == FogMode.Linear)
                sb.AppendLine($", {RenderSettings.fogStartDistance:F1}~{RenderSettings.fogEndDistance:F1}m");
            else
            {
                sb.AppendLine($", 밀도 {RenderSettings.fogDensity:F3}");
                // 화면의 절반이 안개색으로 덮이는 거리 — "얼마나 답답한가"를 미터로 보여 준다
                float d = RenderSettings.fogMode == FogMode.ExponentialSquared
                        ? Mathf.Sqrt(Mathf.Log(2f)) / RenderSettings.fogDensity
                        : Mathf.Log(2f) / RenderSettings.fogDensity;
                sb.AppendLine($"  → 약 {d:F1}m 앞에서 절반이 안개색이 됩니다.");
            }
        }
        else sb.AppendLine("안개: 꺼짐");

        if (TryGetVignette(out Vignette vig, out string profileName))
        {
            sb.Append($"비네트({profileName}): 세기 {vig.intensity.value:F2}");
            if (vig.intensity.value >= 0.7f)
                sb.AppendLine(" <color=orange>← 화면 가장자리를 크게 덮고 있습니다. " +
                              "환경광을 올려도 가장자리는 그대로 어둡습니다.</color>");
            else sb.AppendLine();
        }
        else sb.AppendLine("비네트: 없음");

        var stage = StageSwapper.FindCurrentStage();
        if (stage != null)
        {
            var lights = stage.GetComponentsInChildren<Light>(true);
            int on = 0;
            foreach (var l in lights) if (l.enabled) on++;
            sb.AppendLine($"배경 '{stage.name}'에 딸린 조명: {lights.Length}개 중 {on}개 켜짐 " +
                          "(Tools/TPS/Change Map/배경 조명 켜기·끄기)");
        }

        Debug.Log(sb.ToString());
    }

    // ---------- 도구 ----------

    /// <summary>
    /// 씬에 걸린 Volume들에서 비네트를 찾는다. 여러 개면 가장 우선순위가 높은(=실제로 적용되는)
    /// 전역 Volume의 것을 고른다.
    /// </summary>
    private static bool TryGetVignette(out Vignette vignette, out string profileName)
    {
        vignette = null;
        profileName = null;

        Volume best = null;
        foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (v.sharedProfile == null || !v.sharedProfile.Has<Vignette>()) continue;
            if (best == null || v.priority > best.priority) best = v;
        }
        if (best == null) return false;

        if (!best.sharedProfile.TryGet(out vignette)) return false;
        profileName = best.sharedProfile.name;
        return true;
    }

    /// <summary>씬의 Directional Light(첫 번째).</summary>
    private static Light FindSun()
    {
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (l.type == LightType.Directional) return l;
        return null;
    }

    private static Color Scale(Color c, float f) =>
        new Color(Mathf.Clamp01(c.r * f), Mathf.Clamp01(c.g * f), Mathf.Clamp01(c.b * f), c.a);

    private static string ColorText(Color c) => $"({c.r:F2}, {c.g:F2}, {c.b:F2})";

    private static void Finish(int undoGroup, StringBuilder sb, string tail)
    {
        DynamicGI.UpdateEnvironment();
        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        // 비네트는 씬이 아니라 Volume 프로파일 '에셋'에 들어 있다 —
        // 씬만 더럽혀 두면 저장돼도 비네트 값은 디스크에 남지 않는다.
        AssetDatabase.SaveAssets();
        if (!string.IsNullOrEmpty(tail)) sb.AppendLine(tail);
        Debug.Log(sb.ToString());
    }
}
