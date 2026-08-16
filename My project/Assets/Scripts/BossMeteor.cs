using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 보스 텔레포트 충격파와 함께 하늘에서 떨어지는 운석(투사체).
/// 에셋 없이 코드로 만들며, 스스로 예고 → 낙하 → 착탄까지 처리하고 사라진다.
///
/// - 바닥에 착탄 지점을 알리는 링이 먼저 그려지고, 착탄이 가까울수록 빠르게 깜빡인다.
/// - 그 안쪽에서 두 번째 링이 중심으로 조여들어 "언제 떨어지는지"를 눈으로 셀 수 있다.
/// - 하늘에서 착탄점까지 빛기둥이 서서, 어느 자리가 위험한지 멀리서도 보인다.
/// - 낙하체는 꼬리를 끌며 떨어져, 어디로 오는지 눈으로 쫓을 수 있다.
/// - 착탄 순간 반경 안의 대상에게 피해(구르기 무적으로 흘릴 수 있다) + 충격파 링이 퍼진다.
/// 크기/속도는 사람 1.8m 기준 × k 배율.
/// </summary>
public class BossMeteor : MonoBehaviour
{
    /// <summary>떨어지는 것의 모양. 예고/판정은 같고 보이는 것만 다르다.</summary>
    public enum Style
    {
        Orb,   // 빛나는 구체가 꼬리를 끌며 떨어진다(3단계)
        Beam,  // 하늘에서 레이저 기둥이 내리꽂힌다(2단계 강우)
    }

    private enum State { Falling, Impacted }

    /// <summary>착탄 후 충격파 링이 퍼지는 시간(초).</summary>
    private const float ShockTime = 0.45f;

    private State _state = State.Falling;
    private Transform _body;         // 낙하체 머리(꼬리/광원 부착, 스케일 1)
    private GameObject _glow;        // 빛나는 판(빌보드)
    private Material _bodyMat;
    private LineRenderer _ring;      // 착탄 반경(고정)
    private LineRenderer _closing;   // 중심으로 조여드는 카운트다운 링
    private LineRenderer _column;    // 하늘 → 착탄점 빛기둥
    private LineRenderer _shock;     // 착탄 충격파(퍼져나가며 사라짐)
    private TrailRenderer _trail;
    private Light _light;

    private Vector3 _impact, _start;
    private float _fallTime, _timer, _radius, _damage, _k;
    private Color _color;
    private Style _style = Style.Orb;
    private Transform _target;
    private IDamageable _targetDamage;

    private static GunFx.ImpactFx _sharedImpact;

    /// <summary>낙하물 하나를 예고와 함께 떨어뜨린다.</summary>
    public static BossMeteor Launch(Vector3 impactPoint, float k, Color color,
                                    float damage, float radius, float fallTime, Transform target,
                                    Style style = Style.Orb)
    {
        var go = new GameObject(style == Style.Beam ? "BossSkyBeam" : "BossMeteor");
        go.transform.position = impactPoint;
        var m = go.AddComponent<BossMeteor>();
        m._style = style;
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

        // 글로우 판과 꼬리가 겹치는 지점은 가산으로 더해진다 — 알파를 낮춰 두지 않으면
        // 두 층의 합이 1을 넘어 채널이 잘리고 보라색이 흰색으로 뭉갠다.
        Color hot = Color.Lerp(color, Color.white, 0.15f);
        const float BodyAlpha = 0.6f;
        const float TrailAlpha = 0.45f;

        // 착탄 예고 링(바닥에 살짝 띄워 z-파이팅 방지) — 반지름은 스케일로 준다
        _ring = MakeRing("Warning");
        // 카운트다운 링: 착탄 순간 정확히 중심에서 만나도록 조여든다
        _closing = MakeRing("Countdown");

        // 빛기둥: 하늘에서 착탄점까지 — 어느 자리가 위험한지 멀리서도 보인다
        var colGO = new GameObject("SkyColumn");
        colGO.transform.SetParent(transform, false);
        _column = colGO.AddComponent<LineRenderer>();
        _column.sharedMaterial = GunFx.MakeTracerMaterial();
        _column.useWorldSpace = true;
        _column.positionCount = 2;
        _column.SetPosition(0, impactPoint + Vector3.up * (0.02f * k));
        _column.SetPosition(1, _start);
        _column.numCapVertices = 2;
        _column.shadowCastingMode = ShadowCastingMode.Off;
        _column.receiveShadows = false;

        // 레이저 강우는 낙하체가 없다 — 예고 동안엔 빛기둥만 서 있다가 착탄 순간
        // 그 기둥이 그대로 굵게 내리꽂힌다. 아래 낙하체 생성은 구체일 때만 한다.
        if (_style == Style.Beam) return;

        // 낙하체. 꼬리/광원은 스케일 1인 '머리'에 붙이고, 빛나는 판(Quad)만 크기를 준다 —
        // TrailRenderer 굵기는 트랜스폼 스케일에도 곱해지므로, 판에 같이 붙이면
        // 꼬리가 반지름의 제곱만큼 얇아져 사실상 보이지 않는다.
        var head = new GameObject("Head");
        head.transform.SetParent(transform, false);
        head.transform.position = _start;
        _body = head.transform;

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Glow";
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(head.transform, false);
        quad.transform.localScale = Vector3.one * (1.6f * _radius);
        _glow = quad;
        var rend = quad.GetComponent<Renderer>();
        _bodyMat = new Material(GunFx.MakeTracerMaterial()) { hideFlags = HideFlags.DontSave };
        var bodyColor = new Color(hot.r, hot.g, hot.b, BodyAlpha);
        if (_bodyMat.HasProperty("_BaseColor")) _bodyMat.SetColor("_BaseColor", bodyColor);
        if (_bodyMat.HasProperty("_Color")) _bodyMat.SetColor("_Color", bodyColor);
        rend.sharedMaterial = _bodyMat;
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;

        _trail = head.AddComponent<TrailRenderer>();
        _trail.sharedMaterial = GunFx.MakeTracerMaterial();
        _trail.time = 0.45f;
        _trail.startWidth = 1.1f * _radius;
        _trail.endWidth = 0f;
        _trail.numCapVertices = 2;
        _trail.minVertexDistance = 0.02f * k;
        _trail.shadowCastingMode = ShadowCastingMode.Off;
        _trail.receiveShadows = false;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(hot, 0f), new GradientColorKey(color, 1f) },
            new[] { new GradientAlphaKey(TrailAlpha, 0f), new GradientAlphaKey(0f, 1f) });
        _trail.colorGradient = grad;

        // 비행 중에는 점광원을 달지 않는다.
        // URP 포워드는 오브젝트 하나당 추가 광원을 상위 4개(설정값)만 고른다.
        // 낙하물마다 광원을 달면 7발이 동시에 뜨는 순간 바닥 같은 큰 오브젝트에서
        // '어느 4개를 쓸지'가 매 프레임 뒤바뀌어 화면 전체가 번쩍인다.
        // 빛나는 판 + 꼬리만으로도 충분히 빛나 보이고, 폭발 순간에만 짧게 광원을 켠다.
    }

    /// <summary>바닥에 눕는 단위원 링. 반지름/굵기/색은 SetRing으로 매 프레임 조절한다.</summary>
    private LineRenderer MakeRing(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.position = _impact + Vector3.up * (0.02f * _k);

        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = GunFx.MakeTracerMaterial();
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.numCapVertices = 2;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows = false;

        const int steps = 56;
        lr.positionCount = steps;
        for (int i = 0; i < steps; i++)
        {
            float a = Mathf.PI * 2f * i / steps;
            lr.SetPosition(i, new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)));
        }
        return lr;
    }

    /// <summary>단위원 링을 원하는 반지름/굵기/색으로 세팅.</summary>
    private static void SetRing(LineRenderer lr, float radius, float width, Color color)
    {
        if (lr == null) return;
        radius = Mathf.Max(1e-4f, radius);
        lr.transform.localScale = Vector3.one * radius;
        // LineRenderer 굵기는 트랜스폼 스케일에도 곱해지므로 반지름만큼 되돌린다
        lr.startWidth = lr.endWidth = width / radius;
        lr.startColor = lr.endColor = color;
    }

    private void Update()
    {
        if (_state == State.Falling)
        {
            _timer += Time.deltaTime;
            float t = Mathf.Clamp01(_timer / _fallTime);

            // 가속 낙하(자유낙하 느낌) + 카메라를 향한 빌보드. 레이저 강우는 낙하체가 없다.
            if (_body != null)
            {
                _body.position = Vector3.Lerp(_start, _impact, t * t);
                var cam = Camera.main;
                if (cam != null)
                {
                    Vector3 toCam = _body.position - cam.transform.position;
                    if (toCam.sqrMagnitude > 1e-8f)
                        _body.rotation = Quaternion.LookRotation(toCam.normalized, cam.transform.up);
                }
            }

            // 착탄이 가까울수록 링이 빠르게, 밝게 깜빡인다.
            // 맥동 바닥값을 높여(0.65) 가장 옅은 순간에도 링이 지워지지 않게 한다.
            float freq = Mathf.Lerp(3f, 20f, t);
            float pulse = 0.65f + 0.35f * Mathf.Abs(Mathf.Sin(_timer * freq));
            Color warn = Color.Lerp(_color, Color.white, 0.12f * t);
            warn.a = pulse;
            SetRing(_ring, _radius, 0.26f * _k * (1f + t * 1.5f), warn);

            // 카운트다운 링: 반지름이 착탄 순간 0이 되도록 조여든다(남은 시간이 눈에 보인다)
            Color close = Color.Lerp(_color, Color.white, 0.25f);
            close.a = 0.6f + 0.4f * t;
            SetRing(_closing, _radius * (1f - t), 0.2f * _k * (1f + t), close);

            // 빛기둥: 착탄점 위로 곧게 서서 떨어질수록 진해진다(멀리서도 위험 지점이 보인다)
            Color col = _color;
            col.a = Mathf.Lerp(0.3f, 0.85f, t) * pulse;
            _column.startColor = col;
            _column.endColor = new Color(col.r, col.g, col.b, col.a * 0.3f);
            _column.startWidth = _column.endWidth = 0.5f * _radius * (0.7f + t);

            if (t >= 1f) Impact();
            return;
        }

        // 착탄 후: 충격파 링이 퍼지고 잔광이 사라지면 정리
        _timer += Time.deltaTime;

        float s = Mathf.Clamp01(_timer / ShockTime);
        if (s < 1f)
        {
            Color sc = Color.Lerp(_color, Color.white, 0.3f * (1f - s));
            sc.a = 1f - s;
            SetRing(_shock, _radius * Mathf.Lerp(0.3f, 2.6f, s), 0.3f * _k * (1f - s * 0.6f), sc);

            // 레이저 강우: 예고로 서 있던 빛기둥이 그대로 굵게 내리꽂혔다가 사그라든다
            if (_style == Style.Beam && _column != null)
            {
                Color bc = Color.Lerp(_color, Color.white, 0.35f * (1f - s));
                bc.a = 1f - s;
                _column.startColor = bc;
                _column.endColor = new Color(bc.r, bc.g, bc.b, bc.a * 0.4f);
                _column.startWidth = _column.endWidth = _radius * Mathf.Lerp(1.6f, 0.4f, s);
            }
        }
        else
        {
            if (_shock != null) _shock.enabled = false;
            if (_column != null) _column.enabled = false;
        }

        // 폭발 광원은 0.35초 안에 꺼진다(오래 남으면 다음 낙하물의 광원과 겹쳐 깜빡임을 만든다)
        if (_light != null) _light.intensity = Mathf.Max(0f, _light.intensity - 20f * Time.deltaTime);
        if (_timer > 1.4f) Destroy(gameObject);
    }

    private void Impact()
    {
        _state = State.Impacted;
        _timer = 0f;

        if (_ring != null) _ring.enabled = false;
        if (_closing != null) _closing.enabled = false;
        // 레이저 강우는 빛기둥이 곧 '떨어지는 레이저' 본체다 → 끄지 않고 Update가 내리꽂는다
        if (_column != null && _style != Style.Beam) _column.enabled = false;
        // 머리는 남겨 두고 판만 끈다 → 꼬리가 제자리에서 자연스럽게 사그라든다
        if (_glow != null) _glow.SetActive(false);
        if (_trail != null) _trail.emitting = false;
        if (_light != null) _light.enabled = false; // 낙하체 광원(폭발 광원으로 교체된다)

        // 착탄 이펙트(총기 탄착 FX 재사용 — 색만 보스 것)
        if (_sharedImpact == null || _sharedImpact.Root == null)
            _sharedImpact = GunFx.BuildImpact(_k * 5f, _color);
        _sharedImpact.Spawn(_impact, Vector3.up);
        GameSfx.PlayAt(Sfx.MeteorImpact, _impact, pitch: Random.Range(0.9f, 1.1f));

        // 충격파 링: 착탄 반경 밖으로 퍼져나가며 사라진다(어디까지 위험했는지 남는 잔상)
        _shock = MakeRing("Shockwave");

        // 폭발 광원
        var lightGO = new GameObject("Blast");
        lightGO.transform.SetParent(transform, false);
        lightGO.transform.position = _impact + Vector3.up * (0.3f * _k);
        _light = lightGO.AddComponent<Light>();
        _light.type = LightType.Point;
        _light.color = _color;
        // 범위·세기를 줄이고 아래 Update에서 빠르게 꺼뜨린다 — 여러 발이 연달아 떨어질 때
        // 폭발 광원이 겹쳐 남아 있으면 그것만으로도 광원 개수 제한을 넘겨 깜빡인다.
        _light.range = 9f * _k;
        _light.intensity = 7f;

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
            if (k01 > 0.01f) { cam.AddShake(0.85f * k01, 0.35f); cam.AddFovKick(3f * k01); }
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
