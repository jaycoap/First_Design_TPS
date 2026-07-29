using UnityEngine;

/// <summary>
/// 5초 전 플레이어의 모습을 반투명 고스트로 항상 표시한다.
/// - 매 프레임(LateUpdate, 조준 보정까지 끝난 최종 포즈) 루트 위치/회전 + 힙스 서브트리의
///   본 로컬 포즈를 링버퍼에 기록하고, 고스트(스크립트 제거한 복제본)에 그대로 재생한다.
///   (본 트랜스폼 직접 복사 → 좌표계 변환이 없어 플레이어의 과거 위치·포즈를 정확히 재현)
/// - TimeShiftController가 되감기(그 시점으로 텔레포트)와 지원 사격(고스트 고정)에 사용
/// </summary>
[DefaultExecutionOrder(100)] // PlayerController.LateUpdate(조준 보정) 이후에 기록
public class PlayerTimeGhost : MonoBehaviour
{
    [Tooltip("고스트가 보여줄 과거 시점(초)")]
    [SerializeField] private float delay = 5f;
    [Tooltip("고스트 색(반투명)")]
    [SerializeField] private Color ghostColor = new Color(0.45f, 0.85f, 1f, 0.35f);

    private class Sample
    {
        public float t;
        public Vector3 pos;          // 플레이어 루트 월드 위치
        public Quaternion rot;       // 플레이어 루트 월드 회전
        public Vector3[] lp;         // 본 로컬 위치
        public Quaternion[] lr;      // 본 로컬 회전
        public float health;         // 시간역행 복원용 체력
        public int ammo;             // 시간역행 복원용 탄약
        public bool valid;
    }

    private Sample[] _buf;
    private int _head = -1;
    private int _count;

    private GameObject _ghost;
    private Transform[] _srcBones;   // 플레이어 힙스 서브트리(총 포함)
    private Transform[] _dstBones;   // 고스트의 대응 본(동일 계층이라 순서 일치)
    private int _boneCount;
    private bool _frozen;
    private bool _shown;
    private Vector3 _shownPos;
    private Quaternion _shownRot;

    private PlayerStats _stats;
    private PlayerShooter _shooter;

    /// <summary>고스트가 현재 표시 중인가(= delay초 히스토리 축적 완료 → T 능력 사용 가능).</summary>
    public bool GhostReady => _shown;

    /// <summary>고스트의 총 Transform(지원 사격 발사 원점).</summary>
    public Transform GhostGun { get; private set; }

    /// <summary>고스트가 보여주는 과거 시점(초). TimeShiftController가 월드 역행 범위로 사용.</summary>
    public float Delay => delay;

    /// <summary>시간역행 역재생 중인가(재생 동안 기록/고스트 갱신 중지).</summary>
    public bool IsRewinding { get; private set; }

    private void Start()
    {
        var animator = GetComponentInChildren<Animator>();
        Transform hips = animator != null ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
        if (hips == null)
        {
            Debug.LogWarning("[TimeGhost] 휴머노이드 Animator(힙스 본)가 필요합니다.");
            enabled = false;
            return;
        }

        _ghost = CreateGhost();
        Transform ghostHips = FindDeepChild(_ghost.transform, hips.name);
        if (ghostHips == null)
        {
            Debug.LogWarning("[TimeGhost] 고스트에서 힙스 본을 찾지 못했습니다.");
            Destroy(_ghost);
            enabled = false;
            return;
        }

        // 힙스 서브트리는 스켈레톤+무기뿐이라 런타임 추가 오브젝트가 없어 순서가 정확히 일치한다
        _srcBones = hips.GetComponentsInChildren<Transform>(true);
        _dstBones = ghostHips.GetComponentsInChildren<Transform>(true);
        _boneCount = Mathf.Min(_srcBones.Length, _dstBones.Length);
        if (_srcBones.Length != _dstBones.Length)
            Debug.LogWarning($"[TimeGhost] 본 수 불일치(player {_srcBones.Length} vs ghost {_dstBones.Length}) — 공통 부분만 복사합니다.");

        int cap = Mathf.CeilToInt((delay + 1.5f) * 90f); // 90fps 여유
        _buf = new Sample[cap];
        for (int i = 0; i < cap; i++)
            _buf[i] = new Sample { lp = new Vector3[_boneCount], lr = new Quaternion[_boneCount] };

        _stats = GetComponent<PlayerStats>();
        _shooter = GetComponent<PlayerShooter>();

        _ghost.SetActive(false); // 히스토리가 쌓이면 표시
    }

    /// <summary>플레이어를 복제해 스크립트/물리를 떼고 반투명 고스트로 만든다.</summary>
    private GameObject CreateGhost()
    {
        var ghost = Instantiate(gameObject);
        ghost.name = "PlayerGhost(5s)";

        // RequireComponent 의존 순서 때문에 의존하는 쪽(TimeShiftController)을 먼저 제거해야 한다.
        // (안 그러면 "Can't remove PlayerTimeGhost ..." 오류와 함께 고스트가 고스트를 만드는 재귀 발생)
        var tsc = ghost.GetComponent<TimeShiftController>();
        if (tsc != null) DestroyImmediate(tsc);
        foreach (var mb in ghost.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null) DestroyImmediate(mb);
        var cc = ghost.GetComponent<CharacterController>();
        if (cc != null) DestroyImmediate(cc);
        foreach (var a in ghost.GetComponentsInChildren<Animator>(true))
            if (a != null) DestroyImmediate(a); // 포즈는 본 복사로 직접 구동

        // 반투명 고스트 머티리얼로 교체 + 그림자/이펙트 끔
        var mat = new Material(Shader.Find("Sprites/Default")) { color = ghostColor };
        foreach (var r in ghost.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r is LineRenderer) { r.enabled = false; continue; }
            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        foreach (var l in ghost.GetComponentsInChildren<Light>(true)) l.enabled = false;

        GhostGun = FindDeepChild(ghost.transform, "Gun");
        return ghost;
    }

    private void LateUpdate()
    {
        if (_buf == null || IsRewinding) return; // 역재생 중엔 기록 중지(뒤로 가는 모습이 기록되면 안 됨)

        // --- 기록(조준 보정까지 끝난 최종 포즈) ---
        _head = (_head + 1) % _buf.Length;
        if (_count < _buf.Length) _count++;
        var s = _buf[_head];
        s.t = Time.time;
        s.pos = transform.position;
        s.rot = transform.rotation;
        for (int i = 0; i < _boneCount; i++)
        {
            s.lp[i] = _srcBones[i].localPosition;
            s.lr[i] = _srcBones[i].localRotation;
        }
        s.health = _stats != null ? _stats.Health : 0f;
        s.ammo = _shooter != null ? _shooter.CurrentAmmo : 0;
        s.valid = true;

        // --- 재생(지원 사격 중엔 현재 위치/포즈에 고정) ---
        if (_frozen) return;

        float target = Time.time - delay;
        Sample found = null;
        for (int i = 0; i < _count; i++)
        {
            var cand = _buf[(_head - i + _buf.Length * 2) % _buf.Length];
            if (!cand.valid) break;
            if (cand.t <= target) { found = cand; break; } // 최신부터 훑어 target 이하 첫 샘플
        }

        if (found == null)
        {
            if (_shown) { _shown = false; _ghost.SetActive(false); }
            return;
        }

        if (!_shown) { _shown = true; _ghost.SetActive(true); }
        _ghost.transform.SetPositionAndRotation(found.pos, found.rot);
        for (int i = 0; i < _boneCount; i++)
        {
            _dstBones[i].localPosition = found.lp[i];
            _dstBones[i].localRotation = found.lr[i];
        }
        _shownPos = found.pos;
        _shownRot = found.rot;
    }

    /// <summary>현재 고스트가 표시 중인 과거 상태(되감기 목적지).</summary>
    public bool TryGetGhostState(out Vector3 pos, out Quaternion rot)
    {
        pos = _shownPos;
        rot = _shownRot;
        return _shown;
    }

    /// <summary>히스토리 초기화(되감기 후 호출). 고스트는 delay초 뒤 다시 나타난다(자연 쿨다운).</summary>
    public void ClearHistory()
    {
        _head = -1;
        _count = 0;
        foreach (var s in _buf) s.valid = false;
        _shown = false;
        if (_ghost != null) _ghost.SetActive(false);
    }

    /// <summary>지원 사격 동안 고스트를 현재 위치/포즈에 고정하거나 해제.</summary>
    public void SetFrozen(bool frozen) => _frozen = frozen;

    // ---------- 시간역행: 과거로 되감기는 모습을 역재생 ----------

    /// <summary>
    /// duration(실시간) 동안 기록을 거꾸로 재생해 delay초 전 상태로 되돌린다(위치·자세·체력·탄약).
    /// 재생 동안 조작/애니메이터/물리는 잠기고, 완료 시 히스토리가 초기화된다(고스트 재충전).
    /// </summary>
    public bool StartRewindPlayback(float duration, System.Action onComplete)
    {
        if (IsRewinding || _buf == null || _count == 0) return false;
        StartCoroutine(RewindRoutine(duration, onComplete));
        return true;
    }

    private System.Collections.IEnumerator RewindRoutine(float duration, System.Action onComplete)
    {
        IsRewinding = true;
        _shown = false;
        if (_ghost != null) _ghost.SetActive(false); // 목적지의 고스트는 숨김(내가 그 자리로 되감겨 간다)

        // 조작/애니메이션/물리 잠금 — 포즈는 기록으로 직접 구동
        var animator = GetComponentInChildren<Animator>();
        var cc = GetComponent<CharacterController>();
        var pc = GetComponent<PlayerController>();
        var shooter = GetComponent<PlayerShooter>();
        if (animator != null) animator.enabled = false;
        if (cc != null) cc.enabled = false;
        if (pc != null) pc.enabled = false;
        if (shooter != null) shooter.enabled = false;

        float newest = _buf[_head].t;
        float oldest = _buf[(_head - (_count - 1) + _buf.Length * 2) % _buf.Length].t;
        float span = Mathf.Max(0.0001f, newest - Mathf.Max(oldest, newest - delay));

        float el = 0f;
        while (el < duration)
        {
            el += Time.deltaTime;
            float k = Mathf.Clamp01(el / duration);
            float e = k * k * (3f - 2f * k); // easeInOut — 가속했다 감속하며 되감김
            var s = FindAtOrBefore(newest - span * e);
            if (s != null) ApplyToPlayer(s);
            yield return null;
        }

        // 최종(5초 전) 상태 적용: 위치·자세 + 체력·탄약
        var final = FindAtOrBefore(newest - span);
        if (final != null)
        {
            ApplyToPlayer(final);
            if (_stats != null) _stats.RewindHealth(final.health);
            if (_shooter != null) _shooter.RewindAmmo(final.ammo);
        }

        // 잠금 해제
        if (animator != null) animator.enabled = true;
        if (cc != null) cc.enabled = true;
        if (pc != null) { pc.enabled = true; pc.OnTeleported(); }
        if (shooter != null) shooter.enabled = true;

        ClearHistory(); // 미래가 된 기록은 무효 → 고스트는 delay초 뒤 재충전
        IsRewinding = false;
        onComplete?.Invoke();
    }

    /// <summary>t 이하의 가장 최신 샘플(없으면 가장 오래된 샘플).</summary>
    private Sample FindAtOrBefore(float t)
    {
        for (int i = 0; i < _count; i++)
        {
            var s = _buf[(_head - i + _buf.Length * 2) % _buf.Length];
            if (!s.valid) break;
            if (s.t <= t || i == _count - 1) return s;
        }
        return null;
    }

    /// <summary>샘플의 루트 위치/회전 + 본 포즈를 플레이어 자신에게 적용(역재생용).</summary>
    private void ApplyToPlayer(Sample s)
    {
        transform.SetPositionAndRotation(s.pos, s.rot);
        for (int i = 0; i < _boneCount; i++)
        {
            _srcBones[i].localPosition = s.lp[i];
            _srcBones[i].localRotation = s.lr[i];
        }
    }

    private static Transform FindDeepChild(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            var c = parent.GetChild(i);
            if (c.name == name) return c;
            var f = FindDeepChild(c, name);
            if (f != null) return f;
        }
        return null;
    }
}
