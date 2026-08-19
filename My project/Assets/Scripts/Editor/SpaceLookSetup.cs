using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.Rendering;     // ShaderMessage / ShaderCompilerMessageSeverity
using UnityEditor.SceneManagement;

/// <summary>
/// 우주 배경 꾸미기 도구 — "망해가는 별" 분위기.
///
/// 왜 필요한가
///  - 씬 스카이박스가 유니티 기본 하늘이라 우주 맵에서 창밖이 엉뚱하게 보인다.
///  - 배경 FBX의 NebulaDome(반지름 1600m)은 맵 축척에 따라 카메라 far clip 안팎을 오간다.
///    안으로 들어오면 밋밋한 구가 하늘을 통째로 가리고, 밖이면 아무것도 안 보인다.
///  - Planet / Asteroids 메시는 FBX에 딸려온 밋밋한 머티리얼이라 밝고 평평한 회색 덩어리로 보인다.
///
/// 하는 일
///  1) 절차적 우주 스카이박스(TPS/Space Skybox)를 만들어 씬에 물리고, 그것을 가리는
///     배경 돔은 끈다 — 어두운 보라, 자홍 성운
///  2) 행성(TPS/Planet)을 식어 굳은 암반 + 갈라진 틈의 잔광으로 칠한다
///  3) 부유물(TPS/Space Rock)에 깊은 그늘과 성운색 테두리광을 준다
///  4) 환경광을 스카이박스 기반 → 어두운 3색(Trilight)으로 바꾼다
///     — 밝은 환경광이 남아 있으면 뭘 해도 물체가 허옇게 뜬다
///  5) 세 머티리얼에 씬의 Directional Light 방향을 태양 방향으로 넣는다
///
/// 색은 만들어진 머티리얼 인스펙터에서 조절하면 된다.
/// 단, '우주 배경 적용'을 다시 실행하면 아래 팔레트 값으로 덮어쓴다.
/// 메뉴: Tools/TPS/Space Look/...
/// </summary>
public static class SpaceLookSetup
{
    private const string MaterialFolder = "Assets/Materials";
    private const string SkyMaterialPath = MaterialFolder + "/SpaceSkybox.mat";
    private const string PlanetMaterialPath = MaterialFolder + "/Planet.mat";
    private const string RockMaterialPath = MaterialFolder + "/SpaceRock.mat";

    private const string SkyShaderName = "TPS/Space Skybox";
    private const string PlanetShaderName = "TPS/Planet";
    private const string RockShaderName = "TPS/Space Rock";

    /// <summary>이 이름 조각이 들어간 렌더러에 행성 / 암석 머티리얼을 씌운다.</summary>
    private static readonly string[] PlanetNames = { "planet" };
    private static readonly string[] RockNames = { "asteroid", "rock", "debris" };

    /// <summary>
    /// FBX에 딸려온 배경 껍데기(거대한 돔). 스카이박스가 그 역할을 대신하므로 꺼 버린다.
    ///
    /// 왜 꺼야 하나: NebulaDome은 반지름이 1600m라, 맵 축척에 따라 카메라 far clip 안팎을
    /// 오간다. 안으로 들어오는 순간 밋밋한 FBX 머티리얼을 두른 거대한 구가 하늘을 통째로
    /// 가려 버린다(실제로 맵 축척이 0.13→0.06으로 바뀌자 그렇게 됐다).
    /// far clip을 줄여 막는 방법은 축척이 또 바뀌면 도로 깨진다.
    /// </summary>
    private static readonly string[] BackdropNames = { "nebuladome", "skydome", "dome" };

    /// <summary>
    /// 환경광. 어두운 우주 분위기와 "보스가 보여야 한다" 사이의 타협점이다.
    /// 원래 씬 값(0.212)은 배경이 허옇게 뜨고, 0.07까지 내리면 보스가 실루엣만 남는다.
    /// 부족하면 아래 '환경광 밝게' 메뉴로 단계별로 올린다.
    /// </summary>
    private static readonly Color AmbientSky = new Color(0.135f, 0.120f, 0.165f);
    private static readonly Color AmbientEquator = new Color(0.080f, 0.072f, 0.100f);
    private static readonly Color AmbientGround = new Color(0.038f, 0.034f, 0.046f);

    /// <summary>밝기 조절 한 단계.</summary>
    private const float BrightnessStep = 1.3f;

    // 배경 움직임 속도(초당 각도). 눈에 거슬리지 않으면서 "살아 있다"는 게 보이는 선.
    private const float PlanetSpin = 4f;      // 한 바퀴 90초
    private const float RockOrbit = 2.5f;     // 한 바퀴 약 2분 24초
    private const float RockSpinMin = 8f;     // 조각별 구르기(가장 느린 쪽)
    private const float RockSpinMax = 25f;    // 조각별 구르기(가장 빠른 쪽)

    /// <summary>이미 한 번 적용한 적이 있는가(맵 교체 후 자동 재적용 여부 판단용).</summary>
    public static bool HasAssets =>
        AssetDatabase.LoadAssetAtPath<Material>(SkyMaterialPath) != null;

    // ---------- 메뉴 ----------

    [MenuItem("Tools/TPS/Space Look/우주 배경 적용 (망해가는 별)")]
    public static void ApplyMenu()
    {
        if (!EditorUtility.DisplayDialog("우주 배경",
                "스카이박스 · 행성 · 부유물을 어두운 '망해가는 별' 분위기로 맞춥니다.\n\n" +
                "환경광도 함께 어둡게 내려갑니다.\n" +
                "(밝은 환경광이 남아 있으면 물체가 계속 허옇게 뜹니다)\n\n" +
                "이미 만들어 둔 머티리얼 색은 덮어쓰입니다. 계속할까요?", "적용", "취소"))
            return;

        Apply();
    }

    [MenuItem("Tools/TPS/Space Look/태양 방향 다시 맞추기")]
    public static void RefreshSunMenu()
    {
        Vector3 sun = SunDirection();
        int n = 0;
        foreach (string path in new[] { PlanetMaterialPath, RockMaterialPath })
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) continue;
            m.SetVector("_SunDir", sun);
            EditorUtility.SetDirty(m);
            n++;
        }
        if (n == 0) { Debug.LogWarning("[Space] 머티리얼이 아직 없습니다. 먼저 '우주 배경 적용'을 실행하세요."); return; }

        AssetDatabase.SaveAssets();
        Debug.Log($"[Space] 머티리얼 {n}개의 태양 방향을 {sun} 으로 맞췄습니다.");
    }

    /// <summary>FBX 배경 돔을 다시 켜거나 끈다. 켜면 스카이박스가 가려진다.</summary>
    [MenuItem("Tools/TPS/Space Look/배경 돔 보이기·숨기기")]
    public static void ToggleBackdrop()
    {
        var stage = StageSwapper.FindCurrentStage();
        if (stage == null) { Debug.LogWarning("[Space] 씬에서 배경을 찾지 못했습니다."); return; }

        var domes = new System.Collections.Generic.List<MeshRenderer>();
        foreach (var r in stage.GetComponentsInChildren<MeshRenderer>(true))
            if (Matches(r.gameObject.name.ToLowerInvariant(), BackdropNames)) domes.Add(r);

        if (domes.Count == 0) { Debug.Log("[Space] 이 배경에는 돔 메시가 없습니다."); return; }

        bool on = !domes[0].enabled;
        foreach (var r in domes)
        {
            Undo.RecordObject(r, "배경 돔 토글");
            r.enabled = on;
            EditorUtility.SetDirty(r);
        }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log($"[Space] 배경 돔 {domes.Count}개를 {(on ? "켰습니다 — 스카이박스가 가려집니다" : "껐습니다 — 스카이박스가 보입니다")}.");
    }

    [MenuItem("Tools/TPS/Space Look/환경광 밝게 (+)")]
    public static void Brighter() => StepAmbient(BrightnessStep);

    [MenuItem("Tools/TPS/Space Look/환경광 어둡게 (−)")]
    public static void Darker() => StepAmbient(1f / BrightnessStep);

    /// <summary>환경광을 한 단계 올리거나 내린다. 보스가 안 보이면 이걸로 맞춘다.</summary>
    private static void StepAmbient(float factor)
    {
        if (RenderSettings.ambientMode != AmbientMode.Trilight)
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = AmbientSky;
            RenderSettings.ambientEquatorColor = AmbientEquator;
            RenderSettings.ambientGroundColor = AmbientGround;
        }

        RenderSettings.ambientSkyColor = Scale(RenderSettings.ambientSkyColor, factor);
        RenderSettings.ambientEquatorColor = Scale(RenderSettings.ambientEquatorColor, factor);
        RenderSettings.ambientGroundColor = Scale(RenderSettings.ambientGroundColor, factor);
        DynamicGI.UpdateEnvironment();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        var sun = FindSun();
        string sunNote = sun != null
            ? $" 그래도 어두우면 Directional Light('{sun.name}') 세기({sun.intensity:F2})를 올려 보세요."
            : "";
        Debug.Log($"[Space] 환경광 = {RenderSettings.ambientSkyColor.r:F3} " +
                  $"(하늘) / {RenderSettings.ambientEquatorColor.r:F3} / {RenderSettings.ambientGroundColor.r:F3}." +
                  sunNote);
    }

    private static Color Scale(Color c, float f) =>
        new Color(Mathf.Clamp01(c.r * f), Mathf.Clamp01(c.g * f), Mathf.Clamp01(c.b * f), c.a);

    /// <summary>색은 그대로 두고 움직임만 다시 붙이거나 값을 되돌릴 때.</summary>
    [MenuItem("Tools/TPS/Space Look/행성 자전 · 부유물 띄우기")]
    public static void SetupMotionMenu()
    {
        var stage = StageSwapper.FindCurrentStage();
        if (stage == null) { Debug.LogWarning("[Space] 씬에서 배경을 찾지 못했습니다."); return; }

        Undo.SetCurrentGroupName("배경 움직임");
        int group = Undo.GetCurrentGroup();
        int n = SetupMotion(stage);
        Undo.CollapseUndoOperations(group);

        if (n == 0)
        {
            Debug.LogWarning("[Space] Planet/Asteroids 메시를 찾지 못했습니다.");
            return;
        }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[Space] 배경 오브젝트 {n}개에 움직임을 붙였습니다. " +
                  "플레이 모드에서만 돕니다. 속도는 각 오브젝트의 SpaceDrift 인스펙터에서 조절하세요.");
    }

    /// <summary>배경이 분홍색으로 나올 때(셰이더 컴파일 실패) 원인을 바로 확인한다.</summary>
    [MenuItem("Tools/TPS/Space Look/셰이더 오류 확인")]
    public static void CheckShaders()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<b>[Space] 셰이더 상태</b>");

        foreach (string name in new[] { SkyShaderName, PlanetShaderName, RockShaderName })
        {
            var shader = Shader.Find(name);
            if (shader == null)
            {
                sb.AppendLine($"<color=red>{name}: 찾을 수 없음 (파일이 없거나 이름이 다름)</color>");
                continue;
            }
            if (!ReportShaderErrors(shader, sb))
                sb.AppendLine($"{name}: 정상 (지원={shader.isSupported})");
        }

        var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterialPath);
        sb.AppendLine($"씬 스카이박스: {(RenderSettings.skybox != null ? RenderSettings.skybox.name : "없음")}" +
                      $" / 만들어진 머티리얼: {(sky != null ? sky.name : "없음")}");

        Debug.Log(sb.ToString());
    }

    [MenuItem("Tools/TPS/Space Look/카메라 far clip 늘리기 (먼 배경 보이게)")]
    public static void ExtendFarClip()
    {
        var cam = Camera.main;
        if (cam == null) { Debug.LogWarning("[Space] Main Camera를 찾지 못했습니다."); return; }

        var stage = StageSwapper.FindCurrentStage();
        float need = 200f;
        if (stage != null)
        {
            Bounds b = new Bounds(cam.transform.position, Vector3.zero);
            bool has = false;
            foreach (var r in stage.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer) continue;
                if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
            }
            if (has) need = Vector3.Distance(cam.transform.position, b.center) + b.extents.magnitude;
        }

        float far = Mathf.Ceil(Mathf.Max(need * 1.1f, 200f));
        Undo.RecordObject(cam, "far clip 늘리기");
        float before = cam.farClipPlane;
        cam.farClipPlane = far;
        EditorUtility.SetDirty(cam);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log($"[Space] Main Camera far clip {before:F0} → {far:F0}. " +
                  "이제 NebulaDome 같은 먼 배경도 그려집니다. " +
                  "가까운 물체에서 깊이 정밀도가 떨어지면 되돌리세요.");
    }

    [MenuItem("Tools/TPS/Space Look/되돌리기 (기본 스카이박스)")]
    public static void Revert()
    {
        // RenderSettings는 Undo 대상이 아니라 되돌리기가 안 걸린다 — 다시 적용하려면 메뉴를 또 실행하면 된다
        RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
        RenderSettings.ambientMode = AmbientMode.Skybox;
        DynamicGI.UpdateEnvironment();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[Space] 기본 스카이박스로 되돌렸습니다. " +
                  "(행성·부유물 머티리얼은 그대로 남습니다 — 해당 오브젝트에서 직접 되돌리세요)");
    }

    // ---------- 적용 ----------

    public static void Apply()
    {
        var sky = EnsureMaterial(SkyMaterialPath, SkyShaderName);
        var planet = EnsureMaterial(PlanetMaterialPath, PlanetShaderName);
        var rock = EnsureMaterial(RockMaterialPath, RockShaderName);
        if (sky == null || planet == null || rock == null)
        {
            Debug.LogError("[Space] 셰이더가 준비되지 않아 중단했습니다. " +
                           "Tools/TPS/Space Look/셰이더 오류 확인 을 실행해 보세요.");
            return;
        }

        Vector3 sun = SunDirection();
        PaintSky(sky);
        PaintPlanet(planet, sun);
        PaintRock(rock, sun);

        // --- 스카이박스 · 환경광 ---
        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = AmbientSky;
        RenderSettings.ambientEquatorColor = AmbientEquator;
        RenderSettings.ambientGroundColor = AmbientGround;
        DynamicGI.UpdateEnvironment();

        int painted = 0, moving = 0;
        var stage = StageSwapper.FindCurrentStage();
        if (stage != null)
        {
            painted = ApplyToStage(stage);
            moving = SetupMotion(stage);
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        string note = painted > 0
            ? $"배경 메시 {painted}개(행성·부유물)에 머티리얼을 씌우고, {moving}개를 움직이게 했습니다."
            : "<color=orange>씬에서 Planet/Asteroids 메시를 찾지 못해 스카이박스만 적용했습니다.</color>";
        Debug.Log($"<color=lime>[Space] 우주 배경 적용 완료.</color> {note}\n" +
                  "자전·부유는 플레이 모드에서만 보입니다(에디터에서 씬 위치를 건드리지 않기 위해서).\n" +
                  $"색을 더 만지려면 {MaterialFolder} 안의 머티리얼을 인스펙터에서 여세요. " +
                  "너무 어두우면 각 머티리얼의 '그늘 밝기'와 Lighting 창의 환경광을 올리면 됩니다.");
    }

    /// <summary>
    /// 행성은 자전하고, 부유물 무리는 천천히 돌면서 위아래로 부유하게 만든다.
    /// Asteroids는 조각 수천 개가 메시 하나로 합쳐져 있어 개별 회전이 불가능하므로,
    /// 무리의 중심을 피벗으로 잡아 전체를 돌린다.
    /// </summary>
    public static int SetupMotion(GameObject stage)
    {
        if (stage == null) return 0;

        // 흔들림 폭은 방 크기에 맞춘다(맵 축척이 달라져도 같은 느낌이 나도록)
        float scaleRef = 1f;
        if (ArenaFloorProbe.TryGetBounds(stage, out Bounds room))
            scaleRef = Mathf.Max(room.size.x, room.size.z);

        // --- 대상 모으기 ---
        var rocks = new System.Collections.Generic.List<MeshRenderer>();
        var planets = new System.Collections.Generic.List<MeshRenderer>();
        foreach (var r in stage.GetComponentsInChildren<MeshRenderer>(true))
        {
            string name = r.gameObject.name.ToLowerInvariant();
            if (Matches(name, PlanetNames)) planets.Add(r);
            else if (Matches(name, RockNames)) rocks.Add(r);
        }

        // --- 부유물 무리 전체의 중심 = 공전축이 지나는 지점 ---
        // 조각이 개별 오브젝트로 나뉜 FBX에서는 이걸 각자 자기 중심으로 잡으면
        // 제자리에서 도는 꼴이 되어 아무것도 움직이지 않는 것처럼 보인다.
        Vector3 fieldCenter = Vector3.zero;
        if (rocks.Count > 0)
        {
            Bounds field = rocks[0].bounds;
            for (int i = 1; i < rocks.Count; i++) field.Encapsulate(rocks[i].bounds);
            fieldCenter = field.center;
        }

        int n = 0;

        foreach (var r in planets)
        {
            var drift = Attach(r);
            // 자전: 살짝 기운 축으로 한 바퀴에 약 90초
            drift.SpinAxis = new Vector3(0.15f, 1f, 0.05f);
            drift.SpinSpeed = PlanetSpin;
            drift.OrbitSpeed = 0f;
            drift.BobAmplitude = 0f;
            EditorUtility.SetDirty(drift);
            n++;
        }

        for (int i = 0; i < rocks.Count; i++)
        {
            var drift = Attach(rocks[i]);
            var rnd = new System.Random(i * 7919 + 13);   // 조각마다 다르되 다시 실행해도 같은 값

            // 공전: 무리 전체가 같은 속도로 돈다.
            // 조각마다 속도를 달리하면 몇 분 만에 띠처럼 번져 버린다.
            drift.OrbitAxis = Vector3.up;
            drift.OrbitPivot = fieldCenter;
            drift.OrbitSpeed = RockOrbit;

            // 자전(구르기): 조각마다 다른 축·속도 — "떠다니는" 느낌은 대부분 여기서 나온다
            drift.SpinAxis = RandomAxis(rnd);
            drift.SpinSpeed = Mathf.Lerp(RockSpinMin, RockSpinMax, (float)rnd.NextDouble())
                            * (rnd.Next(2) == 0 ? 1f : -1f);

            // 부유: 폭·주기도 흩어 놓아야 무리가 한 몸처럼 출렁이지 않는다
            drift.BobAmplitude = scaleRef * 0.02f * Mathf.Lerp(0.6f, 1.5f, (float)rnd.NextDouble());
            drift.BobPeriod = Mathf.Lerp(9f, 20f, (float)rnd.NextDouble());

            EditorUtility.SetDirty(drift);
            n++;
        }

        if (n > 0)
            Debug.Log($"[Space] 움직임 설정: 행성 {planets.Count}개(자전 {PlanetSpin}°/s), " +
                      $"부유물 {rocks.Count}개(공전 {RockOrbit}°/s, 중심={fieldCenter}, " +
                      $"자전 {RockSpinMin}~{RockSpinMax}°/s). " +
                      "<b>플레이 모드에서만 움직입니다.</b>");
        return n;
    }

    private static SpaceDrift Attach(MeshRenderer r)
    {
        var drift = r.GetComponent<SpaceDrift>();
        if (drift == null) drift = Undo.AddComponent<SpaceDrift>(r.gameObject);
        else Undo.RecordObject(drift, "배경 움직임");
        return drift;
    }

    private static Vector3 RandomAxis(System.Random rnd)
    {
        // 구면상 균일 분포까지는 필요 없다 — 방향만 고루 흩어지면 된다
        var v = new Vector3((float)rnd.NextDouble() - 0.5f,
                            (float)rnd.NextDouble() - 0.5f,
                            (float)rnd.NextDouble() - 0.5f);
        return v.sqrMagnitude < 1e-4f ? Vector3.up : v.normalized;
    }

    /// <summary>배경 안의 행성·부유물에 머티리얼을 씌운다. 맵을 갈아끼운 뒤에도 다시 부르면 된다.</summary>
    public static int ApplyToStage(GameObject stage)
    {
        var planet = AssetDatabase.LoadAssetAtPath<Material>(PlanetMaterialPath);
        var rock = AssetDatabase.LoadAssetAtPath<Material>(RockMaterialPath);
        if (stage == null || (planet == null && rock == null)) return 0;

        int n = 0;
        foreach (var r in stage.GetComponentsInChildren<MeshRenderer>(true))
        {
            string name = r.gameObject.name.ToLowerInvariant();

            // 배경 돔은 스카이박스를 가리므로 끈다
            if (Matches(name, BackdropNames))
            {
                if (r.enabled)
                {
                    Undo.RecordObject(r, "배경 돔 끄기");
                    r.enabled = false;
                    EditorUtility.SetDirty(r);
                    Debug.Log($"[Space] 배경 돔 '{r.gameObject.name}'을 껐습니다 — 스카이박스를 가리고 있었습니다. " +
                              "다시 보려면 Tools/TPS/Space Look/배경 돔 보이기·숨기기.");
                }
                continue;
            }

            Material use = null;
            if (planet != null && Matches(name, PlanetNames)) use = planet;
            else if (rock != null && Matches(name, RockNames)) use = rock;
            if (use == null) continue;

            Undo.RecordObject(r, "우주 배경 머티리얼");
            var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
            for (int i = 0; i < mats.Length; i++) mats[i] = use;
            r.sharedMaterials = mats;
            EditorUtility.SetDirty(r);
            n++;
        }
        return n;
    }

    // ---------- 팔레트 ----------

    private static void PaintSky(Material m)
    {
        m.SetColor("_SpaceColor", new Color(0.014f, 0.011f, 0.028f));
        m.SetColor("_HorizonColor", new Color(0.045f, 0.020f, 0.055f));
        m.SetColor("_NebulaColorA", new Color(0.52f, 0.14f, 0.44f));
        m.SetColor("_NebulaColorB", new Color(0.22f, 0.09f, 0.42f));
        m.SetFloat("_NebulaAmount", 1.0f);
        m.SetFloat("_NebulaScale", 1.9f);
        m.SetFloat("_NebulaCut", 0.38f);
        m.SetFloat("_StarDensity", 130f);
        m.SetFloat("_StarAmount", 0.13f);
        m.SetFloat("_StarBrightness", 1.8f);
        m.SetFloat("_Exposure", 1.0f);
        EditorUtility.SetDirty(m);
    }

    private static void PaintPlanet(Material m, Vector3 sun)
    {
        m.SetVector("_SunDir", sun);
        m.SetColor("_RockColor", new Color(0.035f, 0.030f, 0.042f));
        m.SetColor("_RockLight", new Color(0.115f, 0.105f, 0.125f));
        m.SetColor("_CrackColor", new Color(0.85f, 0.22f, 0.06f));
        m.SetColor("_AshColor", new Color(0.14f, 0.12f, 0.15f));
        m.SetColor("_AtmoColor", new Color(0.42f, 0.20f, 0.55f));
        m.SetFloat("_SunLevel", 0.85f);
        m.SetFloat("_AmbientLevel", 0.05f);
        m.SetFloat("_Terminator", 0.10f);
        m.SetFloat("_CrackWidth", 0.045f);
        m.SetFloat("_CrackGlow", 1.6f);
        m.SetFloat("_AshAmount", 0.25f);
        m.SetFloat("_AtmoPower", 3.5f);
        m.SetFloat("_AtmoStrength", 0.5f);
        m.SetFloat("_Exposure", 1.0f);
        EditorUtility.SetDirty(m);
    }

    private static void PaintRock(Material m, Vector3 sun)
    {
        m.SetVector("_SunDir", sun);
        m.SetColor("_RockDark", new Color(0.030f, 0.028f, 0.036f));
        m.SetColor("_RockLight", new Color(0.150f, 0.140f, 0.155f));
        m.SetColor("_RimColor", new Color(0.45f, 0.18f, 0.52f));
        m.SetFloat("_SunLevel", 0.9f);
        m.SetFloat("_AmbientLevel", 0.06f);
        m.SetFloat("_Terminator", 0.25f);
        m.SetFloat("_RimPower", 3.0f);
        m.SetFloat("_RimStrength", 0.55f);
        m.SetFloat("_Exposure", 1.0f);
        EditorUtility.SetDirty(m);
    }

    // ---------- 도우미 ----------

    private static bool Matches(string lowerName, string[] fragments)
    {
        foreach (var f in fragments) if (lowerName.Contains(f)) return true;
        return false;
    }

    private static Material EnsureMaterial(string path, string shaderName)
    {
        var shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogError($"[Space] 셰이더 '{shaderName}' 를 찾지 못했습니다. " +
                           "Assets/Shaders 안의 .shader 파일이 있는지 확인하세요.");
            return null;
        }

        var sb = new System.Text.StringBuilder();
        if (ReportShaderErrors(shader, sb))
        {
            Debug.LogError($"[Space] '{shaderName}' 컴파일 오류 — 이 상태면 분홍색으로 그려집니다.\n{sb}");
            return null;
        }

        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            // 셰이더를 새로 만든 뒤에도 옛 머티리얼이 남아 있을 수 있다
            if (existing.shader != shader) existing.shader = shader;
            return existing;
        }

        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder("Assets", "Materials");

        var mat = new Material(shader);
        AssetDatabase.CreateAsset(mat, path);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Space] {path} 를 만들었습니다.");
        return mat;
    }

    /// <summary>컴파일 오류가 있으면 sb에 적고 true.</summary>
    private static bool ReportShaderErrors(Shader shader, System.Text.StringBuilder sb)
    {
        int count = ShaderUtil.GetShaderMessageCount(shader);
        if (count <= 0) return false;

        bool hasError = false;
        var messages = ShaderUtil.GetShaderMessages(shader);
        foreach (var msg in messages)
        {
            bool err = msg.severity == ShaderCompilerMessageSeverity.Error;
            hasError |= err;
            sb.AppendLine($"  {(err ? "오류" : "경고")} (줄 {msg.line}): {msg.message}");
        }
        return hasError;
    }

    /// <summary>씬에서 가장 센 Directional Light(없으면 null).</summary>
    private static Light FindSun()
    {
        Light best = null;
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (l.type != LightType.Directional || !l.enabled) continue;
            if (best == null || l.intensity > best.intensity) best = l;
        }
        return best;
    }

    /// <summary>Directional Light가 비추는 반대 방향 = 태양이 있는 방향.</summary>
    private static Vector3 SunDirection()
    {
        var sun = FindSun();
        return sun != null ? -sun.transform.forward : new Vector3(0.5f, 0.4f, -0.75f).normalized;
    }
}
