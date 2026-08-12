using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;

/// <summary>
/// 보스(AlienMonster) 자동 배치/구성 도구.
///
/// 하는 일
///  1) 보스용 애니메이터(Boss.controller) 생성 — 대기/걷기/사망 로코모션(달리기 없음)
///     (공격 동작은 클립이 없어 BossRig가 절차적으로 만든다)
///  2) 씬에 AlienMonster 프리팹을 "Boss"로 배치하고 플레이어 키에 비례해 크기를 맞춤
///  3) CharacterController 실측 보정 + BossController/BossRig/TimeRewindable 부착 및 참조 연결
///
/// 메뉴: Tools/TPS/Setup Boss (AlienMonster)
/// </summary>
public static class BossSetup
{
    private const string PrefabPath = "Assets/Codersan/AlienMonster/Character/AlienMonster.prefab";
    private const string ModelFbx = "Assets/Codersan/AlienMonster/Character/Model/AlienMonster.fbx";
    private const string AnimDir = "Assets/Codersan/AlienMonster/Character/Model/Animations/";
    private const string ControllerPath = AnimDir + "Boss.controller";

    private const string IdleFbx = AnimDir + "Idle2.fbx";
    private const string LocoDir = "Assets/Codersan/AlienMonster/Character/ThirdPersonController/Animations/";
    private const string WalkFbx = LocoDir + "Locomotion--Walk_N.anim.fbx";
    private const string DeathFbx = "Assets/Resources/Player/Animation/Dying.fbx";
    private const string SfxDir = "Assets/Codersan/AlienMonster/Character/ThirdPersonController/Sfx/";

    /// <summary>보스 키 = 플레이어 키 × 이 배율(보스답게 크게).</summary>
    private const float BossHeightScale = 1.6f;
    /// <summary>플레이어 정면 이 거리(플레이어 키 배수)에 배치한다.</summary>
    private const float SpawnDistance = 10f;

    private const float MoveThreshold = 0.1f;  // Speed: 대기 ↔ 걷기 (보스는 달리지 않는다)

    [MenuItem("Tools/TPS/Setup Boss (AlienMonster)")]
    public static void SetupMenu()
    {
        if (EditorUtility.DisplayDialog("보스 세팅",
                "현재 씬에 AlienMonster 보스를 배치하고 AI/애니메이터를 구성합니다.\n" +
                "(추격 · 근접 할퀴기 · 손끝 레이저 · 텔레포트)\n계속할까요?", "실행", "취소"))
            Setup();
    }

    public static void Setup()
    {
        try
        {
            // 1) 애니메이터 구성
            var controller = BuildAnimator();

            // 2) 씬 배치
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) { Debug.LogError($"[Boss] 프리팹을 찾지 못했습니다: {PrefabPath}"); return; }

            var player = GameObject.Find("Player");
            GameObject boss = GameObject.Find("Boss");
            bool newlyCreated = boss == null;
            if (newlyCreated)
            {
                boss = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                boss.name = "Boss";
            }

            // 3) 크기: 플레이어 키의 BossHeightScale 배
            float playerHeight = 1.8f;
            if (player != null && TryGetRendererBounds(player, out Bounds pb) && pb.size.y > 1e-4f)
                playerHeight = pb.size.y;

            if (newlyCreated) FitHeight(boss, playerHeight * BossHeightScale);

            // 4) 위치: 플레이어 정면
            if (newlyCreated)
            {
                Vector3 basePos = player != null ? player.transform.position : Vector3.zero;
                Vector3 fwd = player != null ? player.transform.forward : Vector3.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
                boss.transform.position = basePos + fwd.normalized * (playerHeight * SpawnDistance);
                if (player != null)
                {
                    Vector3 look = player.transform.position - boss.transform.position;
                    look.y = 0f;
                    if (look.sqrMagnitude > 1e-6f) boss.transform.rotation = Quaternion.LookRotation(look);
                }
            }

            // 5) 콜라이더 실측 보정(총알 판정 + 이동/중력)
            var cc = GetOrAdd<CharacterController>(boss);
            FitCharacterController(boss, cc);

            // 6) 애니메이터 연결
            var anim = boss.GetComponentInChildren<Animator>();
            if (anim != null)
            {
                anim.runtimeAnimatorController = controller;
                anim.applyRootMotion = false; // 이동은 CharacterController가 담당
                if (anim.avatar == null) anim.avatar = LoadAvatar(ModelFbx);
                EditorUtility.SetDirty(anim);
            }
            else Debug.LogWarning("[Boss] Animator를 찾지 못했습니다. 로코모션 없이 AI만 동작합니다.");

            // 7) 컴포넌트 부착 — BossRig는 Animator와 같은 오브젝트에 있어야 본을 찾는다
            GameObject rigHost = anim != null ? anim.gameObject : boss;
            GetOrAdd<BossRig>(rigHost);
            WireFootsteps(rigHost);
            var ai = GetOrAdd<BossController>(boss);
            GetOrAdd<TimeRewindable>(boss); // 시간역행 대상(위치 + 체력이 과거로 되돌아감)

            var so = new SerializedObject(ai);
            var animProp = so.FindProperty("animator");
            if (animProp != null) animProp.objectReferenceValue = anim;
            var targetProp = so.FindProperty("target");
            if (targetProp != null && player != null) targetProp.objectReferenceValue = player.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            // 8) 저장
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = boss;

            Debug.Log($"<color=lime>[Boss] AlienMonster 보스 배치 완료.</color> " +
                      $"키={playerHeight * BossHeightScale:F2}m, 위치={boss.transform.position}. " +
                      "Play를 눌러 추격/할퀴기/레이저/텔레포트를 확인하세요.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Boss] 세팅 중 오류: {e}");
        }
    }

    /// <summary>
    /// 로코모션 클립(Walk_N)에 박힌 AnimationEvent(OnFootstep) 수신부를 붙이고
    /// 팩에 들어있는 발소리 wav를 연결한다. 수신부가 없으면 걸을 때마다
    /// "AnimationEvent 'OnFootstep' has no receiver!" 경고가 뜬다.
    /// </summary>
    private static void WireFootsteps(GameObject host)
    {
        var steps = GetOrAdd<BossFootsteps>(host);
        var so = new SerializedObject(steps);
        var clipsProp = so.FindProperty("footstepClips");
        if (clipsProp != null && clipsProp.arraySize == 0)
        {
            var clips = new System.Collections.Generic.List<AudioClip>();
            for (int i = 1; i <= 5; i++)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(
                    $"{SfxDir}Player_Footstep_{i:00}.wav");
                if (clip != null) clips.Add(clip);
            }
            clipsProp.arraySize = clips.Count;
            for (int i = 0; i < clips.Count; i++)
                clipsProp.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---------- 애니메이터 ----------

    private static AnimatorController BuildAnimator()
    {
        // 로코모션 클립은 이미 Humanoid(다른 아바타에서 복사)라 리그는 건드리지 않고,
        // 발이 지면에 닿도록 Root Transform Y만 발 기준으로 굽는다(플레이어와 동일한 처리).
        EnsureClipSettings(IdleFbx, loop: true);
        EnsureClipSettings(WalkFbx, loop: true);
        EnsureHumanoid(DeathFbx);
        EnsureClipSettings(DeathFbx, loop: false);

        // 대기: 전용 Idle2, 없으면 모델 FBX에 내장된 기본 대기 클립
        AnimationClip idle = LoadClip(IdleFbx) ?? LoadClip(ModelFbx);
        AnimationClip walk = LoadClip(WalkFbx);
        AnimationClip death = LoadClip(DeathFbx);

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        // 파라미터 재구성
        for (int i = controller.parameters.Length - 1; i >= 0; i--) controller.RemoveParameter(i);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float); // 0=대기, 0.5=걷기
        controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);

        while (controller.layers.Length > 1) controller.RemoveLayer(1);
        var sm = controller.layers[0].stateMachine;
        foreach (var cs in sm.states) sm.RemoveState(cs.state);
        sm.anyStateTransitions = new AnimatorStateTransition[0];

        var idleState = sm.AddState("Idle", new Vector3(280, 60, 0));
        idleState.motion = idle;
        idleState.iKOnFeet = true;
        sm.defaultState = idleState;

        var walkState = sm.AddState("Walk", new Vector3(520, 60, 0));
        walkState.motion = walk;
        walkState.iKOnFeet = true;

        // 달리기 상태는 만들지 않는다 — 보스는 걷기만 하고, 먼 거리는 텔레포트로 좁힌다
        AddTransition(idleState, walkState, AnimatorConditionMode.Greater, MoveThreshold, "Speed", 0.12f);
        AddTransition(walkState, idleState, AnimatorConditionMode.Less, MoveThreshold, "Speed", 0.15f);

        // 사망: 어느 상태에서든 Die 트리거로 진입(되살아나면 Idle을 직접 Play)
        if (death != null)
        {
            var deathState = sm.AddState("Death", new Vector3(520, 220, 0));
            deathState.motion = death;
            var t = sm.AddAnyStateTransition(deathState);
            t.hasExitTime = false;
            t.hasFixedDuration = true;
            t.duration = 0.12f;
            t.canTransitionToSelf = false;
            t.AddCondition(AnimatorConditionMode.If, 0f, "Die");
        }
        else Debug.LogWarning("[Boss] Dying.fbx 클립을 로드하지 못해 사망 상태를 건너뜁니다.");

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    private static void AddTransition(AnimatorState from, AnimatorState to,
                                      AnimatorConditionMode mode, float threshold, string param, float duration)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = false;
        t.hasFixedDuration = true;
        t.duration = duration;
        t.AddCondition(mode, threshold, param);
    }

    // ---------- 클립 임포트 설정 ----------

    /// <summary>루프 여부와 "발 기준 Y 고정"을 맞춘다(리그 설정은 건드리지 않는다).</summary>
    private static void EnsureClipSettings(string fbxPath, bool loop)
    {
        var imp = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (imp == null) { Debug.LogWarning($"[Boss] FBX 임포터 없음: {fbxPath}"); return; }

        var clips = imp.clipAnimations;
        if (clips == null || clips.Length == 0) clips = imp.defaultClipAnimations;
        if (clips == null || clips.Length == 0) return;

        bool changed = false;
        foreach (var c in clips)
        {
            if (c.loopTime != loop) { c.loopTime = loop; changed = true; }
            // applyRootMotion=false에서 발이 뜨거나 파묻히지 않도록 Y를 발 기준으로 포즈에 굽는다
            if (!c.lockRootHeightY || c.keepOriginalPositionY || !c.heightFromFeet)
            {
                c.lockRootHeightY = true;
                c.keepOriginalPositionY = false;
                c.heightFromFeet = true;
                changed = true;
            }
        }
        if (changed)
        {
            imp.clipAnimations = clips;
            imp.SaveAndReimport();
        }
    }

    /// <summary>Humanoid가 아니면 자체 스켈레톤 기준으로 Humanoid 재임포트(플레이어 클립용).</summary>
    private static void EnsureHumanoid(string fbxPath)
    {
        var imp = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (imp == null || imp.animationType == ModelImporterAnimationType.Human) return;
        imp.animationType = ModelImporterAnimationType.Human;
        imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        imp.sourceAvatar = null;
        imp.SaveAndReimport();
    }

    /// <summary>FBX의 첫 애니메이션 클립(프리뷰/카메라 트랙 제외).</summary>
    private static AnimationClip LoadClip(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (!(a is AnimationClip clip)) continue;
            if (clip.name.StartsWith("__preview__")) continue;
            // 모델 FBX에는 캐릭터 동작 외에 카메라 트랙이 섞여 있을 수 있다
            if (clip.name.IndexOf("Camera", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
            return clip;
        }
        return null;
    }

    private static Avatar LoadAvatar(string path)
    {
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(path))
            if (a is Avatar av) return av;
        return null;
    }

    // ---------- 크기/콜라이더 ----------

    /// <summary>렌더러 실측 키가 targetHeight가 되도록 루트 스케일을 맞춘다.</summary>
    private static void FitHeight(GameObject go, float targetHeight)
    {
        if (!TryGetRendererBounds(go, out Bounds b) || b.size.y < 1e-5f) return;
        float factor = targetHeight / b.size.y;
        if (factor > 0.98f && factor < 1.02f) return;
        go.transform.localScale *= factor;
        Debug.Log($"[Boss] 크기 보정: {b.size.y:F3}m → {targetHeight:F3}m (×{factor:F4})");
    }

    /// <summary>렌더러 실측으로 CharacterController(캡슐 바닥 = 루트 원점)를 맞춘다.</summary>
    private static void FitCharacterController(GameObject go, CharacterController cc)
    {
        if (!TryGetRendererBounds(go, out Bounds b)) return;

        Vector3 ls = go.transform.lossyScale;
        float sx = Mathf.Max(Mathf.Abs(ls.x), 1e-5f);
        float sy = Mathf.Max(Mathf.Abs(ls.y), 1e-5f);
        float sz = Mathf.Max(Mathf.Abs(ls.z), 1e-5f);

        cc.height = b.size.y / sy;
        cc.radius = Mathf.Min(Mathf.Max(b.size.x, b.size.z) * 0.5f / Mathf.Max(sx, sz), cc.height * 0.4f);

        Vector3 localCenter = go.transform.InverseTransformPoint(b.center);
        cc.center = new Vector3(localCenter.x, cc.height * 0.5f, localCenter.z);
        cc.stepOffset = cc.height * 0.3f;
        cc.skinWidth = Mathf.Max(cc.radius * 0.1f, 0.0001f);
        cc.minMoveDistance = 0f;
        cc.enabled = true;
        EditorUtility.SetDirty(cc);
    }

    private static bool TryGetRendererBounds(GameObject root, out Bounds bounds)
    {
        bounds = new Bounds();
        bool has = false;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer) continue;
            if (!has) { bounds = r.bounds; has = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return has;
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }
}
