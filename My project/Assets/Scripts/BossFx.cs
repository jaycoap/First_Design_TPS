using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 보스(AlienMonster) 전용 이펙트 팩토리. GunFx와 같은 방식으로 에셋 없이 코드로 생성한다.
/// - ChargeOrb : 검지 끝에 붙어 빛이 일렁이는 충전 구체(충전도가 오를수록 커지고 일렁임이 빨라짐)
/// - Beam      : 손끝 → 착탄점 레이저 광선(코어 + 글로우 2겹) + 발사 전 예고선
/// - Flash     : 텔레포트 번쩍임(섬광 + 확산 링 + 순간 광원)
/// - ClawTrail : 할퀴기 궤적(손가락 끝에 붙는 트레일)
/// 모든 크기는 사람 기준(1.8m) 수치 × scale 배율 — 이 프로젝트처럼 캐릭터가 작아도 비율이 맞는다.
/// </summary>
public static class BossFx
{
    // ---------- 공개 API ----------

    /// <summary>
    /// 검지 끝(anchor)에 붙는 충전 구체. Charge(0~1)/Visible을 매 프레임 갱신해 쓴다.
    /// withLight=false면 점광원을 달지 않는다 — 분신처럼 여러 기가 동시에 뜨는 경우에 쓴다
    /// (URP는 오브젝트당 추가 광원을 몇 개만 고르므로, 광원이 많으면 그 선택이 매 프레임
    ///  뒤바뀌며 화면이 번쩍인다).
    /// </summary>
    public static ChargeOrb BuildChargeOrb(Transform anchor, float scale, Color color, bool withLight = true)
    {
        var go = new GameObject("BossChargeOrb");
        go.transform.SetParent(anchor, false);
        go.transform.localPosition = Vector3.zero;
        NeutralizeScale(go.transform); // 본 스케일을 상쇄 — 이펙트 크기를 월드 미터로 다룬다
        var orb = go.AddComponent<ChargeOrb>();
        orb.Init(scale, color, withLight);
        return orb;
    }

    /// <summary>레이저 광선(발사) + 예고선(충전 중 조준선).</summary>
    public static Beam BuildBeam(Transform parent, float scale, Color color)
    {
        var go = new GameObject("BossLaserBeam");
        go.transform.SetParent(parent, false);
        NeutralizeScale(go.transform);
        var beam = go.AddComponent<Beam>();
        beam.Init(scale, color);
        return beam;
    }

    /// <summary>돌진 경로를 바닥에 그리는 예고선(가장자리 두 줄 = 위험 폭 + 중앙선).</summary>
    public static RushPath BuildRushPath(float scale, Color color)
    {
        var go = new GameObject("BossRushPath");
        var path = go.AddComponent<RushPath>();
        path.Init(scale, color);
        return path;
    }

    /// <summary>텔레포트 번쩍임. Spawn(pos)로 발동하는 재사용형 핸들.</summary>
    public static Flash BuildFlash(float scale, Color color)
    {
        Color hot = Color.Lerp(color, Color.white, 0.25f); // 흰빛으로 날리지 않는다 — 보스 색을 유지

        // 섬광: 사라지고/나타나는 자리에서 하얗게 터지는 빛
        ParticleSystem core = NewSystem("BossTeleportFX", null, loop: false);
        var main = core.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.10f, 0.16f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.9f * scale, 1.4f * scale);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = hot;
        FadeOut(core, hot, color);

        // 수직 방전: 몸이 사라진 자리에 남는 에너지 잔재가 위로 솟았다 사라진다
        ParticleSystem bolts = NewSystem("Bolts", core.transform, loop: false);
        var bMain = bolts.main;
        bMain.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
        bMain.startSpeed = new ParticleSystem.MinMaxCurve(2f * scale, 6f * scale);
        bMain.startSize = new ParticleSystem.MinMaxCurve(0.03f * scale, 0.07f * scale);
        bMain.startColor = hot;
        SetCone(bolts, 18f, 0.25f * scale);
        Stretch(bolts, 9f);
        FadeOut(bolts, hot, color);

        // 확산 링: 바닥을 따라 퍼지는 파문
        ParticleSystem ring = NewSystem("EnergyRing", core.transform, loop: false);
        var rMain = ring.main;
        rMain.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.34f);
        rMain.startSpeed = new ParticleSystem.MinMaxCurve(4f * scale, 7f * scale);
        rMain.startSize = new ParticleSystem.MinMaxCurve(0.25f * scale, 0.4f * scale);
        rMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        rMain.startColor = color;
        SetCone(ring, 89f, 0.05f * scale);
        Grow(ring, 2.6f);
        FadeOut(ring, color, new Color(color.r, color.g, color.b, 0f));

        // 순간 광원(GunFx의 감쇠 광원 재사용)
        var light = core.gameObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = 8f * scale;
        light.intensity = 0f;
        var pulse = core.gameObject.AddComponent<GunFx.LightPulse>();
        pulse.Init(light, peakIntensity: 6f, decayTime: 0.22f);

        return new Flash(core, bolts, ring, pulse);
    }

    /// <summary>손가락 끝들에 붙는 할퀴기 궤적. 강타 구간에만 켠다.</summary>
    public static ClawTrail BuildClawTrail(Transform[] tips, float scale, Color color)
    {
        var trails = new System.Collections.Generic.List<TrailRenderer>();
        foreach (var tip in tips)
        {
            if (tip == null) continue;
            var go = new GameObject("ClawTrail");
            go.transform.SetParent(tip, false);
            NeutralizeScale(go.transform);
            var tr = go.AddComponent<TrailRenderer>();
            tr.sharedMaterial = GunFx.MakeTracerMaterial();
            tr.time = 0.16f;
            tr.startWidth = 0.05f * scale;
            tr.endWidth = 0f;
            tr.numCapVertices = 2;
            tr.minVertexDistance = 0.01f * scale;
            tr.autodestruct = false;
            tr.shadowCastingMode = ShadowCastingMode.Off;
            tr.receiveShadows = false;
            tr.emitting = false;

            var grad = new Gradient();
            Color hot = Color.Lerp(color, Color.white, 0.25f);
            grad.SetKeys(
                new[] { new GradientColorKey(hot, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            tr.colorGradient = grad;

            trails.Add(tr);
        }
        return new ClawTrail(trails.ToArray());
    }

    // ---------- 핸들 ----------

    /// <summary>
    /// 손끝 충전 구체. 발사까지 남은 시간에 맞춰 Charge를 0→1로 올리면
    /// 구체가 커지면서 일렁임(맥동)이 점점 빨라진다 — "발사 시간이 임박했다"는 신호.
    /// </summary>
    public class ChargeOrb : MonoBehaviour
    {
        private Transform _core;          // 빌보드 글로우(구체 본체)
        private Material _coreMat;
        private Renderer _coreRenderer;
        private Light _light;
        private ParticleSystem _inflow;   // 주위 에너지가 손끝으로 빨려드는 입자
        private ParticleSystem _burst;    // 발사 순간의 터짐
        private Camera _cam;
        private Color _color;
        private float _scale, _phase, _fade, _burstBoost;

        /// <summary>충전도 0~1. 클수록 크고 일렁임이 빠르다.</summary>
        public float Charge { get; set; }
        /// <summary>표시 여부. 끄면 부드럽게 사라진다.</summary>
        public bool Visible { get; set; }

        internal void Init(float scale, Color color, bool withLight = true)
        {
            _scale = scale;
            _color = color;
            // 흰빛으로 과열시키지 않는다 — 보스 이펙트는 전부 보스 색(보라)으로 읽혀야 한다
            Color hot = Color.Lerp(color, Color.white, 0.25f);

            // 코어: 항상 카메라를 향하는 사각형 + 원형 글로우 텍스처 = 구체처럼 보인다
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Core";
            Object.Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(transform, false);
            _core = quad.transform;
            _coreRenderer = quad.GetComponent<Renderer>();
            _coreMat = NewGlowMaterial(hot);
            _coreRenderer.sharedMaterial = _coreMat;
            _coreRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _coreRenderer.receiveShadows = false;

            if (withLight)
            {
                _light = gameObject.AddComponent<Light>();
                _light.type = LightType.Point;
                _light.color = color;
                _light.range = 2.5f * scale;
                _light.intensity = 0f;
            }

            // 흡입 입자: 구(球) 껍질에서 음(-)의 속도로 출발 → 손끝으로 빨려든다
            _inflow = NewSystem("Inflow", transform, loop: true);
            var iMain = _inflow.main;
            float r = 0.55f * scale;
            float sp = 2.2f * scale;
            iMain.startLifetime = r / sp;
            iMain.startSpeed = -sp;                 // 음수 = 안쪽으로
            iMain.startSize = new ParticleSystem.MinMaxCurve(0.02f * scale, 0.045f * scale);
            iMain.startColor = hot;
            iMain.simulationSpace = ParticleSystemSimulationSpace.Local;
            var iShape = _inflow.shape;
            iShape.enabled = true;
            iShape.shapeType = ParticleSystemShapeType.Sphere;
            iShape.radius = r;
            iShape.radiusThickness = 0f;            // 껍질에서만 생성
            Stretch(_inflow, 3f);
            FadeOut(_inflow, hot, color);

            // 발사 순간의 터짐
            _burst = NewSystem("Burst", transform, loop: false);
            var bMain = _burst.main;
            bMain.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
            bMain.startSpeed = new ParticleSystem.MinMaxCurve(3f * scale, 9f * scale);
            bMain.startSize = new ParticleSystem.MinMaxCurve(0.02f * scale, 0.05f * scale);
            bMain.startColor = hot;
            SetCone(_burst, 70f, 0.02f * scale);
            Stretch(_burst, 5f);
            FadeOut(_burst, hot, color);

            SetShown(false);
        }

        /// <summary>발사 순간: 구체가 터지며 광원이 확 밝아진다.</summary>
        public void Burst()
        {
            if (_burst != null) _burst.Emit(26);
            _burstBoost = 1f;
        }

        private void LateUpdate()
        {
            _fade = Mathf.MoveTowards(_fade, Visible ? 1f : 0f, 5f * Time.deltaTime);
            _burstBoost = Mathf.MoveTowards(_burstBoost, 0f, 4f * Time.deltaTime);

            bool shown = _fade > 0.001f;
            SetShown(shown);
            if (!shown) return;

            // 일렁임: 충전도가 오를수록 주파수가 3Hz → 24Hz로 빨라진다
            float freq = Mathf.Lerp(3f, 24f, Charge);
            _phase += freq * Time.deltaTime;
            float wobble = 0.7f + 0.3f * Mathf.Sin(_phase * Mathf.PI * 2f);

            float size = _scale * Mathf.Lerp(0.1f, 0.42f, Charge) * wobble * _fade;
            size *= 1f + _burstBoost * 1.8f;
            _core.localScale = Vector3.one * size;

            // 카메라를 향해 세워 어느 각도에서도 구체로 보이게(빌보드)
            if (_cam == null) _cam = Camera.main;
            if (_cam != null)
            {
                Vector3 toCam = _core.position - _cam.transform.position;
                if (toCam.sqrMagnitude > 1e-8f)
                    _core.rotation = Quaternion.LookRotation(toCam.normalized, _cam.transform.up);
            }

            // 색: 충전이 진행될수록 코어가 흰빛으로 과열된다
            // 충전이 올라도 흰빛으로 날아가지 않게 — 밝아지되 보라색을 유지한다.
            // 알파를 1로 두면 흡입 입자와 겹치는 중심이 가산 누적으로 하얗게 뭉갠다.
            Color c = Color.Lerp(_color, Color.white, 0.08f + 0.17f * Charge);
            c.a = _fade * 0.7f;
            SetMatColor(_coreMat, c);

            if (_light != null)
            {
                _light.range = _scale * Mathf.Lerp(2f, 6.5f, Charge);
                _light.intensity = (Mathf.Lerp(0.8f, 6f, Charge) * wobble + _burstBoost * 10f) * _fade;
            }

            var em = _inflow.emission;
            em.rateOverTime = Mathf.Lerp(8f, 70f, Charge) * _fade;
        }

        private void SetShown(bool on)
        {
            if (_coreRenderer.enabled == on) return;
            _coreRenderer.enabled = on;
            if (_light != null) _light.enabled = on;
            if (on) _inflow.Play();
            else
            {
                _inflow.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (_light != null) _light.intensity = 0f;
            }
        }
    }

    /// <summary>레이저 광선 + 발사 전 예고선. 예고선이 진해지는 동안 플레이어가 회피할 수 있다.</summary>
    public class Beam : MonoBehaviour
    {
        // 굵기(사람 1.8m 기준, 월드 미터). 굵을수록 위협적으로 보인다.
        private const float CoreWidth = 0.7f;    // 흰 코어
        private const float GlowWidth = 2.4f;    // 바깥 발광
        private const float HaloWidth = 4.6f;    // 가장 바깥 후광 — 멀리서도 광선의 존재가 보인다
        private const float GuideWidth = 0.24f;  // 발사 전 예고선
        private const float LockWidth = 0.5f;    // 조준 고정 경고선

        // 겹쳐 더해지는 세 층의 알파(합 ≈ 1.0). 이 합이 커지면 색이 잘려 흰색이 된다.
        private const float CoreAlpha = 0.5f;
        private const float GlowAlpha = 0.33f;
        private const float HaloAlpha = 0.17f;

        private LineRenderer _core, _glow, _halo, _guide, _lock;
        private float _life, _duration;
        private float _scale;
        private Color _color;

        internal void Init(float scale, Color color)
        {
            _scale = scale;
            _color = color;
            // 가산 블렌딩은 겹친 레이어가 그대로 더해진다 — 세 겹의 알파 합이 1을 넘으면
            // 채널이 잘리며 흰색으로 뭉갠다. 합이 1 근처에 머물도록 각 층을 낮게 잡는다.
            Color hot = Color.Lerp(color, Color.white, 0.2f);

            _halo = NewLine(transform, "Halo", new Color(color.r, color.g, color.b, HaloAlpha), HaloWidth * scale);
            _glow = NewLine(transform, "Glow", new Color(color.r, color.g, color.b, GlowAlpha), GlowWidth * scale);
            _core = NewLine(transform, "Core", new Color(hot.r, hot.g, hot.b, CoreAlpha), CoreWidth * scale);
            _guide = NewLine(transform, "Guide", new Color(color.r, color.g, color.b, 0.25f), GuideWidth * scale);
            _lock = NewLine(transform, "LockWarning", Color.Lerp(color, Color.white, 0.35f), LockWidth * scale);
        }

        /// <summary>
        /// 충전 중 조준선(예고). alpha 0~1로 점점 진해지고, 충전이 오를수록 맥동이 빨라진다.
        /// locked=true(조준 고정 구간)면 흰 경고선이 겹쳐 빠르게 깜빡인다 — "지금 구르면 피한다"는 신호.
        /// </summary>
        public void Preview(Vector3 from, Vector3 to, float alpha, bool locked = false)
        {
            alpha = Mathf.Clamp01(alpha);

            // 맥동: 충전이 진행될수록 4Hz → 14Hz로 빨라진다(임박 신호).
            // 바닥값을 높게 잡아 가장 옅은 순간에도 선이 사라져 보이지 않게 한다.
            float pulse = 0.78f + 0.22f * Mathf.Sin(Time.time * Mathf.Lerp(4f, 14f, alpha) * Mathf.PI * 2f);

            SetLine(_guide, from, to);
            Color c = Color.Lerp(_color, Color.white, 0.08f + 0.15f * alpha);
            c.a = alpha * pulse;
            _guide.startColor = c;
            _guide.endColor = new Color(c.r, c.g, c.b, c.a * 0.6f); // 끝까지 진하게 이어진다
            _guide.startWidth = _guide.endWidth = GuideWidth * _scale * (0.9f + alpha * (1f + 0.5f * pulse));

            if (locked)
            {
                SetLine(_lock, from, to);
                float blink = 0.65f + 0.35f * Mathf.Abs(Mathf.Sin(Time.time * 18f));
                Color lc = Color.Lerp(_color, Color.white, 0.45f); // 보라를 유지한 밝은 경고색
                lc.a = blink;
                _lock.startColor = lc;
                _lock.endColor = new Color(lc.r, lc.g, lc.b, blink * 0.7f);
                _lock.startWidth = _lock.endWidth = LockWidth * _scale * (1f + blink);
            }
            else if (_lock != null) _lock.enabled = false;
        }

        public void HidePreview()
        {
            if (_guide != null) _guide.enabled = false;
            if (_lock != null) _lock.enabled = false;
        }

        /// <summary>발사: 굵은 광선이 터졌다가 duration 동안 가늘어지며 사라진다.</summary>
        public void Fire(Vector3 from, Vector3 to, float duration)
        {
            HidePreview();
            _duration = Mathf.Max(0.01f, duration);
            _life = _duration;
            SetLine(_core, from, to);
            SetLine(_glow, from, to);
            SetLine(_halo, from, to);
        }

        /// <summary>발사 중 시작점이 움직였을 때(손이 흔들릴 때) 광선을 따라 붙인다.</summary>
        public void UpdateOrigin(Vector3 from)
        {
            if (_life <= 0f) return;
            _core.SetPosition(0, from);
            _glow.SetPosition(0, from);
            _halo.SetPosition(0, from);
        }

        public void Hide()
        {
            _life = 0f;
            if (_core != null) _core.enabled = false;
            if (_glow != null) _glow.enabled = false;
            if (_halo != null) _halo.enabled = false;
            HidePreview();
        }

        private void LateUpdate()
        {
            if (_life <= 0f) return;
            _life -= Time.deltaTime;
            float k = Mathf.Clamp01(_life / _duration);
            if (k <= 0f) { Hide(); return; }

            // 발사 직후 2배 이상으로 터졌다가(과열) 굵기를 유지한 채 사그라든다.
            // 미세한 떨림(flicker)을 섞어 '살아있는 에너지'로 보이게 한다.
            float burst = 1f + 1.3f * k * k * k;
            float flicker = 1f + 0.09f * Mathf.Sin(Time.time * 70f);
            float w = Mathf.Lerp(0.5f, 1f, k) * burst * flicker;

            _core.startWidth = _core.endWidth = CoreWidth * _scale * w;
            _glow.startWidth = _glow.endWidth = GlowWidth * _scale * w;
            _halo.startWidth = _halo.endWidth = HaloWidth * _scale * w;

            // 후광은 먼저 옅어져 광선이 '식는' 느낌을 준다
            _halo.startColor = _halo.endColor = new Color(_color.r, _color.g, _color.b, HaloAlpha * k);
        }

        private void SetLine(LineRenderer lr, Vector3 from, Vector3 to)
        {
            lr.enabled = true;
            lr.positionCount = 2;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
        }
    }

    /// <summary>
    /// 돌진 경로 예고선. 바닥에 "이 폭 안에 있으면 받힌다"는 통로를 그린다 —
    /// 가장자리 두 줄이 위험 폭을, 중앙선이 돌진 방향을 알린다.
    /// 예비동작 동안 alpha를 올려 진해지게 하고, 돌진이 시작되면 Hide한다.
    /// </summary>
    public class RushPath : MonoBehaviour
    {
        private LineRenderer _left, _right, _center;
        private float _scale;
        private Color _color;

        public GameObject Root => gameObject;

        internal void Init(float scale, Color color)
        {
            _scale = scale;
            _color = color;
            _left = NewLine(transform, "EdgeL", color, 0.18f * scale);
            _right = NewLine(transform, "EdgeR", color, 0.18f * scale);
            _center = NewLine(transform, "Center", Color.Lerp(color, Color.white, 0.25f), 0.1f * scale);
            _left.useWorldSpace = _right.useWorldSpace = _center.useWorldSpace = true;
        }

        /// <summary>from→to 경로를 반폭 halfWidth로 그린다. alpha 0~1.</summary>
        public void Show(Vector3 from, Vector3 to, float halfWidth, float alpha)
        {
            alpha = Mathf.Clamp01(alpha);
            Vector3 dir = to - from;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-8f) { Hide(); return; }
            Vector3 side = Vector3.Cross(Vector3.up, dir.normalized) * halfWidth;

            // 맥동: 돌진이 임박할수록 빨라진다(바닥값을 높여 늘 또렷하게 보이도록)
            float pulse = 0.78f + 0.22f * Mathf.Sin(Time.time * Mathf.Lerp(5f, 16f, alpha) * Mathf.PI * 2f);

            Color edge = Color.Lerp(_color, Color.white, 0.08f + 0.12f * alpha);
            edge.a = alpha * pulse;
            float edgeWidth = 0.18f * _scale * (0.9f + alpha * 1.6f);
            SetPath(_left, from + side, to + side, edge, edgeWidth);
            SetPath(_right, from - side, to - side, edge, edgeWidth);

            Color mid = Color.Lerp(_color, Color.white, 0.3f);
            mid.a = alpha * pulse * 0.9f;
            SetPath(_center, from, to, mid, 0.1f * _scale * (0.9f + alpha * 2f));
        }

        public void Hide()
        {
            if (_left != null) _left.enabled = false;
            if (_right != null) _right.enabled = false;
            if (_center != null) _center.enabled = false;
        }

        private static void SetPath(LineRenderer lr, Vector3 from, Vector3 to, Color c, float width)
        {
            lr.enabled = true;
            lr.positionCount = 2;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            lr.startColor = c;
            lr.endColor = new Color(c.r, c.g, c.b, c.a * 0.8f); // 통로 끝까지 진하게
            lr.startWidth = lr.endWidth = width;
        }
    }

    /// <summary>텔레포트 번쩍임(재사용형).</summary>
    public class Flash
    {
        private readonly ParticleSystem _core, _bolts, _ring;
        private readonly GunFx.LightPulse _light;
        public GameObject Root => _core != null ? _core.gameObject : null;

        internal Flash(ParticleSystem core, ParticleSystem bolts, ParticleSystem ring, GunFx.LightPulse light)
        {
            _core = core; _bolts = bolts; _ring = ring; _light = light;
        }

        public void Spawn(Vector3 pos)
        {
            if (_core == null) return;
            _core.transform.SetPositionAndRotation(pos, Quaternion.identity);
            _core.Emit(2);
            _bolts.Emit(14);
            _ring.Emit(2);
            if (_light != null) _light.Pulse();
        }
    }

    /// <summary>손가락 끝 궤적(할퀴기).</summary>
    public class ClawTrail
    {
        private readonly TrailRenderer[] _trails;
        internal ClawTrail(TrailRenderer[] trails) { _trails = trails; }

        public void SetEmitting(bool on)
        {
            foreach (var t in _trails)
            {
                if (t == null) continue;
                if (on && !t.emitting) t.Clear(); // 이전 궤적이 이어져 허공을 가로지르지 않게
                t.emitting = on;
            }
        }
    }

    // ---------- 내부 헬퍼 ----------

    /// <summary>
    /// 부모(본/루트)의 스케일을 상쇄해 이 오브젝트의 월드 스케일을 1로 만든다.
    /// 캐릭터/본 스케일이 어떤 값이든 이펙트 크기를 월드 미터 기준으로 다룰 수 있다.
    /// </summary>
    private static void NeutralizeScale(Transform t)
    {
        Transform p = t.parent;
        if (p == null) { t.localScale = Vector3.one; return; }
        Vector3 s = p.lossyScale;
        t.localScale = new Vector3(
            Mathf.Abs(s.x) < 1e-8f ? 1f : 1f / s.x,
            Mathf.Abs(s.y) < 1e-8f ? 1f : 1f / s.y,
            Mathf.Abs(s.z) < 1e-8f ? 1f : 1f / s.z);
    }

    /// <summary>2점 라인 렌더러 골격(광선/예고선 공용).</summary>
    private static LineRenderer NewLine(Transform parent, string name, Color color, float width)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = GunFx.MakeTracerMaterial();
        lr.startColor = lr.endColor = color;
        lr.startWidth = lr.endWidth = width;
        lr.positionCount = 2;
        lr.numCapVertices = 3;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.enabled = false;
        return lr;
    }

    private static Material NewGlowMaterial(Color c)
    {
        var m = new Material(GunFx.MakeTracerMaterial()) { hideFlags = HideFlags.DontSave };
        SetMatColor(m, c);
        return m;
    }

    private static void SetMatColor(Material m, Color c)
    {
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", c);
    }

    /// <summary>수동 방출형 파티클 시스템 골격(GunFx.NewSystem과 같은 규약).</summary>
    private static ParticleSystem NewSystem(string name, Transform parent, bool loop)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = loop;
        main.duration = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 256;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.enabled = false;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = GunFx.MakeTracerMaterial();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return ps;
    }

    private static void SetCone(ParticleSystem ps, float angle, float radius)
    {
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = angle;
        shape.radius = Mathf.Max(0.001f, radius);
    }

    private static void Stretch(ParticleSystem ps, float lengthScale)
    {
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = lengthScale;
    }

    private static void FadeOut(ParticleSystem ps, Color from, Color to)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 0.6f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);
    }

    private static void Grow(ParticleSystem ps, float endScale)
    {
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f,
            AnimationCurve.EaseInOut(0f, 1f / endScale, 1f, 1f));
        var main = ps.main;
        main.startSize = new ParticleSystem.MinMaxCurve(
            main.startSize.constantMin * endScale, main.startSize.constantMax * endScale);
    }
}
