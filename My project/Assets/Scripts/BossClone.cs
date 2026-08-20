using UnityEngine;

/// <summary>
/// 보스 분신(체력 30% "분신 처형" 패턴에서 소환).
/// 보스 모델을 복제해 스크립트/물리를 모두 떼어낸 껍데기이며, AI는 없고
/// BossController가 지휘하는 대로 팔을 들어 레이저를 충전하다가 일제히 발사한다.
///
/// - IDamageable을 구현하지만 피해를 받지 않는다 — 플레이어/분신의 공격이 "겨눌 수 있는"
///   표적이 되어야, 진짜 보스를 잘못 짚었을 때 협공이 헛나가는 긴장감이 생긴다.
/// - 진짜 보스와 분신은 충전 색으로만 구분된다(진짜만 다른 색).
/// </summary>
[DefaultExecutionOrder(60)] // BossRig(50)가 팔 포즈를 적용한 뒤 손끝 위치를 읽는다
public class BossClone : MonoBehaviour, IDamageable
{
    private BossRig _rig;
    private BossFx.ChargeOrb _orb;
    private BossFx.Beam _beam;
    private BossFx.Flash _flash;
    private Transform _target;
    private IDamageable _targetDamage;
    private float _k = 1f;
    private LayerMask _mask;
    private Vector3 _aimDir = Vector3.forward;
    private bool _charging;
    private float _charge;

    /// <summary>충전 진행도 0~1(BossController가 매 프레임 갱신). 1에 가까울수록 빠르게 일렁인다.</summary>
    public void SetCharge(float value) => _charge = Mathf.Clamp01(value);

    /// <summary>분신은 피해를 받지 않는다(가짜라는 사실은 색으로만 드러난다).</summary>
    public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitNormal) { }

    // ---------- 생성/소멸 ----------

    /// <summary>
    /// 보스 원본(source)을 복제해 분신을 만든다.
    /// 비활성 컨테이너 안에서 복제하므로 원본 스크립트들의 Awake가 돌지 않는다
    /// (돌면 분신이 또 분신을 만들거나 AI가 두 벌 도는 사고가 난다).
    /// </summary>
    public static BossClone Spawn(GameObject source, Vector3 position, Quaternion rotation,
                                  float k, Color color, LayerMask mask)
    {
        var holder = new GameObject("BossCloneBuilder");
        holder.SetActive(false);

        GameObject go = Instantiate(source, holder.transform);
        go.name = "BossClone";

        // 보스에 붙어 있던 이펙트를 통째로 들어낸다.
        // ※ 반드시 스크립트를 지우기 '전'에 해야 한다 — 표식(BossFx.Tag)도 MonoBehaviour라
        //   아래 루프가 먼저 돌면 표식이 사라져 이펙트를 못 찾는다.
        // ※ 예전에는 렌더러 종류(LineRenderer/TrailRenderer/ParticleSystemRenderer)로 걸렀는데,
        //   이펙트가 셰이더 쿼드(MeshRenderer)로 바뀌면서 그 필터를 빠져나갔다. 그 결과
        //   분신마다 원본의 죽은 광선·장판이 복제돼 붙고, 아래 'r.enabled = true'가 그것들을
        //   전부 켜 버려서 분신 머리 위·옆에 엉뚱한 레이저가 떠 있었다.
        foreach (var tag in go.GetComponentsInChildren<BossFx.Tag>(true))
            if (tag != null) DestroyImmediate(tag.gameObject);

        // 스크립트/물리 잔재 제거 — 순수한 모델 + Animator만 남긴다
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null) DestroyImmediate(mb);
        var cc = go.GetComponent<CharacterController>();
        if (cc != null) DestroyImmediate(cc);

        // 부위 히트박스의 콜라이더까지 정리한다(스크립트는 위에서 이미 제거됐다).
        // 분신은 물리적으로 존재하지 않는 껍데기이며, 남겨두면 제 손끝에서 나가는
        // 레이저를 제 팔이 가로막는다(분신의 사격은 자기 몸을 걸러내지 않는다).
        foreach (var col in go.GetComponentsInChildren<Collider>(true))
            if (col != null) DestroyImmediate(col);
        // 표식이 없는 옛 잔재(프리팹에 직접 박아 둔 트레일·파티클 등)까지 마저 정리한다
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;
            if (r is LineRenderer || r is TrailRenderer || r is ParticleSystemRenderer)
                DestroyImmediate(r.gameObject);
        }
        foreach (var l in go.GetComponentsInChildren<Light>(true))
            if (l != null) DestroyImmediate(l.gameObject);

        // 원본이 피격 점멸 중이었다면 그 색까지 복제된다 → 프로퍼티 블록을 비워 원래 색으로
        var mpb = new MaterialPropertyBlock();
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;
            r.SetPropertyBlock(mpb);
            r.enabled = true; // 텔레포트 연출로 숨겨진 상태가 복제되지 않게
        }

        go.transform.SetParent(null, false);
        go.transform.SetPositionAndRotation(position, rotation);
        go.SetActive(true);
        Destroy(holder);

        var clone = go.AddComponent<BossClone>();
        clone.Init(k, color, mask);
        return clone;
    }

    private void Init(float k, Color color, LayerMask mask)
    {
        _k = k;
        _mask = mask;

        var anim = GetComponentInChildren<Animator>();
        if (anim != null)
        {
            anim.applyRootMotion = false;
            _rig = anim.GetComponent<BossRig>();
            if (_rig == null) _rig = anim.gameObject.AddComponent<BossRig>();
        }

        Transform tip = _rig != null ? _rig.IndexTip(BossRig.Arm.Left) : null;
        // 분신은 10기가 동시에 뜬다 — 각자 점광원을 켜면 광원 개수 제한을 크게 넘겨
        // 매 프레임 어느 광원을 쓸지가 뒤바뀌며 화면이 번쩍인다. 구체 글로우만으로 충분하다.
        _orb = BossFx.BuildChargeOrb(tip != null ? tip : transform, _k, color, withLight: false);
        _beam = BossFx.BuildBeam(transform, _k, color);
        _flash = BossFx.BuildFlash(_k, color);
        _flash.Spawn(BodyCenter()); // 등장 번쩍임
    }

    /// <summary>사라짐 연출 후 제거.</summary>
    public void Despawn()
    {
        if (_flash != null)
        {
            _flash.Spawn(BodyCenter());
            // 섬광 파티클은 씬 최상위에 있으므로 잔상이 끝난 뒤 정리한다
            if (_flash.Root != null) Destroy(_flash.Root, 1.5f);
        }
        if (_beam != null) _beam.Hide();
        Destroy(gameObject);
    }

    // ---------- 충전/발사 ----------

    /// <summary>충전 자세 시작 — 대상을 향해 몸을 돌리고 왼팔을 뻗는다.</summary>
    public void BeginCharge(Transform target)
    {
        _target = target;
        _targetDamage = target != null ? target.GetComponentInParent<IDamageable>() : null;
        _charging = true;

        FaceTarget(instant: true);
        _aimDir = AimDirection();
    }

    /// <summary>일제 사격 한 발. 조준이 이미 대상에 고정돼 있어 확실히 명중한다.</summary>
    public void Fire(float damage)
    {
        _charging = false;
        _charge = 0f;
        if (_orb != null) { _orb.Burst(); _orb.Visible = false; }

        Vector3 from = MuzzlePoint();
        _aimDir = AimDirection();
        float range = 200f * _k;
        Vector3 to = from + _aimDir * range;
        if (Physics.Raycast(from, _aimDir, out RaycastHit hit, range, _mask, QueryTriggerInteraction.Ignore))
            to = hit.point;

        if (_beam != null) _beam.Fire(from, to, 0.6f);
        GameSfx.PlayAt(Sfx.BossLaser, from, pitch: Random.Range(0.95f, 1.08f));

        if (_targetDamage != null && _target != null)
        {
            Vector3 center = TargetCenter();
            _targetDamage.TakeDamage(damage, center, -_aimDir);
        }
    }

    private void Update()
    {
        if (!_charging) return;

        FaceTarget(instant: false);

        // 조준은 계속 대상을 따라간다 — 이 패턴은 회피가 아니라 '진짜 찾기'로 파훼한다
        _aimDir = Vector3.RotateTowards(_aimDir, AimDirection(), 360f * Mathf.Deg2Rad * Time.deltaTime, 0f);

        if (_rig != null)
        {
            _rig.AimArm(BossRig.Arm.Left, _aimDir, 1f);
            _rig.PointIndex(BossRig.Arm.Left, 1f);
        }
    }

    private void LateUpdate()
    {
        if (_orb == null) return;
        _orb.Visible = _charging;
        _orb.Charge = _charge;

        if (_charging && _beam != null)
        {
            Vector3 from = MuzzlePoint();
            _beam.Preview(from, from + _aimDir * (200f * _k), Mathf.Lerp(0.3f, 0.95f, _charge * _charge));
        }
    }

    // ---------- 헬퍼 ----------

    private void FaceTarget(bool instant)
    {
        if (_target == null) return;
        Vector3 to = _target.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 1e-6f) return;

        Quaternion want = Quaternion.LookRotation(to.normalized);
        transform.rotation = instant ? want
            : Quaternion.Slerp(transform.rotation, want, 8f * Time.deltaTime);
    }

    private Vector3 AimDirection()
    {
        Vector3 d = TargetCenter() - MuzzlePoint();
        return d.sqrMagnitude > 1e-8f ? d.normalized : transform.forward;
    }

    private Vector3 MuzzlePoint()
    {
        Transform tip = _rig != null ? _rig.IndexTip(BossRig.Arm.Left) : null;
        if (tip != null) return tip.position;
        return transform.position + Vector3.up * (1.4f * _k) + transform.forward * (0.4f * _k);
    }

    private Vector3 TargetCenter()
    {
        if (_target == null) return transform.position + transform.forward;
        var cc = _target.GetComponentInParent<CharacterController>();
        return cc != null ? _target.TransformPoint(cc.center) : _target.position + Vector3.up * (0.9f * _k);
    }

    private Vector3 BodyCenter() => transform.position + Vector3.up * (0.9f * _k);
}
