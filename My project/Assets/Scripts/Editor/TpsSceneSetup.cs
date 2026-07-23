using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// TPS 씬을 자동으로 구성/연결하는 에디터 도구.
/// - Player/Gun FBX 인스턴스화, 컴포넌트 추가 및 모든 참조 자동 연결
/// - Main Camera에 TPS 카메라 세팅
/// - Player 레이어 생성 후 카메라 충돌/사격 마스크에서 제외
/// - 바닥/테스트 표적 생성
/// 메뉴: Tools/TPS/Setup Scene (Rebuild)
/// 처음 컴파일 후 에디터로 돌아오면 자동 실행 여부를 한 번 물어본다.
/// </summary>
[InitializeOnLoad]
public static class TpsSceneSetup
{
    private const string PlayerFbxPath = "Assets/Resources/Player/source/Idle.fbx";
    private const string GunFbxPath = "Assets/Resources/Gun/source/assault_rifle.fbx";
    private const string HandBoneName = "mixamorig:RightHand";
    private const string AutoRunKey = "TPS_AutoSetup_Done_v1";

    // ---- 최초 1회 자동 프롬프트 ----
    static TpsSceneSetup()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (SessionState.GetBool(AutoRunKey, false)) return;
            if (EditorPrefs.GetBool(AutoRunKey, false)) return;
            SessionState.SetBool(AutoRunKey, true);

            bool run = EditorUtility.DisplayDialog(
                "TPS 자동 세팅",
                "TPS 씬을 자동으로 구성하고 모든 스크립트를 연결할까요?\n" +
                "(Player/Gun 배치, 카메라, 이동/조준/발사, 테스트 표적)",
                "실행", "나중에");
            if (run)
            {
                SetupScene();
                EditorPrefs.SetBool(AutoRunKey, true);
            }
        };
    }

    [MenuItem("Tools/TPS/Setup Scene (Rebuild)")]
    public static void SetupSceneMenu()
    {
        if (EditorUtility.DisplayDialog("TPS 세팅",
                "현재 씬에 TPS 구성을 만듭니다. 계속할까요?", "실행", "취소"))
            SetupScene();
    }

    public static void SetupScene()
    {
        try
        {
            EnsurePlayerRigIsHumanoid();
            int playerLayer = EnsureLayer("Player");
            int playerMask = 1 << playerLayer;

            // ---- 바닥 ----
            EnsureGround();

            // ---- Player ----
            GameObject playerFbx = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerFbxPath);
            if (playerFbx == null) { Debug.LogError($"[TPS] Player FBX 못 찾음: {PlayerFbxPath}"); return; }

            GameObject player = FindOrCreatePlayer(playerFbx);
            player.transform.position = new Vector3(0f, 0f, 0f);
            SetLayerRecursive(player, playerLayer);

            var cc = GetOrAdd<CharacterController>(player);
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.height = 1.8f;
            cc.radius = 0.3f;

            var weaponHolder = GetOrAdd<WeaponHolder>(player);
            var controller = GetOrAdd<PlayerController>(player);
            var shooter = GetOrAdd<PlayerShooter>(player);

            // ---- Gun ----
            GameObject gunFbx = AssetDatabase.LoadAssetAtPath<GameObject>(GunFbxPath);
            GameObject gun = null;
            Transform muzzle = null;
            Transform hand = FindDeepChild(player.transform, HandBoneName);
            if (gunFbx != null)
            {
                // 재실행 시 중복 생성 방지: 손 밑에 이미 있으면 재사용
                Transform existingGun = hand != null ? FindDeepChild(hand, "Gun") : null;
                if (existingGun == null) existingGun = FindDeepChild(player.transform, "Gun");

                bool newlyCreated = existingGun == null;
                if (!newlyCreated)
                {
                    gun = existingGun.gameObject;
                }
                else
                {
                    gun = (GameObject)PrefabUtility.InstantiatePrefab(gunFbx);
                    gun.name = "Gun";
                }

                if (hand != null && gun.transform.parent != hand)
                    gun.transform.SetParent(hand, false);

                // 새로 만들 때만 초기 배치(재실행 시 사용자가 맞춘 위치 보존)
                if (newlyCreated && hand != null)
                {
                    gun.transform.localPosition = Vector3.zero;
                    gun.transform.localRotation = Quaternion.identity;
                }

                // 총구 포인트(중복 방지)
                muzzle = FindDeepChild(gun.transform, "Muzzle");
                if (muzzle == null)
                {
                    muzzle = new GameObject("Muzzle").transform;
                    muzzle.SetParent(gun.transform, false);
                    muzzle.localPosition = new Vector3(0f, 0f, 0.5f); // 배럴 앞쪽(대략), 필요시 조정
                }
            }

            // ---- Camera ----
            Camera cam = Camera.main;
            if (cam == null)
            {
                var camObj = GameObject.Find("Main Camera");
                if (camObj != null) cam = camObj.GetComponent<Camera>();
            }
            ThirdPersonCamera tpsCam = null;
            if (cam != null)
            {
                // 태그 보정(Camera.main 인식)
                if (cam.gameObject.tag != "MainCamera") cam.gameObject.tag = "MainCamera";

                // TPS 카메라는 스크립트로만 제어 → 플레이어 등 다른 오브젝트의 자식이면 분리
                if (cam.transform.parent != null) cam.transform.SetParent(null, true);

                tpsCam = GetOrAdd<ThirdPersonCamera>(cam.gameObject);
                var camSo = new SerializedObject(tpsCam);
                SetObj(camSo, "target", player.transform);
                SetInt(camSo, "collisionMask", ~playerMask); // Player 제외
                camSo.ApplyModifiedPropertiesWithoutUndo();

                // 에디트 모드 Game 뷰에서도 3인칭으로 보이도록 카메라를 플레이어 뒤에 배치
                const float pivotHeight = 1.5f;
                const float distance = 4f;
                Vector3 pivot = player.transform.position + Vector3.up * pivotHeight;
                Vector3 back = -player.transform.forward;
                cam.transform.position = pivot + back * distance + Vector3.up * 0.3f;
                cam.transform.rotation = Quaternion.LookRotation((pivot - cam.transform.position).normalized);
            }
            else Debug.LogWarning("[TPS] Main Camera를 찾지 못했습니다. 카메라 세팅을 건너뜁니다.");

            // ---- WeaponHolder 참조 ----
            {
                var so = new SerializedObject(weaponHolder);
                SetString(so, "handBoneName", HandBoneName);
                if (gun != null)
                {
                    SetObj(so, "existingWeapon", gun);
                    // 런타임 재적용 시 에디터에서 맞춘 위치/스케일이 유지되도록 직렬화 값에 반영
                    SetVec3(so, "localPosition", gun.transform.localPosition);
                    SetVec3(so, "localEulerAngles", gun.transform.localEulerAngles);
                    SetVec3(so, "localScale", gun.transform.localScale);
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // ---- PlayerController 참조 ----
            {
                var so = new SerializedObject(controller);
                if (tpsCam != null) SetObj(so, "tpsCamera", tpsCam);
                var anim = player.GetComponentInChildren<Animator>();
                if (anim != null) SetObj(so, "animator", anim);
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // ---- PlayerShooter 참조 ----
            {
                var so = new SerializedObject(shooter);
                if (tpsCam != null) SetObj(so, "tpsCamera", tpsCam);
                if (cam != null) SetObj(so, "aimCamera", cam);
                if (muzzle != null) SetObj(so, "muzzlePoint", muzzle);
                var anim = player.GetComponentInChildren<Animator>();
                if (anim != null) SetObj(so, "animator", anim);
                SetInt(so, "hitMask", ~playerMask); // Player 제외(자기 몸 안 맞게)
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // ---- 실제 모델 크기 측정 후 콜라이더/총 자동 보정 ----
            AutoFitPlayerAndGun(player, cc, gun);

            // ---- 테스트 표적 ----
            EnsureTargetDummy();

            // ---- 저장 ----
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = player;

            Debug.Log("<color=lime>[TPS] 씬 자동 세팅 완료.</color> Play를 눌러 이동(WASD)·조준(우클릭)·발사(좌클릭)를 확인하세요.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[TPS] 세팅 중 오류: {e}");
        }
    }

    // ---------- 모델 크기 측정 / 자동 보정 ----------

    /// <summary>메뉴에서 단독 실행: 현재 씬의 Player/Gun을 실측해 콜라이더·총 크기를 맞춘다.</summary>
    [MenuItem("Tools/TPS/Auto-Fit Colliders and Gun")]
    public static void AutoFitMenu()
    {
        var player = GameObject.Find("Player");
        if (player == null) { Debug.LogError("[TPS] 'Player' 오브젝트를 찾지 못했습니다."); return; }
        var cc = player.GetComponent<CharacterController>();
        if (cc == null) cc = player.AddComponent<CharacterController>();
        var gunT = FindDeepChild(player.transform, "Gun");
        AutoFitPlayerAndGun(player, cc, gunT != null ? gunT.gameObject : null);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
    }

    /// <summary>메뉴에서 단독 실행: 모델 실측 정보를 콘솔에 출력(진단용).</summary>
    [MenuItem("Tools/TPS/Log Model Info")]
    public static void LogModelInfo()
    {
        var player = GameObject.Find("Player");
        if (player == null) { Debug.LogError("[TPS] 'Player' 없음"); return; }

        var gunT = FindDeepChild(player.transform, "Gun");
        bool hasP = TryGetRendererBounds(player, gunT, out Bounds pB);
        Debug.Log($"[TPS] Player: rootLocalScale={player.transform.localScale}, lossyScale={player.transform.lossyScale}\n" +
                  (hasP ? $"  월드 바운즈 size={pB.size} (키={pB.size.y:F3}m), center={pB.center}" : "  Renderer 없음"));

        var hand = FindDeepChild(player.transform, HandBoneName);
        if (hand != null)
            Debug.Log($"[TPS] {HandBoneName}: lossyScale={hand.lossyScale}");

        if (gunT != null)
        {
            bool hasG = TryGetRendererBounds(gunT.gameObject, null, out Bounds gB);
            Debug.Log($"[TPS] Gun: localScale={gunT.localScale}, lossyScale={gunT.lossyScale}\n" +
                      (hasG ? $"  월드 바운즈 size={gB.size} (최대변={Mathf.Max(gB.size.x, gB.size.y, gB.size.z):F3}m)" : "  Renderer 없음"));
        }
    }

    /// <summary>Player Renderer를 실측해 CharacterController를 맞추고, Gun을 캐릭터 대비 적정 크기로 스케일한다.</summary>
    private static void AutoFitPlayerAndGun(GameObject player, CharacterController cc, GameObject gun)
    {
        if (player == null) return;

        Transform gunT = gun != null ? gun.transform : FindDeepChild(player.transform, "Gun");

        // --- Player 실측 (총 제외) ---
        if (cc != null && TryGetRendererBounds(player, gunT, out Bounds pB))
        {
            Vector3 ls = player.transform.lossyScale;
            float sx = Mathf.Abs(ls.x), sy = Mathf.Abs(ls.y), sz = Mathf.Abs(ls.z);
            sx = Mathf.Max(sx, 1e-5f); sy = Mathf.Max(sy, 1e-5f); sz = Mathf.Max(sz, 1e-5f);

            float worldHeight = pB.size.y;
            float worldRadius = Mathf.Max(pB.size.x, pB.size.z) * 0.5f;

            // CharacterController 값은 로컬(프리스케일). 스케일로 나눠 월드 크기를 맞춘다.
            cc.height = worldHeight / sy;
            cc.radius = Mathf.Min(worldRadius / Mathf.Max(sx, sz), cc.height * 0.4f);

            // 중심: 수평은 바운즈 중심, 수직은 캡슐 '바닥'을 루트 원점(=발밑)에 맞춘다.
            // (애니메이션을 Root Transform Y "Based Upon Feet"로 구우면 발이 루트 높이에 오므로,
            //  캡슐 바닥을 루트 원점에 두면 발이 정확히 지면에 닿는다. T-포즈 발 위치로 잡으면 애니메이션 후 뜬다.)
            Vector3 localCenter = player.transform.InverseTransformPoint(pB.center);
            cc.center = new Vector3(localCenter.x, cc.height * 0.5f, localCenter.z);

            // stepOffset / skinWidth 등도 크기에 비례해서 설정(안 그러면 "Step Offset must be ..." 오류로 CC 비활성)
            cc.stepOffset = cc.height * 0.3f;           // 항상 height + 2*radius 이하 → 유효
            cc.skinWidth = Mathf.Max(cc.radius * 0.1f, 0.0001f);
            cc.minMoveDistance = 0f;
            cc.enabled = true;

            Debug.Log($"[TPS] CharacterController 실측 적용: worldHeight={worldHeight:F3}m, worldRadius={worldRadius:F3}m " +
                      $"→ height={cc.height:F3}, radius={cc.radius:F3}, step={cc.stepOffset:F3}, skin={cc.skinWidth:F4}, center={cc.center}");

            // === 캐릭터 실제 키에 맞춰 카메라·이동·중력을 비례 조정 (모델 스케일 무관하게 동작) ===
            TuneCameraAndMovementToScale(player, worldHeight);
        }

        // --- Gun 크기 보정: 캐릭터 키의 약 0.55배 길이로 ---
        if (gunT != null && TryGetRendererBounds(player, gunT, out Bounds playerB)
            && TryGetRendererBounds(gunT.gameObject, null, out Bounds gunB))
        {
            float gunLen = Mathf.Max(gunB.size.x, gunB.size.y, gunB.size.z);
            float targetLen = playerB.size.y * 0.55f;
            if (gunLen > 1e-5f && targetLen > 1e-5f)
            {
                float factor = targetLen / gunLen;
                // 과보정 방지: 이미 비슷하면(0.8~1.25배) 건드리지 않음
                if (factor < 0.8f || factor > 1.25f)
                {
                    gunT.localScale *= factor;
                    Debug.Log($"[TPS] Gun 크기 보정: 현재 길이={gunLen:F3}m → 목표={targetLen:F3}m (×{factor:F4}) " +
                              $"newLocalScale={gunT.localScale}");
                }
            }
        }
    }

    /// <summary>root 하위 Renderer들의 합집합 월드 바운즈. exclude 하위는 제외.</summary>
    private static bool TryGetRendererBounds(GameObject root, Transform exclude, out Bounds bounds)
    {
        bounds = new Bounds();
        bool has = false;
        var renderers = root.GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
        {
            if (r is ParticleSystemRenderer) continue;
            if (exclude != null && (r.transform == exclude || r.transform.IsChildOf(exclude))) continue;
            if (!has) { bounds = r.bounds; has = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return has;
    }

    /// <summary>캐릭터 실제 키(worldHeight)를 1.8m 기준으로 환산해 카메라 거리·이동 속도·중력을 비례 조정.</summary>
    private static void TuneCameraAndMovementToScale(GameObject player, float worldHeight)
    {
        if (worldHeight < 1e-4f) return;
        float k = worldHeight / 1.8f; // 1.8m 사람 기준 배율

        // --- 카메라 ---
        var cam = Camera.main;
        var tpsCam = cam != null ? cam.GetComponent<ThirdPersonCamera>() : null;
        if (tpsCam != null)
        {
            var so = new SerializedObject(tpsCam);
            SetFloat(so, "pivotHeight", worldHeight * 0.83f);
            SetFloat(so, "normalDistance", worldHeight * 2.2f);
            SetFloat(so, "aimDistance", worldHeight * 1.15f);
            SetFloat(so, "collisionRadius", worldHeight * 0.11f);
            SetVec2(so, "normalShoulder", new Vector2(worldHeight * 0.33f, 0f));
            SetVec2(so, "aimShoulder", new Vector2(worldHeight * 0.44f, worldHeight * 0.06f));
            so.ApplyModifiedPropertiesWithoutUndo();

            // 에디트 모드 프리뷰 위치도 새 스케일로 재배치
            float pivotH = worldHeight * 0.83f;
            Vector3 pivot = player.transform.position + Vector3.up * pivotH;
            Vector3 back = -player.transform.forward;
            cam.transform.position = pivot + back * (worldHeight * 2.2f) + Vector3.up * (worldHeight * 0.15f);
            cam.transform.rotation = Quaternion.LookRotation((pivot - cam.transform.position).normalized);
        }

        // --- 이동/중력 ---
        var pc = player.GetComponent<PlayerController>();
        if (pc != null)
        {
            var so = new SerializedObject(pc);
            SetFloat(so, "walkSpeed", 2.5f * k);
            SetFloat(so, "runSpeed", 5.5f * k);
            SetFloat(so, "aimSpeed", 2f * k);
            SetFloat(so, "gravity", -20f * k);
            SetFloat(so, "jumpHeight", 1.2f * k);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        Debug.Log($"[TPS] 스케일 비례 튜닝(배율 k={k:F3}): 카메라 거리≈{worldHeight * 2.2f:F2}m, 걷기≈{2.5f * k:F2}m/s");
    }

    // ---------- 헬퍼 ----------

    private static GameObject FindOrCreatePlayer(GameObject playerFbx)
    {
        var existing = GameObject.Find("Player");
        if (existing != null) return existing;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(playerFbx);
        go.name = "Player";
        return go;
    }

    private static void EnsureGround()
    {
        if (GameObject.Find("Ground") != null) return;
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.position = Vector3.zero;
        ground.transform.localScale = new Vector3(5f, 1f, 5f);
    }

    private static void EnsureTargetDummy()
    {
        if (GameObject.Find("TargetDummy") != null) return;
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "TargetDummy";
        cube.transform.position = new Vector3(0f, 0.5f, 6f);
        cube.AddComponent<TargetDummy>();
    }

    private static void EnsurePlayerRigIsHumanoid()
    {
        var importer = AssetImporter.GetAtPath(PlayerFbxPath) as ModelImporter;
        if (importer != null && importer.animationType != ModelImporterAnimationType.Human)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.SaveAndReimport();
        }
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.transform)
            SetLayerRecursive(t.gameObject, layer);
    }

    private static Transform FindDeepChild(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child.name == name) return child;
            var found = FindDeepChild(child, name);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>비어 있는 첫 User 레이어(8~31)에 이름을 등록하고 인덱스를 반환.</summary>
    private static int EnsureLayer(string layerName)
    {
        var tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tagManager.FindProperty("layers");

        // 이미 존재?
        for (int i = 0; i < layers.arraySize; i++)
        {
            var p = layers.GetArrayElementAtIndex(i);
            if (p.stringValue == layerName) return i;
        }
        // 빈 User 슬롯(8~31)에 등록
        for (int i = 8; i < layers.arraySize; i++)
        {
            var p = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(p.stringValue))
            {
                p.stringValue = layerName;
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                return i;
            }
        }
        Debug.LogWarning("[TPS] 빈 User 레이어가 없어 Default(0)를 사용합니다.");
        return 0;
    }

    private static void SetObj(SerializedObject so, string field, Object value)
    {
        var p = so.FindProperty(field);
        if (p != null) p.objectReferenceValue = value;
        else Debug.LogWarning($"[TPS] 필드 없음: {field}");
    }
    private static void SetInt(SerializedObject so, string field, int value)
    {
        var p = so.FindProperty(field);
        if (p != null) p.intValue = value;
    }
    private static void SetString(SerializedObject so, string field, string value)
    {
        var p = so.FindProperty(field);
        if (p != null) p.stringValue = value;
    }
    private static void SetVec3(SerializedObject so, string field, Vector3 value)
    {
        var p = so.FindProperty(field);
        if (p != null) p.vector3Value = value;
    }
    private static void SetVec2(SerializedObject so, string field, Vector2 value)
    {
        var p = so.FindProperty(field);
        if (p != null) p.vector2Value = value;
    }
    private static void SetFloat(SerializedObject so, string field, float value)
    {
        var p = so.FindProperty(field);
        if (p != null) p.floatValue = value;
    }
}
