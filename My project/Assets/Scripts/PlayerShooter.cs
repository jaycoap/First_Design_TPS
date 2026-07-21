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
    [SerializeField] private bool requireAimToFire = false;
    [SerializeField] private LayerMask hitMask = ~0;

    [Header("이펙트(선택)")]
    [SerializeField] private ParticleSystem muzzleFlash;
    [SerializeField] private GameObject impactPrefab;
    [SerializeField] private LineRenderer tracer;
    [SerializeField] private float tracerDuration = 0.03f;

    private float _nextFireTime;
    private float _tracerHideTime;
    private static readonly int FireHash = Animator.StringToHash("Fire");

    private void Awake()
    {
        if (aimCamera == null) aimCamera = Camera.main;
        if (tpsCamera == null && aimCamera != null) tpsCamera = aimCamera.GetComponent<ThirdPersonCamera>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (tracer != null) tracer.enabled = false;
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

    private void Fire()
    {
        // 화면 중앙에서 카메라 정면으로 레이 발사
        Ray ray = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 targetPoint = ray.origin + ray.direction * range;
        bool didHit = Physics.Raycast(ray, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore);
        if (didHit)
        {
            targetPoint = hit.point;

            // 데미지 전달
            var damageable = hit.collider.GetComponentInParent<IDamageable>();
            damageable?.TakeDamage(damage, hit.point, hit.normal);

            // 임팩트 이펙트
            if (impactPrefab != null)
            {
                GameObject fx = Instantiate(impactPrefab, hit.point, Quaternion.LookRotation(hit.normal));
                Destroy(fx, 3f);
            }
        }

        // 총구 이펙트 / 트레이서
        Vector3 muzzlePos = muzzlePoint != null ? muzzlePoint.position : ray.origin;
        if (muzzleFlash != null) muzzleFlash.Play();
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
