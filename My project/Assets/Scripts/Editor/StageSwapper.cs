using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// 배경(맵) FBX를 씬에 갈아끼우고, 아레나 벽을 새 맵 바닥에 맞추는 도구.
///
/// 교체할 때 하는 일
///  1) 지금 씬에 있는 배경(Resources/BackGround/source 에서 온 루트 오브젝트)을 지운다
///  2) 새 배경 FBX를 배치하고, 바닥·벽·기둥에만 MeshCollider를 붙인다
///     (네뷸라·행성·글로우 같은 연출용 껍데기는 총알·카메라가 통과해야 하므로 제외)
///  3) 바닥을 실제로 훑어(<see cref="ArenaFloorProbe"/>) 지금 아레나 기준 반지름에 맞게 축척·정렬한다
///     → 플레이어/보스 위치와 밸런스(돌진 거리 등)를 그대로 쓸 수 있다
///  4) ArenaWall을 새 바닥 모양 그대로 다시 만든다(원이 아닌 방이면 그 모양을 따라간다)
///  5) FBX에 딸려온 카메라는 지우고, 조명은 꺼둔 채로 둔다(밝기가 튀는 걸 방지)
///
/// 메뉴: Tools/TPS/Change Map/...
/// 되돌리기는 Ctrl+Z 한 번. 결과가 마음에 들면 씬을 저장(Ctrl+S)한다.
/// </summary>
public static class StageSwapper
{
    private const string StageFolder = "Assets/Resources/BackGround/source";

    /// <summary>ArenaWall이 없을 때 쓸 기본 아레나 반지름(m).</summary>
    private const float DefaultArenaRadius = 1.5f;

    /// <summary>벽을 몇 조각으로 만들지 = 바닥을 몇 방향으로 잴지.</summary>
    private const int WallSegments = 48;

    /// <summary>연출용 껍데기 — 콜라이더를 붙이지 않는다(이 검사를 먼저 한다).</summary>
    private static readonly string[] DecorNames =
    {
        "glow", "light", "lamp", "nebula", "planet", "asteroid", "atmosphere",
        "fog", "backdrop", "bounce", "ceilfill", "rim", "cable", "sky", "dome", "volume"
    };

    /// <summary>발판·구조물 — 여기에만 콜라이더를 붙인다.</summary>
    private static readonly string[] SolidNames =
    {
        "architecture", "props", "stage", "ground", "arena", "floor", "wall", "platform"
    };

    // ---------- 메뉴 ----------

    [MenuItem("Tools/TPS/Change Map/background_sample2")]
    public static void SwapToSample2() => SwapMenu("background_sample2");

    [MenuItem("Tools/TPS/Change Map/background_sample1")]
    public static void SwapToSample1() => SwapMenu("background_sample1");

    [MenuItem("Tools/TPS/Change Map/010_Stage (원래 맵)")]
    public static void SwapToOriginal() => SwapMenu("010_Stage");

    private static void SwapMenu(string fbxName)
    {
        if (EditorUtility.DisplayDialog("맵 교체",
                $"현재 배경을 {fbxName} 으로 교체합니다.\n\n" +
                "새 배경은 지금 아레나 크기에 맞춰 자동으로 축척·정렬되고,\n" +
                "아레나 벽도 새 바닥 모양에 맞춰 다시 만들어집니다.\n\n" +
                "마음에 안 들면 Ctrl+Z 로 되돌릴 수 있습니다. 계속할까요?", "교체", "취소"))
            Swap(fbxName);
    }

    /// <summary>
    /// 맵은 그대로 두고 아레나 벽만 지금 바닥에 다시 맞춘다.
    /// 플레이어가 서 있는 바닥을 기준으로 재기 때문에, 맵 교체 후 벽이 안 맞을 때 이걸 쓰면 된다.
    /// </summary>
    [MenuItem("Tools/TPS/Change Map/아레나 벽을 지금 맵에 맞추기")]
    public static void FitWallMenu()
    {
        var stage = FindCurrentStage();
        if (stage == null)
        {
            Debug.LogError("[Map] 씬에서 배경을 찾지 못했습니다. 먼저 맵을 배치하세요.");
            return;
        }

        Undo.SetCurrentGroupName("아레나 벽 맞추기");
        int group = Undo.GetCurrentGroup();

        Physics.SyncTransforms();
        if (FitWall(stage, SeedPoint(), out string report))
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Undo.CollapseUndoOperations(group);
            Debug.Log($"<color=lime>[Arena] 벽을 새 바닥에 맞췄습니다.</color> {report}\n" +
                      "씬 뷰에서 ArenaWall을 선택하면 초록 외곽선으로 모양이 보입니다. 확인 후 Ctrl+S.");
        }
        else
        {
            Debug.LogError($"[Arena] 바닥을 재지 못했습니다. {report}\n" +
                           "Tools/TPS/Change Map/진단 (아레나·바닥 상태) 를 실행해 상태를 확인하세요.");
        }
    }

    /// <summary>배경 FBX에 딸려온 조명을 한꺼번에 켜고 끈다(교체 직후 기본은 꺼짐).</summary>
    [MenuItem("Tools/TPS/Change Map/배경 조명 켜기·끄기")]
    public static void ToggleStageLights()
    {
        var stage = FindCurrentStage();
        if (stage == null) { Debug.LogWarning("[Map] 씬에서 배경을 찾지 못했습니다."); return; }

        var lights = stage.GetComponentsInChildren<Light>(true);
        if (lights.Length == 0) { Debug.Log("[Map] 이 배경에는 딸려온 조명이 없습니다."); return; }

        bool on = !lights[0].enabled;
        foreach (var l in lights)
        {
            Undo.RecordObject(l, "배경 조명 토글");
            l.enabled = on;
            EditorUtility.SetDirty(l);
        }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[Map] 배경 조명 {lights.Length}개를 {(on ? "켰습니다" : "껐습니다")}.");
    }

    /// <summary>플레이어·바닥·벽이 실제로 어디에 있는지 찍어 본다(벽이 안 막을 때 원인 찾기용).</summary>
    [MenuItem("Tools/TPS/Change Map/진단 (아레나·바닥 상태)")]
    public static void Diagnose()
    {
        Physics.SyncTransforms();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<b>[진단] 아레나·바닥 상태</b>");

        var stage = FindCurrentStage();
        if (stage == null) sb.AppendLine("배경: 없음(Resources/BackGround/source 에서 온 루트를 못 찾음)");
        else
        {
            int cols = stage.GetComponentsInChildren<Collider>().Length;
            sb.AppendLine($"배경: {stage.name}  위치={stage.transform.position}  스케일={stage.transform.localScale.x:F4}  콜라이더={cols}개");
            if (ArenaFloorProbe.TryGetBounds(stage, out Bounds b))
                sb.AppendLine($"  콜라이더 바운즈: 중심={b.center} 크기={b.size}");
        }

        var player = GameObject.Find("Player");
        if (player == null) sb.AppendLine("Player: 없음");
        else
        {
            Vector3 p = player.transform.position;
            var cc = player.GetComponent<CharacterController>();
            sb.AppendLine($"Player: 위치={p}  키={(cc != null ? cc.height * Mathf.Abs(player.transform.lossyScale.y) : 0f):F3}  레이어={LayerMask.LayerToName(player.layer)}({player.layer})");

            // 자기 캡슐을 맞으면 바닥을 못 본다 — 플레이어 밑의 것은 빼고 쏜다
            if (RayIgnoring(p + Vector3.up * 0.5f, Vector3.down, 5f, player.transform, out RaycastHit h))
                sb.AppendLine($"  발밑: {h.collider.name} (y={h.point.y:F3}, 레이어={LayerMask.LayerToName(h.collider.gameObject.layer)})");
            else
                sb.AppendLine("  <color=red>발밑: 5m 안에 아무것도 없음 (허공에 떠 있거나 바닥이 훨씬 아래)</color>");
        }

        var wall = Object.FindFirstObjectByType<ArenaWall>();
        if (wall == null) sb.AppendLine("ArenaWall: 없음");
        else
        {
            int segs = wall.transform.childCount;
            sb.AppendLine($"ArenaWall: 위치={wall.transform.position}  기준반지름={wall.BaseRadius:F3}  " +
                          $"안쪽={wall.Radius:F3}  바깥={wall.OuterRadius:F3}  " +
                          $"높이={wall.Height:F3}  아래로={wall.Skirt:F3}  " +
                          $"조각={segs}개  프로필={(wall.HasProfile ? "있음(바닥 모양)" : "없음(완전한 원)")}  " +
                          $"레이어={LayerMask.LayerToName(wall.gameObject.layer)}({wall.gameObject.layer})");

            if (segs == 0)
                sb.AppendLine("  <color=red>조각이 하나도 없습니다 — 벽이 실제로는 존재하지 않습니다.</color>");

            // 인스펙터 값이 아니라 '진짜 콜라이더'가 어디에 있는지 본다
            // (값만 바꾸고 '벽 다시 만들기'를 안 하면 둘이 따로 논다)
            float rMin = float.MaxValue, rMax = 0f, yMin = float.MaxValue, yMax = float.MinValue;
            foreach (Transform t in wall.transform)
            {
                var box = t.GetComponent<BoxCollider>();
                if (box == null) continue;
                Bounds bb = box.bounds;
                yMin = Mathf.Min(yMin, bb.min.y);
                yMax = Mathf.Max(yMax, bb.max.y);
                Vector3 fl = t.position - wall.transform.position; fl.y = 0f;
                rMin = Mathf.Min(rMin, fl.magnitude);
                rMax = Mathf.Max(rMax, fl.magnitude);
            }
            if (segs > 0)
            {
                sb.AppendLine($"  실제 콜라이더: 반지름 {rMin:F3}~{rMax:F3}, 높이 y {yMin:F3}~{yMax:F3}");
                if (Mathf.Abs(rMax - wall.OuterRadius) > wall.OuterRadius * 0.05f)
                    sb.AppendLine("  <color=orange>인스펙터 값과 실제 콜라이더가 다릅니다 — '벽 다시 만들기'를 하지 않은 상태입니다.</color>");
            }

            if (player != null)
            {
                float py = player.transform.position.y;
                if (py > yMax)
                    sb.AppendLine($"  <color=red>플레이어(y={py:F3})가 벽 윗면(y={yMax:F3})보다 높습니다 — 벽 위로 넘어 다닙니다.</color>");
                else if (py < yMin)
                    sb.AppendLine($"  <color=red>플레이어(y={py:F3})가 벽 밑동(y={yMin:F3})보다 낮습니다 — 벽 아래로 빠져나갑니다.</color>");

                Vector3 flat = player.transform.position - wall.transform.position; flat.y = 0f;
                sb.AppendLine($"  플레이어~벽중심 거리={flat.magnitude:F3} (안쪽 반지름 {wall.Radius:F3})");
            }

            int mine = wall.gameObject.layer, other = player != null ? player.layer : 0;
            if (Physics.GetIgnoreLayerCollision(mine, other))
                sb.AppendLine($"  <color=red>레이어 {LayerMask.LayerToName(mine)}↔{LayerMask.LayerToName(other)} 충돌이 꺼져 있습니다.</color>");

            // --- 벽이 정말 막는가: 플레이어 허리 높이에서 바깥으로 쏴 본다 ---
            if (player != null && segs > 0)
            {
                float waist = PlayerHeight() * 0.5f;
                Vector3 from = player.transform.position + Vector3.up * waist;
                int open = 0;
                string firstOpen = null;
                for (int a = 0; a < 16; a++)
                {
                    float ang = Mathf.PI * 2f * a / 16f;
                    Vector3 dir = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                    if (RayIgnoring(from, dir, wall.OuterRadius * 3f, player.transform, out RaycastHit wh)
                        && wh.collider.gameObject.layer == wall.gameObject.layer)
                        continue;

                    open++;
                    if (firstOpen == null)
                        firstOpen = $"{Mathf.RoundToInt(ang * Mathf.Rad2Deg)}° 방향";
                }
                if (open == 0)
                    sb.AppendLine($"  허리 높이(y={from.y:F3})에서는 16방향 모두 벽이 막습니다.");
                else
                    sb.AppendLine($"  <color=red>허리 높이(y={from.y:F3})에서 {open}/16 방향이 벽에 막히지 않습니다 (예: {firstOpen}).</color>");
            }

            // --- 아레나 안에 구멍이나 낮은 층이 있는가(떨어져서 벽 밑으로 나가는 경로) ---
            if (stage != null)
            {
                float refY = player != null ? player.transform.position.y : wall.transform.position.y;
                float r = wall.Radius;
                int holes = 0, drops = 0, total = 0;
                float worst = 0f;
                Vector3 worstAt = wall.transform.position;

                for (int ring = 1; ring <= 6; ring++)
                {
                    float rad = r * (ring / 6f) * 0.95f;
                    for (int a = 0; a < 24; a++)
                    {
                        float ang = Mathf.PI * 2f * a / 24f;
                        Vector3 q = wall.transform.position + new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * rad;
                        total++;

                        // 배경의 바닥만 인정한다(벽 조각이나 보스를 밟은 걸로 치면 구멍을 놓친다)
                        Vector3 top = new Vector3(q.x, refY + wall.Height, q.z);
                        if (!RayIgnoring(top, Vector3.down, wall.Height + wall.Skirt + r,
                                         player != null ? player.transform : null, out RaycastHit fh,
                                         mustBeUnder: stage.transform))
                        {
                            holes++;
                            worstAt = q;
                            continue;
                        }
                        float drop = refY - fh.point.y;
                        if (drop > PlayerHeight())
                        {
                            drops++;
                            if (drop > worst) { worst = drop; worstAt = q; }
                        }
                    }
                }

                if (holes > 0)
                    sb.AppendLine($"  <color=red>아레나 안 {total}곳 중 {holes}곳은 발밑에 바닥이 아예 없습니다 (예: {worstAt}).</color>");
                if (drops > 0)
                    sb.AppendLine($"  <color=orange>아레나 안 {total}곳 중 {drops}곳은 바닥이 {worst:F2} 아래에 있습니다 (예: {worstAt}). " +
                                  "여기로 떨어지면 벽 밑으로 나갈 수 있습니다.</color>");
                if (holes == 0 && drops == 0)
                    sb.AppendLine($"  아레나 안 {total}곳 모두 같은 높이의 바닥이 있습니다.");
            }
        }

        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// 가장 가까운 것을 맞히는 레이.
    /// <paramref name="ignore"/> 아래의 콜라이더(보통 플레이어 자기 캡슐)는 빼고,
    /// <paramref name="mustBeUnder"/>를 주면 그 아래의 콜라이더만 인정한다.
    /// </summary>
    private static bool RayIgnoring(Vector3 from, Vector3 dir, float dist, Transform ignore, out RaycastHit best,
                                    Transform mustBeUnder = null)
    {
        best = default;
        var hits = Physics.RaycastAll(from, dir, dist, ~0, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        bool found = false;
        foreach (var h in hits)
        {
            if (ignore != null && h.collider.transform.IsChildOf(ignore)) continue;
            if (mustBeUnder != null && !h.collider.transform.IsChildOf(mustBeUnder)) continue;
            if (h.distance < nearest) { nearest = h.distance; best = h; found = true; }
        }
        return found;
    }

    // ---------- 교체 ----------

    public static void Swap(string fbxName)
    {
        string path = $"{StageFolder}/{fbxName}.fbx";
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (model == null)
        {
            Debug.LogError($"[Map] {path} 를 찾을 수 없습니다. 파일 이름을 확인하세요.");
            return;
        }

        try
        {
            Undo.SetCurrentGroupName($"맵 교체: {fbxName}");
            int group = Undo.GetCurrentGroup();

            float targetRadius = TargetRadius();

            // --- 기존 배경 제거 ---
            var old = FindCurrentStage();
            string oldName = old != null ? old.name : "(없음)";
            if (old != null) Undo.DestroyObjectImmediate(old);

            // --- 새 배경 배치 ---
            var stage = (GameObject)PrefabUtility.InstantiatePrefab(model);
            if (stage == null) { Debug.LogError($"[Map] {fbxName} 인스턴스화에 실패했습니다."); return; }
            Undo.RegisterCreatedObjectUndo(stage, "맵 교체");
            stage.name = fbxName;
            stage.transform.SetParent(null, false);
            stage.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            stage.transform.localScale = Vector3.one;

            int cams = StripCameras(stage);
            int lights = DisableLights(stage);
            var solids = AddColliders(stage);
            Physics.SyncTransforms();

            if (solids.Count == 0)
            {
                Debug.LogError($"[Map] {fbxName} 에서 발판으로 쓸 메시를 찾지 못했습니다. " +
                               "StageSwapper.SolidNames 에 이름 조각을 추가하세요.");
                return;
            }

            // --- 발판을 훑어 지금 아레나 크기에 맞추기 ---
            // 아직 플레이어가 새 맵 위에 없으므로, 면 높이 후보를 전부 재 보고 진짜 바닥을 고르게 한다
            if (!ArenaFloorProbe.TryGetBounds(stage, out Bounds b) ||
                !ArenaFloorProbe.Measure(stage, b.center, WallSegments, out var floor, searchLevels: true))
            {
                Debug.LogError("[Map] 바닥을 훑지 못해 자동 축척을 건너뛰었습니다. " +
                               $"{fbxName} 의 Scale Factor 를 직접 맞춘 뒤 " +
                               "Tools/TPS/Change Map/아레나 벽을 지금 맵에 맞추기 를 실행하세요.");
                return;
            }

            float scale = targetRadius / floor.min;
            stage.transform.localScale = Vector3.one * scale;
            stage.transform.position = -scale * floor.center;   // 바닥 중심 → 원점, 바닥 → y 0
            Physics.SyncTransforms();

            Debug.Log($"<color=lime>[Map] {oldName} → {fbxName} 교체 완료.</color> " +
                      $"발판 반지름 {floor.min:F2}~{floor.max:F2} → 스케일 {scale:F4} 로 기준 반지름 {targetRadius:F2} 에 맞췄습니다. " +
                      $"콜라이더 {solids.Count}개 추가, 카메라 {cams}개 제거, 조명 {lights}개 꺼둠.");

            // --- 아레나 벽을 새 바닥에 맞춰 다시 만들기 ---
            if (FitWall(stage, Vector3.zero, out string report))
                Debug.Log($"[Arena] 벽을 새 바닥에 맞췄습니다. {report}");
            else
                Debug.LogWarning($"[Arena] 벽을 맞추지 못했습니다. {report} " +
                                 "Tools/TPS/Change Map/아레나 벽을 지금 맵에 맞추기 를 따로 실행해 보세요.");

            // --- 우주 배경을 이미 쓰고 있으면 새 맵에도 다시 씌운다 ---
            if (SpaceLookSetup.HasAssets)
            {
                int painted = SpaceLookSetup.ApplyToStage(stage);
                int moving = SpaceLookSetup.SetupMotion(stage);
                if (painted > 0 || moving > 0)
                    Debug.Log($"[Space] 새 배경의 행성·부유물 {painted}개에 머티리얼을 다시 씌우고 {moving}개를 움직이게 했습니다.");
            }

            CheckActors(stage);

            Selection.activeGameObject = stage;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Undo.CollapseUndoOperations(group);

            Debug.Log("[Map] 씬 뷰에서 확인한 뒤 Ctrl+S 로 저장하세요. 되돌리려면 Ctrl+Z.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Map] 교체 중 오류: {e}");
        }
    }

    // ---------- 아레나 벽 ----------

    /// <summary>바닥을 재서 ArenaWall을 그 모양 그대로 다시 만든다.</summary>
    private static bool FitWall(GameObject stage, Vector3 seed, out string report)
    {
        report = "";
        if (!ArenaFloorProbe.Measure(stage, seed, WallSegments, out var floor))
        {
            report = $"기준점 {seed} 아래에서 바닥을 찾지 못했습니다.";
            return false;
        }

        var wall = Object.FindFirstObjectByType<ArenaWall>();
        if (wall == null)
        {
            var go = new GameObject("ArenaWall");
            int layer = LayerMask.NameToLayer("ArenaWall");
            if (layer >= 0) go.layer = layer;
            wall = go.AddComponent<ArenaWall>();
            Undo.RegisterCreatedObjectUndo(go, "아레나 벽 생성");
        }
        else
        {
            // 조각을 통째로 다시 만들기 때문에 자식까지 기록해 둔다
            Undo.RegisterFullObjectHierarchyUndo(wall.gameObject, "아레나 벽 맞추기");
        }

        float playerH = PlayerHeight();

        wall.transform.SetPositionAndRotation(floor.center, Quaternion.identity);
        wall.transform.localScale = Vector3.one;

        // 높이·두께는 손대 놓은 값이 넉넉하면 그대로 둔다(넘어가거나 뚫리지만 않으면 된다)
        if (wall.Height < playerH * 3f) wall.Height = playerH * 4f;
        if (wall.Thickness < playerH * 0.5f) wall.Thickness = playerH * 0.5f;

        // 바닥 아래로도 내린다. 아레나 안에서 아래층으로 떨어졌을 때
        // 벽 밑동보다 낮아져 그냥 걸어 나가는 걸 막는다.
        float below = 0f;
        if (ArenaFloorProbe.TryGetBounds(stage, out Bounds sb))
            below = Mathf.Max(0f, floor.center.y - sb.min.y);
        wall.Skirt = Mathf.Clamp(below + playerH, playerH * 2f, playerH * 30f);

        // 가장자리에서 플레이어 몸 반쯤 안쪽에 세운다(발끝이 허공에 걸치지 않게)
        float inset = playerH * 0.25f;
        var profile = new float[floor.profile.Length];
        for (int i = 0; i < profile.Length; i++)
            profile[i] = Mathf.Max(playerH * 0.5f, floor.profile[i] - inset);

        wall.SetProfile(profile);
        wall.Rebuild();
        EditorUtility.SetDirty(wall);

        report = $"중심={floor.center}, 반지름 {wall.Radius:F2}~{wall.OuterRadius:F2}, " +
                 $"높이={wall.Height:F2}(+아래로 {wall.Skirt:F2}), 조각={wall.transform.childCount}개";
        return true;
    }

    /// <summary>맵을 맞출 목표 반지름 = 인스펙터에 적힌 기준 반지름(프로필을 씌워도 안 바뀐다).</summary>
    private static float TargetRadius()
    {
        var wall = Object.FindFirstObjectByType<ArenaWall>();
        return wall != null ? wall.BaseRadius : DefaultArenaRadius;
    }

    /// <summary>바닥 측정 기준점: 플레이어가 서 있는 자리(없으면 원점).</summary>
    private static Vector3 SeedPoint()
    {
        var player = GameObject.Find("Player");
        return player != null ? player.transform.position : Vector3.zero;
    }

    private static float PlayerHeight()
    {
        var player = GameObject.Find("Player");
        if (player == null) return 0.2f;
        var cc = player.GetComponent<CharacterController>();
        if (cc != null) return cc.height * Mathf.Abs(player.transform.lossyScale.y);
        return 0.2f;
    }

    // ---------- 배치 정리 ----------

    /// <summary>FBX에 딸려온 카메라/오디오리스너 제거(게임 카메라와 겹치면 화면이 엉킨다).</summary>
    private static int StripCameras(GameObject stage)
    {
        int n = 0;
        foreach (var cam in stage.GetComponentsInChildren<Camera>(true))
        {
            var listener = cam.GetComponent<AudioListener>();
            if (listener != null) Object.DestroyImmediate(listener);
            Object.DestroyImmediate(cam);
            n++;
        }
        return n;
    }

    /// <summary>딸려온 조명은 꺼둔다. 밝기가 이 프로젝트 스케일과 안 맞을 수 있어 기본은 꺼짐.</summary>
    private static int DisableLights(GameObject stage)
    {
        int n = 0;
        foreach (var l in stage.GetComponentsInChildren<Light>(true)) { l.enabled = false; n++; }
        return n;
    }

    /// <summary>발판·구조물 메시에만 MeshCollider를 붙인다.</summary>
    private static List<Collider> AddColliders(GameObject stage)
    {
        var made = new List<Collider>();
        foreach (var mf in stage.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            string n = mf.gameObject.name.ToLowerInvariant();
            if (Matches(n, DecorNames)) continue;   // 연출용은 통과시켜야 한다
            if (!Matches(n, SolidNames)) continue;

            var mc = mf.gameObject.GetComponent<MeshCollider>();
            if (mc == null) mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = false;
            made.Add(mc);
        }
        return made;
    }

    private static bool Matches(string lowerName, string[] fragments)
    {
        foreach (var f in fragments) if (lowerName.Contains(f)) return true;
        return false;
    }

    /// <summary>씬에 있는 배경 루트 찾기: Resources/BackGround/source 에서 온 루트 오브젝트.</summary>
    public static GameObject FindCurrentStage()
    {
        foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
            if (src == null) continue;
            string p = AssetDatabase.GetAssetPath(src).Replace('\\', '/');
            if (p.StartsWith(StageFolder)) return go;
        }
        return null;
    }

    /// <summary>플레이어·보스가 새 바닥 위에 제대로 서 있는지 확인만 하고 알린다(위치는 건드리지 않는다).</summary>
    private static void CheckActors(GameObject stage)
    {
        var wall = Object.FindFirstObjectByType<ArenaWall>();
        float radius = wall != null ? wall.Radius : DefaultArenaRadius;
        Vector3 center = wall != null ? wall.transform.position : Vector3.zero;

        foreach (string name in new[] { "Player", "Boss" })
        {
            var go = GameObject.Find(name);
            if (go == null) continue;

            Vector3 p = go.transform.position;
            Vector3 flat = p - center; flat.y = 0f;
            if (flat.magnitude > radius)
            {
                Debug.LogWarning($"[Map] {name} 이(가) 아레나 밖({p})에 있습니다. 안쪽으로 옮겨주세요.");
                continue;
            }
            if (!Physics.Raycast(p + Vector3.up * radius, Vector3.down, out RaycastHit hit, radius * 2f,
                                 ~0, QueryTriggerInteraction.Ignore)
                || !hit.collider.transform.IsChildOf(stage.transform))
            {
                Debug.LogWarning($"[Map] {name} 발밑에 새 배경의 바닥이 없습니다. 위치를 확인하세요.");
            }
            else if (Mathf.Abs(hit.point.y - p.y) > radius * 0.05f)
            {
                Debug.LogWarning($"[Map] {name} 의 y({p.y:F3})가 새 바닥 높이({hit.point.y:F3})와 다릅니다. 스냅이 필요할 수 있습니다.");
            }
        }
    }
}
