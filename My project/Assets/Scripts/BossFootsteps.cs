using UnityEngine;

/// <summary>
/// 보스 발소리. Codersan 로코모션 클립(Walk_N/Run_N 등)에는 원래 컨트롤러가 받던
/// AnimationEvent(OnFootstep/OnLand)가 박혀 있어, 받는 쪽이 없으면 콘솔에
/// "has no receiver!" 경고가 계속 뜬다. 이 컴포넌트가 그 이벤트를 받아 발소리를 낸다.
///
/// - Animator와 같은 GameObject에 있어야 이벤트가 전달된다(BossSetup이 그렇게 붙인다).
/// - 클립을 비워두면 소리 없이 이벤트만 삼킨다(경고만 사라짐).
/// - 보스답게 피치를 낮춰(기본 0.7) 무겁게 들리도록 한다.
/// </summary>
[DefaultExecutionOrder(50)]
public class BossFootsteps : MonoBehaviour
{
    [Tooltip("발소리 후보(랜덤 재생). 비우면 무음.")]
    [SerializeField] private AudioClip[] footstepClips;
    [Tooltip("착지음. 비우면 발소리에서 하나를 쓴다.")]
    [SerializeField] private AudioClip landClip;
    [Range(0f, 1f)]
    [SerializeField] private float volume = 0.5f;
    [Tooltip("재생 피치(낮을수록 크고 무거운 몸집 느낌)")]
    [SerializeField] private float pitch = 0.7f;
    [Tooltip("피치 랜덤 편차")]
    [SerializeField] private float pitchJitter = 0.08f;

    private AudioSource _source;

    private void Awake()
    {
        if (footstepClips == null || footstepClips.Length == 0) return;

        _source = GetComponent<AudioSource>();
        if (_source == null) _source = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.loop = false;
        _source.spatialBlend = 1f; // 3D — 보스에게서 들린다
        _source.rolloffMode = AudioRolloffMode.Linear;
    }

    // ---- AnimationEvent 수신부(이름이 클립의 functionName과 일치해야 한다) ----

    private void OnFootstep(AnimationEvent evt)
    {
        // 블렌드 중인 다른 클립의 이벤트까지 겹쳐 울리지 않게 가중치가 큰 쪽만 재생
        if (evt.animatorClipInfo.weight < 0.5f) return;
        Play(PickFootstep());
    }

    private void OnLand(AnimationEvent evt)
    {
        if (evt.animatorClipInfo.weight < 0.5f) return;
        Play(landClip != null ? landClip : PickFootstep());
    }

    private AudioClip PickFootstep()
    {
        if (footstepClips == null || footstepClips.Length == 0) return null;
        return footstepClips[Random.Range(0, footstepClips.Length)];
    }

    private void Play(AudioClip clip)
    {
        if (clip == null || _source == null) return;
        _source.pitch = pitch + Random.Range(-pitchJitter, pitchJitter);
        _source.PlayOneShot(clip, volume);
    }
}
