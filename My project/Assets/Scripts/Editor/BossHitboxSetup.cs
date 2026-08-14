using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// 보스 부위별 데미지(히트박스) 자동 구성 도구.
///
/// 하는 일
///  1) 휴머노이드 본을 따라 부위 히트박스(캡슐/구)를 만들고 <see cref="BossHitbox"/>를 붙인다
///     — 머리 ×2.5 / 가슴·복부 ×1 / 팔·다리·손·발 ×0.6 (이후 인스펙터에서 자유롭게 조절)
///  2) 히트박스 전용 레이어를 만들고 <b>물리 충돌을 전부 끈다</b>
///     (총알 판정만 받고 플레이어·보스를 밀지 않는다. 레이캐스트는 충돌 매트릭스와 무관)
///  3) 보스 본체(CharacterController)를 전용 레이어로 옮겨 <b>사격/조준 마스크에서 제외</b>한다
///     — 몸통 캡슐이 총알을 먼저 막으면 부위 판정이 영영 오지 않기 때문이다
///  4) 카메라 충돌 / 보스 장애물 마스크에서는 히트박스를 빼 준다
///
/// 메뉴: Tools/TPS/Setup Boss Hitboxes (부위별 데미지)
/// </summary>
public static class BossHitboxSetup
{
    // ---- 기본 배율(생성 시점의 초깃값. 이후엔 각 히트박스 인스펙터에서 조절) ----
    private const float HeadMultiplier = 2.5f;
    private const float BodyMultiplier = 1f;
    private const float LimbMultiplier = 0.6f;

    /// <summary>
    /// 히트박스 반지름 상한/하한 = 보스 키 × 이 값.
    /// 상한이 너무 빡빡하면 몸통이 실제 몸보다 훨씬 얇게 잘려 총알이 그냥 통과한다
    /// (0.09였을 때 알리언의 몸통이 의도의 1/4로 잘렸다).
    /// </summary>
    private const float MaxRadiusRatio = 0.20f;
    private const float MinRadiusRatio = 0.015f;

    /// <summary>
    /// 몸통 반지름 = 허리~목 길이 × 이 값.
    /// CharacterController 반지름은 <b>팔을 벌린 폭</b>(렌더러 바운즈)에서 나온 값이라
    /// 몸통 기준으로 쓰면 지나치게 뚱뚱해져 머리·팔 히트박스를 통째로 삼켜 버린다.
    /// </summary>
    private const float ChestRadiusRatio = 0.38f;
    private const float AbdomenRadiusRatio = 0.40f;

    private const string NamePrefix = "Hitbox_";

    /// <summary>이미 만들어져 있는 히트박스(오브젝트 이름 → 컴포넌트). 다시 실행하면 이걸 갱신한다.</summary>
    private static readonly Dictionary<string, BossHitbox> _existing = new Dictionary<string, BossHitbox>();

    /// <summary>캡슐 부위 정의: 시작 본 → 끝 본을 잇는 마디.</summary>
    private struct Segment
    {
        public string display;       // 인스펙터에 보일 부위 이름
        public string objectName;    // GameObject 이름
        public HumanBodyBones from;
        public HumanBodyBones to;
        public float radiusRatio;    // 반지름 = 마디 길이 × 이 값
        public float multiplier;

        public Segment(string display, string objectName, HumanBodyBones from, HumanBodyBones to,
                       float radiusRatio, float multiplier)
        {
            this.display = display; this.objectName = objectName;
            this.from = from; this.to = to;
            this.radiusRatio = radiusRatio; this.multiplier = multiplier;
        }
    }

    /// <summary>팔·다리 마디. 몸통/머리는 크기 계산이 달라 따로 만든다.</summary>
    private static readonly Segment[] Limbs =
    {
        new Segment("왼팔(위)",   "ArmUpperL", HumanBodyBones.LeftUpperArm,  HumanBodyBones.LeftLowerArm,  0.35f, LimbMultiplier),
        new Segment("왼팔(아래)", "ArmLowerL", HumanBodyBones.LeftLowerArm,  HumanBodyBones.LeftHand,      0.30f, LimbMultiplier),
        new Segment("오른팔(위)",   "ArmUpperR", HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 0.35f, LimbMultiplier),
        new Segment("오른팔(아래)", "ArmLowerR", HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,     0.30f, LimbMultiplier),
        new Segment("왼다리(위)",   "LegUpperL", HumanBodyBones.LeftUpperLeg,  HumanBodyBones.LeftLowerLeg,  0.38f, LimbMultiplier),
        new Segment("왼다리(아래)", "LegLowerL", HumanBodyBones.LeftLowerLeg,  HumanBodyBones.LeftFoot,      0.32f, LimbMultiplier),
        new Segment("오른다리(위)",   "LegUpperR", HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0.38f, LimbMultiplier),
        new Segment("오른다리(아래)", "LegLowerR", HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,     0.32f, LimbMultiplier),
    };

    [MenuItem("Tools/TPS/Setup Boss Hitboxes (부위별 데미지)")]
    public static void SetupMenu()
    {
        if (EditorUtility.DisplayDialog("보스 부위별 데미지",
                "보스 본에 부위 히트박스를 만들고 데미지 배율을 설정합니다.\n\n" +
                $"머리 ×{HeadMultiplier} · 가슴/복부 ×{BodyMultiplier} · 팔다리 ×{LimbMultiplier}\n" +
                "(생성 후 각 히트박스 인스펙터에서 배율을 바꿀 수 있습니다)\n\n" +
                "이미 있으면 배율은 그대로 두고 크기·위치만 다시 계산합니다. 계속할까요?", "실행", "취소"))
            Setup();
    }

    public static void Setup()
    {
        try
        {
            var boss = Object.FindFirstObjectByType<BossController>();
            if (boss == null)
            {
                Debug.LogError("[BossHitbox] 씬에서 보스를 찾지 못했습니다. " +
                               "먼저 Tools/TPS/Setup Boss (AlienMonster)를 실행하세요.");
                return;
            }

            var anim = boss.GetComponentInChildren<Animator>();
            if (anim == null || anim.avatar == null || !anim.avatar.isHuman)
            {
                Debug.LogError("[BossHitbox] 보스가 휴머노이드 아바타가 아니라 본 위치를 알 수 없습니다. " +
                               "FBX Import Settings → Rig → Animation Type = Humanoid로 임포트하세요.");
                return;
            }

            // --- 레이어 ---
            int hitboxLayer = EnsureLayer(BossHitbox.HitboxLayer);
            int bodyLayer = EnsureLayer(BossHitbox.BodyLayer);
            ClearCollisionMatrixRow(hitboxLayer);
            ApplyMasks(1 << hitboxLayer, 1 << bodyLayer);

            // 보스 본체(CharacterController)는 총알에 맞지 않는다 — 부위 히트박스만 맞는다
            Undo.RecordObject(boss.gameObject, "Boss Body Layer");
            boss.gameObject.layer = bodyLayer;
            EditorUtility.SetDirty(boss.gameObject);

            // --- 기존 히트박스는 지우지 않고 그대로 다시 쓴다 ---
            // 한 번 프리팹에 적용하고 나면 인스턴스에서 자식을 지울 수 없고, 지웠다 다시 만들면
            // 손으로 조정한 배율도 날아간다. 이름으로 찾아 크기·위치만 다시 계산한다.
            _existing.Clear();
            foreach (var old in boss.GetComponentsInChildren<BossHitbox>(true))
                if (old != null) _existing[old.gameObject.name] = old;
            int reused = _existing.Count;

            // --- 크기 기준 ---
            // 본체 캡슐이 사격 마스크에서 빠지므로, 히트박스가 그 부피를 대신 덮어야 한다.
            // 얇으면 총알이 몸을 그냥 통과해 명중도 타임포스 획득도 사라진다.
            float worldHeight = BossWorldHeight(boss);
            float maxRadius = worldHeight * MaxRadiusRatio;
            float minRadius = worldHeight * MinRadiusRatio;

            var made = new List<BossHitbox>();

            // --- 머리 ---
            Transform head = anim.GetBoneTransform(HumanBodyBones.Head);
            Vector3 headCenter = Vector3.zero;
            float headRadius = 0f;
            if (head != null)
            {
                Transform tip = FurthestChild(head);
                headRadius = worldHeight * 0.075f;
                headCenter = head.position + boss.transform.up * headRadius;
                if (tip != null)
                {
                    Vector3 toTip = tip.position - head.position;
                    if (toTip.magnitude > 1e-4f)
                    {
                        headRadius = toTip.magnitude * 0.55f;
                        headCenter = head.position + toTip * 0.5f;
                    }
                }
                made.Add(CreateSphere(boss, head, headCenter, headRadius, "머리", "Head",
                                      HeadMultiplier, hitboxLayer));
            }
            else Debug.LogWarning("[BossHitbox] Head 본이 없어 머리 히트박스를 건너뜁니다.");

            // --- 몸통: 가슴(Spine→Neck) + 복부(Hips→Spine) ---
            // 반지름은 허리~목 길이에서 뽑는다. CharacterController 반지름은 팔을 벌린 폭이라
            // 그대로 쓰면 몸통이 머리까지 삼켜 헤드샷이 영영 안 터진다(아래 클램프가 2차 방어선).
            Transform hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            Transform spine = anim.GetBoneTransform(HumanBodyBones.Spine);
            Transform chestEnd = anim.GetBoneTransform(HumanBodyBones.Neck)
                              ?? anim.GetBoneTransform(HumanBodyBones.Head);

            float torsoLength = hips != null && chestEnd != null
                ? Vector3.Distance(hips.position, chestEnd.position)
                : worldHeight * 0.38f;

            if (spine != null && chestEnd != null)
            {
                Vector3 chestCenter = (spine.position + chestEnd.position) * 0.5f;
                float r = torsoLength * ChestRadiusRatio;
                // 머리 구가 가슴 밖으로 튀어나오도록 제한 — 안 그러면 머리를 겨눠도 가슴이 먼저 맞는다
                if (head != null)
                    r = Mathf.Min(r, Vector3.Distance(chestCenter, headCenter) + headRadius * 0.4f);
                made.Add(CreateCapsule(boss, spine, spine.position, chestEnd.position,
                                       Clamp(r, minRadius, maxRadius),
                                       "가슴", "Chest", BodyMultiplier, hitboxLayer));
            }
            if (hips != null && spine != null)
                made.Add(CreateCapsule(boss, hips, hips.position, spine.position,
                                       Clamp(torsoLength * AbdomenRadiusRatio, minRadius, maxRadius),
                                       "복부", "Abdomen", BodyMultiplier, hitboxLayer));

            // --- 팔/다리 ---
            foreach (var seg in Limbs)
            {
                Transform a = anim.GetBoneTransform(seg.from);
                Transform b = anim.GetBoneTransform(seg.to);
                if (a == null || b == null)
                {
                    Debug.LogWarning($"[BossHitbox] '{seg.display}' 본이 없어 건너뜁니다.");
                    continue;
                }
                float len = Vector3.Distance(a.position, b.position);
                float r = Clamp(len * seg.radiusRatio, minRadius, maxRadius);
                made.Add(CreateCapsule(boss, a, a.position, b.position, r,
                                       seg.display, seg.objectName, seg.multiplier, hitboxLayer));
            }

            // --- 손/발(마디 캡슐이 손목·발목에서 끝나 생기는 빈틈 메우기) ---
            AddTipSphere(boss, anim, HumanBodyBones.LeftHand, "왼손", "HandL",
                         worldHeight * 0.06f, minRadius, maxRadius, hitboxLayer, made);
            AddTipSphere(boss, anim, HumanBodyBones.RightHand, "오른손", "HandR",
                         worldHeight * 0.06f, minRadius, maxRadius, hitboxLayer, made);
            AddTipSphere(boss, anim, HumanBodyBones.LeftFoot, "왼발", "FootL",
                         worldHeight * 0.065f, minRadius, maxRadius, hitboxLayer, made);
            AddTipSphere(boss, anim, HumanBodyBones.RightFoot, "오른발", "FootR",
                         worldHeight * 0.065f, minRadius, maxRadius, hitboxLayer, made);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = boss.gameObject;

            Debug.Log($"<color=lime>[BossHitbox] 부위 히트박스 {made.Count}개 구성 완료.</color>" +
                      (reused > 0 ? $" (기존 {reused}개는 배율을 유지한 채 크기만 갱신)" : "") +
                      $"\n{Report(made)}" +
                      "\n크기가 안 맞으면 각 Hitbox_* 오브젝트의 콜라이더를 직접 늘리면 되고, " +
                      "배율은 BossHitbox의 Damage Multiplier에서 바꾼다." +
                      (reused > 0 ? "\n※ 히트박스가 프리팹에 적용돼 있다면, 씬의 Boss 우클릭 → Prefab → Apply All 로 프리팹에도 반영하세요."
                                  : ""));
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[BossHitbox] 구성 중 오류: {e}");
        }
    }

    // ---------- 히트박스 생성 ----------

    /// <summary>본 a→b를 잇는 캡슐 히트박스. 로컬 Z를 마디 방향으로 맞춰 direction=2로 세운다.</summary>
    private static BossHitbox CreateCapsule(BossController boss, Transform bone, Vector3 a, Vector3 b,
                                            float worldRadius, string display, string objectName,
                                            float multiplier, int layer)
    {
        GameObject go = NewHitboxObject(bone, objectName, layer, out BossHitbox existing);

        Vector3 dir = b - a;
        float len = dir.magnitude;
        go.transform.position = (a + b) * 0.5f;
        if (len > 1e-5f)
        {
            // 캡슐은 축을 중심으로 대칭이라 up이 무엇이든 상관없다 — 마디와 평행하지만 않으면 된다
            // (다리처럼 수직인 마디에서 LookRotation이 퇴화하는 것을 막는다)
            Vector3 fwd = dir / len;
            Vector3 up = Mathf.Abs(Vector3.Dot(fwd, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
            go.transform.rotation = Quaternion.LookRotation(fwd, up);
        }
        else go.transform.rotation = bone.rotation;

        float s = UniformScale(go.transform);
        var col = EnsureCollider<CapsuleCollider>(go);
        col.direction = 2;                                   // 로컬 Z = 캡슐 축
        col.center = Vector3.zero;
        col.radius = worldRadius / s;
        col.height = Mathf.Max(len / s, col.radius * 2f);    // 길이가 지름보다 짧으면 구가 된다

        return Configure(go, boss, display, multiplier, existing);
    }

    /// <summary>구 히트박스(머리·손·발).</summary>
    private static BossHitbox CreateSphere(BossController boss, Transform bone, Vector3 center,
                                           float worldRadius, string display, string objectName,
                                           float multiplier, int layer)
    {
        GameObject go = NewHitboxObject(bone, objectName, layer, out BossHitbox existing);
        go.transform.position = center;
        go.transform.rotation = bone.rotation;

        var col = EnsureCollider<SphereCollider>(go);
        col.center = Vector3.zero;
        col.radius = worldRadius / UniformScale(go.transform);

        return Configure(go, boss, display, multiplier, existing);
    }

    /// <summary>손목/발목 끝의 작은 구(마디 캡슐이 끝나는 지점의 빈틈을 메운다).</summary>
    private static void AddTipSphere(BossController boss, Animator anim, HumanBodyBones bone,
                                     string display, string objectName, float radius,
                                     float min, float max, int layer, List<BossHitbox> made)
    {
        Transform t = anim.GetBoneTransform(bone);
        if (t == null) return;

        // 손끝/발끝 쪽으로 반지름만큼 밀어 실제 손·발 위치에 맞춘다
        Transform tip = FurthestChild(t);
        Vector3 center = t.position;
        if (tip != null)
        {
            Vector3 d = tip.position - t.position;
            if (d.magnitude > 1e-4f) center = t.position + d.normalized * Mathf.Min(d.magnitude * 0.5f, radius);
        }
        made.Add(CreateSphere(boss, t, center, Clamp(radius, min, max), display, objectName,
                              LimbMultiplier, layer));
    }

    /// <summary>같은 이름의 히트박스가 이미 있으면 그것을 쓰고, 없으면 본 아래에 새로 만든다.</summary>
    private static GameObject NewHitboxObject(Transform bone, string objectName, int layer, out BossHitbox existing)
    {
        string goName = NamePrefix + objectName;
        if (_existing.TryGetValue(goName, out existing) && existing != null)
        {
            var reused = existing.gameObject;
            Undo.RecordObject(reused, "Update Boss Hitbox");
            Undo.RecordObject(reused.transform, "Update Boss Hitbox");
            reused.layer = layer;
            return reused;
        }

        existing = null;
        var go = new GameObject(goName);
        Undo.RegisterCreatedObjectUndo(go, "Create Boss Hitbox");
        go.transform.SetParent(bone, false);
        go.layer = layer;
        return go;
    }

    /// <summary>필요한 콜라이더를 확보(있으면 그대로 쓰고 Undo만 걸어 둔다).</summary>
    private static T EnsureCollider<T>(GameObject go) where T : Collider
    {
        var col = go.GetComponent<T>();
        if (col == null) return Undo.AddComponent<T>(go);
        Undo.RecordObject(col, "Update Boss Hitbox");
        return col;
    }

    private static BossHitbox Configure(GameObject go, BossController boss, string display, float multiplier,
                                        BossHitbox existing)
    {
        var hb = existing != null ? existing : Undo.AddComponent<BossHitbox>(go);
        Undo.RecordObject(hb, "Update Boss Hitbox");
        // 이미 있던 부위는 손으로 조정한 배율을 존중한다(크기·위치만 다시 계산)
        hb.Configure(boss, display, existing != null ? existing.DamageMultiplier : multiplier);
        EditorUtility.SetDirty(hb);
        return hb;
    }

    // ---------- 레이어 / 마스크 ----------

    /// <summary>
    /// 사격·조준 마스크에는 히트박스를 넣고 본체를 뺀다.
    /// 카메라 충돌과 보스 장애물 판정에서는 히트박스를 뺀다(총알 판정 전용이므로).
    /// </summary>
    private static void ApplyMasks(int hitboxBit, int bodyBit)
    {
        foreach (var sh in Object.FindObjectsByType<PlayerShooter>(FindObjectsSortMode.None))
            EditMask(sh, "hitMask", hitboxBit, bodyBit);
        foreach (var ch in Object.FindObjectsByType<Crosshair>(FindObjectsSortMode.None))
            EditMask(ch, "hitMask", hitboxBit, bodyBit);
        foreach (var pc in Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            EditMask(pc, "aimMask", hitboxBit, bodyBit);

        foreach (var cam in Object.FindObjectsByType<ThirdPersonCamera>(FindObjectsSortMode.None))
            EditMask(cam, "collisionMask", 0, hitboxBit);
        foreach (var boss in Object.FindObjectsByType<BossController>(FindObjectsSortMode.None))
            EditMask(boss, "obstacleMask", 0, hitboxBit);
    }

    private static void EditMask(Object target, string field, int addBits, int removeBits)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null) return;
        p.intValue = (p.intValue | addBits) & ~removeBits;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>이 레이어는 어떤 레이어와도 물리 충돌하지 않는다(레이캐스트는 그대로 맞는다).</summary>
    private static void ClearCollisionMatrixRow(int layer)
    {
        for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(layer, i, true);
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
        throw new System.InvalidOperationException(
            $"빈 User 레이어가 없어 '{layerName}'을 만들지 못했습니다. Project Settings → Tags and Layers에서 자리를 비우세요.");
    }

    // ---------- 헬퍼 ----------

    /// <summary>본 아래 직계 자식 중 가장 멀리 있는 것(머리끝·손끝 근사).</summary>
    private static Transform FurthestChild(Transform t)
    {
        Transform best = null;
        float bestDist = 0f;
        for (int i = 0; i < t.childCount; i++)
        {
            Transform c = t.GetChild(i);
            float d = Vector3.Distance(c.position, t.position);
            if (d > bestDist) { bestDist = d; best = c; }
        }
        return best;
    }

    /// <summary>보스의 실제 키(렌더러 실측, 실패 시 CharacterController).</summary>
    private static float BossWorldHeight(BossController boss)
    {
        bool has = false;
        Bounds b = new Bounds();
        foreach (var r in boss.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer) continue;
            if (!has) { b = r.bounds; has = true; }
            else b.Encapsulate(r.bounds);
        }
        if (has && b.size.y > 1e-4f) return b.size.y;

        var cc = boss.GetComponent<CharacterController>();
        return cc != null ? cc.height * Mathf.Abs(boss.transform.lossyScale.y) : 1.8f;
    }

    /// <summary>
    /// 콜라이더 수치는 로컬 값이라 본의 월드 스케일로 나눠야 한다.
    /// 본 스케일이 축마다 다르면 평균을 쓴다(캐릭터 리그는 보통 균일).
    /// </summary>
    private static float UniformScale(Transform t)
    {
        Vector3 s = t.lossyScale;
        float avg = (Mathf.Abs(s.x) + Mathf.Abs(s.y) + Mathf.Abs(s.z)) / 3f;
        return Mathf.Max(avg, 1e-5f);
    }

    private static float Clamp(float v, float min, float max) => Mathf.Clamp(v, min, max);

    private static string Report(List<BossHitbox> made)
    {
        var sb = new StringBuilder();
        foreach (var hb in made)
        {
            if (hb == null) continue;
            sb.Append($"  {hb.PartName} ×{hb.DamageMultiplier:0.##}");
            var cap = hb.GetComponent<CapsuleCollider>();
            var sph = hb.GetComponent<SphereCollider>();
            float s = UniformScale(hb.transform);
            if (cap != null) sb.Append($"  (캡슐 길이 {cap.height * s:0.00}m, 반지름 {cap.radius * s:0.00}m)");
            else if (sph != null) sb.Append($"  (구 반지름 {sph.radius * s:0.00}m)");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
