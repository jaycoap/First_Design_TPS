using UnityEngine;

/// <summary>
/// 전투 배경음악. 씬에 살아있는 보스가 있는 동안 음원을 반복 재생하고,
/// 보스를 쓰러뜨리면(또는 보스가 사라지면) 서서히 잦아든다.
///
/// - 음원은 <c>Assets/Resources/Sounds/</c> 에서 이름으로 자동 로드한다(Inspector 지정이 우선).
///   효과음(<see cref="GameSfx"/>)과 같은 규약이라 파일만 넣으면 씬 배치가 필요 없다.
/// - BossController.Awake가 <see cref="Ensure"/>로 이 오브젝트를 보장한다.
/// - 시간역행 재생 중에는 음정을 늘어뜨려 시간이 되감기는 감각에 붙인다.
/// - 페이드는 Time.timeScale의 영향을 받지 않도록 unscaled 시간으로 처리한다.
/// </summary>
[DefaultExecutionOrder(-50)]
public class BattleMusic : MonoBehaviour
{
    /// <summary>Resources/Sounds 안의 배경음악 파일 이름(확장자 제외).</summary>
    public const string TrackName = "Echoes of the Void";

    [Tooltip("비우면 Resources/Sounds/" + TrackName + " 을 자동으로 불러온다.")]
    [SerializeField] private AudioClip track;
    [SerializeField, Range(0f, 1f)] private float volume = 0.4f;
    [Tooltip("전투가 시작될 때 볼륨이 차오르는 시간(초)")]
    [SerializeField] private float fadeInTime = 1.5f;
    [Tooltip("전투가 끝날 때 잦아드는 시간(초)")]
    [SerializeField] private float fadeOutTime = 3f;
    [Tooltip("보스를 놓친 뒤에도 전투로 유지하는 시간(초).\n" +
             "시간역행이 보스 컴포넌트를 잠깐 끄는 동안 음악이 움푹 꺼지는 것을 막는다.")]
    [SerializeField] private float battleHoldTime = 1.5f;
    [Tooltip("시간역행 재생 중 음정 배율 — 시간이 되감기는 감각에 음악도 함께 붙는다.")]
    [SerializeField, Range(0.3f, 1f)] private float rewindPitch = 0.75f;

    private static BattleMusic _instance;

    private AudioSource _source;
    private float _level;              // 현재 볼륨 비율 0~1(페이드 상태)
    private float _lastBattleTime = -99f;

    /// <summary>씬에 배경음악 재생기를 보장한다(BossController가 호출).</summary>
    public static void Ensure()
    {
        if (_instance != null) return;
        _instance = FindFirstObjectByType<BattleMusic>();
        if (_instance == null) new GameObject("BattleMusic").AddComponent<BattleMusic>();
    }

    private void Awake()
    {
        // 씬을 다시 불러도 음악이 끊기지 않게 하나만 남긴다
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);

        if (track == null) track = Resources.Load<AudioClip>("Sounds/" + TrackName);
        if (track == null)
        {
            Debug.LogWarning($"[BattleMusic] 'Assets/Resources/Sounds/{TrackName}' 음원을 찾지 못했습니다. " +
                             "배경음악 없이 계속 진행합니다.");
            return;
        }

        _source = gameObject.AddComponent<AudioSource>();
        _source.clip = track;
        _source.playOnAwake = false;
        _source.loop = true;
        _source.volume = 0f;
        _source.spatialBlend = 0f;     // 2D — 카메라 위치와 무관하게 일정하게 들린다
        _source.dopplerLevel = 0f;
        _source.ignoreListenerPause = true;
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    private void Update()
    {
        if (_source == null) return;

        float dt = Time.unscaledDeltaTime;

        if (InBattle()) _lastBattleTime = Time.unscaledTime;
        bool battle = Time.unscaledTime - _lastBattleTime <= battleHoldTime;

        float fade = battle ? fadeInTime : fadeOutTime;
        _level = Mathf.MoveTowards(_level, battle ? 1f : 0f, fade > 0.01f ? dt / fade : 1f);

        if (_level > 0.001f)
        {
            if (!_source.isPlaying) _source.Play();
            _source.volume = volume * _level;

            float wantPitch = TimeShiftController.RewindActive ? rewindPitch : 1f;
            _source.pitch = Mathf.MoveTowards(_source.pitch, wantPitch, 2f * dt);
        }
        else if (_source.isPlaying)
        {
            _source.Stop();
            _source.pitch = 1f;
        }
    }

    /// <summary>전투 중인가 — 살아있는 보스가 씬에 있는가.</summary>
    private static bool InBattle()
    {
        var boss = BossController.Active;
        return boss != null && !boss.IsDead && boss.isActiveAndEnabled && !boss.IntroPlaying;
    }
}
