using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;

/// <summary>
/// Player 애니메이터(로코모션) 자동 구성 도구.
///
/// 만드는 상태 흐름:
///   Idle ──(Speed>0.1)──▶ Walk ──(IsRunning)──▶ Idle To Running ──(끝나면)──▶ Rifle Run
///     ▲                    │  ▲                      │                          │
///     └──(Speed<0.1)───────┘  └──(!IsRunning)◀───────┴──────────────────────────┘
///   (Walk/Run에서 Speed<0.1이면 Idle로 복귀)
///
/// - PlayerController가 넘기는 파라미터(Speed:float, IsRunning:bool, IsAiming:bool, Roll:trigger)를 사용한다.
///   IsRunning = 이동 중 Shift를 누르면 true (기본 즉시; 걷기/정지→달리기 트리거).
///   Roll = 달리는 중 C키 → Rifle Run에서 Running Dive Roll로 1회 전환.
/// - 캐릭터는 이동 방향으로 몸을 회전(PlayerController)하므로, 정면/대각선 이동 모두
///   같은 전방 클립으로 표현되고 방향은 몸 회전이 담당한다.
/// - 필요한 FBX(Walk Forward/Idle To Running/Rifle Run 등)가 Generic이면 Humanoid로
///   재임포트하고(자체 스켈레톤에서 아바타 생성), 루프 여부도 맞춘다.
///
/// 메뉴: Tools/TPS/Build Player Animator
/// </summary>
public static class PlayerAnimatorSetup
{
    private const string AnimDir = "Assets/Resources/Player/Animation/";
    private const string ControllerPath = AnimDir + "Player.controller";

    private const string IdleFbx = AnimDir + "Rifle Aiming Idle.fbx"; // 대기(휴머노이드)
    private const string WalkFbx = AnimDir + "Walk Forward.fbx";       // 걷기 루프
    private const string StartRunFbx = AnimDir + "Idle To Running.fbx"; // 출발 동작
    private const string RunFbx = AnimDir + "Rifle Run.fbx";           // 달리기 루프
    private const string RollFbx = AnimDir + "Running Dive Roll.fbx";   // 다이브 롤(C)
    private const string ReloadFbx = AnimDir + "Reloading.fbx";          // 재장전(상체 레이어)
    private const string FireFbx = AnimDir + "Aiming Firing Rifle.fbx";  // 발사(상체 레이어)

    private const float MoveThreshold = 0.1f; // Speed 이동 판정 임계값

    [MenuItem("Tools/TPS/Build Player Animator")]
    public static void BuildMenu()
    {
        if (EditorUtility.DisplayDialog("Player 애니메이터 구성",
                "Player.controller에 Idle/Walk/Idle To Running/Rifle Run/Running Dive Roll 로코모션을 만듭니다.\n" +
                "필요한 FBX는 Humanoid로 재임포트됩니다. 계속할까요?", "실행", "취소"))
            Build();
    }

    public static void Build()
    {
        try
        {
            // 1) 필요한 FBX를 Humanoid로 맞추고 루프를 설정한다.
            //    각 클립은 자기 스켈레톤에서 아바타를 생성(CreateFromThisModel)한다.
            //    mixamo 표준 스켈레톤은 자동 매핑되며, Player Animator가 Idle.fbx 아바타를
            //    쓰므로 런타임에 휴머노이드 리타게팅으로 정상 재생된다.
            // 모든 클립의 Y를 발 기준으로 포즈에 굽는다(bakeYToFeet).
            // applyRootMotion=false라 루트 Y가 버려지는데, 원본(Original) 기준이면
            // 믹사모 클립의 원본 Y 오프셋이 남아 캐릭터가 공중에 떠 보인다.
            EnsureHumanoidClip(IdleFbx, loop: true, bakeYToFeet: true);      // 대기 = 반복
            EnsureHumanoidClip(WalkFbx, loop: true, bakeYToFeet: true);      // 걷기 = 반복
            EnsureHumanoidClip(StartRunFbx, loop: false, bakeYToFeet: true); // 출발 = 1회
            EnsureHumanoidClip(RunFbx, loop: true, bakeYToFeet: true);       // 달리기 = 반복
            EnsureHumanoidClip(RollFbx, loop: false, bakeYToFeet: true);     // 다이브 롤 = 1회
            EnsureHumanoidClip(ReloadFbx, loop: false, bakeYToFeet: true);   // 재장전 = 1회(상체 전용)
            EnsureHumanoidClip(FireFbx, loop: false, bakeYToFeet: true);     // 발사 = 1회(상체 전용)

            // 2) 클립 로드
            AnimationClip idleClip = LoadClip(IdleFbx);
            AnimationClip walkClip = LoadClip(WalkFbx);
            AnimationClip startRunClip = LoadClip(StartRunFbx);
            AnimationClip runClip = LoadClip(RunFbx);
            AnimationClip rollClip = LoadClip(RollFbx);
            AnimationClip reloadClip = LoadClip(ReloadFbx);
            AnimationClip fireClip = LoadClip(FireFbx);
            if (walkClip == null || startRunClip == null || runClip == null)
            {
                Debug.LogError("[TPS-Anim] Walk Forward / Idle To Running / Rifle Run 클립을 로드하지 못했습니다.");
                return;
            }

            // 3) 컨트롤러 로드(없으면 생성) 후 초기화
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            // 파라미터 재구성(중복 방지)
            //  Speed     : 이동 여부(0=정지). PlayerController가 이동 속도×입력크기로 세팅
            //  IsRunning : Shift를 이동 중 1초 이상 유지하면 true (걷기→달리기 전환 트리거)
            //  IsAiming  : 조준 여부
            for (int i = controller.parameters.Length - 1; i >= 0; i--)
                controller.RemoveParameter(i);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("IsRunning", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsAiming", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Roll", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Fire", AnimatorControllerParameterType.Trigger); // PlayerShooter가 발사 시 호출(경고 방지)
            controller.AddParameter("Reload", AnimatorControllerParameterType.Trigger); // PlayerShooter가 재장전 시 호출

            // 루트 스테이트머신 상태 초기화
            var sm = controller.layers[0].stateMachine;
            var existing = sm.states;
            foreach (var cs in existing) sm.RemoveState(cs.state);
            sm.anyStateTransitions = new AnimatorStateTransition[0];

            // 4) 상태 생성
            // Foot IK(iKOnFeet): 믹사모 리그 → 이 모델 아바타로 리타게팅될 때 생기는
            // 발 높이 오차를 지면에 다시 심어 보정한다(발이 공중에 뜨는 문제 해결).
            var idle = sm.AddState("Idle", new Vector3(280, 60, 0));
            idle.motion = idleClip; // 대기 클립이 없으면 null(Idle 빈 상태)
            idle.iKOnFeet = true;

            var walk = sm.AddState("Walk", new Vector3(500, 60, 0));
            walk.motion = walkClip;
            walk.iKOnFeet = true;

            var startRun = sm.AddState("Idle To Running", new Vector3(720, 60, 0));
            startRun.motion = startRunClip;
            startRun.speed = 1.4f; // 출발 동작을 빠르게 재생해 달리기 진입을 앞당김
            startRun.iKOnFeet = true;

            var run = sm.AddState("Rifle Run", new Vector3(940, 60, 0));
            run.motion = runClip;
            run.iKOnFeet = true;

            sm.defaultState = idle;

            // 5) 트랜지션
            // Idle → Idle To Running : 정지 상태에서 바로 달리기(Shift+이동)로 출발
            //   (Idle→Walk보다 먼저 평가되도록 앞에 추가)
            var tIdleRun = idle.AddTransition(startRun);
            tIdleRun.hasExitTime = false;
            tIdleRun.hasFixedDuration = true;
            tIdleRun.duration = 0.08f;
            tIdleRun.AddCondition(AnimatorConditionMode.If, 0f, "IsRunning");

            // Idle → Walk : 이동 시작(Speed > 임계값)
            var tWalk = idle.AddTransition(walk);
            tWalk.hasExitTime = false;
            tWalk.hasFixedDuration = true;
            tWalk.duration = 0.10f;
            tWalk.AddCondition(AnimatorConditionMode.Greater, MoveThreshold, "Speed");

            // Walk → Idle : 멈추면 대기로
            var tWalkStop = walk.AddTransition(idle);
            tWalkStop.hasExitTime = false;
            tWalkStop.hasFixedDuration = true;
            tWalkStop.duration = 0.15f;
            tWalkStop.AddCondition(AnimatorConditionMode.Less, MoveThreshold, "Speed");

            // Walk → Idle To Running : 이동 중 Shift 1초+ 유지(IsRunning)
            var tStart = walk.AddTransition(startRun);
            tStart.hasExitTime = false;
            tStart.hasFixedDuration = true;
            tStart.duration = 0.10f;
            tStart.AddCondition(AnimatorConditionMode.If, 0f, "IsRunning");

            // Idle To Running → Rifle Run : 출발 동작 중반(50%)에 일찍 달리기로 블렌드
            var tToRun = startRun.AddTransition(run);
            tToRun.hasExitTime = true;
            tToRun.exitTime = 0.5f;
            tToRun.hasFixedDuration = true;
            tToRun.duration = 0.2f;

            // Idle To Running → Walk : 출발 도중 Shift를 떼면 걷기로 취소
            var tStartCancel = startRun.AddTransition(walk);
            tStartCancel.hasExitTime = false;
            tStartCancel.hasFixedDuration = true;
            tStartCancel.duration = 0.15f;
            tStartCancel.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsRunning");

            // Idle To Running → Idle : 출발 도중 멈추면 대기로 취소
            var tCancel = startRun.AddTransition(idle);
            tCancel.hasExitTime = false;
            tCancel.hasFixedDuration = true;
            tCancel.duration = 0.15f;
            tCancel.AddCondition(AnimatorConditionMode.Less, MoveThreshold, "Speed");

            // Rifle Run → Walk : Shift를 떼면(달리기 해제) 걷기로
            var tRunToWalk = run.AddTransition(walk);
            tRunToWalk.hasExitTime = false;
            tRunToWalk.hasFixedDuration = true;
            tRunToWalk.duration = 0.20f;
            tRunToWalk.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsRunning");

            // Rifle Run → Idle : 멈추면 대기로
            var tStop = run.AddTransition(idle);
            tStop.hasExitTime = false;
            tStop.hasFixedDuration = true;
            tStop.duration = 0.20f;
            tStop.AddCondition(AnimatorConditionMode.Less, MoveThreshold, "Speed");

            // 다이브 롤(C): 달리는 중에만 발동. Rifle Run ↔ Running Dive Roll
            if (rollClip != null)
            {
                var roll = sm.AddState("Running Dive Roll", new Vector3(940, 220, 0));
                roll.motion = rollClip;

                // Rifle Run → Running Dive Roll : Roll 트리거
                var tRoll = run.AddTransition(roll);
                tRoll.hasExitTime = false;
                tRoll.hasFixedDuration = true;
                tRoll.duration = 0.08f;
                tRoll.AddCondition(AnimatorConditionMode.If, 0f, "Roll");

                // Running Dive Roll → Rifle Run : 롤이 끝나면 달리기로 복귀
                // (달리기 해제/정지 상태면 Rifle Run의 기존 전환이 Walk/Idle로 이어받음)
                var tRollEnd = roll.AddTransition(run);
                tRollEnd.hasExitTime = true;
                tRollEnd.exitTime = 0.85f;
                tRollEnd.hasFixedDuration = true;
                tRollEnd.duration = 0.15f;
            }
            else
            {
                Debug.LogWarning("[TPS-Anim] Running Dive Roll 클립을 로드하지 못해 롤 상태를 건너뜁니다.");
            }

            // 6) 상체(UpperBody) 오버라이드 레이어: 걷기 중에도 상체는 소총 파지 자세 유지.
            //    (Walk Forward가 맨손 걷기라 팔이 흔들려 총이 공중에 뜬 것처럼 보이는 문제 해결)
            //    재장전(Reload)도 이 레이어에서 재생 → 대기/걷기/달리기 어느 상태든 모션 1개로 처리.
            //    레이어 가중치는 PlayerController가 걷기 또는 재장전 중일 때 1로 올린다.
            while (controller.layers.Length > 1) controller.RemoveLayer(1); // 재실행 중복 방지
            var upperMask = LoadOrCreateUpperBodyMask();
            if (idleClip != null && upperMask != null)
            {
                var usm = new AnimatorStateMachine { name = "UpperBody", hideFlags = HideFlags.HideInHierarchy };
                AssetDatabase.AddObjectToAsset(usm, controller);
                var hold = usm.AddState("Rifle Hold", new Vector3(280, 60, 0));
                hold.motion = idleClip;

                AnimatorState reloadState = null;
                if (reloadClip != null)
                {
                    var reload = usm.AddState("Reload", new Vector3(500, 60, 0));
                    reload.motion = reloadClip;
                    reloadState = reload;

                    // Rifle Hold → Reload : Reload 트리거(PlayerShooter가 발동)
                    var tReload = hold.AddTransition(reload);
                    tReload.hasExitTime = false;
                    tReload.hasFixedDuration = true;
                    tReload.duration = 0.1f;
                    tReload.AddCondition(AnimatorConditionMode.If, 0f, "Reload");

                    // Reload → Rifle Hold : 모션이 끝나면 복귀
                    var tReloadEnd = reload.AddTransition(hold);
                    tReloadEnd.hasExitTime = true;
                    tReloadEnd.exitTime = 0.95f;
                    tReloadEnd.hasFixedDuration = true;
                    tReloadEnd.duration = 0.15f;
                }
                else
                {
                    Debug.LogWarning("[TPS-Anim] Reloading.fbx 클립을 로드하지 못해 재장전 상태를 건너뜁니다.");
                }

                // 발사 모션: Fire 트리거로 한 번 재생 후 파지 자세로 복귀.
                // 상체 레이어라 걷기/달리기 중에도 다리는 그대로 두고 상체만 반동을 준다.
                if (fireClip != null)
                {
                    var fire = usm.AddState("Fire", new Vector3(500, 220, 0));
                    fire.motion = fireClip;

                    var tFire = hold.AddTransition(fire);
                    tFire.hasExitTime = false;
                    tFire.hasFixedDuration = true;
                    tFire.duration = 0.04f;   // 즉발
                    tFire.AddCondition(AnimatorConditionMode.If, 0f, "Fire");

                    var tFireEnd = fire.AddTransition(hold);
                    tFireEnd.hasExitTime = true;
                    tFireEnd.exitTime = 0.7f;
                    tFireEnd.hasFixedDuration = true;
                    tFireEnd.duration = 0.08f;

                    // Fire → Reload : 마지막 발을 쏘면 자동 재장전이 걸리는데, 이 전환이 없으면
                    // 발사 모션이 exitTime(0.7)까지 다 돌기를 기다린 뒤에야 재장전 모션이 시작된다.
                    // 그 1초 남짓 동안 PlayerShooter는 Reload 상태 진입을 못 봐서 타이머 폴백으로
                    // 재장전을 판정하게 되고, 결국 재장전 모션이 끝나기 전에 발사 잠금이 풀린다.
                    // 아래 연사 전환보다 먼저 등록해야 Fire/Reload가 같이 걸렸을 때 재장전이 이긴다.
                    if (reloadState != null)
                    {
                        var tFireReload = fire.AddTransition(reloadState);
                        tFireReload.hasExitTime = false;
                        tFireReload.hasFixedDuration = true;
                        tFireReload.duration = 0.1f;
                        tFireReload.AddCondition(AnimatorConditionMode.If, 0f, "Reload");
                    }

                    // 연사: 발사 모션 중에도 Fire가 다시 오면 처음부터 재생
                    var tFireLoop = fire.AddTransition(fire);
                    tFireLoop.hasExitTime = false;
                    tFireLoop.hasFixedDuration = true;
                    tFireLoop.duration = 0.03f;
                    tFireLoop.AddCondition(AnimatorConditionMode.If, 0f, "Fire");
                }
                else
                {
                    Debug.LogWarning("[TPS-Anim] Aiming Firing Rifle.fbx 클립을 로드하지 못해 발사 상태를 건너뜁니다.");
                }

                controller.AddLayer(new AnimatorControllerLayer
                {
                    name = "UpperBody",
                    avatarMask = upperMask,
                    defaultWeight = 0f,
                    blendingMode = AnimatorLayerBlendingMode.Override,
                    stateMachine = usm
                });
            }

            // IK Pass 켜기: PlayerController.OnAnimatorIK(시선 LookAt IK)가 호출되기 위한 조건.
            // controller.layers는 복사본을 반환하므로 수정 후 반드시 재대입해야 한다.
            var layersArr = controller.layers;
            layersArr[0].iKPass = true;
            controller.layers = layersArr;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            // 7) 씬에 Player가 있으면 Animator에 컨트롤러 연결 + 루트모션 끄기
            //    (이동은 CharacterController가 담당하므로 루트모션이 켜지면 이중 이동)
            WireSceneAnimator(controller);

            Debug.Log("<color=lime>[TPS-Anim] Player 애니메이터 구성 완료.</color> " +
                      "이동=Walk, Shift=즉시 Idle To Running→Rifle Run, C=Running Dive Roll 로 연결되었습니다.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[TPS-Anim] 구성 중 오류: {e}");
        }
    }

    // ---------- 헬퍼 ----------

    /// <summary>
    /// FBX를 Humanoid(자체 스켈레톤에서 아바타 생성) + 루프 설정으로 맞춘다.
    /// 이전에 원본 없는 "Copy From Other" 상태로 깨져 있어도 CreateFromThisModel로 복구된다.
    /// </summary>
    private static void EnsureHumanoidClip(string fbxPath, bool loop, bool bakeYToFeet = false)
    {
        var imp = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (imp == null) { Debug.LogWarning($"[TPS-Anim] FBX 임포터 없음: {fbxPath}"); return; }

        bool changed = false;

        if (imp.animationType != ModelImporterAnimationType.Human)
        {
            imp.animationType = ModelImporterAnimationType.Human;
            changed = true;
        }
        // 자체 스켈레톤 기반 아바타 생성(원본 아바타 참조 불필요 → 복사 실패 오류 회피)
        if (imp.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel || imp.sourceAvatar != null)
        {
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.sourceAvatar = null;
            changed = true;
        }

        // 루프/루트 설정: 명시적 클립 정의가 없으면 기본 클립 정의를 가져와 수정
        var clips = imp.clipAnimations;
        if (clips == null || clips.Length == 0) clips = imp.defaultClipAnimations;
        if (clips != null && clips.Length > 0)
        {
            bool clipChanged = false;
            foreach (var c in clips)
            {
                if (c.loopTime != loop) { c.loopTime = loop; clipChanged = true; }

                // 발을 지면에 고정: Root Transform Position(Y) = Bake Into Pose + Based Upon Feet
                // (applyRootMotion=false 상태에서 몸이 땅에 박히거나 뜨는 것을 방지)
                if (bakeYToFeet && (!c.lockRootHeightY || c.keepOriginalPositionY || !c.heightFromFeet))
                {
                    c.lockRootHeightY = true;       // Bake Into Pose (Y)
                    c.keepOriginalPositionY = false;
                    c.heightFromFeet = true;        // Based Upon = Feet
                    clipChanged = true;
                }
            }
            if (clipChanged) { imp.clipAnimations = clips; changed = true; }
        }

        if (changed) imp.SaveAndReimport();

        // 재임포트 후 아바타 유효성 확인
        var av = LoadAvatar(fbxPath);
        if (av == null || !av.isValid || !av.isHuman)
            Debug.LogWarning($"[TPS-Anim] {fbxPath} 의 Humanoid 아바타가 유효하지 않습니다. " +
                             "Inspector의 Rig 탭에서 Configure로 본 매핑을 확인하세요.");
    }

    /// <summary>상체(몸통/머리/양팔/손가락)만 활성화한 아바타 마스크를 로드하거나 생성.</summary>
    private static AvatarMask LoadOrCreateUpperBodyMask()
    {
        const string path = AnimDir + "UpperBody.mask";
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
        if (mask == null)
        {
            mask = new AvatarMask();
            AssetDatabase.CreateAsset(mask, path);
        }
        for (var p = AvatarMaskBodyPart.Root; p < AvatarMaskBodyPart.LastBodyPart; p++)
            mask.SetHumanoidBodyPartActive(p, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightHandIK, true);
        EditorUtility.SetDirty(mask);
        return mask;
    }

    private static Avatar LoadAvatar(string path)
    {
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(path))
            if (a is Avatar av) return av;
        return null;
    }

    private static AnimationClip LoadClip(string path)
    {
        // 재임포트 직후 서브에셋이 아직 등록 전이라 null이 나오는 것을 방지:
        // 강제 동기 임포트 후 로드한다.
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (a is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                return clip;
        }
        return null;
    }

    /// <summary>
    /// 씬의 Player Animator에 컨트롤러/아바타를 연결하고, PlayerController의 animator 참조도 물려준다.
    /// (Player가 씬에 없으면 조용히 건너뜀 → 컨트롤러 에셋만 갱신됨)
    /// </summary>
    private static void WireSceneAnimator(AnimatorController controller)
    {
        var player = GameObject.Find("Player");
        if (player == null)
        {
            Debug.Log("[TPS-Anim] 씬에 'Player'가 없어 컨트롤러 에셋만 갱신했습니다. " +
                      "Player가 있는 씬을 연 뒤 다시 실행하면 Animator까지 자동 연결됩니다.");
            return;
        }

        var anim = player.GetComponentInChildren<Animator>();
        if (anim == null)
        {
            Debug.LogWarning("[TPS-Anim] Player에 Animator가 없습니다.");
            return;
        }

        // 컨트롤러 연결
        if (anim.runtimeAnimatorController != controller)
            anim.runtimeAnimatorController = controller;

        // 아바타 연결(비어 있으면 Player 모델 아바타 사용)
        if (anim.avatar == null)
        {
            var av = LoadAvatar("Assets/Resources/Player/source/Idle.fbx");
            if (av != null) anim.avatar = av;
        }

        anim.applyRootMotion = false; // 이동은 CharacterController가 처리
        EditorUtility.SetDirty(anim);

        // PlayerController.animator 참조 + aimMask(자기 레이어 제외) 연결
        var pc = player.GetComponent<PlayerController>();
        if (pc != null)
        {
            var so = new SerializedObject(pc);
            var p = so.FindProperty("animator");
            if (p != null) p.objectReferenceValue = anim;
            var mask = so.FindProperty("aimMask");
            if (mask != null) mask.intValue = ~(1 << player.layer); // 조준 레이가 자기 몸에 막히지 않게
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // PlayerShooter: 조준(우클릭) 중에만 발사 허용
        var shooter = player.GetComponent<PlayerShooter>();
        if (shooter != null)
        {
            var sso = new SerializedObject(shooter);
            var rq = sso.FindProperty("requireAimToFire");
            if (rq != null) rq.boolValue = true;
            sso.ApplyModifiedPropertiesWithoutUndo();
        }

        // 스탯(체력/기력/타임포스)과 HUD가 없으면 추가
        if (!player.TryGetComponent<PlayerStats>(out _))
            player.AddComponent<PlayerStats>();
        if (Object.FindFirstObjectByType<HudUI>() == null)
        {
            var hud = new GameObject("GameHUD");
            hud.AddComponent<HudUI>();
        }

        // 시간 능력: 5초 전 고스트 + T 선택(좌=시간역행 / 우=시간공명)
        if (!player.TryGetComponent<PlayerTimeGhost>(out _))
            player.AddComponent<PlayerTimeGhost>();
        if (!player.TryGetComponent<TimeShiftController>(out _))
            player.AddComponent<TimeShiftController>();

        // 시간역행 대상: 표적/적에 TimeRewindable 자동 부착(앞으로 만들 보스도 이 컴포넌트만 붙이면 됨)
        foreach (var dummy in Object.FindObjectsByType<TargetDummy>(FindObjectsSortMode.None))
            if (!dummy.TryGetComponent<TimeRewindable>(out _))
                dummy.gameObject.AddComponent<TimeRewindable>();

        // 씬 저장
        var scene = player.scene;
        if (scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        Debug.Log("[TPS-Anim] 씬 Player Animator에 컨트롤러/아바타 연결 완료(applyRootMotion=false).");
    }
}
