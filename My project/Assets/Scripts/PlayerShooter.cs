using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// TPS 발사 로직.
/// - 좌클릭으로 발사(연사 지원, fireRate로 속도 제어)
/// - 조준(우클릭) 여부는 ThirdPersonCamera에서 참조
/// - 화면 중앙에서 카메라 정면으로 Raycast → 명중 지점 계산
/// - 총구(muzzlePoint)에서 명중 지점을 향해 발사 이펙트/트레이서 원점 사용
/// - 명중 시 임팩트 이펙트 생성, IDamageable 대상엔 데미지 전달
/// Animator가 있으면 Fire 트리거를 발생시킨다(없어도 동작).
/// </summary>
public class PlayerShooter : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private ThirdPersonCamera tpsCamera;
    [SerializeField] private Camera aimCamera;
    [Tooltip("총구 위치. WeaponHolder가 붙인 총의 총구 빈 오브젝트를 지정.")]
    [SerializeField] private Transform muzzlePoint;
    [SerializeField] private Animator animator;

    [Header("발사 설정")]
    [SerializeField] private float fireRate = 10f;      // 초당 발사 수
    [SerializeField] private float range = 200f;
    [SerializeField] private float damage = 15f;
    [Tooltip("조준(우클릭) 중일 때만 발사할지 여부")]
    [SerializeField] private bool requireAimToFire = true;
    [SerializeField] private LayerMask hitMask = ~0;

    [Header("무기 조준 정렬(총구를 크로스헤어로)")]
    [Tooltip("애니메이션 이후 총을 화면 중앙(크로스헤어) 지점으로 직접 겨눈다.\n" +
             "주의: 손에 맞춰둔 장착 회전을 덮어써 총이 손에서 이탈할 수 있어 기본 꺼짐.\n" +
             "시선/상체 정렬은 PlayerController의 LookAt IK가 담당한다.")]
    [SerializeField] private bool aimWeaponToCrosshair = false;
    [Range(0f, 1f)]
    [Tooltip("정렬 강도(1=완전히 크로스헤어로, 0=애니메이션 그대로).")]
    [SerializeField] private float aimWeight = 1f;
    [Tooltip("총열 방향(총 로컬 축) 수동 지정. (0,0,0)이면 메시로 자동 판별. 자동이 앞뒤/축이 틀리면 예: (0,0,1),(0,0,-1),(1,0,0) 등으로 지정.")]
    [SerializeField] private Vector3 barrelAxisOverride = Vector3.zero;

    [Header("이펙트(선택)")]
    [SerializeField] private ParticleSystem muzzleFlash;
    [SerializeField] private GameObject impactPrefab;
    [SerializeField] private LineRenderer tracer;
    [SerializeField] private float tracerDuration = 0.03f;

    private float _nextFireTime;
    private float _tracerHideTime;
    private Vector3 _localBarrelAxis = Vector3.forward; // 총 로컬 총열 축(자동 판별)
    private Vector3 _gunBoundsCenter;                    // 총 로컬 바운즈 중심(총구 끝 계산용)
    private float _gunExtentAlong;                       // 총열 축 방향 절반 길이
    private bool _barrelResolved;
    private ParticleSystem _autoFlash;                   // 자동 생성 총구 화염
    private static readonly int FireHash = Animator.StringToHash("Fire");

    private void Awake()
    {
        if (aimCamera == null) aimCamera = Camera.main;
        if (tpsCamera == null && aimCamera != null) tpsCamera = aimCamera.GetComponent<ThirdPersonCamera>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (tracer == null) tracer = CreateTracer(); // 지정 안 하면 자동 생성
        tracer.enabled = false;
    }

    private void Update()
    {
        if (Mouse.current == null || aimCamera == null) return;

        bool canFire = !requireAimToFire || (tpsCamera != null && tpsCamera.IsAiming);
        if (canFire && Mouse.current.leftButton.isPressed && Time.time >= _nextFireTime)
        {
            _nextFireTime = Time.time + 1f / Mathf.Max(0.01f, fireRate);
            Fire();
        }

        if (tracer != null && tracer.enabled && Time.time >= _tracerHideTime)
            tracer.enabled = false;
    }

    // 애니메이션(손 포즈) 이후에 총을 크로스헤어 지점으로 정렬 → 조준 포즈의 총구 어긋남 보정
    private void LateUpdate()
    {
        if (!aimWeaponToCrosshair || aimCamera == null || muzzlePoint == null) return;

        Transform gun = muzzlePoint.parent; // Muzzle의 부모 = 총 오브젝트
        if (gun == null) return;

        // 총열(로컬) 축 1회 결정: 오버라이드 우선, 없으면 메시 최장축 + 플레이어 정면 기준 부호
        if (!_barrelResolved)
        {
            _localBarrelAxis = ResolveLocalBarrelAxis(gun);
            _barrelResolved = true;
        }

        Vector3 barrelWorld = gun.rotation * _localBarrelAxis;         // 현재 총열 방향(월드)
        if (Vector3.Dot(barrelWorld, transform.forward) < 0f) barrelWorld = -barrelWorld; // 앞 반구로
        Vector3 desiredDir = (GetAimPoint() - muzzlePoint.position);   // 조준 방향
        if (desiredDir.sqrMagnitude < 1e-6f) return;
        desiredDir.Normalize();

        // 손 포즈(그립=손)를 유지한 채 총열만 조준방향으로 델타 회전.
        // 몸통도 크로스헤어를 향하므로 델타가 작아 롤/뒤집힘이 생기지 않는다.
        Quaternion delta = Quaternion.FromToRotation(barrelWorld, desiredDir);
        Quaternion target = delta * gun.rotation;
        gun.rotation = Quaternion.Slerp(gun.rotation, target, aimWeight);
    }

    /// <summary>
    /// 총 메시 최장축을 총열 축으로 판별(오버라이드 우선)하고,
    /// 총구 끝 계산용 로컬 바운즈 중심/절반 길이도 저장한다. 부호는 사용처에서 매 프레임 결정.
    /// </summary>
    private Vector3 ResolveLocalBarrelAxis(Transform gun)
    {
        bool has = false;
        Bounds local = new Bounds();
        foreach (var mf in gun.GetComponentsInChildren<MeshFilter>())
            if (mf.sharedMesh != null) EncapsulateLocal(gun, mf.transform, mf.sharedMesh.bounds, ref local, ref has);
        foreach (var smr in gun.GetComponentsInChildren<SkinnedMeshRenderer>())
            if (smr.sharedMesh != null) EncapsulateLocal(gun, smr.transform, smr.sharedMesh.bounds, ref local, ref has);

        Vector3 barrel;
        if (barrelAxisOverride.sqrMagnitude > 1e-6f) barrel = barrelAxisOverride.normalized;
        else if (has)
        {
            Vector3 s = local.size;
            int longest = s.x >= s.y && s.x >= s.z ? 0 : (s.y >= s.z ? 1 : 2);
            barrel = longest == 0 ? Vector3.right : longest == 1 ? Vector3.up : Vector3.forward;
        }
        else barrel = Vector3.forward;

        _gunBoundsCenter = has ? local.center : Vector3.zero;
        Vector3 abs = new Vector3(Mathf.Abs(barrel.x), Mathf.Abs(barrel.y), Mathf.Abs(barrel.z));
        _gunExtentAlong = has ? Vector3.Dot(local.extents, abs) : 0.3f;
        return barrel;
    }

    private static void EncapsulateLocal(Transform gun, Transform meshT, Bounds meshBounds, ref Bounds acc, ref bool has)
    {
        Vector3 c = meshBounds.center, e = meshBounds.extents;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = c + new Vector3(
                (i & 1) == 0 ? -e.x : e.x,
                (i & 2) == 0 ? -e.y : e.y,
                (i & 4) == 0 ? -e.z : e.z);
            Vector3 localPt = gun.InverseTransformPoint(meshT.TransformPoint(corner));
            if (!has) { acc = new Bounds(localPt, Vector3.zero); has = true; }
            else acc.Encapsulate(localPt);
        }
    }

    /// <summary>화면 중앙(크로스헤어) 레이가 맞는 월드 지점. 없으면 사거리 끝점.</summary>
    private Vector3 GetAimPoint()
    {
        Ray ray = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Physics.Raycast(ray, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore)
            && !hit.collider.transform.IsChildOf(transform)) // 자기 몸 제외
            return hit.point;
        return ray.origin + ray.direction * range;
    }

    private void Fire()
    {
        // 화면 중앙에서 카메라 정면으로 레이 발사(탄착 판정은 항상 크로스헤어와 일치)
        Ray ray = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 targetPoint = ray.origin + ray.direction * range;
        bool didHit = Physics.Raycast(ray, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore);
        if (didHit)
        {
            targetPoint = hit.point;

            // 데미지 전달
            var damageable = hit.collider.GetComponentInParent<IDamageable>();
            damageable?.TakeDamage(damage, hit.point, hit.normal);

            SpawnImpact(hit.point, hit.normal);
        }

        // 총구 위치(총 메시 실측 끝) / 발사 방향
        GetMuzzle(out Vector3 muzzlePos, out _);
        Vector3 fireDir = (targetPoint - muzzlePos).normalized;

        // 총구 화염
        if (muzzleFlash != null)
        {
            muzzleFlash.transform.SetPositionAndRotation(muzzlePos, Quaternion.LookRotation(fireDir));
            muzzleFlash.Play();
        }
        else
        {
            if (_autoFlash == null)
                _autoFlash = BuildBurstFx(transform, "MuzzleFlashFX",
                    new Color(1f, 0.85f, 0.35f), life: 0.06f, size: 0.05f, count: 12, coneAngle: 25f,
                    minSpeed: 2f, maxSpeed: 4f);
            _autoFlash.transform.SetPositionAndRotation(muzzlePos, Quaternion.LookRotation(fireDir));
            _autoFlash.Play();
        }

        // 트레이서(총구 → 탄착점)
        if (tracer != null)
        {
            tracer.enabled = true;
            tracer.positionCount = 2;
            tracer.SetPosition(0, muzzlePos);
            tracer.SetPosition(1, targetPoint);
            _tracerHideTime = Time.time + tracerDuration;
        }

        if (animator != null && animator.runtimeAnimatorController != null)
            animator.SetTrigger(FireHash);
    }

    /// <summary>탄착 이펙트: 프리팹이 있으면 사용, 없으면 스파크 버스트를 코드로 생성.</summary>
    private void SpawnImpact(Vector3 pos, Vector3 normal)
    {
        if (impactPrefab != null)
        {
            GameObject fx = Instantiate(impactPrefab, pos, Quaternion.LookRotation(normal));
            Destroy(fx, 3f);
            return;
        }
        var ps = BuildBurstFx(null, "ImpactFX",
            new Color(1f, 0.8f, 0.45f), life: 0.25f, size: 0.03f, count: 16, coneAngle: 45f,
            minSpeed: 0.8f, maxSpeed: 2f);
        ps.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(normal));
        ps.Play();
        Destroy(ps.gameObject, 1f);
    }

    /// <summary>총구 위치(총 메시 바운즈의 총열 방향 끝)와 총열 방향(월드)을 구한다.</summary>
    private void GetMuzzle(out Vector3 pos, out Vector3 dir)
    {
        Transform gun = muzzlePoint != null ? muzzlePoint.parent : null;
        if (gun != null)
        {
            if (!_barrelResolved)
            {
                _localBarrelAxis = ResolveLocalBarrelAxis(gun);
                _barrelResolved = true;
            }
            Vector3 axisW = gun.rotation * _localBarrelAxis;
            float sign = Vector3.Dot(axisW, transform.forward) < 0f ? -1f : 1f; // 총구는 항상 앞 반구
            pos = gun.TransformPoint(_gunBoundsCenter + _localBarrelAxis * (_gunExtentAlong * sign));
            dir = axisW * sign;
            return;
        }
        pos = muzzlePoint != null ? muzzlePoint.position : aimCamera.transform.position;
        dir = aimCamera.transform.forward;
    }

    /// <summary>1회 버스트 파티클 이펙트를 코드로 생성(에셋 불필요).</summary>
    private static ParticleSystem BuildBurstFx(Transform parent, string name, Color color,
        float life, float size, int count, float coneAngle, float minSpeed, float maxSpeed)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = life;
        main.startLifetime = life;
        main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
        main.startColor = color;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 64;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = coneAngle;
        shape.radius = 0.01f;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.material = new Material(Shader.Find("Sprites/Default"));
        return ps;
    }

    /// <summary>노란 탄도선용 LineRenderer를 코드로 생성.</summary>
    private LineRenderer CreateTracer()
    {
        var go = new GameObject("TracerFX");
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = new Color(1f, 0.9f, 0.4f, 1f);
        lr.endColor = new Color(1f, 0.6f, 0.1f, 0.35f);
        lr.startWidth = 0.015f;
        lr.endWidth = 0.008f;
        lr.positionCount = 2;
        lr.numCapVertices = 2;
        lr.enabled = false;
        return lr;
    }

    private void OnDrawGizmosSelected()
    {
        if (muzzlePoint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(muzzlePoint.position, muzzlePoint.forward * 2f);
        }
    }
}

/// <summary>피격 가능한 대상 인터페이스. 적/파괴 오브젝트가 구현한다.</summary>
public interface IDamageable
{
    void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitNormal);
}
