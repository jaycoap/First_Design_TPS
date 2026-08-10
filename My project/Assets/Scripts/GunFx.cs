using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 총기 이펙트(총구 화염/탄착/트레이서)를 에셋 없이 코드로 생성하는 팩토리.
/// - URP 파티클 셰이더를 우선 사용하고, 없으면 빌트인/Sprites로 폴백
/// - 소프트 글로우 텍스처를 프로시저럴로 생성(가산 블렌딩으로 빛나는 느낌)
/// - 모든 크기/속도는 사람 기준(1.8m) 수치 × scale 배율로 통일
///   (이 프로젝트 캐릭터는 약 0.2m라 scale ≈ 0.11 → 미니 월드에 맞는 이펙트 크기)
/// </summary>
public static class GunFx
{
    private static Texture2D _glowTex;
    private static Material _addMat;    // 가산(레이저 빔·섬광·플라즈마 전부 발광체라 가산만 쓴다)

    // ---------- 공개 API ----------

    /// <summary>
    /// 레이저 총구 방출 FX(에너지 섬광 + 플라즈마 입자 + 확산 링 + 광원).
    /// 재사용형 — Fire(pos, dir)로 발동. 화약 연출(연기)은 쓰지 않는다.
    /// </summary>
    public static MuzzleFx BuildMuzzleFlash(Transform parent, float scale, Color color)
    {
        Color hot = HotCore(color); // 중심부는 흰빛에 가깝게 → 과열된 에너지 느낌

        // 코어: 총구에서 터지는 에너지 섬광
        ParticleSystem core = NewSystem("LaserMuzzleFX", parent, AdditiveMat);
        var main = core.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.05f, 0.08f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.3f * scale, 0.5f * scale);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = hot;
        FadeOut(core, hot, color);

        // 플라즈마 입자: 총구 앞으로 빠르게 뻗는 에너지 조각(중력 없음 = 무게감 없는 빛)
        ParticleSystem sparks = NewSystem("Plasma", core.transform, AdditiveMat);
        var sMain = sparks.main;
        sMain.startLifetime = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
        sMain.startSpeed = new ParticleSystem.MinMaxCurve(10f * scale, 18f * scale);
        sMain.startSize = new ParticleSystem.MinMaxCurve(0.015f * scale, 0.035f * scale);
        sMain.startColor = hot;
        SetCone(sparks, 12f, 0.015f * scale); // 레이저답게 좁은 원뿔
        Stretch(sparks, 8f);
        FadeOut(sparks, Color.white, color);

        // 확산 링: 총구에서 옆으로 퍼지는 얇은 에너지 파문
        ParticleSystem ring = NewSystem("EnergyRing", core.transform, AdditiveMat);
        var rMain = ring.main;
        rMain.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.18f);
        rMain.startSpeed = new ParticleSystem.MinMaxCurve(1.5f * scale, 2.5f * scale);
        rMain.startSize = new ParticleSystem.MinMaxCurve(0.06f * scale, 0.1f * scale);
        rMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        rMain.startColor = color;
        SetCone(ring, 88f, 0.01f * scale); // 거의 평면으로 퍼짐
        Grow(ring, 2.2f);
        FadeOut(ring, color, new Color(color.r, color.g, color.b, 0f));

        // 총구 점광원: 발사 순간 레이저 색으로 확 켜졌다 꺼짐
        var light = core.gameObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = 3f * scale;
        light.intensity = 0f;
        var pulse = core.gameObject.AddComponent<LightPulse>();
        pulse.Init(light, peakIntensity: 4.5f, decayTime: 0.08f);

        return new MuzzleFx(core, sparks, ring, pulse);
    }

    /// <summary>
    /// 레이저 탄착 FX(에너지 작렬 + 튀는 플라즈마 + 확산 링).
    /// 하나를 만들어 재사용한다 — Spawn(pos, normal)로 발동. 먼지/파편은 쓰지 않는다.
    /// </summary>
    public static ImpactFx BuildImpact(float scale, Color color)
    {
        Color hot = HotCore(color);

        // 작렬: 맞은 자리가 하얗게 타는 순간
        ParticleSystem flash = NewSystem("LaserImpactFX", null, AdditiveMat);
        var fMain = flash.main;
        fMain.startLifetime = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
        fMain.startSpeed = 0f;
        fMain.startSize = new ParticleSystem.MinMaxCurve(0.12f * scale, 0.2f * scale);
        fMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        fMain.startColor = hot;
        FadeOut(flash, hot, color);

        // 튀는 플라즈마: 중력을 거의 받지 않고 빠르게 사라지는 빛 조각
        ParticleSystem sparks = NewSystem("Plasma", flash.transform, AdditiveMat);
        var sMain = sparks.main;
        sMain.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
        sMain.startSpeed = new ParticleSystem.MinMaxCurve(3f * scale, 7f * scale);
        sMain.startSize = new ParticleSystem.MinMaxCurve(0.012f * scale, 0.025f * scale);
        sMain.startColor = hot;
        sMain.gravityModifier = scale * 0.15f; // 거의 무중력 — 파편이 아니라 에너지
        SetCone(sparks, 55f, 0.01f * scale);
        Stretch(sparks, 6f);
        FadeOut(sparks, Color.white, color);

        // 충격 링: 표면을 따라 퍼지는 에너지 파문
        ParticleSystem ring = NewSystem("EnergyRing", flash.transform, AdditiveMat);
        var rMain = ring.main;
        rMain.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.25f);
        rMain.startSpeed = new ParticleSystem.MinMaxCurve(1.2f * scale, 2f * scale);
        rMain.startSize = new ParticleSystem.MinMaxCurve(0.05f * scale, 0.09f * scale);
        rMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        rMain.startColor = color;
        SetCone(ring, 88f, 0.01f * scale);
        Grow(ring, 2.5f);
        FadeOut(ring, color, new Color(color.r, color.g, color.b, 0f));

        return new ImpactFx(flash, sparks, ring);
    }

    /// <summary>레이저 코어용: 채도를 낮춰 흰빛에 가깝게(과열된 중심부 표현).</summary>
    private static Color HotCore(Color c) => Color.Lerp(c, Color.white, 0.65f);

    /// <summary>트레이서용 가산 글로우 머티리얼.</summary>
    public static Material MakeTracerMaterial() => AdditiveMat;

    /// <summary>재사용형 총구 화염 핸들. 위치/방향을 잡고 즉시 방출한다.</summary>
    public class MuzzleFx
    {
        private readonly ParticleSystem _core, _sparks, _ring;
        private readonly LightPulse _light;
        public GameObject Root => _core != null ? _core.gameObject : null;

        internal MuzzleFx(ParticleSystem core, ParticleSystem sparks, ParticleSystem ring, LightPulse light)
        {
            _core = core; _sparks = sparks; _ring = ring; _light = light;
        }

        public void Fire(Vector3 pos, Vector3 dir)
        {
            if (_core == null) return;
            _core.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir));
            // Play/Stop 상태와 무관하게 항상 터지도록 직접 방출(연사 대응)
            _core.Emit(2);
            _sparks.Emit(8);
            _ring.Emit(1);
            if (_light != null) _light.Pulse();
        }
    }

    /// <summary>재사용형 탄착 FX 핸들. 위치/방향을 잡고 즉시 방출한다.</summary>
    public class ImpactFx
    {
        private readonly ParticleSystem _flash, _sparks, _ring;
        public GameObject Root => _flash != null ? _flash.gameObject : null;

        internal ImpactFx(ParticleSystem flash, ParticleSystem sparks, ParticleSystem ring)
        {
            _flash = flash; _sparks = sparks; _ring = ring;
        }

        public void Spawn(Vector3 pos, Vector3 normal)
        {
            if (_flash == null) return;
            // 월드 시뮬레이션이라 루트를 옮겨도 이미 방출된 입자는 제자리에 남는다
            _flash.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(normal));
            _flash.Emit(1);
            _sparks.Emit(10);
            _ring.Emit(1);
        }
    }

    /// <summary>발사 순간 점광원을 확 켰다가 빠르게 감쇠시키는 컴포넌트.</summary>
    public class LightPulse : MonoBehaviour
    {
        private Light _light;
        private float _peak, _decayTime, _timer;

        public void Init(Light light, float peakIntensity, float decayTime)
        {
            _light = light; _peak = peakIntensity; _decayTime = decayTime;
            enabled = false;
        }

        public void Pulse() { _timer = _decayTime; enabled = true; }

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer <= 0f) { _light.intensity = 0f; enabled = false; return; }
            _light.intensity = _peak * (_timer / _decayTime);
        }
    }

    // ---------- 내부 헬퍼 ----------

    /// <summary>버스트형 파티클 시스템 공통 골격(월드 시뮬레이션, 수동 재생).</summary>
    private static ParticleSystem NewSystem(string name, Transform parent, Material mat)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();

        // AddComponent 직후엔 기본 playOnAwake=true로 이미 재생 중이라
        // duration 등 변경이 막힌다 → 완전히 정지시킨 뒤 설정한다.
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.2f;
        // 월드 시뮬레이션: FX 오브젝트를 다음 발사 위치로 옮겨도 기존 입자가 따라오지 않음
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 128;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.enabled = false;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = mat;
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

    /// <summary>속도 방향으로 입자를 늘리는 렌더 모드(스파크 궤적 느낌).</summary>
    private static void Stretch(ParticleSystem ps, float lengthScale)
    {
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = lengthScale;
    }

    /// <summary>수명에 따라 색을 바꾸며 알파를 0으로 페이드.</summary>
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

    /// <summary>수명 동안 크기를 키움(연기/먼지 퍼짐).</summary>
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

    // ---------- 머티리얼/텍스처 ----------

    private static Material AdditiveMat => _addMat != null ? _addMat : _addMat = MakeMat(additive: true);

    private static Material MakeMat(bool additive)
    {
        Shader sh = null;
        if (GraphicsSettings.currentRenderPipeline != null)
            sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null)
            sh = Shader.Find(additive ? "Legacy Shaders/Particles/Additive"
                                      : "Legacy Shaders/Particles/Alpha Blended");
        if (sh == null) sh = Shader.Find("Sprites/Default");

        var m = new Material(sh);
        Texture2D tex = GlowTex;
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);

        // URP Particles/Unlit: 런타임에 투명/블렌드 상태를 직접 지정
        if (m.HasProperty("_Surface"))
        {
            m.SetFloat("_Surface", 1f);                       // Transparent
            m.SetFloat("_Blend", additive ? 2f : 0f);         // Additive / Alpha
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", additive ? (int)BlendMode.One : (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
        }
        return m;
    }

    private static Texture2D GlowTex
    {
        get
        {
            if (_glowTex != null) return _glowTex;
            _glowTex = MakeRadialGlow(64);
            return _glowTex;
        }
    }

    /// <summary>중심이 밝고 가장자리로 부드럽게 사라지는 원형 글로우 텍스처.</summary>
    private static Texture2D MakeRadialGlow(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };
        float half = (size - 1) * 0.5f;
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                float a = Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f);   // 부드러운 감쇠
                a += Mathf.Pow(Mathf.Clamp01(1f - d * 2.5f), 2f) * 0.6f; // 밝은 코어
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }
}
