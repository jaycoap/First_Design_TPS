using UnityEngine;

/// <summary>
/// 발사 테스트용 표적. IDamageable을 구현해 피격 시 체력이 닳고, 0이 되면 파괴된다.
/// 아무 큐브에나 이 컴포넌트와 Collider를 붙이면 사격 대상이 된다.
/// </summary>
public class TargetDummy : MonoBehaviour, IDamageable, IRewindableExtra
{
    [SerializeField] private float maxHealth = 50f;
    [Tooltip("피격 시 잠깐 표시할 색(있으면 Renderer 색을 깜빡임)")]
    [SerializeField] private Color hitFlashColor = Color.red;
    [SerializeField] private float flashDuration = 0.08f;

    private float _health;
    private Renderer _renderer;
    private Color _baseColor;
    private float _flashUntil;

    private void Awake()
    {
        _health = maxHealth;
        _renderer = GetComponentInChildren<Renderer>();
        if (_renderer != null) _baseColor = _renderer.material.color;
    }

    private void Update()
    {
        if (_renderer != null && Time.time >= _flashUntil && _renderer.material.color != _baseColor)
            _renderer.material.color = _baseColor;
    }

    public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitNormal)
    {
        _health -= amount;

        if (_renderer != null)
        {
            _renderer.material.color = hitFlashColor;
            _flashUntil = Time.time + flashDuration;
        }

        if (_health <= 0f)
            Destroy(gameObject);
    }

    // ---------- 시간역행(TimeRewindable) 연동: 체력도 과거 값으로 복원 ----------
    // (파괴된 표적은 되살아나지 않음 — 보스는 Destroy 대신 사망 연출을 쓰면 완전 복원 가능)

    public float CaptureRewindExtra() => _health;

    public void ApplyRewindExtra(float value) => _health = Mathf.Min(value, maxHealth);
}
