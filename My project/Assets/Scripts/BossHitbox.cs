using UnityEngine;

/// <summary>
/// 보스 부위 히트박스. 본(bone)에 붙은 콜라이더 하나 = 부위 하나이며,
/// 들어온 피해에 배율을 곱해 보스 본체로 넘긴다(머리 ×2.5, 몸통 ×1, 팔다리 ×0.6 …).
///
/// 동작 원리
///  - 사격 코드는 모두 hit.collider.GetComponentInParent&lt;IDamageable&gt;()로 대상을 찾는다.
///    GetComponentInParent는 "맞은 콜라이더의 오브젝트부터" 위로 올라가므로,
///    콜라이더와 같은 오브젝트에 있는 이 컴포넌트가 BossController보다 먼저 잡힌다
///    → PlayerShooter/TimeShiftController는 고칠 것이 없다.
///  - 보스 본체(CharacterController)는 BodyLayer로 옮겨져 사격 마스크에서 빠진다.
///    총알이 몸통 캡슐에 먼저 막히면 부위 판정이 영영 오지 않기 때문이다.
///  - 히트박스는 HitboxLayer(물리 충돌 없음)에 올라간다. 레이캐스트는 충돌 매트릭스와
///    무관하게 그대로 맞으므로, 총알 판정만 받고 아무도 밀지 않는다.
///
/// 배치는 메뉴 <b>Tools/TPS/Setup Boss Hitboxes (부위별 데미지)</b>가 자동으로 한다.
/// 생성 뒤 배율은 각 히트박스 인스펙터에서 자유롭게 조절하면 된다.
/// </summary>
[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class BossHitbox : MonoBehaviour, IDamageable
{
    /// <summary>부위 히트박스 전용 레이어(물리 충돌 없음 · 사격 마스크에는 포함).</summary>
    public const string HitboxLayer = "BossHitbox";
    /// <summary>보스 본체(CharacterController) 레이어. 사격/조준 마스크에서 제외된다.</summary>
    public const string BodyLayer = "BossBody";

    [Tooltip("표시용 부위 이름(머리 / 가슴 / 왼팔 …). 판정에는 쓰이지 않는다.")]
    [SerializeField] private string partName = "부위";

    [Tooltip("이 부위에 맞았을 때의 데미지 배율.\n1 = 그대로, 2.5 = 약점(머리), 0.6 = 팔다리, 0 = 무효.")]
    [SerializeField, Min(0f)] private float damageMultiplier = 1f;

    [Tooltip("비우면 부모에서 자동으로 찾는다.")]
    [SerializeField] private BossController owner;

    private Collider _col;

    public string PartName => partName;
    public float DamageMultiplier => damageMultiplier;

    /// <summary>이 부위의 콜라이더(숨김/충돌 제외 처리에 쓰인다).</summary>
    public Collider Collider
    {
        get
        {
            if (_col == null) _col = GetComponent<Collider>();
            return _col;
        }
    }

    /// <summary>배율이 1을 넘는 약점인가(피격 점멸을 길게 준다).</summary>
    public bool IsWeakPoint => damageMultiplier > 1.001f;

    private void Awake()
    {
        _col = GetComponent<Collider>();
        if (owner == null) owner = GetComponentInParent<BossController>();
        if (owner == null)
            Debug.LogWarning($"[BossHitbox] '{name}'의 주인(BossController)을 찾지 못했습니다. " +
                             "이 부위는 피해를 전달하지 않습니다.");
    }

    public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitNormal)
    {
        if (owner == null) return;
        owner.TakeDamage(amount * damageMultiplier, hitPoint, hitNormal, this);
    }

    /// <summary>에디터 도구가 생성 직후 값을 채우는 용도.</summary>
    public void Configure(BossController bossOwner, string part, float multiplier)
    {
        owner = bossOwner;
        partName = part;
        damageMultiplier = multiplier;
    }
}
