using UnityEngine;

/// <summary>
/// 보스의 팔/손 절차적 포즈. 공격 전용 애니메이션 클립이 없으므로,
/// 애니메이터가 만든 포즈 위에 LateUpdate에서 팔을 직접 겨눠 덮어쓴다(휴머노이드 본 기준).
///
/// - AimArm     : 어깨→손이 지정한 월드 방향을 향하도록 위팔/아래팔을 돌린다(팔을 크게 뻗는 형태)
/// - CurlHand   : 손가락을 갈고리처럼 구부린다(할퀴기)
/// - PointIndex : 검지를 방향으로 곧게 편다(레이저 발사 자세)
/// - TwistSpine : 상체를 좌우로 틀어 스윙에 무게를 준다
///
/// 요청은 매 프레임 지정해야 한다(프레임 끝에 가중치 초기화) — 요청이 끊기면 애니메이션 포즈로 자연히 복귀.
/// 리그가 휴머노이드가 아니면 아무것도 하지 않는다(공격 자체는 그대로 동작).
/// </summary>
[RequireComponent(typeof(Animator))]
[DefaultExecutionOrder(50)] // BossController(60)보다 먼저 LateUpdate → 컨트롤러가 포즈 결과(손끝 위치)를 읽는다
public class BossRig : MonoBehaviour
{
    public enum Arm { Left = 0, Right = 1 }

    [Tooltip("팔을 뻗을 때 팔꿈치를 펴는 정도(1=완전히 곧게)")]
    [SerializeField, Range(0f, 1f)] private float elbowStraighten = 0.9f;
    [Tooltip("손가락 한 마디당 최대 구부림 각도(할퀴기)")]
    [SerializeField] private float fingerCurlAngle = 45f;

    private struct ArmPose
    {
        public Vector3 dir;      // 겨냥할 월드 방향
        public float aim;        // 팔 겨냥 가중치
        public float curl;       // 손가락 구부림 0~1
        public float curlWeight;
        public float point;      // 검지 펴기 가중치
    }

    private Animator _anim;
    private ArmPose[] _pose = new ArmPose[2];
    private float _spineTwist, _spineWeight;

    // 본 캐시
    private readonly Transform[] _upper = new Transform[2];
    private readonly Transform[] _lower = new Transform[2];
    private readonly Transform[] _hand = new Transform[2];
    private readonly Transform[][,] _finger = new Transform[2][,]; // [arm][손가락 0~4, 마디 0~2]
    private readonly Vector3[] _bendAxisLocal = new Vector3[2];    // 손 로컬 기준 손가락 굽힘 축
    private readonly float[] _bendSign = new float[2];
    private readonly Transform[] _indexTip = new Transform[2];
    private Transform _chest;
    private bool _ready;

    /// <summary>검지 끝 앵커(레이저 발사 원점 / 충전 구체 부착점). 없으면 손 본.</summary>
    public Transform IndexTip(Arm arm) => _indexTip[(int)arm] != null ? _indexTip[(int)arm] : _hand[(int)arm];

    /// <summary>손 본(없으면 null).</summary>
    public Transform Hand(Arm arm) => _hand[(int)arm];

    /// <summary>할퀴기 궤적을 붙일 손가락 끝들(검지/중지/약지 말단).</summary>
    public Transform[] ClawTips(Arm arm)
    {
        int a = (int)arm;
        if (_finger[a] == null) return new Transform[0];
        var list = new System.Collections.Generic.List<Transform>();
        for (int f = 1; f <= 3; f++) // 검지/중지/약지
        {
            Transform t = LastJoint(a, f);
            if (t != null) list.Add(t);
        }
        return list.ToArray();
    }

    private void Awake()
    {
        _anim = GetComponent<Animator>();
        if (_anim == null || _anim.avatar == null || !_anim.avatar.isHuman)
        {
            Debug.LogWarning("[Boss] 휴머노이드 아바타가 아니어서 절차적 팔 포즈를 사용할 수 없습니다. " +
                             "(공격 판정/이펙트는 정상 동작합니다)");
            return;
        }

        CacheArm(Arm.Left, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                 HumanBodyBones.LeftThumbProximal);
        CacheArm(Arm.Right, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                 HumanBodyBones.RightThumbProximal);

        _chest = _anim.GetBoneTransform(HumanBodyBones.UpperChest)
              ?? _anim.GetBoneTransform(HumanBodyBones.Chest)
              ?? _anim.GetBoneTransform(HumanBodyBones.Spine);

        _ready = _hand[0] != null || _hand[1] != null;
    }

    // ---------- 외부 요청(매 프레임 호출) ----------

    /// <summary>팔 전체(어깨→손)를 월드 방향 dir로 뻗는다. weight 0~1.</summary>
    public void AimArm(Arm arm, Vector3 dir, float weight)
    {
        int a = (int)arm;
        _pose[a].dir = dir;
        _pose[a].aim = Mathf.Clamp01(weight);
    }

    /// <summary>손가락을 갈고리처럼 구부린다. curl 0~1.</summary>
    public void CurlHand(Arm arm, float curl, float weight)
    {
        int a = (int)arm;
        _pose[a].curl = Mathf.Clamp01(curl);
        _pose[a].curlWeight = Mathf.Clamp01(weight);
    }

    /// <summary>검지를 AimArm과 같은 방향으로 곧게 편다(레이저 자세).</summary>
    public void PointIndex(Arm arm, float weight) => _pose[(int)arm].point = Mathf.Clamp01(weight);

    /// <summary>상체를 좌우로 튼다(도). 스윙에 무게를 싣는 용도.</summary>
    public void TwistSpine(float degrees, float weight)
    {
        _spineTwist = degrees;
        _spineWeight = Mathf.Clamp01(weight);
    }

    // ---------- 적용 ----------

    private void LateUpdate()
    {
        if (!_ready) { ClearRequests(); return; }

        if (_spineWeight > 0.001f && _chest != null)
            _chest.rotation = Quaternion.AngleAxis(_spineTwist * _spineWeight, transform.up) * _chest.rotation;

        for (int a = 0; a < 2; a++)
        {
            ref ArmPose p = ref _pose[a];
            if (p.aim > 0.001f) ApplyArmAim(a, p.dir.normalized, p.aim);
            if (p.point > 0.001f) ApplyPointIndex(a, p.dir.normalized, p.point);
            if (p.curlWeight > 0.001f) ApplyCurl(a, p.curl * p.curlWeight);
        }

        ClearRequests();
    }

    private void ClearRequests()
    {
        for (int a = 0; a < 2; a++)
        {
            _pose[a].aim = 0f;
            _pose[a].curlWeight = 0f;
            _pose[a].point = 0f;
        }
        _spineWeight = 0f;
    }

    /// <summary>
    /// 위팔을 돌려 어깨→손이 dir을 향하게 하고, 아래팔을 한 번 더 돌려 팔꿈치를 편다.
    /// 본의 로컬 축을 몰라도 되도록 "현재 방향 → 목표 방향" 델타 회전만 쓴다(리그 무관).
    /// </summary>
    private void ApplyArmAim(int a, Vector3 dir, float weight)
    {
        Transform upper = _upper[a], lower = _lower[a], hand = _hand[a];
        if (upper == null || hand == null) return;

        Vector3 cur = hand.position - upper.position;
        if (cur.sqrMagnitude > 1e-10f)
        {
            Quaternion delta = Quaternion.FromToRotation(cur.normalized, dir);
            upper.rotation = Quaternion.Slerp(Quaternion.identity, delta, weight) * upper.rotation;
        }

        if (lower == null) return;
        // 위팔을 돌린 뒤의 최신 위치 기준으로 아래팔을 정렬 = 팔꿈치 펴기
        Vector3 cur2 = hand.position - lower.position;
        if (cur2.sqrMagnitude > 1e-10f)
        {
            Quaternion delta2 = Quaternion.FromToRotation(cur2.normalized, dir);
            lower.rotation = Quaternion.Slerp(Quaternion.identity, delta2, weight * elbowStraighten) * lower.rotation;
        }
    }

    /// <summary>검지 마디들을 dir 방향으로 곧게 편다(손끝에서 레이저가 나가는 자세).</summary>
    private void ApplyPointIndex(int a, Vector3 dir, float weight)
    {
        if (_finger[a] == null) return;
        for (int j = 0; j < 3; j++)
        {
            Transform bone = _finger[a][1, j];
            if (bone == null) continue;

            // 이 마디가 현재 향하는 방향 = 다음 마디 쪽. 말단은 이전 마디에서 이어지는 방향으로 근사한다.
            Transform next = j + 1 < 3 ? _finger[a][1, j + 1] : null;
            Transform prev = j > 0 ? _finger[a][1, j - 1] : _hand[a];
            Vector3 cur = next != null ? next.position - bone.position
                        : prev != null ? bone.position - prev.position
                        : Vector3.zero;
            if (cur.sqrMagnitude < 1e-12f) continue;

            Quaternion delta = Quaternion.FromToRotation(cur.normalized, dir);
            bone.rotation = Quaternion.Slerp(Quaternion.identity, delta, weight) * bone.rotation;
        }
    }

    /// <summary>손가락 전체를 손바닥 쪽으로 구부린다(갈고리 손).</summary>
    private void ApplyCurl(int a, float amount)
    {
        if (_finger[a] == null || _hand[a] == null) return;
        Vector3 axis = _hand[a].TransformDirection(_bendAxisLocal[a]) * _bendSign[a];

        for (int f = 0; f < 5; f++)
        {
            for (int j = 0; j < 3; j++)
            {
                Transform bone = _finger[a][f, j];
                if (bone == null) continue;
                // 뿌리보다 끝마디를 더 굽혀야 갈고리 모양이 된다
                float k = f == 0 ? 0.55f : (0.7f + 0.25f * j); // 엄지는 약하게
                bone.rotation = Quaternion.AngleAxis(fingerCurlAngle * k * amount, axis) * bone.rotation;
            }
        }
    }

    // ---------- 본 캐시 ----------

    private void CacheArm(Arm arm, HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones hand,
                          HumanBodyBones thumbProximal)
    {
        int a = (int)arm;
        _upper[a] = _anim.GetBoneTransform(upper);
        _lower[a] = _anim.GetBoneTransform(lower);
        _hand[a] = _anim.GetBoneTransform(hand);
        if (_hand[a] == null) return;

        // 손가락 본은 HumanBodyBones에서 [엄지·검지·중지·약지·새끼] × [뿌리·중간·끝] 순으로 연속 배치돼 있다
        _finger[a] = new Transform[5, 3];
        for (int f = 0; f < 5; f++)
            for (int j = 0; j < 3; j++)
                _finger[a][f, j] = _anim.GetBoneTransform((HumanBodyBones)((int)thumbProximal + f * 3 + j));

        // 굽힘 축: 손가락 뿌리들이 늘어선 선(너클 라인)과 평행하다
        Transform index = _finger[a][1, 0];
        Transform little = _finger[a][4, 0] ?? _finger[a][3, 0] ?? _finger[a][2, 0];
        Vector3 axisWorld = (index != null && little != null && little != index)
            ? (little.position - index.position)
            : _hand[a].right;
        if (axisWorld.sqrMagnitude < 1e-10f) axisWorld = _hand[a].right;
        _bendAxisLocal[a] = _hand[a].InverseTransformDirection(axisWorld.normalized);

        // 부호: 축을 중심으로 돌렸을 때 손끝이 손목에 "가까워지는" 쪽이 구부리는 방향
        _bendSign[a] = ResolveBendSign(a);

        // 검지 끝 앵커: 말단 마디 끝(레이저 발사 원점 / 충전 구체 부착점)
        _indexTip[a] = MakeIndexTip(a);
    }

    private float ResolveBendSign(int a)
    {
        Transform root = _finger[a][2, 0] ?? _finger[a][1, 0]; // 중지(없으면 검지) 뿌리
        Transform tip = LastJoint(a, root == _finger[a][1, 0] ? 1 : 2);
        if (root == null || tip == null || tip == root) return 1f;

        Vector3 axis = _hand[a].TransformDirection(_bendAxisLocal[a]);
        Vector3 rel = tip.position - root.position;
        Vector3 plus = root.position + Quaternion.AngleAxis(45f, axis) * rel;
        Vector3 minus = root.position + Quaternion.AngleAxis(-45f, axis) * rel;
        Vector3 wrist = _hand[a].position;
        return (plus - wrist).sqrMagnitude < (minus - wrist).sqrMagnitude ? 1f : -1f;
    }

    /// <summary>손가락 f의 존재하는 마지막 마디.</summary>
    private Transform LastJoint(int a, int f)
    {
        if (_finger[a] == null) return null;
        for (int j = 2; j >= 0; j--)
            if (_finger[a][f, j] != null) return _finger[a][f, j];
        return null;
    }

    /// <summary>검지 말단 앞쪽에 앵커를 만들어 손끝(발사 원점)으로 쓴다.</summary>
    private Transform MakeIndexTip(int a)
    {
        Transform distal = LastJoint(a, 1);
        if (distal == null) return null;

        // 마디 길이만큼 더 뻗은 지점 = 손톱 끝. 이전 마디가 없으면 손 크기로 근사.
        Transform prev = _finger[a][1, 1] ?? _finger[a][1, 0] ?? _hand[a];
        Vector3 dir = distal.position - prev.position;
        float len = dir.magnitude;
        if (len < 1e-6f) { dir = distal.forward; len = 0.02f; }
        Vector3 tipWorld = distal.position + dir.normalized * len;

        var go = new GameObject("IndexTip");
        go.transform.SetParent(distal, true);
        go.transform.position = tipWorld;
        go.transform.rotation = distal.rotation;
        return go.transform;
    }
}
