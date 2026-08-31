using UnityEngine;
using UnityEngine.Rendering;
using BossFX;   // BossPatternFX 패키지(셰이더 + 라이브러리)

/// <summary>
/// 보스(AlienMonster) 전용 이펙트 팩토리.
/// 겉으로 드러나는 API는 예전 그대로지만, 안쪽은 전부 <b>BossPatternFX 셰이더</b>로 다시 그렸다.
/// 라인 렌더러·파티클로 흉내 내던 것을 SDF 셰이더 한 장으로 바꿔서,
/// 어느 거리·각도에서 봐도 가장자리가 또렷하고 Bloom을 먹으면 발광체처럼 읽힌다.
///
/// - ChargeOrb : 검지 끝 충전 구체(Radial/Orb) + 안으로 조여드는 링 = "곧 쏜다"는 신호
/// - Beam      : 예고선 → 광선(Beam 셰이더의 _Charge로 굵기, _Fire로 뻗는 길이를 제어)
/// - RushPath  : 돌진 통로(Telegraph/Line). 예비동작 동안 보스 발밑에서 앞으로 차오른다
/// - Flash     : 텔레포트 번쩍임(Radial/Burst 섬광 + 바닥 링 + 순간 광원)
/// - RoarAura  : 포효(Telegraph/Circle 장판 + 수직 광선 기둥 + 광원)
/// - ClawSlash : 허공에 새겨지는 발톱 자국(호 리본 메시 + Beam 셰이더)
/// - ClawTrail : 손끝 궤적(뼈에 붙는 TrailRenderer — 셰이더 대응물이 없어 그대로 둔다)
///
/// 모든 크기는 사람 기준(1.8m) 수치 × scale 배율 — 이 프로젝트처럼 캐릭터가 작아도 비율이 맞는다.
/// </summary>
public static class BossFx
{
    // ---------- 공유 머티리얼 ----------
    // 셰이더당 하나만 만들어 돌려 쓰고, 개별 값은 MaterialPropertyBlock으로 넘긴다
    // (프로퍼티가 전부 UnityPerMaterial 안에 있어 SRP Batcher가 그대로 묶어 준다).

    private static Material _telegraphMat, _beamMat, _radialMat;

    // BossFXLibrary 가 미리 잡아 두지 않은 프로퍼티
    private static readonly int PHeadTaper = Shader.PropertyToID("_HeadTaper");

    internal static Material TelegraphMat
    {
        get
        {
            if (_telegraphMat == null) _telegraphMat = BossFXLibrary.CreateMaterial("BossFX/Telegraph");
            return _telegraphMat;
        }
    }

    internal static Material BeamMat
    {
        get
        {
            if (_beamMat == null) _beamMat = BossFXLibrary.CreateMaterial("BossFX/Beam");
            return _beamMat;
        }
    }

    internal static Material RadialMat
    {
        get
        {
            if (_radialMat == null) _radialMat = BossFXLibrary.CreateMaterial("BossFX/Radial");
            return _radialMat;
        }
    }

    // ---------- 공개 API ----------

    /// <summary>
    /// BossFx가 만든 이펙트 오브젝트라는 표식. 보스를 복제할 때(BossClone) 이것만 보고
    /// 통째로 들어내면 된다.
    ///
    /// 예전에는 복제 쪽에서 렌더러 종류(LineRenderer/TrailRenderer/ParticleSystemRenderer)로
    /// 이펙트를 걸러냈는데, 이펙트가 셰이더 쿼드(MeshRenderer)로 바뀌자 그 필터를 빠져나가
    /// 분신마다 죽은 광선·장판이 그대로 복제돼 붙었다. 종류가 아니라 표식으로 거른다.
    /// </summary>
    public sealed class Tag : MonoBehaviour { }

    /// <summary>이펙트 루트에 표식을 달고 그대로 돌려준다.</summary>
    private static GameObject Mark(GameObject go)
    {
        go.AddComponent<Tag>();
        return go;
    }

    /// <summary>
    /// 검지 끝(anchor)에 붙는 충전 구체. Charge(0~1)/Visible을 매 프레임 갱신해 쓴다.
    /// withLight=false면 점광원을 달지 않는다 — 분신처럼 여러 기가 동시에 뜨는 경우에 쓴다
    /// (URP는 오브젝트당 추가 광원을 몇 개만 고르므로, 광원이 많으면 그 선택이 매 프레임
    ///  뒤바뀌며 화면이 번쩍인다).
    /// </summary>
    public static ChargeOrb BuildChargeOrb(Transform anchor, float scale, Color color, bool withLight = true)
    {
        var go = Mark(new GameObject("BossChargeOrb"));
        go.transform.SetParent(anchor, false);
        go.transform.localPosition = Vector3.zero;
        NeutralizeScale(go.transform); // 본 스케일을 상쇄 — 이펙트 크기를 월드 미터로 다룬다
        var orb = go.AddComponent<ChargeOrb>();
        orb.Init(scale, color, withLight);
        return orb;
    }

    /// <summary>
    /// 머리 본에 붙는 눈빛. head가 없으면(비휴머노이드 리그) null을 돌려준다 —
    /// 대신 몸통에 붙이면 발밑에서 빛나므로, 못 붙이면 아예 안 붙이는 편이 낫다.
    /// </summary>
    public static EyeGlow BuildEyeGlow(Transform head, Transform headTop, Transform body,
                                       float scale, Color color,
                                       Vector3 offset, float radius, bool withLight = true)
    {
        if (head == null) return null;
        var go = Mark(new GameObject("BossEyeGlow"));
        go.transform.SetParent(head, false);
        go.transform.localPosition = Vector3.zero;
        NeutralizeScale(go.transform); // 본 스케일 상쇄 — 눈 크기를 월드 미터로 다룬다
        var eyes = go.AddComponent<EyeGlow>();
        eyes.Init(head, headTop, body, scale, color, offset, radius, withLight);
        return eyes;
    }

    /// <summary>레이저 광선(발사) + 예고선(충전 중 조준선).</summary>
    public static Beam BuildBeam(Transform parent, float scale, Color color)
    {
        var go = Mark(new GameObject("BossLaserBeam"));
        go.transform.SetParent(parent, false);
        NeutralizeScale(go.transform);
        var beam = go.AddComponent<Beam>();
        beam.Init(scale, color);
        return beam;
    }

    /// <summary>할퀸 자리에 남는 발톱 자국(평행한 호 세 줄). Play로 한 번씩 터뜨린다.</summary>
    public static ClawSlash BuildClawSlash(float scale, Color color)
    {
        var go = Mark(new GameObject("BossClawSlash"));
        var slash = go.AddComponent<ClawSlash>();
        slash.Init(scale, color);
        return slash;
    }

    /// <summary>돌진 경로를 바닥에 그리는 예고 장판.</summary>
    public static RushPath BuildRushPath(float scale, Color color)
    {
        var go = Mark(new GameObject("BossRushPath"));
        var path = go.AddComponent<RushPath>();
        path.Init(scale, color);
        return path;
    }

    /// <summary>텔레포트 번쩍임. Spawn(pos)로 발동하는 재사용형 핸들.</summary>
    public static Flash BuildFlash(float scale, Color color)
    {
        var go = Mark(new GameObject("BossTeleportFX"));

        // 순간 광원(GunFx의 감쇠 광원 재사용) — 셰이더 섬광만으로는 주변이 안 밝아진다
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = 8f * scale;
        light.intensity = 0f;
        light.shadows = LightShadows.None;
        var pulse = go.AddComponent<GunFx.LightPulse>();
        pulse.Init(light, peakIntensity: 6f, decayTime: 0.22f);

        return new Flash(go, pulse, scale, color);
    }

    /// <summary>
    /// 포효(기 모으기) 오라. 발밑 장판이 차오르고 그 위로 광선 기둥이 솟는다.
    /// SetPower(0~1)로 매 프레임 세기를 올리면 기가 차오르는 것처럼 보이고,
    /// Burst()로 해방하는 순간의 충격파를 터뜨린다.
    /// anchor(보스)에 붙으므로 보스가 사라질 때 같이 정리된다.
    /// </summary>
    public static RoarAura BuildRoarAura(Transform anchor, float scale, Color color)
    {
        var go = Mark(new GameObject("BossRoarAura"));
        go.transform.SetParent(anchor, false);
        go.transform.localPosition = Vector3.zero;
        NeutralizeScale(go.transform); // 본 스케일 상쇄 — 이펙트 크기를 월드 미터로 다룬다
        var aura = go.AddComponent<RoarAura>();
        aura.Init(scale, color);
        return aura;
    }

    /// <summary>손가락 끝들에 붙는 할퀴기 궤적. 강타 구간에만 켠다.</summary>
    public static ClawTrail BuildClawTrail(Transform[] tips, float scale, Color color)
    {
        var trails = new System.Collections.Generic.List<TrailRenderer>();
        foreach (var tip in tips)
        {
            if (tip == null) continue;
            var go = Mark(new GameObject("ClawTrail"));
            go.transform.SetParent(tip, false);
            NeutralizeScale(go.transform);
            var tr = go.AddComponent<TrailRenderer>();
            tr.sharedMaterial = GunFx.MakeTracerMaterial();
            tr.time = 0.22f;                 // 조금 더 길게 남아 호가 이어져 보인다
            tr.startWidth = 0.11f * scale;   // 0.05는 이 스케일에서 실오라기처럼 보였다
            tr.endWidth = 0f;
            tr.numCapVertices = 3;
            tr.minVertexDistance = 0.008f * scale;
            tr.autodestruct = false;
            tr.shadowCastingMode = ShadowCastingMode.Off;
            tr.receiveShadows = false;
            tr.emitting = false;

            var grad = new Gradient();
            Color hot = Color.Lerp(color, Color.white, 0.2f);
            // 손끝 세 줄이 겹쳐 더해지므로 알파를 낮게 — 겹친 자리가 하얗게 뭉치지 않게
            grad.SetKeys(
                new[] { new GradientColorKey(hot, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0f, 1f) });
            tr.colorGradient = grad;

            trails.Add(tr);
        }
        return new ClawTrail(trails.ToArray());
    }

    // ---------- 셰이더 판때기 공통 ----------
    // BossMeteor/BossOrb 도 같은 부품으로 그리므로 어셈블리 안에서는 열어 둔다.

    /// <summary>
    /// BossPatternFX 셰이더 하나를 얹은 판(쿼드) 한 장.
    /// 값은 전부 MaterialPropertyBlock으로 넘기므로 머티리얼 인스턴스가 늘어나지 않는다.
    /// </summary>
    internal sealed class Surface
    {
        public readonly Transform T;
        private readonly MeshRenderer _r;
        private readonly MaterialPropertyBlock _mpb;

        public Surface(string name, Transform parent, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            T = go.transform;

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            _r = go.AddComponent<MeshRenderer>();
            _r.sharedMaterial = mat;
            _r.shadowCastingMode = ShadowCastingMode.Off;
            _r.receiveShadows = false;
            _r.lightProbeUsage = LightProbeUsage.Off;
            _r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _r.enabled = false;

            _mpb = new MaterialPropertyBlock();
        }

        /// <summary>
        /// 표시 여부. 보스가 파괴된 뒤 정리 코드가 한 번 더 들어오는 경우가 있어
        /// 렌더러가 이미 사라졌는지 확인한다(Unity의 == 오버로드가 파괴된 객체를 null로 본다).
        /// </summary>
        public bool Shown
        {
            get => _r != null && _r.enabled;
            set { if (_r != null) _r.enabled = value; }
        }

        public Surface Set(int id, float v) { _mpb.SetFloat(id, v); return this; }
        public Surface Set(int id, Color c) { _mpb.SetColor(id, c); return this; }

        /// <summary>쌓아 둔 값을 한 번에 밀어 넣는다.</summary>
        public void Apply() => _r.SetPropertyBlock(_mpb);
    }

    /// <summary>
    /// 머리 본에서 정수리까지의 길이(월드 미터). 눈 위치·크기의 기준자다.
    /// 정수리 본이 없는 리그에서는 사람 비율(1.8m 기준 0.13m)로 어림한다.
    /// </summary>
    public static float HeadLengthOf(Transform head, Transform headTop, float scale)
    {
        if (head != null && headTop != null)
        {
            float len = Vector3.Distance(headTop.position, head.position);
            if (len > 1e-5f) return len;
        }
        return 0.13f * scale;
    }

    /// <summary>
    /// 머리 본을 기준으로 두 눈의 월드 위치를 계산한다. offset은 <b>머리 길이에 대한 비율</b>
    /// (x=좌우 간격, y=머리 축 방향 위, z=얼굴 앞)이라 모델 크기가 달라도 그대로 맞는다.
    ///
    /// '위'는 몸통의 위가 아니라 <b>머리 본 → 정수리</b> 방향이다. 이 보스는 머리를 앞으로
    /// 크게 내밀고 있어서, 몸통 기준으로 눈을 얹으면 얼굴이 아니라 뒤통수 위에 뜬다.
    /// '앞'은 그 축에 수직으로 세운 몸통 정면 — 얼굴 면을 따라간다.
    ///
    /// 에디터 기즈모와 앵커 생성 도구도 같은 계산을 써야 하므로 여기에 둔다.
    /// </summary>
    public static void ResolveEyePositions(Transform head, Transform headTop, Transform body,
                                           Vector3 offset, float scale,
                                           out Vector3 left, out Vector3 right, out float headLen)
    {
        headLen = HeadLengthOf(head, headTop, scale);
        if (head == null) { left = right = Vector3.zero; return; }

        Transform b = body != null ? body : head;

        Vector3 up = headTop != null ? headTop.position - head.position : b.up;
        up = up.sqrMagnitude > 1e-8f ? up.normalized : b.up;

        // 얼굴 정면: 몸통 정면에서 머리 축 성분을 빼 축과 직각으로 만든다.
        // (머리를 완전히 젖혀 두 방향이 나란해지는 극단에서는 몸통 정면을 그대로 쓴다)
        Vector3 fwd = b.forward - up * Vector3.Dot(b.forward, up);
        fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : b.forward;

        Vector3 side = Vector3.Cross(up, fwd).normalized;   // 머리 기준 왼쪽

        Vector3 center = head.position + up * (offset.y * headLen) + fwd * (offset.z * headLen);
        Vector3 half = side * (offset.x * headLen);
        left = center + half;
        right = center - half;
    }

    /// <summary>XZ 평면에 눕힌 판을 카메라 쪽으로 세운다(빌보드).</summary>
    internal static void FaceCamera(Transform t, Camera cam)
    {
        if (cam == null) return;
        Vector3 dir = t.position - cam.transform.position;
        if (dir.sqrMagnitude < 1e-8f) return;
        // 눕힌 쿼드의 법선(+Y)을 카메라 쪽(+Z)으로 돌린다
        t.rotation = Quaternion.LookRotation(dir.normalized) * Quaternion.Euler(90f, 0f, 0f);
    }

    /// <summary>
    /// QuadForward 판을 from → to 로 걸친다. 이 메시는 <b>로컬 +X</b>로 뻗으므로
    /// LookRotation(+Z 기준)에 -90도 보정을 곱해야 길이축이 진행 방향에 맞는다.
    ///
    /// 폭축은 "빔 축 × 시선"으로 잡는다 — 이래야 판의 법선이 카메라를 정면으로 본다.
    /// (빔 축과 시선이 이루는 평면에 판을 눕히면 옆면만 보여 빔이 사라진다.)
    /// </summary>
    internal static void PlaceBeamQuad(Transform t, Vector3 from, Vector3 to, float span, Camera cam)
    {
        Vector3 dir = to - from;
        float len = dir.magnitude;
        if (len < 1e-5f) { dir = Vector3.forward; len = 1e-5f; }
        else dir /= len;

        Vector3 up = Vector3.up;
        if (cam != null)
        {
            Vector3 side = Vector3.Cross(dir, cam.transform.position - from);
            if (side.sqrMagnitude > 1e-8f) up = side.normalized;
        }

        t.position = from;
        t.rotation = Quaternion.LookRotation(dir, up) * Quaternion.Euler(0f, -90f, 0f);
        t.localScale = new Vector3(len, span, 1f);
    }

    // ---------- 핸들 ----------

    /// <summary>
    /// 손끝 충전 구체. 발사까지 남은 시간에 맞춰 Charge를 0→1로 올리면
    /// 구체가 커지면서 일렁임(맥동)이 빨라지고, 바깥 링이 중심으로 조여든다 —
    /// "발사 시간이 임박했다"는 두 겹의 신호.
    /// </summary>
    public class ChargeOrb : MonoBehaviour
    {
        private Surface _core;    // Radial/Orb — 구체 본체
        private Surface _ring;    // Radial/Ring — 안으로 조여드는 수렴 링
        private Light _light;
        private Camera _cam;
        private Color _color, _hot;
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
            _hot = Color.Lerp(color, Color.white, 0.25f);

            _core = new Surface("Core", transform, BossFXLibrary.QuadXZ, RadialMat);
            _core.Set(BossFXLibrary.PMode, (float)(int)BossRadialMode.Orb)
                 .Set(BossFXLibrary.PColorCore, _hot)
                 .Set(BossFXLibrary.PColorEdge, color)
                 .Set(BossFXLibrary.PThickness, 0.38f)   // 밝은 심(心)이 차지하는 비율
                 .Set(BossFXLibrary.PFalloff, 2.4f)
                 .Apply();

            _ring = new Surface("Converge", transform, BossFXLibrary.QuadXZ, RadialMat);
            _ring.Set(BossFXLibrary.PMode, (float)(int)BossRadialMode.Ring)
                 .Set(BossFXLibrary.PColorCore, _hot)
                 .Set(BossFXLibrary.PColorEdge, color)
                 .Set(BossFXLibrary.PThickness, 0.09f)
                 .Set(BossFXLibrary.PFalloff, 2.6f)
                 .Apply();

            if (withLight)
            {
                _light = gameObject.AddComponent<Light>();
                _light.type = LightType.Point;
                _light.color = color;
                _light.range = 2.5f * scale;
                _light.intensity = 0f;
                _light.shadows = LightShadows.None;
            }

            SetShown(false);
        }

        /// <summary>발사 순간: 구체가 터지며 광원이 확 밝아진다.</summary>
        public void Burst()
        {
            _burstBoost = 1f;

            // 방사형 섬광 한 장 — 총구 화염에 해당한다
            BossImpactFX.Spawn(new BossImpactSettings
            {
                mode = BossRadialMode.Burst,
                radius = 1.1f * _scale,
                duration = 0.26f,
                falloff = 2.2f,
                coreColor = Color.Lerp(_hot, Color.white, 0.2f),
                edgeColor = _color,
                intensity = 3.6f,
                flatOnGround = false,       // 공중에서 터지므로 카메라를 향해 세운다
            }, transform.position);
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

            if (_cam == null) _cam = Camera.main;

            // --- 구체: 차오를수록 확실히 커진다(이것도 "곧 쏜다"는 경고다) ---
            float size = _scale * Mathf.Lerp(0.14f, 0.62f, Charge) * wobble * _fade;
            size *= 1f + _burstBoost * 1.8f;
            _core.T.localScale = new Vector3(size, 1f, size);
            _core.T.position = transform.position;
            FaceCamera(_core.T, _cam);

            // 가산 합성이라 세기를 그대로 올리면 중심이 흰색으로 뭉갠다 — 낮게 유지한다
            _core.Set(BossFXLibrary.PIntensity, Mathf.Lerp(1.1f, 2.6f, Charge) + _burstBoost * 3f)
                 .Set(BossFXLibrary.POpacity, _fade * 0.85f)
                 .Apply();

            // --- 수렴 링: 바깥에서 시작해 충전이 끝나는 순간 중심에서 만난다 ---
            float ringSpan = _scale * 1.5f * _fade;
            _ring.T.localScale = new Vector3(ringSpan, 1f, ringSpan);
            _ring.T.position = transform.position;
            FaceCamera(_ring.T, _cam);
            _ring.Set(BossFXLibrary.PRadius, Mathf.Lerp(0.95f, 0.12f, Charge))
                 .Set(BossFXLibrary.PIntensity, 2.2f)
                 // 충전이 시작돼야 보이고, 조여들수록 진해진다
                 .Set(BossFXLibrary.POpacity, _fade * Mathf.SmoothStep(0f, 1f, Charge) * 0.9f)
                 .Apply();

            if (_light != null)
            {
                _light.range = _scale * Mathf.Lerp(2f, 6.5f, Charge);
                _light.intensity = (Mathf.Lerp(0.8f, 6f, Charge) * wobble + _burstBoost * 10f) * _fade;
            }
        }

        private void SetShown(bool on)
        {
            if (_core.Shown == on) return;
            _core.Shown = on;
            _ring.Shown = on;
            if (_light != null)
            {
                _light.enabled = on;
                if (!on) _light.intensity = 0f;
            }
        }
    }

    /// <summary>
    /// 보스의 <b>빛나는 눈</b>. 살아서 모습을 드러내고 있는 내내 켜져 있다.
    ///
    /// 위치를 정하는 방법은 두 가지다.
    ///  1. <b>앵커</b>(<see cref="SetAnchors"/>) — 씬에 놓인 빈 오브젝트를 그대로 따라간다.
    ///     눈금이 아니라 눈으로 맞추는 방법이라, 모델을 보면서 끌어다 두면 된다.
    ///  2. 앵커가 없으면 <b>머리 길이의 비율</b>로 계산한다(<see cref="ResolveEyePositions"/>).
    ///
    /// 눈동자는 카메라를 향해 세운 Radial/Orb 판 두 장이다. 깊이 판정을 살려 두므로
    /// 뒤에서 보면 머리에 가려 보이지 않는다 — 정면으로 마주 봤을 때만 노려보는 눈이 된다.
    /// </summary>
    public class EyeGlow : MonoBehaviour
    {
        private Surface _left, _right;
        private Light _light;
        private Camera _cam;
        private Transform _head, _headTop, _body;
        private Transform _anchorL, _anchorR;
        private Color _color;
        private float _scale, _fade, _radius;
        private Vector3 _offset;   // 머리 길이에 대한 비율 (x=좌우 간격, y=머리 축 위, z=얼굴 앞)

        /// <summary>켜짐 여부. 끄면 부드럽게 사그라든다.</summary>
        public bool Active { get; set; }

        /// <summary>
        /// 눈을 붙일 자리를 씬 오브젝트로 직접 지정한다(둘 다 있어야 쓰인다).
        /// 앵커의 <c>localScale.x</c>가 눈 크기 배율이 되므로, 오브젝트를 키우면 눈도 커진다.
        /// </summary>
        public void SetAnchors(Transform left, Transform right)
        {
            _anchorL = left;
            _anchorR = right;
        }

        internal void Init(Transform head, Transform headTop, Transform body, float scale, Color color,
                           Vector3 offset, float radius, bool withLight)
        {
            _head = head;
            _headTop = headTop;
            _body = body != null ? body : head;
            _scale = scale;
            _color = color;
            _offset = offset;
            _radius = Mathf.Max(0.001f, radius);

            // 가산 합성이라 심을 흰색까지 올리면 보라색이 날아간다 —
            // 다른 보스 이펙트와 같은 규칙으로 살짝만 과열시킨다.
            Color hot = Color.Lerp(color, Color.white, 0.3f);

            _left = MakeEye("EyeL", hot);
            _right = MakeEye("EyeR", hot);

            if (withLight)
            {
                // 눈에서 새어 나오는 빛. 하나만 단다 — URP 포워드는 오브젝트당 추가 광원을
                // 상위 몇 개만 고르므로, 상시 켜 두는 광원을 둘로 늘릴 이유가 없다.
                _light = gameObject.AddComponent<Light>();
                _light.type = LightType.Point;
                _light.color = color;
                _light.range = 2.4f * scale;
                _light.intensity = 0f;
                _light.shadows = LightShadows.None;
                _light.enabled = false;
            }
        }

        /// <summary>머리 본에서 정수리까지의 길이(월드 미터). 정수리 본이 없으면 사람 비율로 어림한다.</summary>
        private float HeadLength => HeadLengthOf(_head, _headTop, _scale);

        private Surface MakeEye(string name, Color hot)
        {
            var s = new Surface(name, transform, BossFXLibrary.QuadXZ, RadialMat);
            s.Set(BossFXLibrary.PMode, (float)(int)BossRadialMode.Orb)
             .Set(BossFXLibrary.PColorCore, hot)
             .Set(BossFXLibrary.PColorEdge, _color)
             .Set(BossFXLibrary.PThickness, 0.32f)   // 밝은 심이 작을수록 '동공'처럼 읽힌다
             .Set(BossFXLibrary.PFalloff, 2.8f)
             .Apply();
            return s;
        }

        private void LateUpdate()
        {
            _fade = Mathf.MoveTowards(_fade, Active ? 1f : 0f, 2.5f * Time.deltaTime);

            bool shown = _fade > 0.002f && _head != null;
            if (_left != null) _left.Shown = shown;
            if (_right != null) _right.Shown = shown;
            if (_light != null) _light.enabled = shown;
            if (!shown) return;

            if (_cam == null) _cam = Camera.main;

            // 느린 호흡. 깜빡임이 아니라 '살아 있다'는 정도의 흔들림만 준다.
            float breathe = 0.9f + 0.1f * Mathf.Sin(Time.time * 2.2f);

            float headLen = HeadLength;
            Vector3 lPos, rPos;
            float lSize = 1f, rSize = 1f;

            if (_anchorL != null && _anchorR != null)
            {
                lPos = _anchorL.position;
                rPos = _anchorR.position;
                lSize = Mathf.Max(0.01f, _anchorL.localScale.x);
                rSize = Mathf.Max(0.01f, _anchorR.localScale.x);
            }
            else ResolveEyePositions(_head, _headTop, _body, _offset, _scale, out lPos, out rPos, out headLen);

            // 눈판을 놓기 전에 뿌리를 옮긴다 — 나중에 옮기면 부모가 움직인 만큼
            // 자식(눈)의 월드 위치가 함께 끌려가 한 프레임 어긋난다.
            if (_light != null)
            {
                transform.position = (lPos + rPos) * 0.5f;   // 광원을 두 눈 사이로 끌어온다
                _light.intensity = 2.2f * _fade * breathe;
            }

            float d = _radius * headLen * breathe * _fade;
            PlaceEye(_left, lPos, d * lSize);
            PlaceEye(_right, rPos, d * rSize);
        }

        private void PlaceEye(Surface s, Vector3 at, float diameter)
        {
            if (s == null) return;
            s.T.position = at;
            s.T.localScale = new Vector3(diameter, 1f, diameter);
            FaceCamera(s.T, _cam);
            s.Set(BossFXLibrary.PIntensity, 3.2f)
             .Set(BossFXLibrary.POpacity, _fade * 0.9f)
             .Apply();
        }
    }

    /// <summary>
    /// 레이저 광선 + 발사 전 예고선. 예고선이 진해지는 동안 플레이어가 회피할 수 있다.
    /// 예고선과 광선은 서로 다른 판이라 동시에 떠 있을 수 있다(충전하며 쏘는 구간).
    /// </summary>
    public class Beam : MonoBehaviour
    {
        // 판의 세로 폭(사람 1.8m 기준, 월드 미터). 셰이더의 _CoreWidth/_GlowWidth는
        // 이 폭에 대한 비율이므로, 실제로 보이는 광선 굵기는 이 값 × 비율이다.
        private const float BeamSpan = 4.6f;   // 예전 후광(Halo) 굵기와 같게 잡았다
        private const float GuideSpan = 2.0f;

        private Surface _beam, _guide;
        private Camera _cam;
        private Color _color, _hot;
        private float _scale;

        private Vector3 _from, _to;            // 발사 중인 광선의 양 끝
        private float _life, _duration, _extend;

        internal void Init(float scale, Color color)
        {
            _scale = scale;
            _color = color;
            _hot = Color.Lerp(color, Color.white, 0.2f);

            // 배치·회전·빌보드는 전부 PlaceBeamQuad가 잡는다(QuadForward의 +X 축 보정 포함)
            _beam = new Surface("Beam", transform, BossFXLibrary.QuadForward, BeamMat);
            _beam.Set(BossFXLibrary.PColorCore, _hot)
                 .Set(BossFXLibrary.PColorGlow, color)
                 .Apply();

            _guide = new Surface("Guide", transform, BossFXLibrary.QuadForward, BeamMat);
            _guide.Set(BossFXLibrary.PColorCore, Color.Lerp(color, Color.white, 0.35f))
                  .Set(BossFXLibrary.PColorGlow, color)
                  .Apply();
        }

        /// <summary>
        /// 충전 중 조준선(예고). alpha 0~1로 점점 진해지고, 충전이 오를수록 맥동이 빨라진다.
        /// locked=true(조준 고정 구간)면 선이 굵어지며 빠르게 깜빡인다 — "지금 구르면 피한다"는 신호.
        /// </summary>
        public void Preview(Vector3 from, Vector3 to, float alpha, bool locked = false)
        {
            alpha = Mathf.Clamp01(alpha);
            _guide.Shown = true;

            if (_cam == null) _cam = Camera.main;
            PlaceBeamQuad(_guide.T, from, to, GuideSpan * _scale, _cam);

            // 맥동: 충전이 진행될수록 4Hz → 14Hz로 빨라진다(임박 신호).
            // 바닥값을 높게 잡아 가장 옅은 순간에도 선이 사라져 보이지 않게 한다.
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * Mathf.Lerp(4f, 14f, alpha) * Mathf.PI * 2f);
            float blink = locked ? 0.8f + 0.2f * Mathf.Abs(Mathf.Sin(Time.time * 18f)) : 1f;

            // _Charge가 굵기다(0.12배 ~ 1배). 예고선은 실처럼 가늘게, 조준이 고정되면 눈에 띄게 굵어진다.
            float charge = locked ? Mathf.Lerp(0.10f, 0.34f, alpha) : Mathf.Lerp(0.02f, 0.16f, alpha);

            _guide.Set(BossFXLibrary.PCharge, charge)
                  .Set(BossFXLibrary.PFire, 1f)
                  .Set(BossFXLibrary.PIntensity, Mathf.Lerp(1.2f, 3.0f, alpha) * blink)
                  .Set(BossFXLibrary.POpacity, alpha * pulse)
                  .Apply();
        }

        public void HidePreview()
        {
            if (_guide != null) _guide.Shown = false;
        }

        /// <summary>발사: 굵은 광선이 뻗어나가 터졌다가 duration 동안 사그라들며 사라진다.</summary>
        public void Fire(Vector3 from, Vector3 to, float duration)
        {
            HidePreview();
            _from = from;
            _to = to;
            _duration = Mathf.Max(0.01f, duration);
            _life = _duration;
            // 끝까지 뻗는 데 걸리는 시간. 길어야 사거리가 보이고, 짧아야 "즉발"로 읽힌다.
            _extend = Mathf.Min(0.06f, _duration * 0.25f);

            if (_cam == null) _cam = Camera.main;
            _beam.Shown = true;
            PlaceBeamQuad(_beam.T, _from, _to, BeamSpan * _scale, _cam);
            _beam.Set(BossFXLibrary.PCharge, 1f)
                 .Set(BossFXLibrary.PFire, 0f)
                 .Set(BossFXLibrary.PIntensity, 4.5f)
                 .Set(BossFXLibrary.POpacity, 1f)
                 .Apply();
        }

        /// <summary>발사 중 시작점이 움직였을 때(손이 흔들릴 때) 광선을 따라 붙인다.</summary>
        public void UpdateOrigin(Vector3 from)
        {
            if (_life <= 0f) return;
            _from = from;   // 실제 배치는 LateUpdate가 매 프레임 다시 잡는다
        }

        public void Hide()
        {
            _life = 0f;
            if (_beam != null) _beam.Shown = false;
            HidePreview();
        }

        private void LateUpdate()
        {
            if (_cam == null) _cam = Camera.main;
            if (_life <= 0f) return;

            _life -= Time.deltaTime;
            float k = Mathf.Clamp01(_life / _duration);
            if (k <= 0f) { Hide(); return; }

            // 광선은 월드에 고정된 선이다 — 보스가 움직여도 끌려가지 않도록 매 프레임 다시 건다
            // (겸사겸사 카메라를 향한 빌보드도 여기서 갱신된다)
            PlaceBeamQuad(_beam.T, _from, _to, BeamSpan * _scale, _cam);

            // 뻗어나가는 진행도. _Fire가 1이 되면 광선이 끝까지 닿는다.
            float elapsed = _duration - _life;
            float fire = _extend > 1e-4f ? Mathf.Clamp01(elapsed / _extend) : 1f;

            // 발사 직후 과열됐다가(굵고 밝게) 굵기를 잃으며 사그라든다.
            // 미세한 떨림은 셰이더의 _FlickerAmount가 이미 넣어 준다.
            float burst = 1f + 1.3f * k * k * k;

            _beam.Set(BossFXLibrary.PFire, fire)
                 .Set(BossFXLibrary.PCharge, Mathf.Lerp(0.45f, 1f, k))
                 .Set(BossFXLibrary.PIntensity, 3.0f * burst)
                 .Set(BossFXLibrary.POpacity, Mathf.SmoothStep(0f, 1f, k))
                 .Apply();
        }

    }

    /// <summary>
    /// 돌진 경로 예고 장판. 바닥에 "이 폭 안에 있으면 받힌다"는 통로를 그린다.
    /// 예비동작 동안 alpha를 올리면 통로가 보스 발밑에서 앞으로 <b>차오르며</b>
    /// 언제 튀어나올지를 눈으로 셀 수 있게 한다. 돌진이 시작되면 Hide한다.
    /// </summary>
    public class RushPath : MonoBehaviour
    {
        private Surface _band;
        private Color _color;

        public GameObject Root => gameObject;

        internal void Init(float scale, Color color)
        {
            _color = color;

            _band = new Surface("Corridor", transform, BossFXLibrary.QuadXZ, TelegraphMat);
            _band.Set(BossFXLibrary.PShape, (float)(int)BossShape.Line)
                 .Set(BossFXLibrary.PFillMode, (float)(int)BossFillMode.Linear) // 뒤 → 앞으로 차오른다
                 .Set(BossFXLibrary.PColorBase, color)
                 .Set(BossFXLibrary.PColorHot, Color.Lerp(color, Color.white, 0.35f))
                 .Set(BossFXLibrary.PColorEdge, Color.Lerp(color, Color.white, 0.55f))
                 .Set(BossFXLibrary.PConeDirection, 0f)
                 .Apply();
        }

        /// <summary>from→to 경로를 반폭 halfWidth로 그린다. alpha 0~1.</summary>
        public void Show(Vector3 from, Vector3 to, float halfWidth, float alpha)
        {
            alpha = Mathf.Clamp01(alpha);
            Vector3 dir = to - from;
            dir.y = 0f;
            float len = dir.magnitude;
            if (len < 1e-5f) { Hide(); return; }
            dir /= len;

            _band.Shown = true;

            // Line 도형은 판 전체를 길이로 쓴다 — 중심을 앞으로 절반 밀어 시작점을 발밑에 맞춘다
            _band.T.position = from + dir * (len * 0.5f);
            _band.T.rotation = Quaternion.LookRotation(dir);
            _band.T.localScale = new Vector3(len, 1f, len);

            // 맥동: 돌진이 임박할수록 빨라진다(바닥값을 높여 늘 또렷하게 보이도록)
            float pulse = 0.78f + 0.22f * Mathf.Sin(Time.time * Mathf.Lerp(5f, 16f, alpha) * Mathf.PI * 2f);

            _band.Set(BossFXLibrary.PLineWidth, Mathf.Clamp(halfWidth * 2f / len, 0.001f, 1f))
                 .Set(BossFXLibrary.PFill, alpha)          // 차오르는 정도 = 준비된 정도
                 .Set(BossFXLibrary.PIntensity, Mathf.Lerp(0.8f, 1.6f, alpha) * pulse)
                 .Set(BossFXLibrary.POpacity, alpha)
                 .Apply();
        }

        public void Hide()
        {
            if (_band != null) _band.Shown = false;
        }
    }

    /// <summary>텔레포트 번쩍임(재사용형).</summary>
    public class Flash
    {
        private readonly GameObject _root;
        private readonly GunFx.LightPulse _light;
        private readonly float _scale;
        private readonly Color _color, _hot;

        public GameObject Root => _root;

        internal Flash(GameObject root, GunFx.LightPulse light, float scale, Color color)
        {
            _root = root;
            _light = light;
            _scale = scale;
            _color = color;
            _hot = Color.Lerp(color, Color.white, 0.25f);
        }

        public void Spawn(Vector3 pos)
        {
            if (_root == null) return;
            _root.transform.position = pos;
            if (_light != null) _light.Pulse();

            // 방사형 섬광 — 사라지거나 나타나는 그 자리에서 터진다
            BossImpactFX.Spawn(new BossImpactSettings
            {
                mode = BossRadialMode.Burst,
                radius = 1.6f * _scale,
                duration = 0.22f,
                falloff = 2.0f,
                coreColor = Color.Lerp(_hot, Color.white, 0.25f),
                edgeColor = _color,
                intensity = 4.5f,
                flatOnGround = false,
            }, pos);

            // 퍼지는 링 — 공간이 밀려나는 잔재
            BossImpactFX.Spawn(new BossImpactSettings
            {
                mode = BossRadialMode.Ring,
                radius = 2.6f * _scale,
                duration = 0.34f,
                thickness = 0.10f,
                falloff = 2.4f,
                coreColor = _hot,
                edgeColor = _color,
                intensity = 3.0f,
                flatOnGround = true,
                groundOffset = 0f,
            }, pos);
        }
    }

    /// <summary>
    /// 포효 오라. <see cref="SetPower"/>를 매 프레임 올려 주면 발밑 장판이 차오르고
    /// 그 위로 광선 기둥이 굵어진다. <see cref="Burst"/>로 해방 순간을 터뜨린다.
    /// </summary>
    public class RoarAura : MonoBehaviour
    {
        private const float DiscRadius = 2.6f;   // 발밑 장판 반지름(사람 기준 m)
        private const float ColumnHeight = 6.0f; // 솟구치는 기둥 높이
        private const float Peak = 7f;           // 광원 최대 세기

        private Surface _disc, _column;
        private Light _light;
        private Camera _cam;
        private Color _color, _hot;
        private float _scale, _power;

        public GameObject Root => gameObject;

        internal void Init(float scale, Color color)
        {
            _scale = scale;
            _color = color;
            _hot = Color.Lerp(color, Color.white, 0.35f);

            // 발밑 장판: 중심에서 바깥으로 차오른다 = 기가 차오르는 게 눈에 보인다
            _disc = new Surface("AuraDisc", transform, BossFXLibrary.QuadXZ, TelegraphMat);
            _disc.Set(BossFXLibrary.PShape, (float)(int)BossShape.Circle)
                 .Set(BossFXLibrary.PFillMode, (float)(int)BossFillMode.Radial)
                 .Set(BossFXLibrary.PColorBase, color)
                 .Set(BossFXLibrary.PColorHot, _hot)
                 .Set(BossFXLibrary.PColorEdge, Color.Lerp(color, Color.white, 0.5f))
                 .Apply();

            // 기둥: 발밑에서 위로 뻗는 광선 한 줄
            _column = new Surface("AuraColumn", transform, BossFXLibrary.QuadForward, BeamMat);
            _column.Set(BossFXLibrary.PColorCore, Color.Lerp(color, Color.white, 0.45f))
                   .Set(BossFXLibrary.PColorGlow, color)
                   .Set(BossFXLibrary.PFire, 1f)
                   .Apply();

            // 오라 광원 — 어두운 맵에서 보스 자신을 비춰 주는 역할도 한다
            _light = gameObject.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = _hot;
            _light.range = 10f * scale;
            _light.intensity = 0f;
            _light.shadows = LightShadows.None;

            SetPower(0f);
        }

        /// <summary>기의 세기 0~1. 매 프레임 넣는다.</summary>
        public void SetPower(float power)
        {
            _power = Mathf.Clamp01(power);
            bool on = _power > 0.001f;

            _disc.Shown = on;
            _column.Shown = on;
            _light.enabled = on;
            if (!on) { _light.intensity = 0f; return; }

            // 광원은 제곱으로 올려 후반에 확 밝아지게 한다(차오르는 느낌)
            _light.intensity = Peak * _power * _power;
        }

        /// <summary>해방하는 순간의 충격파.</summary>
        public void Burst()
        {
            _light.intensity = Peak * 1.6f;

            BossImpactFX.Spawn(new BossImpactSettings
            {
                mode = BossRadialMode.Ring,
                radius = DiscRadius * 2.4f * _scale,
                duration = 0.5f,
                thickness = 0.13f,
                falloff = 2.2f,
                coreColor = Color.Lerp(_hot, Color.white, 0.2f),
                edgeColor = _color,
                intensity = 4.0f,
                flatOnGround = true,
                groundOffset = 0.02f * _scale,
            }, transform.position);

            BossImpactFX.Spawn(new BossImpactSettings
            {
                mode = BossRadialMode.Burst,
                radius = 2.2f * _scale,
                duration = 0.32f,
                falloff = 2.0f,
                coreColor = Color.white,
                edgeColor = _color,
                intensity = 4.0f,
                flatOnGround = false,
            }, transform.position + Vector3.up * (0.9f * _scale));
        }

        private void LateUpdate()
        {
            if (_power <= 0.001f) return;
            if (_cam == null) _cam = Camera.main;

            // 맥동 — 힘이 찰수록 빨라진다
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * Mathf.Lerp(3f, 12f, _power) * Mathf.PI * 2f);

            // --- 발밑 장판 ---
            float span = DiscRadius * 2f * _scale;
            _disc.T.position = transform.position + Vector3.up * (0.02f * _scale);
            _disc.T.rotation = Quaternion.identity;
            _disc.T.localScale = new Vector3(span, 1f, span);
            _disc.Set(BossFXLibrary.PFill, _power)
                 .Set(BossFXLibrary.PIntensity, Mathf.Lerp(0.7f, 1.8f, _power) * pulse)
                 .Set(BossFXLibrary.POpacity, Mathf.SmoothStep(0f, 1f, _power))
                 .Apply();

            // --- 광선 기둥: 발밑에서 위로 ---
            float h = ColumnHeight * _scale * Mathf.Lerp(0.35f, 1f, _power);
            Vector3 foot = transform.position;
            PlaceBeamQuad(_column.T, foot, foot + Vector3.up * h, 2.4f * _scale, _cam);
            _column.Set(BossFXLibrary.PCharge, Mathf.Lerp(0.15f, 0.9f, _power))
                   .Set(BossFXLibrary.PIntensity, Mathf.Lerp(1.5f, 4.0f, _power) * pulse)
                   .Set(BossFXLibrary.POpacity, Mathf.SmoothStep(0f, 1f, _power))
                   .Apply();
        }
    }

    /// <summary>
    /// 할퀸 자국. 손끝 트레일(<see cref="ClawTrail"/>)이 "팔이 지나간 길"이라면,
    /// 이쪽은 그 순간 허공에 <b>새겨지는 발톱 자국</b>이다 — 평행한 호 세 줄이 한 번에 나타났다가
    /// 빠르게 사라져 "베였다"는 인상을 만든다.
    ///
    /// 호 하나하나가 Beam 셰이더를 입은 리본 메시라, 가운데에 흰 심이 서고
    /// 바깥으로 발광이 번지며, 흐르는 노이즈가 얹혀 에너지 자국처럼 보인다.
    /// </summary>
    public class ClawSlash : MonoBehaviour
    {
        private const int Streaks = 3;   // 발톱 자국 수
        private const int Steps = 20;    // 호를 몇 점으로 그릴지
        private const float Life = 0.26f;

        private Surface[] _ribbons;
        private Mesh[] _meshes;
        private Vector3[] _verts;
        private float _scale, _life;

        public GameObject Root => gameObject;

        internal void Init(float scale, Color color)
        {
            _scale = scale;

            _ribbons = new Surface[Streaks];
            _meshes = new Mesh[Streaks];
            _verts = new Vector3[Steps * 2];

            Color hot = Color.Lerp(color, Color.white, 0.35f);

            for (int i = 0; i < Streaks; i++)
            {
                _meshes[i] = BuildRibbonMesh();
                _ribbons[i] = new Surface($"Streak{i}", transform, _meshes[i], BeamMat);
                _ribbons[i]
                    .Set(BossFXLibrary.PColorCore, hot)
                    .Set(BossFXLibrary.PColorGlow, color)
                    .Set(BossFXLibrary.PCharge, 1f)
                    .Set(BossFXLibrary.PFire, 1f)
                    // 굵기는 리본 메시가 이미 sin 곡선으로 재우므로 셰이더 taper 는 끈다
                    // (켜 두면 호의 시작과 끝이 비대칭이 된다)
                    .Set(PHeadTaper, 0f)
                    .Apply();
                // 리본은 이미 월드 좌표로 정점을 넣으므로 변환을 걸지 않는다
                _ribbons[i].T.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                _ribbons[i].T.localScale = Vector3.one;
            }
        }

        /// <summary>UV.x = 호를 따라(0→1), UV.y = 두께 방향(0/1). Beam 셰이더가 기대하는 배치.</summary>
        private static Mesh BuildRibbonMesh()
        {
            var mesh = new Mesh { name = "BossClawStreak" };
            mesh.MarkDynamic();

            var uv = new Vector2[Steps * 2];
            var tris = new int[(Steps - 1) * 6];
            for (int i = 0; i < Steps; i++)
            {
                float t = i / (float)(Steps - 1);
                uv[i * 2] = new Vector2(t, 0f);
                uv[i * 2 + 1] = new Vector2(t, 1f);
            }
            for (int i = 0; i < Steps - 1; i++)
            {
                int v = i * 2, o = i * 6;
                tris[o] = v; tris[o + 1] = v + 1; tris[o + 2] = v + 2;
                tris[o + 3] = v + 1; tris[o + 4] = v + 3; tris[o + 5] = v + 2;
            }

            mesh.vertices = new Vector3[Steps * 2];
            mesh.uv = uv;
            mesh.triangles = tris;
            return mesh;
        }

        /// <summary>
        /// center를 중심으로 fromDir → toDir 로 훑는 호를 그린다.
        /// radius는 발톱이 지나간 반지름(보스 사거리에 맞추면 판정과 눈이 일치한다).
        /// </summary>
        public void Play(Vector3 center, Vector3 fromDir, Vector3 toDir, float radius)
        {
            if (_ribbons == null) return;
            if (fromDir.sqrMagnitude < 1e-6f || toDir.sqrMagnitude < 1e-6f) return;

            fromDir.Normalize();
            toDir.Normalize();

            // 스윙 평면의 법선 — 이 방향으로 세 줄을 어긋내면 발톱 간격이 되고,
            // 리본의 두께도 이 방향으로 낸다(호가 평면 안에서 납작해 보이지 않게).
            Vector3 normal = Vector3.Cross(fromDir, toDir);
            if (normal.sqrMagnitude < 1e-6f) normal = Vector3.up;
            normal.Normalize();

            float spacing = 0.12f * radius;
            float half = 0.22f * _scale;   // 리본 반폭 — 셰이더가 이 안에 심과 발광을 그린다

            for (int s = 0; s < Streaks; s++)
            {
                Vector3 offset = normal * ((s - (Streaks - 1) * 0.5f) * spacing);
                // 줄마다 반지름을 살짝 달리해 자국이 완전히 평행하지 않게(손가락 길이 차이)
                float r = radius * (1f - s * 0.06f);

                for (int i = 0; i < Steps; i++)
                {
                    float t = i / (float)(Steps - 1);
                    Vector3 dir = Vector3.Slerp(fromDir, toDir, t).normalized;
                    Vector3 p = center + dir * r + offset;

                    // 가운데가 두껍고 양 끝이 뾰족한 자국 — 베고 지나간 형태
                    float w = half * Mathf.Sin(t * Mathf.PI);
                    _verts[i * 2] = p - normal * w;
                    _verts[i * 2 + 1] = p + normal * w;
                }

                _meshes[s].vertices = _verts;
                _ribbons[s].Shown = true;
            }
            _life = Life;
        }

        public void Hide()
        {
            _life = 0f;
            if (_ribbons == null) return;
            foreach (var r in _ribbons) if (r != null) r.Shown = false;
        }

        private void LateUpdate()
        {
            if (_life <= 0f) return;

            _life -= Time.deltaTime;
            float k = Mathf.Clamp01(_life / Life);
            if (k <= 0f) { Hide(); return; }

            // 빠르게 옅어지며 가늘어진다 — 자국이 허공에서 지워지는 느낌
            foreach (var r in _ribbons)
            {
                if (r == null) continue;
                r.Set(BossFXLibrary.PCharge, Mathf.Lerp(0.3f, 1f, k))
                 .Set(BossFXLibrary.PIntensity, 3.2f * k)
                 .Set(BossFXLibrary.POpacity, k * k)
                 .Apply();
            }
        }

        private void OnDestroy()
        {
            if (_meshes == null) return;
            foreach (var m in _meshes) if (m != null) Destroy(m);
        }
    }

    /// <summary>손가락 끝 궤적(할퀴기). 뼈에 붙는 트레일이라 셰이더 대응물이 없어 그대로 둔다.</summary>
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
}
