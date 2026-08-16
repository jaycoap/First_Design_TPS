using UnityEngine;

/// <summary>게임 효과음 종류. 추가할 때 GameSfx.Build/DefaultVolume에도 같이 정의한다.</summary>
public enum Sfx
{
    PlayerFire,     // 플레이어 레이저 발사
    Impact,         // 탄착
    CritImpact,     // 약점(머리) 탄착
    Reload,         // 재장전
    GhostFire,      // 과거의 나(협공) 발사
    TimeSupport,    // 협공 발동
    PlayerHurt,     // 플레이어 피격
    BossCharge,     // 보스 레이저 충전
    BossLaser,      // 보스 레이저 발사
    BossSwing,      // 보스 할퀴기
    BossRush,       // 보스 돌진(할퀴기와 다른 소리 — 달려드는 바람)
    BossTeleport,   // 보스 텔레포트
    MeteorImpact,   // 운석 착탄
    BossRoar,       // 보스 등장(인트로 컷신)
    BossDeath,      // 보스 사망
}

/// <summary>
/// 효과음 세트. 종류마다 다음 순서로 소리를 정한다.
///  1) <see cref="GameSfxOverrides"/>로 씬에서 직접 지정한 클립
///  2) <c>Assets/Resources/Sounds/</c> 에 있는 진짜 음원(<see cref="ResourceName"/>의 이름으로 자동 로드)
///  3) 없으면 파형을 코드로 합성한 <b>임시</b> 사운드
/// 덕분에 음원을 구한 종류만 파일을 넣으면 되고, 나머지는 합성음으로 계속 굴러간다.
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
    private const string SoundFolder = "Sounds/";

    private static readonly AudioClip[] _clips = new AudioClip[System.Enum.GetValues(typeof(Sfx)).Length];
    private static GameObject _root;
    private static AudioSource[] _sources;
    private static VoiceLimiter[] _limiters;
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

        AudioSource src = NextSource(out VoiceLimiter limiter);
        if (src == null) return;

        src.transform.position = position;
        src.spatialBlend = spatial ? 1f : 0f;
        src.clip = clip;
        src.pitch = pitch;
        src.volume = Mathf.Clamp01(volume * DefaultVolume(id) * MasterVolume);

        // 쓸 구간이 지정돼 있으면 그 지점부터 재생하고, 끝나는 지점에서 끊는다
        PlayWindow(id, out float start, out float end);
        src.time = start > 0f ? Mathf.Clamp(start, 0f, Mathf.Max(0f, clip.length - 0.02f)) : 0f;
        src.Play();

        if (limiter != null) limiter.Limit(end > start ? end - start : 0f, src.volume);
    }

    private static AudioClip Clip(Sfx id)
    {
        int i = (int)id;
        // 진짜 음원이 있으면 그것을, 없으면 합성음을 쓴다(둘 다 처음 쓸 때 한 번만 준비한다)
        if (_clips[i] == null) _clips[i] = LoadReal(id) ?? Build(id);
        return _clips[i];
    }

    /// <summary>
    /// Resources/Sounds 에 넣어둔 진짜 음원 파일 이름(확장자 제외). null이면 합성음을 쓴다.
    /// 음원을 새로 구하면 파일을 그 폴더에 넣고 여기에 이름만 추가하면 된다.
    /// </summary>
    private static string ResourceName(Sfx id)
    {
        switch (id)
        {
            case Sfx.PlayerFire: return "sound-work-futuristic-gun-shot-sci-fi-217154";
            case Sfx.Impact: return "47313572-sci-fi-sfx-8-350836";
            case Sfx.CritImpact: return "daviddumaisaudio-sci-fi-whoosh-impact-9-variations-204491";
            case Sfx.BossLaser: return "rescopicsound-sci-fi-elements-impact-hit-small-extended-230524";
            case Sfx.BossTeleport: return "freesound_community-swoosh-19-100109";
            case Sfx.BossRush: return "freesound_community-swoosh-11-46749";
            case Sfx.BossRoar: return "dragon-studio-alien-sounds-463202";
            default: return null;
        }
    }

    /// <summary>
    /// 음원에서 실제로 쓸 구간(초). start=0, end=0이면 클립 전체를 그대로 쓴다.
    ///
    /// 여러 변형이 한 파일에 이어 붙은 팩은 앞의 한 발만 잘라 써야 하고(안 그러면 한 번
    /// 맞을 때마다 변형이 줄줄이 다 울린다), 파일 맨 앞의 무음/도입부를 건너뛰어야
    /// 타격 순간과 소리가 어긋나지 않는다.
    /// </summary>
    private static void PlayWindow(Sfx id, out float start, out float end)
    {
        switch (id)
        {
            // '9 variations' 팩 — 앞의 도입부를 건너뛰고 첫 한 발만 쓴다
            case Sfx.CritImpact: start = 0.25f; end = 1f; return;
            default: start = 0f; end = 0f; return;
        }
    }

    private static AudioClip LoadReal(Sfx id)
    {
        string name = ResourceName(id);
        if (string.IsNullOrEmpty(name)) return null;

        var clip = Resources.Load<AudioClip>(SoundFolder + name);
        if (clip == null)
            Debug.LogWarning($"[GameSfx] 'Assets/Resources/{SoundFolder}{name}' 음원을 찾지 못해 " +
                             $"{id}는 임시 합성음으로 재생합니다.");
        return clip;
    }

    private static AudioSource NextSource(out VoiceLimiter limiter)
    {
        EnsureRoot();
        int i = _next;
        _next = (_next + 1) % _sources.Length;
        limiter = _limiters[i];
        return _sources[i];
    }

    /// <summary>
    /// 보이스 하나를 정해진 시간 뒤에 멈춘다(긴 음원에서 앞부분만 쓸 때).
    /// 뚝 끊기면 '딱' 소리가 나므로 마지막 짧은 구간을 페이드해서 내린다.
    /// 보이스가 다음 소리에 재사용되면 Limit이 다시 불려 상태가 덮어써진다.
    /// </summary>
    private class VoiceLimiter : MonoBehaviour
    {
        private const float Fade = 0.08f;

        private AudioSource _src;
        private float _stopAt, _volume;

        internal void Bind(AudioSource src) => _src = src;

        /// <summary>seconds 뒤에 정지. 0 이하면 제한 없음(클립 끝까지 재생).</summary>
        internal void Limit(float seconds, float volume)
        {
            _volume = volume;
            _stopAt = seconds > 0f ? Time.unscaledTime + seconds : 0f;
        }

        private void Update()
        {
            if (_stopAt <= 0f || _src == null) return;

            float remain = _stopAt - Time.unscaledTime;
            if (remain > Fade) return;

            if (remain <= 0f)
            {
                _stopAt = 0f;
                _src.Stop();
                _src.volume = _volume;
                return;
            }
            _src.volume = _volume * (remain / Fade);
        }
    }

    private static void EnsureRoot()
    {
        if (_root != null) return;

        _root = new GameObject("GameSfx");
        Object.DontDestroyOnLoad(_root);

        _sources = new AudioSource[PoolSize];
        _limiters = new VoiceLimiter[PoolSize];
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

            var limiter = go.AddComponent<VoiceLimiter>();
            limiter.Bind(src);
            _limiters[i] = limiter;
        }
    }

    /// <summary>종류별 기본 음량 — 자주 울리는 소리일수록 작게 잡아 두었다.</summary>
    private static float DefaultVolume(Sfx id)
    {
        switch (id)
        {
            case Sfx.PlayerFire: return 0.32f;   // 초당 10발이라 작게 — 겹쳐 울려도 뭉치지 않을 만큼만
            case Sfx.GhostFire: return 0.18f;
            case Sfx.Impact: return 0.30f;
            case Sfx.CritImpact: return 0.55f;
            case Sfx.Reload: return 0.45f;
            case Sfx.TimeSupport: return 0.45f;  // 톱니는 사인보다 크게 들려 조금 낮춘다
            case Sfx.PlayerHurt: return 0.70f;
            case Sfx.BossCharge: return 0.45f;
            case Sfx.BossLaser: return 0.75f;
            case Sfx.BossSwing: return 0.55f;
            case Sfx.BossRush: return 0.75f;   // 달려드는 소리라 크게 — 등 뒤에서도 알아채야 한다
            case Sfx.BossTeleport: return 0.60f;
            case Sfx.MeteorImpact: return 0.70f;
            case Sfx.BossRoar: return 0.9f;   // 등장 연출 — 가장 크게
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
            // 협공: 음정이 또렷한 아르페지오는 장난감처럼 들려서 쓰지 않는다.
            // 살짝 어긋난 두 톱니의 맥놀이를 위로 훑어 '시간이 휘는' 소리를 만든다.
            // (시간역행은 무음이다 — 되감기 연출은 화면/음악 쪽에서만 처리한다)
            case Sfx.TimeSupport:
                return Warp("SfxSupport", 0.45f, 300f, 1400f, detune: 1.011f, noise: 0.14f, swellAt: 0.7f);

            case Sfx.PlayerHurt: return NoiseHit("SfxHurt", 0.25f, 16f, 0.12f, tone: 90f, toneMix: 0.5f);

            // 보스: 차오르는 충전 → 무겁게 떨어지는 발사
            case Sfx.BossCharge: return Sweep("SfxCharge", 1.0f, 120f, 900f, 0f, saw: 0.5f, swell: true);
            case Sfx.BossLaser: return Sweep("SfxBossLaser", 0.55f, 900f, 90f, 7f, saw: 0.6f, noise: 0.15f);
            case Sfx.BossSwing: return Whoosh("SfxSwing", 0.32f, 0.05f, 0.40f);
            case Sfx.BossRush: return Whoosh("SfxRush", 0.55f, 0.03f, 0.30f); // 더 길고 묵직한 바람
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

    /// <summary>
    /// 시간이 휘는 소리. 살짝 어긋난 톱니 두 개를 겹쳐 맥놀이(금속성 울림)를 만들고,
    /// 주파수를 from→to로 훑으면서 소리가 부풀었다가(swellAt) 빨려들 듯 꺼진다.
    /// 순음 아르페지오와 달리 <b>또렷한 음정이 없어</b> 장난감처럼 들리지 않는다.
    /// </summary>
    /// <param name="detune">두 오실레이터의 주파수 비(1.007 = 0.7% 어긋남 → 느린 맥놀이)</param>
    /// <param name="noise">저역 노이즈를 섞는 비율(바람 소리처럼 두께를 준다)</param>
    /// <param name="swellAt">0~1 중 소리가 가장 커지는 지점. 이후로는 빠르게 꺼진다.</param>
    private static AudioClip Warp(string name, float len, float from, float to,
                                  float detune, float noise, float swellAt)
    {
        int n = Samples(len);
        var data = new float[n];
        float pa = 0f, pb = 0f, lp = 0f;
        swellAt = Mathf.Clamp(swellAt, 0.05f, 0.95f);

        for (int i = 0; i < n; i++)
        {
            float u = i / (float)n;
            // u²로 훑어야 끝으로 갈수록 급격히 변한다(선형은 밋밋한 사이렌이 된다)
            float f = Mathf.Lerp(from, to, u * u);
            pa += f / Rate;
            pb += f * detune / Rate;
            if (pa > 1f) pa -= 1f;
            if (pb > 1f) pb -= 1f;

            float s = ((pa * 2f - 1f) + (pb * 2f - 1f)) * 0.4f;
            lp = Mathf.Lerp(lp, Random.Range(-1f, 1f), 0.08f); // 저역 통과 노이즈
            s = Mathf.Lerp(s, lp, noise);

            float env = u < swellAt
                ? Mathf.Pow(u / swellAt, 1.5f)                       // 부풀어 오름
                : Mathf.Pow(1f - (u - swellAt) / (1f - swellAt), 2f); // 빨려들 듯 꺼짐
            data[i] = s * env * Attack(i / (float)Rate);
        }
        return Make(name, data);
    }

    /// <summary>음을 차례로 이어 붙인다. 내려가면 하강, 올라가면 상승 아르페지오.</summary>
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
