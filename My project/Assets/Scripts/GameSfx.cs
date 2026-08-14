using UnityEngine;

/// <summary>게임 효과음 종류. 추가할 때 GameSfx.Build/DefaultVolume에도 같이 정의한다.</summary>
public enum Sfx
{
    PlayerFire,     // 플레이어 레이저 발사
    Impact,         // 탄착
    CritImpact,     // 약점(머리) 탄착
    Reload,         // 재장전
    GhostFire,      // 과거의 나(협공) 발사
    TimeRewind,     // 시간역행 발동
    TimeSupport,    // 협공 발동
    PlayerHurt,     // 플레이어 피격
    BossCharge,     // 보스 레이저 충전
    BossLaser,      // 보스 레이저 발사
    BossSwing,      // 보스 할퀴기
    BossTeleport,   // 보스 텔레포트
    MeteorImpact,   // 운석 착탄
    BossDeath,      // 보스 사망
}

/// <summary>
/// <b>임시</b> 효과음 세트 — 오디오 에셋 없이 파형을 코드로 합성해서 쓴다.
/// 이 프로젝트는 이펙트·HUD도 전부 코드로 만들므로 사운드만 에셋에 의존하지 않도록 맞췄다.
///
/// 진짜 사운드로 교체하는 방법(코드 수정 없이):
///  1) 씬의 아무 오브젝트에 <see cref="GameSfxOverrides"/>를 붙인다
///  2) 목록에 종류 + AudioClip을 넣으면 그 종류만 진짜 클립으로 재생된다
/// 전체 음량은 <see cref="MasterVolume"/>으로 한 번에 조절한다.
///
/// 재생은 16개짜리 보이스 풀을 돌려쓴다(연사해도 GameObject를 새로 만들지 않는다).
/// </summary>
public static class GameSfx
{
    /// <summary>전체 음량 배수(0~1). 임시 사운드가 시끄러우면 이 값을 낮춘다.</summary>
    public static float MasterVolume { get; set; } = 0.8f;

    private const int Rate = 44100;
    private const int PoolSize = 16;

    private static readonly AudioClip[] _clips = new AudioClip[System.Enum.GetValues(typeof(Sfx)).Length];
    private static GameObject _root;
    private static AudioSource[] _sources;
    private static int _next;

    /// <summary>합성음 대신 쓸 진짜 클립을 지정한다(null이면 합성음으로 되돌아간다).</summary>
    public static void Override(Sfx id, AudioClip clip) => _clips[(int)id] = clip;

    /// <summary>화면(플레이어) 기준 2D 재생 — 총소리·재장전·시간 능력처럼 '내 소리'에 쓴다.</summary>
    public static void Play(Sfx id, float volume = 1f, float pitch = 1f)
        => PlayInternal(id, Vector3.zero, false, volume, pitch);

    /// <summary>월드 위치에서 나는 3D 재생 — 탄착·보스 공격처럼 '저기서 나는 소리'에 쓴다.</summary>
    public static void PlayAt(Sfx id, Vector3 position, float volume = 1f, float pitch = 1f)
        => PlayInternal(id, position, true, volume, pitch);

    private static void PlayInternal(Sfx id, Vector3 position, bool spatial, float volume, float pitch)
    {
        AudioClip clip = Clip(id);
        if (clip == null) return;

        AudioSource src = NextSource();
        if (src == null) return;

        src.transform.position = position;
        src.spatialBlend = spatial ? 1f : 0f;
        src.clip = clip;
        src.pitch = pitch;
        src.volume = Mathf.Clamp01(volume * DefaultVolume(id) * MasterVolume);
        src.Play();
    }

    private static AudioClip Clip(Sfx id)
    {
        int i = (int)id;
        if (_clips[i] == null) _clips[i] = Build(id); // 처음 쓸 때만 합성한다
        return _clips[i];
    }

    private static AudioSource NextSource()
    {
        EnsureRoot();
        AudioSource src = _sources[_next];
        _next = (_next + 1) % _sources.Length;
        return src;
    }

    private static void EnsureRoot()
    {
        if (_root != null) return;

        _root = new GameObject("GameSfx");
        Object.DontDestroyOnLoad(_root);

        _sources = new AudioSource[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            var go = new GameObject($"Voice{i:00}");
            go.transform.SetParent(_root.transform, false);

            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.rolloffMode = AudioRolloffMode.Linear;
            // 이 프로젝트는 모델이 작아(보스 키가 월드 0.3 남짓) 거리 단위도 작다.
            // 기본값(1~500)을 쓰면 감쇠가 전혀 없어 위치감이 사라진다.
            src.minDistance = 1f;
            src.maxDistance = 40f;
            _sources[i] = src;
        }
    }

    /// <summary>종류별 기본 음량 — 자주 울리는 소리일수록 작게 잡아 두었다.</summary>
    private static float DefaultVolume(Sfx id)
    {
        switch (id)
        {
            case Sfx.PlayerFire: return 0.22f;   // 초당 10발이라 가장 작게
            case Sfx.GhostFire: return 0.18f;
            case Sfx.Impact: return 0.30f;
            case Sfx.CritImpact: return 0.55f;
            case Sfx.Reload: return 0.45f;
            case Sfx.TimeRewind: return 0.60f;
            case Sfx.TimeSupport: return 0.55f;
            case Sfx.PlayerHurt: return 0.70f;
            case Sfx.BossCharge: return 0.45f;
            case Sfx.BossLaser: return 0.75f;
            case Sfx.BossSwing: return 0.55f;
            case Sfx.BossTeleport: return 0.60f;
            case Sfx.MeteorImpact: return 0.70f;
            case Sfx.BossDeath: return 0.85f;
            default: return 0.5f;
        }
    }

    // ---------- 파형 합성 ----------

    private static AudioClip Build(Sfx id)
    {
        switch (id)
        {
            // 레이저: 높은 데서 뚝 떨어지는 짧은 스윕
            case Sfx.PlayerFire: return Sweep("SfxFire", 0.13f, 2400f, 520f, 30f, saw: 0.35f, noise: 0.08f);
            case Sfx.GhostFire: return Sweep("SfxGhostFire", 0.16f, 1500f, 300f, 26f, saw: 0.20f, noise: 0.05f);

            // 탄착: 짧은 노이즈 + 저음. 약점은 맑은 종소리로 확실히 구분된다.
            case Sfx.Impact: return NoiseHit("SfxImpact", 0.09f, 45f, 0.35f, tone: 320f, toneMix: 0.35f);
            case Sfx.CritImpact: return Bell("SfxCrit", 0.30f, 1560f, 2340f, 13f);

            case Sfx.Reload: return Clicks("SfxReload", 0.34f, new[] { 0f, 0.16f }, 90f, 0.5f);

            // 시간 능력: 내려가는 아르페지오(역행) / 짧게 튀어 오르는 두 음(협공)
            case Sfx.TimeRewind:
                return Arpeggio("SfxRewind", new[] { 1046f, 880f, 698f, 523f, 392f }, 0.24f, 6f, 0.25f);
            case Sfx.TimeSupport:
                return Arpeggio("SfxSupport", new[] { 784f, 1174f }, 0.09f, 16f, 0.2f);

            case Sfx.PlayerHurt: return NoiseHit("SfxHurt", 0.25f, 16f, 0.12f, tone: 90f, toneMix: 0.5f);

            // 보스: 차오르는 충전 → 무겁게 떨어지는 발사
            case Sfx.BossCharge: return Sweep("SfxCharge", 1.0f, 120f, 900f, 0f, saw: 0.5f, swell: true);
            case Sfx.BossLaser: return Sweep("SfxBossLaser", 0.55f, 900f, 90f, 7f, saw: 0.6f, noise: 0.15f);
            case Sfx.BossSwing: return Whoosh("SfxSwing", 0.32f, 0.05f, 0.40f);
            case Sfx.BossTeleport: return Sweep("SfxTeleport", 0.45f, 220f, 1600f, 6f, saw: 0.3f, noise: 0.1f);
            case Sfx.MeteorImpact: return NoiseHit("SfxMeteor", 0.5f, 8f, 0.08f, tone: 60f, toneMix: 0.6f);
            case Sfx.BossDeath: return Sweep("SfxBossDeath", 1.4f, 300f, 60f, 2.2f, saw: 0.55f, noise: 0.2f);

            default: return null;
        }
    }

    /// <summary>
    /// 주파수가 변하는 톤. 위상을 누적해야(sin(2πft)가 아니라) 스윕이 의도한 속도로 들린다.
    /// swell=true면 소리가 점점 커진다(충전음).
    /// </summary>
    private static AudioClip Sweep(string name, float len, float from, float to, float decay,
                                   float saw = 0f, float noise = 0f, bool swell = false)
    {
        int n = Samples(len);
        var data = new float[n];
        float phase = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float u = t / len;
            phase += Mathf.Lerp(from, to, u) / Rate;
            if (phase > 1f) phase -= 1f;

            float s = Mathf.Sin(phase * Mathf.PI * 2f);
            if (saw > 0f) s = Mathf.Lerp(s, phase * 2f - 1f, saw);
            if (noise > 0f) s = Mathf.Lerp(s, Random.Range(-1f, 1f), noise);

            float env = swell ? u * u : Mathf.Exp(-t * decay);
            data[i] = s * env * Attack(t);
        }
        return Make(name, data);
    }

    /// <summary>두 배음이 겹친 맑은 종소리(약점 명중).</summary>
    private static AudioClip Bell(string name, float len, float f0, float f1, float decay)
    {
        int n = Samples(len);
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float s = Mathf.Sin(t * f0 * Mathf.PI * 2f) * 0.7f
                    + Mathf.Sin(t * f1 * Mathf.PI * 2f) * 0.3f;
            // 맨 앞에 아주 짧은 노이즈를 섞어 '맞은 순간'의 타격감을 준다
            s = Mathf.Lerp(s, Random.Range(-1f, 1f), Mathf.Exp(-t * 400f) * 0.6f);
            data[i] = s * Mathf.Exp(-t * decay) * Attack(t);
        }
        return Make(name, data);
    }

    /// <summary>노이즈 타격음(저역 통과) + 선택적 저음 톤.</summary>
    private static AudioClip NoiseHit(string name, float len, float decay, float lowpass,
                                      float tone = 0f, float toneMix = 0f)
    {
        int n = Samples(len);
        var data = new float[n];
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            lp = Mathf.Lerp(lp, Random.Range(-1f, 1f), lowpass);
            float s = lp;
            if (tone > 0f) s = Mathf.Lerp(s, Mathf.Sin(t * tone * Mathf.PI * 2f), toneMix);
            data[i] = s * Mathf.Exp(-t * decay) * Attack(t);
        }
        return Make(name, data);
    }

    /// <summary>지정한 시각마다 딸깍(재장전).</summary>
    private static AudioClip Clicks(string name, float len, float[] at, float decay, float lowpass)
    {
        int n = Samples(len);
        var data = new float[n];
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            lp = Mathf.Lerp(lp, Random.Range(-1f, 1f), lowpass);

            float env = 0f;
            foreach (float start in at)
                if (t >= start) env = Mathf.Max(env, Mathf.Exp(-(t - start) * decay));

            data[i] = lp * env;
        }
        return Make(name, data);
    }

    /// <summary>음을 차례로 이어 붙인다(시간 능력). 내려가면 역행, 올라가면 발동.</summary>
    private static AudioClip Arpeggio(string name, float[] steps, float stepLen, float decay, float overlap)
    {
        float len = stepLen * steps.Length + overlap;
        int n = Samples(len);
        var data = new float[n];

        for (int s = 0; s < steps.Length; s++)
        {
            int start = Samples(stepLen * s);
            int count = Samples(stepLen + overlap);
            for (int i = 0; i < count && start + i < n; i++)
            {
                float t = i / (float)Rate;
                float v = Mathf.Sin(t * steps[s] * Mathf.PI * 2f) * Mathf.Exp(-t * decay) * Attack(t);
                data[start + i] = Mathf.Clamp(data[start + i] + v * 0.7f, -1f, 1f);
            }
        }
        return Make(name, data);
    }

    /// <summary>휘두르는 바람소리 — 필터가 열렸다 닫히고 음량이 부풀었다 사라진다.</summary>
    private static AudioClip Whoosh(string name, float len, float lp0, float lp1)
    {
        int n = Samples(len);
        var data = new float[n];
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float u = i / (float)n;
            lp = Mathf.Lerp(lp, Random.Range(-1f, 1f), Mathf.Lerp(lp0, lp1, Mathf.Sin(u * Mathf.PI)));
            data[i] = lp * Mathf.Sin(u * Mathf.PI); // 지나가는 느낌: 가운데가 가장 크다
        }
        return Make(name, data);
    }

    /// <summary>맨 앞 1ms를 부드럽게 올려 '딱' 하는 클릭 노이즈를 없앤다.</summary>
    private static float Attack(float t) => Mathf.Clamp01(t * 1000f);

    private static int Samples(float seconds) => Mathf.Max(1, Mathf.RoundToInt(seconds * Rate));

    private static AudioClip Make(string name, float[] data)
    {
        var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
