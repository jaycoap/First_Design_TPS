using UnityEngine;
using UnityEngine.Rendering;
using BossFX;   // BossPatternFX 패키지(셰이더 + 라이브러리)

/// <summary>
/// 보스가 3단계에서 레이저 대신 쏘는 <b>레이저 구체</b>(투사체).
/// 에셋 없이 코드로 만들며, 스스로 비행 → 명중/수명 종료 → 폭발까지 처리하고 사라진다.
///
/// - 레이저와 달리 <b>날아가는 것이 눈에 보인다</b> — 발사 순간이 아니라 도달할 때까지 피할 수 있다.
/// - 대신 착탄하면 반경 안이 전부 피해라 옆으로 살짝 비키는 것만으로는 부족하다.
/// - 벽에 닿아도 터진다(스피어캐스트로 진행 경로를 확인).
///
/// 구체와 폭발은 BossPatternFX 의 Radial 셰이더로 그린다 — 판 한 장에 SDF 로 그리므로
/// 가까이서 봐도 테두리가 뭉개지지 않고, 폭발은 섬광 + 퍼지는 링 두 겹으로 읽힌다.
/// 크기/속도는 사람 1.8m 기준 × k 배율.
/// </summary>
public class BossOrb : MonoBehaviour
{
    /// <summary>폭발 잔광이 사라지기까지의 시간(초).</summary>
    private const float FadeTime = 0.9f;

    private Transform _head;      // 꼬리/광원(스케일 1)
    private BossFx.Surface _glow; // 빛나는 판(Radial/Orb, 빌보드)
    private TrailRenderer _trail;
    private Light _light;

    private Vector3 _dir;
    private float _speed, _life, _timer, _radius, _damage, _k;
    private Color _color;
    private LayerMask _mask;
    private Transform _target;
    private IDamageable _targetDamage;
    private Transform _owner;     // 보스 자신 — 제 몸에 터지지 않도록 제외
    private bool _exploded;

    /// <summary>구체 하나를 발사한다.</summary>
    public static BossOrb Launch(Vector3 from, Vector3 dir, float k, Color color,
                                 float damage, float radius, float speed, float life,
                                 Transform target, Transform owner, LayerMask mask)
    {
        var go = new GameObject("BossOrb");
        go.transform.position = from;
        var orb = go.AddComponent<BossOrb>();
        orb.Init(dir, k, color, damage, radius, speed, life, target, owner, mask);
        return orb;
    }

    private void Init(Vector3 dir, float k, Color color, float damage, float radius,
                      float speed, float life, Transform target, Transform owner, LayerMask mask)
    {
        _dir = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.forward;
        _k = k;
        _color = color;
        _damage = damage;
        _radius = radius;
        _speed = speed;
        _life = Mathf.Max(0.2f, life);
        _target = target;
        _targetDamage = target != null ? target.GetComponentInParent<IDamageable>() : null;
        _owner = owner;
        _mask = mask;

        // 글로우 판과 꼬리가 겹치는 지점은 가산으로 더해진다 — 알파를 낮추지 않으면
        // 두 층의 합이 1을 넘어 채널이 잘리고 보라색이 흰색으로 뭉갠다.
        Color hot = Color.Lerp(color, Color.white, 0.15f);
        const float BodyAlpha = 0.6f;
        const float TrailAlpha = 0.45f;

        // 머리(스케일 1) — 꼬리 굵기가 판 크기에 곱해지지 않도록 분리한다
        var head = new GameObject("Head");
        head.transform.SetParent(transform, false);
        _head = head.transform;

        // 구체 본체: 중심이 밝고 바깥으로 발광이 번지는 Radial/Orb 판 한 장
        _glow = new BossFx.Surface("Glow", head.transform, BossFXLibrary.QuadXZ, BossFx.RadialMat);
        _glow.Set(BossFXLibrary.PMode, (float)(int)BossRadialMode.Orb)
             .Set(BossFXLibrary.PColorCore, Color.Lerp(hot, Color.white, 0.2f))
             .Set(BossFXLibrary.PColorEdge, color)
             .Set(BossFXLibrary.PThickness, 0.4f)     // 밝은 심이 차지하는 비율
             .Set(BossFXLibrary.PFalloff, 2.2f)
             // 꼬리와 겹치는 자리가 가산으로 더해진다 — 세기를 낮게 잡아 흰색으로 뭉개지지 않게
             .Set(BossFXLibrary.PIntensity, 2.4f)
             .Set(BossFXLibrary.POpacity, BodyAlpha)
             .Apply();
        _glow.Shown = true;
        _glow.T.localScale = new Vector3(1.1f * _radius, 1f, 1.1f * _radius);

        _trail = head.AddComponent<TrailRenderer>();
        _trail.sharedMaterial = GunFx.MakeTracerMaterial();
        _trail.time = 0.3f;
        _trail.startWidth = 0.8f * _radius;
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

        // 비행 중에는 점광원을 달지 않는다 — 낙하물과 같은 이유다.
        // URP 포워드는 오브젝트당 추가 광원을 상위 몇 개만 고르므로, 구체 3발이 동시에
        // 날아다니며 광원을 켜면 그 선택이 매 프레임 뒤바뀌어 화면이 번쩍인다.
        // 폭발 순간에만 짧게 켠다(Explode).
    }

    private void Update()
    {
        if (_exploded)
        {
            // 폭발 후: 꼬리가 사라질 때까지 두었다가 정리
            _timer += Time.deltaTime;
            if (_light != null) _light.intensity = Mathf.Max(0f, _light.intensity - 20f * Time.deltaTime);
            if (_timer > FadeTime) Destroy(gameObject);
            return;
        }

        _timer += Time.deltaTime;
        float step = _speed * Time.deltaTime;

        // 진행 경로에 벽이 있으면 그 자리에서 터진다(빠른 구체가 벽을 뚫지 않도록 스윕 검사)
        if (Physics.SphereCast(_head.position, _radius * 0.4f, _dir, out RaycastHit hit, step,
                               _mask, QueryTriggerInteraction.Ignore)
            && !IsOwn(hit.collider))
        {
            _head.position = hit.point;
            Explode();
            return;
        }

        _head.position += _dir * step;
        Billboard();
        Pulse();

        // 플레이어에 닿았는가(구르기 무적은 PlayerStats가 처리)
        if (_target != null)
        {
            Vector3 to = TargetCenter() - _head.position;
            if (to.sqrMagnitude <= _radius * _radius) { Explode(); return; }
        }

        if (_timer >= _life) Explode(); // 빗나갔으면 허공에서 스스로 터진다
    }

    /// <summary>보스 자신(또는 그 자식)인가 — 발사 직후 제 몸에 터지는 것을 막는다.</summary>
    private bool IsOwn(Collider col)
        => _owner != null && col != null && col.transform.IsChildOf(_owner);

    /// <summary>어느 각도에서도 구체로 보이도록 판을 카메라 쪽으로 세운다.</summary>
    private void Billboard()
    {
        if (_glow != null) BossFx.FaceCamera(_glow.T, Camera.main);
    }

    /// <summary>날아가는 동안 크기가 미세하게 일렁여 '살아있는 에너지'로 보이게 한다.</summary>
    private void Pulse()
    {
        if (_glow == null) return;
        float wob = 0.85f + 0.15f * Mathf.Sin(Time.time * 18f);
        float d = 1.1f * _radius * wob;
        _glow.T.localScale = new Vector3(d, 1f, d);
    }

    private void Explode()
    {
        if (_exploded) return;
        _exploded = true;
        _timer = 0f;

        Vector3 at = _head.position;
        if (_glow != null) _glow.Shown = false;
        if (_trail != null) _trail.emitting = false;

        // 폭발: 방사형 섬광이 먼저 터지고, 피해 반경만큼 링이 퍼져 "어디까지 위험했는지"를 남긴다
        BossImpactFX.Spawn(new BossImpactSettings
        {
            mode = BossRadialMode.Burst,
            radius = _radius * 1.8f,
            duration = 0.26f,
            falloff = 2.0f,
            coreColor = Color.Lerp(_color, Color.white, 0.45f),
            edgeColor = _color,
            intensity = 4.5f,
            flatOnGround = false,
        }, at);

        BossImpactFX.Spawn(new BossImpactSettings
        {
            mode = BossRadialMode.Ring,
            radius = _radius * 2.2f,
            duration = 0.4f,
            thickness = 0.12f,
            falloff = 2.4f,
            coreColor = Color.Lerp(_color, Color.white, 0.3f),
            edgeColor = _color,
            intensity = 3.4f,
            flatOnGround = true,
            groundOffset = 0f,
        }, at);

        GameSfx.PlayAt(Sfx.MeteorImpact, at, pitch: Random.Range(1.05f, 1.2f)); // 운석보다 가볍게

        // 폭발 순간에만 짧게 켜지는 광원(아래 Update가 빠르게 꺼뜨린다)
        _light = _head.gameObject.AddComponent<Light>();
        _light.type = LightType.Point;
        _light.color = _color;
        _light.range = 8f * _k;
        _light.intensity = 7f;

        // 반경 안이면 피해(구르기 중이면 PlayerStats가 무효 처리 = 회피)
        if (_targetDamage != null && _target != null)
        {
            Vector3 to = TargetCenter() - at;
            to.y *= 0.5f; // 높이 차는 관대하게
            if (to.sqrMagnitude <= _radius * _radius)
                _targetDamage.TakeDamage(_damage, at, (at - TargetCenter()).normalized);
        }

        var cam = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
        if (cam != null && _target != null)
        {
            float d = Vector3.Distance(TargetCenter(), at);
            float k01 = Mathf.Clamp01(1f - d / (_radius * 4f));
            if (k01 > 0.01f) cam.AddShake(0.6f * k01, 0.3f);
        }
    }

    private Vector3 TargetCenter()
    {
        if (_target == null) return _head.position;
        var cc = _target.GetComponentInParent<CharacterController>();
        return cc != null ? _target.TransformPoint(cc.center) : _target.position + Vector3.up * (0.9f * _k);
    }

}
