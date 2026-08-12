using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// 원형 아레나의 낙하 방지 벽을 자동 생성하는 도구.
///
/// 하는 일
///  1) 바닥을 아래로 레이캐스트하며 방사형으로 훑어 "실제로 발판이 있는 반지름"을 측정
///     (스테이지 모델의 바운즈는 배경 구조물까지 포함할 수 있어 믿지 않는다)
///  2) 그 반지름에 보이지 않는 벽(ArenaWall)을 세운다
///  3) 벽 전용 레이어를 만들고 카메라 충돌/사격/조준/보스 레이저 마스크에서 제외
///     (보이지 않는 벽에 총알이 맞거나 카메라가 튕기지 않도록)
///
/// 메뉴: Tools/TPS/Build Arena Wall (낙하 방지)
/// </summary>
public static class ArenaWallSetup
{
    private const string WallLayerName = "ArenaWall";
    private const string WallObjectName = "ArenaWall";

    /// <summary>측정한 반지름에 곱하는 여유(가장자리 살짝 안쪽에 벽을 세운다).</summary>
    private const float RadiusInset = 0.97f;
    /// <summary>벽 높이 = 플레이어 키 × 이 배율.</summary>
    private const float HeightScale = 4f;

    [MenuItem("Tools/TPS/Build Arena Wall (낙하 방지)")]
    public static void BuildMenu()
    {
        if (EditorUtility.DisplayDialog("아레나 낙하 방지 벽",
                "바닥을 훑어 원형 발판의 반지름을 측정하고, 그 둘레에 보이지 않는 벽을 세웁니다.\n" +
                "(플레이어·보스 모두 밖으로 나가지 못하게 됩니다)\n계속할까요?", "실행", "취소"))
            Build();
    }

    public static void Build()
    {
        try
        {
            var player = GameObject.Find("Player");

            // --- 기준점/크기 ---
            float playerHeight = 1.8f;
            if (player != null && TryGetBounds(player, out Bounds pb) && pb.size.y > 1e-4f)
                playerHeight = pb.size.y;

            Vector3 origin = player != null ? player.transform.position : Vector3.zero;
            var arena = FindArena();
            if (arena != null && TryGetBounds(arena, out Bounds ab))
                origin = new Vector3(ab.center.x, origin.y, ab.center.z);

            // --- 발판 반지름 측정 ---
            if (!TryMeasureRadius(origin, playerHeight, player, out Vector3 center, out float radius))
            {
                Debug.LogError("[Arena] 바닥을 찾지 못했습니다. 아레나 오브젝트에 Collider(Mesh Collider)가 있는지 확인하세요. " +
                               "특정 오브젝트를 기준으로 재고 싶으면 그 오브젝트를 선택한 뒤 다시 실행하세요.");
                return;
            }
            radius *= RadiusInset;

            // --- 레이어 ---
            int layer = EnsureLayer(WallLayerName);
            int wallMask = 1 << layer;

            // --- 벽 생성/갱신 ---
            var wallGo = GameObject.Find(WallObjectName);
            if (wallGo == null) wallGo = new GameObject(WallObjectName);
            wallGo.layer = layer;
            wallGo.transform.position = center;
            wallGo.transform.rotation = Quaternion.identity;
            wallGo.transform.localScale = Vector3.one;

            var wall = wallGo.GetComponent<ArenaWall>();
            if (wall == null) wall = wallGo.AddComponent<ArenaWall>();
            wall.Radius = radius;
            wall.Height = playerHeight * HeightScale;
            wall.Thickness = Mathf.Max(playerHeight * 0.5f, 0.05f);
            wall.Rebuild();
            EditorUtility.SetDirty(wall);

            // --- 마스크에서 제외(보이지 않는 벽이 총알·카메라·레이저를 막지 않도록) ---
            ExcludeFromMasks(wallMask);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = wallGo;

            Debug.Log($"<color=lime>[Arena] 낙하 방지 벽 생성 완료.</color> " +
                      $"중심={center}, 반지름={radius:F2}m, 높이={wall.Height:F2}m. " +
                      "크기가 안 맞으면 ArenaWall 인스펙터에서 Radius를 고치고 우클릭 → '벽 다시 만들기'.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Arena] 생성 중 오류: {e}");
        }
    }

    // ---------- 발판 측정 ----------

    /// <summary>
    /// 중심에서 바깥으로 조금씩 나아가며 아래로 레이를 쏴, 발판이 끊기는 지점을 찾는다.
    /// 여러 방향의 최솟값을 쓰므로 어떤 방향으로도 떨어질 수 없는 반지름이 나온다.
    /// </summary>
    private static bool TryMeasureRadius(Vector3 origin, float playerHeight, GameObject player,
                                         out Vector3 center, out float radius)
    {
        center = origin;
        radius = 0f;

        float up = playerHeight * 10f;          // 레이 시작 높이
        float step = playerHeight * 0.25f;      // 탐색 간격
        float maxRadius = playerHeight * 200f;  // 탐색 한계
        float tolerance = playerHeight * 2f;    // 같은 바닥으로 볼 높이 차(턱/장식 허용)

        if (!SampleGround(origin, up, player, out float groundY)) return false;
        center = new Vector3(origin.x, groundY, origin.z);

        const int directions = 32;
        float min = maxRadius;
        for (int d = 0; d < directions; d++)
        {
            float a = 360f / directions * d * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));

            float edge = maxRadius;
            for (float r = step; r <= maxRadius; r += step)
            {
                Vector3 p = center + dir * r;
                if (!SampleGround(p, up, player, out float y) || Mathf.Abs(y - groundY) > tolerance)
                {
                    edge = r - step; // 직전 지점까지가 발판
                    break;
                }
            }
            min = Mathf.Min(min, edge);
        }

        radius = min;
        return radius > step;
    }

    /// <summary>해당 XZ 위치의 바닥 높이(플레이어/보스 등 캐릭터는 무시).</summary>
    private static bool SampleGround(Vector3 pos, float up, GameObject player, out float y)
    {
        y = 0f;
        Vector3 from = new Vector3(pos.x, pos.y + up, pos.z);
        var hits = Physics.RaycastAll(from, Vector3.down, up * 2f, ~0, QueryTriggerInteraction.Ignore);

        bool found = false;
        float best = float.MinValue;
        foreach (var h in hits)
        {
            if (player != null && h.collider.transform.IsChildOf(player.transform)) continue;
            if (h.collider.GetComponentInParent<BossController>() != null) continue;
            if (h.collider.GetComponentInParent<ArenaWall>() != null) continue;
            if (h.point.y > best) { best = h.point.y; found = true; }
        }
        if (found) y = best;
        return found;
    }

    /// <summary>아레나(발판) 오브젝트 추정: 이름(Stage/Ground/Arena) 우선, 없으면 선택 중인 오브젝트.</summary>
    private static GameObject FindArena()
    {
        foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
        {
            if (go.transform.parent != null) continue;
            string n = go.name.ToLowerInvariant();
            if (n.Contains("stage") || n.Contains("ground") || n.Contains("arena")) return go;
        }

        // 이름으로 못 찾으면 선택 중인 오브젝트를 쓰되, 캐릭터/벽 자신은 기준이 될 수 없다
        var sel = Selection.activeGameObject;
        if (sel == null) return null;
        if (sel.name == "Player" || sel.GetComponentInParent<PlayerController>() != null) return null;
        if (sel.GetComponentInParent<BossController>() != null) return null;
        if (sel.GetComponentInParent<ArenaWall>() != null) return null;
        return sel;
    }

    // ---------- 마스크/레이어 ----------

    /// <summary>보이지 않는 벽이 카메라 충돌·사격·조준·보스 레이저를 가로막지 않게 마스크에서 뺀다.</summary>
    private static void ExcludeFromMasks(int wallMask)
    {
        foreach (var cam in Object.FindObjectsByType<ThirdPersonCamera>(FindObjectsSortMode.None))
            ClearMaskBits(cam, "collisionMask", wallMask);
        foreach (var ch in Object.FindObjectsByType<Crosshair>(FindObjectsSortMode.None))
            ClearMaskBits(ch, "hitMask", wallMask);
        foreach (var sh in Object.FindObjectsByType<PlayerShooter>(FindObjectsSortMode.None))
            ClearMaskBits(sh, "hitMask", wallMask);
        foreach (var pc in Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            ClearMaskBits(pc, "aimMask", wallMask);
        foreach (var boss in Object.FindObjectsByType<BossController>(FindObjectsSortMode.None))
            ClearMaskBits(boss, "obstacleMask", wallMask);
    }

    private static void ClearMaskBits(Object target, string field, int mask)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null) return;
        p.intValue &= ~mask;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>비어 있는 첫 User 레이어(8~31)에 이름을 등록하고 인덱스를 반환.</summary>
    private static int EnsureLayer(string layerName)
    {
        var tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tagManager.FindProperty("layers");

        for (int i = 0; i < layers.arraySize; i++)
            if (layers.GetArrayElementAtIndex(i).stringValue == layerName) return i;

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
        Debug.LogWarning("[Arena] 빈 User 레이어가 없어 Default(0)를 사용합니다.");
        return 0;
    }

    private static bool TryGetBounds(GameObject root, out Bounds bounds)
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
}
