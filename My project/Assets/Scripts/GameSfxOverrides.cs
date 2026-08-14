using UnityEngine;

/// <summary>
/// 임시 합성음을 진짜 사운드로 갈아끼우는 지점.
///
/// 씬의 아무 오브젝트(예: GameHUD)에 붙이고 목록에 [종류 + AudioClip]을 넣으면,
/// 지정한 종류만 그 클립으로 재생된다. 지정하지 않은 종류는 <see cref="GameSfx"/>의
/// 합성음을 그대로 쓰므로, 사운드를 하나씩 교체해 나갈 수 있다.
/// 코드는 건드릴 필요가 없다.
/// </summary>
[DisallowMultipleComponent]
public class GameSfxOverrides : MonoBehaviour
{
    [System.Serializable]
    public struct Entry
    {
        [Tooltip("교체할 효과음 종류")]
        public Sfx sound;
        [Tooltip("이 종류에 쓸 진짜 클립. 비우면 합성음을 그대로 쓴다.")]
        public AudioClip clip;
    }

    [Tooltip("합성음 대신 쓸 클립 목록. 비어 있으면 전부 합성음으로 재생된다.")]
    [SerializeField] private Entry[] overrides;

    [Tooltip("전체 효과음 음량(0~1). 임시 사운드가 시끄러우면 여기서 줄인다.")]
    [SerializeField, Range(0f, 1f)] private float masterVolume = 0.8f;

    private void Awake() => Apply();

    /// <summary>인스펙터에서 값을 바꾸면 플레이 중에도 즉시 반영된다.</summary>
    private void OnValidate()
    {
        if (Application.isPlaying) Apply();
    }

    private void Apply()
    {
        GameSfx.MasterVolume = masterVolume;
        if (overrides == null) return;

        foreach (var e in overrides)
            if (e.clip != null) GameSfx.Override(e.sound, e.clip);
    }
}
