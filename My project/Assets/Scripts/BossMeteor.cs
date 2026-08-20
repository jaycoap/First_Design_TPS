using UnityEngine;
using UnityEngine.Rendering;
using BossFX;   // BossPatternFX 패키지(셰이더 + 라이브러리)

/// <summary>
/// 보스 텔레포트 충격파와 함께 하늘에서 떨어지는 운석(투사체).
/// 에셋 없이 코드로 만들며, 스스로 예고 → 낙하 → 착탄까지 처리하고 사라진다.
///
/// - 바닥에 착탄 범위만 한 경고 장판(BossPatternFX/Telegraph)이 깔리고, 착탄까지 남은
///   시간만큼 중심에서 바깥으로 차오른다 — "언제 떨어지는지"를 눈으로 셀 수 있다.
/// - 하늘에서 착탄점까지 빛기둥이 서서, 어느 자리가 위험한지 멀리서도 보인다.
/// - 낙하체는 꼬리를 끌며 떨어져, 어디로 오는지 눈으로 쫓을 수 있다.
/// - 착탄 순간 반경 안의 대상에게 피해(구르기 무적으로 흘릴 수 있다) + 섬광·충격파 링.
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
    private BossFx.Surface _glow;    // 낙하체 본체(Radial/Orb, 빌보드)
    private BossFx.Surface _warn;    // 착탄 범위 경고 장판(Telegraph/Circle)
    private BossFx.Surface _column;  // 하늘 → 착탄점 빛기둥(Beam)
    private TrailRenderer _trail;
    private Light _light;

    private Vector3 _impact, _start;
    private float _fallTime, _timer, _radius, _damage, _k;
    private float _delay;            // 발사 지연 — 이만큼 기다렸다가 예고를 시작한다
    private Color _color;
    private Style _style = Style.Orb;
    private Transform _target;
    private IDamageable _targetDamage;

    /// <summary>
    /// 낙하물 하나를 예고와 함께 떨어뜨린다.
    /// startDelay 를 주면 그만큼 기다렸다가 예고를 시작한다 — 여러 발을 한 프레임에
    /// 다 만들어 놓고 시차만 다르게 주기 위한 것이다. 뿌리는 쪽이 코루틴으로 한 발씩
    /// 만들면, 그 코루틴이 중간에 끊길 때 나머지가 통째로 사라진다.
    /// </summary>
    public static BossMeteor Launch(Vector3 impactPoint, float k, Color color,
                                    float damage, float radius, float fallTime, Transform target,
                                    Style style = Style.Orb, float startDelay = 0f)
    {
        var go = new GameObject(style == Style.Beam ? "BossSkyBeam" : "BossMeteor");
        go.transform.position = impactPoint;
        var m = go.AddComponent<BossMeteor>();
        m._style = style;
        m._delay = Mathf.Max(0f, startDelay);
        m.Init(impactPoint, k, color, damage, radius, fallTime, target);
        return m;
    }

    /// <summary>
    /// 어느 높이에서 떨어뜨릴지. 야외라면 넉넉히 높은 데서 떨어져야 "하늘에서 온다"가 되지만,
    /// 천장이 있는 실내에서 그 높이를 그대로 쓰면 낙하도 빛기둥도 천장에 가려 아무것도 안 보인다.
    /// 착탄점 위로 레이를 쏴 실제 머리 위 여유를 재고, 그 안쪽에서 떨어뜨린다.
    /// (사람·보스는 지나칠 수 있어야 하므로 CharacterController를 가진 것은 무시한다)
    /// </summary>
    private static float ResolveFallHeight(Vector3 impactPoint, float k)
    {
        float wanted = 14f * k;

        var hits = Physics.RaycastAll(impactPoint + Vector3.up * (0.05f * k), Vector3.up,
                                      wanted, ~0, QueryTriggerInteraction.Ignore);
        float headroom = wanted;
        foreach (var h in hits)
        {
            if (h.collider.GetComponentInParent<CharacterController>() != null) continue; // 캐릭터는 통과
            headroom = Mathf.Min(headroom, h.distance);
        }

        // 천장에 딱 붙이면 빛기둥 끝이 천장에 파묻힌다 — 조금 띄운다.
        // 너무 낮아지면 예고가 사라지므로 최소 높이는 지킨다.
        return Mathf.Max(2.5f * k, headroom * 0.85f);
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
        _start = impactPoint + Vector3.up * ResolveFallHeight(impactPoint, k);

        // 글로우 판과 꼬리가 겹치는 지점은 가산으로 더해진다 — 알파를 낮춰 두지 않으면
        // 두 층의 합이 1을 넘어 채널이 잘리고 보라색이 흰색으로 뭉갠다.
        Color hot = Color.Lerp(color, Color.white, 0.15f);
        const float BodyAlpha = 0.6f;
        const float TrailAlpha = 0.45f;

        // 착탄 경고 장판: 범위를 통째로 칠하고, 남은 시간만큼 중심에서 바깥으로 차오른다.
        // 얇은 선은 이 스케일(반경 30cm 남짓)에서 화면상 몇 픽셀이라 눈에 안 들어오므로
        // 처음부터 면으로 깐다.
        _warn = new BossFx.Surface("Warning", transform, BossFXLibrary.QuadXZ, BossFx.TelegraphMat);
        _warn.Set(BossFXLibrary.PShape, (float)(int)BossShape.Circle)
             .Set(BossFXLibrary.PFillMode, (float)(int)BossFillMode.Radial)
             .Set(BossFXLibrary.PColorBase, color)
             .Set(BossFXLibrary.PColorHot, Color.Lerp(color, Color.white, 0.4f))
             .Set(BossFXLibrary.PColorEdge, Color.Lerp(color, Color.white, 0.6f))
             .Apply();
        _warn.Shown = true;
        // 쿼드는 1x1 이고 셰이더 좌표가 [-1,1] 이므로 지름(=반지름 x2)이 곧 스케일이다
        _warn.T.position = impactPoint + Vector3.up * (0.02f * k);
        _warn.T.rotation = Quaternion.identity;
        _warn.T.localScale = new Vector3(radius * 2f, 1f, radius * 2f);

        // 빛기둥: 하늘에서 착탄점까지 — 어느 자리가 위험한지 멀리서도 보인다
        _column = new BossFx.Surface("SkyColumn", transform, BossFXLibrary.QuadForward, BossFx.BeamMat);
        _column.Set(BossFXLibrary.PColorCore, Color.Lerp(hot, Color.white, 0.3f))
               .Set(BossFXLibrary.PColorGlow, color)
               .Set(BossFXLibrary.PFire, 1f)
               .Apply();
        _column.Shown = true;

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

        _glow = new BossFx.Surface("Glow", head.transform, BossFXLibrary.QuadXZ, BossFx.RadialMat);
        _glow.Set(BossFXLibrary.PMode, (float)(int)BossRadialMode.Orb)
             .Set(BossFXLibrary.PColorCore, Color.Lerp(hot, Color.white, 0.2f))
             .Set(BossFXLibrary.PColorEdge, color)
             .Set(BossFXLibrary.PThickness, 0.42f)
             .Set(BossFXLibrary.PFalloff, 2.2f)
             // 꼬리와 겹치는 자리가 가산으로 더해진다 — 세기를 낮게 잡아 흰색으로 뭉개지지 않게
             .Set(BossFXLibrary.PIntensity, 2.6f)
             .Set(BossFXLibrary.POpacity, BodyAlpha)
             .Apply();
        _glow.Shown = true;
        _glow.T.localScale = new Vector3(1.6f * _radius, 1f, 1.6f * _radius);

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

        if (_delay > 0f) SetVisible(false);
    }

    /// <summary>발사 지연 동안에는 아무것도 보이지 않아야 한다.</summary>
    private void SetVisible(bool on)
    {
        if (_warn != null) _warn.Shown = on;
        if (_column != null) _column.Shown = on;
        if (_glow != null) _glow.Shown = on;
        if (_trail != null) _trail.emitting = on;
    }

    private void Update()
    {
        if (_delay > 0f)
        {
            _delay -= Time.deltaTime;
            if (_delay > 0f) return;
            SetVisible(true);   // 이제부터 예고 시작
        }

        if (_state == State.Falling)
        {
            _timer += Time.deltaTime;
            float t = Mathf.Clamp01(_timer / _fallTime);

            var cam = Camera.main;

            // 가속 낙하(자유낙하 느낌) + 카메라를 향한 빌보드. 레이저 강우는 낙하체가 없다.
            if (_body != null)
            {
                _body.position = Vector3.Lerp(_start, _impact, t * t);
                BossFx.FaceCamera(_glow.T, cam);
            }

            // 착탄이 가까울수록 빠르게, 밝게 맥동한다.
            // 바닥값을 높여(0.65) 가장 옅은 순간에도 장판이 지워지지 않게 한다.
            float freq = Mathf.Lerp(3f, 20f, t);
            float pulse = 0.65f + 0.35f * Mathf.Abs(Mathf.Sin(_timer * freq));

            // 경고 장판: 남은 시간만큼 중심에서 바깥으로 차오른다.
            // 가산 블렌딩이라 세기를 높이면 기둥과 겹친 자리가 하얗게 뭉갠다 — 낮게 유지한다.
            _warn.Set(BossFXLibrary.PFill, t)
                 .Set(BossFXLibrary.PIntensity, Mathf.Lerp(0.7f, 1.5f, t) * pulse)
                 .Set(BossFXLibrary.POpacity, Mathf.Lerp(0.55f, 1f, t))
                 .Apply();

            // 빛기둥: 착탄점 위로 곧게 서서 떨어질수록 굵고 진해진다(멀리서도 위험 지점이 보인다)
            BossFx.PlaceBeamQuad(_column.T, _impact + Vector3.up * (0.02f * _k), _start,
                                 1.6f * _radius * (0.7f + t), cam);
            _column.Set(BossFXLibrary.PCharge, Mathf.Lerp(0.18f, 0.5f, t))
                   .Set(BossFXLibrary.PIntensity, Mathf.Lerp(1.2f, 2.8f, t) * pulse)
                   .Set(BossFXLibrary.POpacity, Mathf.Lerp(0.35f, 0.9f, t))
                   .Apply();

            if (t >= 1f) Impact();
            return;
        }

        // 착탄 후: 충격파 링이 퍼지고 잔광이 사라지면 정리
        _timer += Time.deltaTime;

        float s = Mathf.Clamp01(_timer / ShockTime);
        if (s < 1f)
        {
            // 레이저 강우: 예고로 서 있던 빛기둥이 그대로 굵게 내리꽂혔다가 사그라든다.
            // 위→아래로 다시 걸어야 셰이더의 끝단 가늘어짐이 착탄점을 찌르는 쪽으로 향한다.
            if (_style == Style.Beam && _column != null)
            {
                BossFx.PlaceBeamQuad(_column.T, _start, _impact,
                                     _radius * Mathf.Lerp(3.2f, 0.8f, s), Camera.main);
                _column.Set(BossFXLibrary.PCharge, 1f)
                       .Set(BossFXLibrary.PIntensity, Mathf.Lerp(6f, 1.5f, s))
                       .Set(BossFXLibrary.POpacity, 1f - s)
                       .Apply();
            }
        }
        else if (_column != null) _column.Shown = false;

        // 폭발 광원은 0.35초 안에 꺼진다(오래 남으면 다음 낙하물의 광원과 겹쳐 깜빡임을 만든다)
        if (_light != null) _light.intensity = Mathf.Max(0f, _light.intensity - 20f * Time.deltaTime);
        if (_timer > 1.4f) Destroy(gameObject);
    }

    private void Impact()
    {
        _state = State.Impacted;
        _timer = 0f;

        if (_warn != null) _warn.Shown = false;
        // 레이저 강우는 빛기둥이 곧 '떨어지는 레이저' 본체다 → 끄지 않고 Update가 내리꽂는다
        if (_column != null && _style != Style.Beam) _column.Shown = false;
        // 머리는 남겨 두고 판만 끈다 → 꼬리가 제자리에서 자연스럽게 사그라든다
        if (_glow != null) _glow.Shown = false;
        if (_trail != null) _trail.emitting = false;
        if (_light != null) _light.enabled = false; // 낙하체 광원(폭발 광원으로 교체된다)

        // 착탄 섬광
        BossImpactFX.Spawn(new BossImpactSettings
        {
            mode = BossRadialMode.Burst,
            radius = _radius * 2.2f,
            duration = 0.28f,
            falloff = 2.0f,
            coreColor = Color.Lerp(_color, Color.white, 0.5f),
            edgeColor = _color,
            intensity = 5f,
            flatOnGround = false,
        }, _impact + Vector3.up * (0.2f * _k));

        // 충격파 링: 착탄 반경 밖으로 퍼져나가며 사라진다(어디까지 위험했는지 남는 잔상)
        BossImpactFX.Spawn(new BossImpactSettings
        {
            mode = BossRadialMode.Ring,
            radius = _radius * 2.6f,
            duration = ShockTime,
            thickness = 0.12f,
            falloff = 2.2f,
            coreColor = Color.Lerp(_color, Color.white, 0.3f),
            edgeColor = _color,
            intensity = 3.6f,
            flatOnGround = true,
            groundOffset = 0.02f * _k,
        }, _impact);

        GameSfx.PlayAt(Sfx.MeteorImpact, _impact, pitch: Random.Range(0.9f, 1.1f));

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

}
