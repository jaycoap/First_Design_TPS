using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 보스 텔레포트 충격파와 함께 하늘에서 떨어지는 운석(투사체).
/// 에셋 없이 코드로 만들며, 스스로 예고 → 낙하 → 착탄까지 처리하고 사라진다.
///
/// - 바닥에 착탄 지점을 알리는 링이 먼저 그려지고, 착탄이 가까울수록 빠르게 깜빡인다.
/// - 낙하체는 꼬리를 끌며 떨어져, 어디로 오는지 눈으로 쫓을 수 있다.
/// - 착탄 순간 반경 안의 대상에게 피해(구르기 무적으로 흘릴 수 있다).
/// 크기/속도는 사람 1.8m 기준 × k 배율.
/// </summary>
public class BossMeteor : MonoBehaviour
{
    private enum State { Falling, Impacted }

    private State _state = State.Falling;
    private Transform _body;
    private Material _bodyMat;
    private LineRenderer _ring;
    private TrailRenderer _trail;
    private Light _light;
    private ParticleSystem _burst;

    private Vector3 _impact, _start;
    private float _fallTime, _timer, _radius, _damage, _k;
    private Color _color;
    private Transform _target;
    private IDamageable _targetDamage;

    private static GunFx.ImpactFx _sharedImpact;

    /// <summary>운석 하나를 예고와 함께 떨어뜨린다.</summary>
    public static BossMeteor Launch(Vector3 impactPoint, float k, Color color,
                                    float damage, float radius, float fallTime, Transform target)
    {
        var go = new GameObject("BossMeteor");
        go.transform.position = impactPoint;
        var m = go.AddComponent<BossMeteor>();
        m.Init(impactPoint, k, color, damage, radius, fallTime, target);
        return m;
    }

    private void Init(Vector3 impactPoint, float k, Color color,
                      float damage, float radius, float fallTime, Transform target)
    {
        _impact = impactPoint;
        _k = k;
        _color = color;
        _damage = damage;
        _radius = radius;
        _fallTime = Mathf.Max(0.15f, fallTime);
        _target = target;
        _targetDamage = target != null ? target.GetComponentInParent<IDamageable>() : null;
        _start = impactPoint + Vector3.up * (14f * k);

        Color hot = Color.Lerp(color, Color.white, 0.7f);

        // 착탄 예고 링(바닥에 살짝 띄워 z-파이팅 방지)
        var ringGO = new GameObject("Warning");
        ringGO.transform.SetParent(transform, false);
        ringGO.transform.position = impactPoint + Vector3.up * (0.02f * k);
        _ring = ringGO.AddComponent<LineRenderer>();
        _ring.sharedMaterial = GunFx.MakeTracerMaterial();
        _ring.useWorldSpace = false;
        _ring.loop = true;
        _ring.startWidth = _ring.endWidth = 0.05f * k;
        _ring.numCapVertices = 2;
        _ring.shadowCastingMode = ShadowCastingMode.Off;
        _ring.receiveShadows = false;
        const int steps = 40;
        _ring.positionCount = steps;
        for (int i = 0; i < steps; i++)
        {
            float a = Mathf.PI * 2f * i / steps;
            _ring.SetPosition(i, new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * _radius);
        }

        // 낙하체: 카메라를 향하는 글로우 + 꼬리
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Body";
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(transform, false);
        quad.transform.position = _start;
        quad.transform.localScale = Vector3.one * (0.5f * _radius);
        _body = quad.transform;
        var rend = quad.GetComponent<Renderer>();
        _bodyMat = new Material(GunFx.MakeTracerMaterial()) { hideFlags = HideFlags.DontSave };
        if (_bodyMat.HasProperty("_BaseColor")) _bodyMat.SetColor("_BaseColor", hot);
        if (_bodyMat.HasProperty("_Color")) _bodyMat.SetColor("_Color", hot);
        rend.sharedMaterial = _bodyMat;
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;

        _trail = quad.AddComponent<TrailRenderer>();
        _trail.sharedMaterial = GunFx.MakeTracerMaterial();
        _trail.time = 0.35f;
        _trail.startWidth = 0.35f * _radius;
        _trail.endWidth = 0f;
        _trail.numCapVertices = 2;
        _trail.minVertexDistance = 0.02f * k;
        _trail.shadowCastingMode = ShadowCastingMode.Off;
        _trail.receiveShadows = false;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(hot, 0f), new GradientColorKey(color, 1f) },
            new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
        _trail.colorGradient = grad;

        _light = quad.AddComponent<Light>();
        _light.type = LightType.Point;
        _light.color = color;
        _light.range = 4f * k;
        _light.intensity = 2.5f;
    }

    private void Update()
    {
        if (_state == State.Falling)
        {
            _timer += Time.deltaTime;
            float t = Mathf.Clamp01(_timer / _fallTime);

            // 가속 낙하(자유낙하 느낌) + 카메라를 향한 빌보드
            _body.position = Vector3.Lerp(_start, _impact, t * t);
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 toCam = _body.position - cam.transform.position;
                if (toCam.sqrMagnitude > 1e-8f)
                    _body.rotation = Quaternion.LookRotation(toCam.normalized, cam.transform.up);
            }

            // 착탄이 가까울수록 링이 빠르게, 밝게 깜빡인다
            float freq = Mathf.Lerp(3f, 18f, t);
            float pulse = 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(_timer * freq));
            var c = new Color(_color.r, _color.g, _color.b, pulse);
            _ring.startColor = _ring.endColor = c;
            _ring.startWidth = _ring.endWidth = 0.05f * _k * (1f + t);

            if (t >= 1f) Impact();
            return;
        }

        // 착탄 후: 잔광이 사라지면 정리
        _timer += Time.deltaTime;
        if (_light != null) _light.intensity = Mathf.Max(0f, _light.intensity - 12f * Time.deltaTime);
        if (_timer > 1.4f) Destroy(gameObject);
    }

    private void Impact()
    {
        _state = State.Impacted;
        _timer = 0f;

        if (_ring != null) _ring.enabled = false;
        if (_body != null) _body.gameObject.SetActive(false);
        if (_trail != null) _trail.emitting = false;

        // 착탄 이펙트(총기 탄착 FX 재사용 — 색만 보스 것)
        if (_sharedImpact == null || _sharedImpact.Root == null)
            _sharedImpact = GunFx.BuildImpact(_k * 3f, _color);
        _sharedImpact.Spawn(_impact, Vector3.up);

        // 폭발 광원
        var lightGO = new GameObject("Blast");
        lightGO.transform.SetParent(transform, false);
        lightGO.transform.position = _impact + Vector3.up * (0.3f * _k);
        _light = lightGO.AddComponent<Light>();
        _light.type = LightType.Point;
        _light.color = _color;
        _light.range = 10f * _k;
        _light.intensity = 8f;

        // 반경 안이면 피해(구르기 중이면 PlayerStats가 무효 처리 = 회피)
        if (_targetDamage != null && _target != null)
        {
            Vector3 to = TargetCenter() - _impact;
            to.y *= 0.5f; // 높이 차는 관대하게
            if (to.sqrMagnitude <= _radius * _radius)
                _targetDamage.TakeDamage(_damage, _impact, Vector3.up);
        }

        var cam = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
        if (cam != null && _target != null)
        {
            // 가까울수록 크게 흔들린다
            float d = Vector3.Distance(TargetCenter(), _impact);
            float k01 = Mathf.Clamp01(1f - d / (_radius * 4f));
            if (k01 > 0.01f) cam.AddShake(0.5f * k01, 0.3f);
        }
    }

    private Vector3 TargetCenter()
    {
        var cc = _target.GetComponentInParent<CharacterController>();
        return cc != null ? _target.TransformPoint(cc.center) : _target.position + Vector3.up * (0.9f * _k);
    }

    private void OnDestroy()
    {
        if (_bodyMat != null) Destroy(_bodyMat);
    }
}
