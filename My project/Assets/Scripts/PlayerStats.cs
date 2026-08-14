using UnityEngine;

/// <summary>
/// 플레이어 스탯: 체력 / 기력(구르기 에너지) / 타임포스.
/// - 체력: IDamageable로 피격 시 감소(사망 처리는 추후 확장 지점)
/// - 기력: 구르기에 소모, 잠시 후 자동 회복
/// - 타임포스: 과거의 나에게 지원 요청/과거 회귀용 자원.
///   기본은 획득형(자동 회복 0) — AddTimeForce로 채우고 TryUseTimeForce로 소모한다.
/// HudUI가 이 컴포넌트를 읽어 바를 표시한다.
/// </summary>
public class PlayerStats : MonoBehaviour, IDamageable
{
    [Header("체력")]
    [SerializeField] private float maxHealth = 100f;

    [Header("기력(구르기)")]
    [SerializeField] private float maxStamina = 100f;
    [Tooltip("구르기 1회 기력 소모량")]
    [SerializeField] private float rollStaminaCost = 30f;
    [Tooltip("초당 기력 회복량")]
    [SerializeField] private float staminaRegenRate = 25f;
    [Tooltip("기력 사용 후 회복이 시작되기까지의 대기 시간(초)")]
    [SerializeField] private float staminaRegenDelay = 0.8f;

    [Header("타임포스")]
    [SerializeField] private float maxTimeForce = 100f;
    [Tooltip("시작 타임포스")]
    [SerializeField] private float startTimeForce = 0f;
    [Tooltip("초당 자동 충전량(0이면 획득형 자원)")]
    [SerializeField] private float timeForceRegenRate = 0f;
    [Tooltip("구르기로 적 공격을 회피했을 때 획득량")]
    [SerializeField] private float timeForceOnDodge = 1f;
    [Tooltip("공격을 적에게 명중시켰을 때 획득량")]
    [SerializeField] private float timeForceOnHit = 1f;
    [Tooltip("약점(머리 등 배율 1 초과 부위)에 명중시켰을 때 획득량.\n" +
             "정확히 쏠수록 시간 능력을 빨리 쓸 수 있게 하는 보상.")]
    [SerializeField] private float timeForceOnWeakPointHit = 3f;

    private float _health, _stamina, _timeForce;
    private float _lastStaminaUseTime;
    private PlayerController _controller; // 구르기(회피) 상태 확인용

    public float Health => _health;
    public float MaxHealth => maxHealth;
    public float Stamina => _stamina;
    public float MaxStamina => maxStamina;
    public float TimeForce => _timeForce;
    public float MaxTimeForce => maxTimeForce;
    public bool IsDead => _health <= 0f;

    private void Awake()
    {
        _health = maxHealth;
        _stamina = maxStamina;
        _timeForce = Mathf.Clamp(startTimeForce, 0f, maxTimeForce);
        _controller = GetComponent<PlayerController>();
    }

    private void Update()
    {
        // 기력: 마지막 사용 후 딜레이가 지나면 자동 회복
        if (_stamina < maxStamina && Time.time - _lastStaminaUseTime >= staminaRegenDelay)
            _stamina = Mathf.Min(maxStamina, _stamina + staminaRegenRate * Time.deltaTime);

        // 타임포스: 자동 충전형으로 쓰고 싶으면 timeForceRegenRate > 0
        if (timeForceRegenRate > 0f && _timeForce < maxTimeForce)
            _timeForce = Mathf.Min(maxTimeForce, _timeForce + timeForceRegenRate * Time.deltaTime);
    }

    // ---------- 기력 ----------

    /// <summary>구르기용 기력 소모. 부족하면 false(구르기 불가).</summary>
    public bool TryUseRollStamina() => TryUseStamina(rollStaminaCost);

    public bool TryUseStamina(float amount)
    {
        if (_stamina < amount) return false;
        _stamina -= amount;
        _lastStaminaUseTime = Time.time;
        return true;
    }

    // ---------- 타임포스 ----------

    public void AddTimeForce(float amount)
        => _timeForce = Mathf.Clamp(_timeForce + amount, 0f, maxTimeForce);

    /// <summary>공격이 적에게 명중했을 때 호출(PlayerShooter). 타임포스 획득.</summary>
    /// <param name="weakPoint">머리 등 약점 부위였는가(획득량이 늘어난다).</param>
    public void GainTimeForceOnHit(bool weakPoint = false)
        => AddTimeForce(weakPoint ? timeForceOnWeakPointHit : timeForceOnHit);

    /// <summary>타임포스 소모(과거 지원 요청/회귀 발동 시). 부족하면 false.</summary>
    public bool TryUseTimeForce(float amount)
    {
        if (_timeForce < amount) return false;
        _timeForce -= amount;
        return true;
    }

    // ---------- 체력 ----------

    public void Heal(float amount)
        => _health = Mathf.Clamp(_health + amount, 0f, maxHealth);

    /// <summary>시간역행 적용: 체력을 기록 시점 값으로 되돌린다(회복·감소 모두 가능).</summary>
    public void RewindHealth(float health)
        => _health = Mathf.Clamp(health, 0f, maxHealth);

    public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitNormal)
    {
        if (IsDead) return;

        // 구르기 중 피격 = 회피 성공: 데미지 무효(무적 프레임) + 타임포스 획득
        if (_controller != null && _controller.IsRolling)
        {
            AddTimeForce(timeForceOnDodge);
            return;
        }

        _health = Mathf.Max(0f, _health - amount);
        GameSfx.Play(Sfx.PlayerHurt); // 회피(구르기)로 흘렸을 때는 위에서 이미 빠져나갔다
        if (IsDead)
            Debug.Log("[PlayerStats] 플레이어 사망 — 사망 연출/리스폰은 추후 구현 지점.");
    }
}
