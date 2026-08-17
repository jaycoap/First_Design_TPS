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

    [Header("시간역행 잔상")]
    [Tooltip("역행 중 잔상을 남길 간격(초). 작을수록 촘촘하다.")]
    [SerializeField] private float afterimageInterval = 0.14f;
    [Tooltip("잔상 하나가 사라지기까지의 시간(초)")]
    [SerializeField] private float afterimageLife = 0.7f;
    [Tooltip("잔상 색(알파가 시작 진하기)")]
    [SerializeField] private Color afterimageColor = new Color(0.5f, 0.9f, 1f, 0.5f);

    private class Sample
    {
        public float t;
        public Vector3 pos;          // 플레이어 루트 월드 위치
        public Quaternion rot;       // 플레이어 루트 월드 회전
        public Vector3[] lp;         // 본 로컬 위치
        public Quaternion[] lr;      // 본 로컬 회전
        public float health;         // 시간역행 복원용 체력
        public float stamina;        // 시간역행 복원용 기력(구르기 에너지)
        public int ammo;             // 시간역행 복원용 탄약
        public bool valid;
    }

    /// <summary>
    /// 초당 기록 횟수. 버퍼 칸 수를 이 값으로 나눈 만큼이 곧 보관 시간이 되므로,
    /// 프레임마다 기록하면 안 된다 — fps가 이 값을 넘는 순간 버퍼가 delay초를 못 담아
    /// '5초 전 샘플'을 영영 못 찾고 고스트가 나타나지 않는다(= T 능력 전체가 잠긴다).
    /// </summary>
    private const float SampleRate = 90f;

    private Sample[] _buf;
    private int _head = -1;
    private int _count;
    private float _nextSampleTime;

    private GameObject _ghost;
    private Animator _ghostAnimator; // 지원 사격 중에만 켜서 조준/발사 모션 재생
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

    /// <summary>
    /// 고스트 몸이 향한 방향(월드). 총열의 앞뒤를 판별하는 기준으로 쓴다 —
    /// 총은 항상 몸 앞쪽에 들려 있으므로, 목표 방향이 아니라 이 값을 기준 삼아야
    /// "지금 총이 실제로 어디를 향하는가"를 왜곡 없이 잴 수 있다.
    /// </summary>
    public Vector3 GhostForward => _ghost != null ? _ghost.transform.forward : transform.forward;

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

        int cap = Mathf.CeilToInt((delay + 1.5f) * SampleRate); // delay + 1.5초 분량
        _buf = new Sample[cap];
        for (int i = 0; i < cap; i++)
            _buf[i] = new Sample { lp = new Vector3[_boneCount], lr = new Quaternion[_boneCount] };
        _nextSampleTime = Time.time;

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

        // Animator는 지우지 않고 꺼둔다. 평소 포즈는 본 복사로 구동하고,
        // 지원 사격 때만 켜서 조준·발사 애니메이션을 재생한다(SetGhostAnimating).
        _ghostAnimator = ghost.GetComponentInChildren<Animator>(true);
        if (_ghostAnimator != null)
        {
            _ghostAnimator.applyRootMotion = false; // 위치는 이 스크립트가 정한다
            _ghostAnimator.enabled = false;
        }

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

        // --- 기록(조준 보정까지 끝난 최종 포즈) — SampleRate로 제한 ---
        // 재생은 매 프레임 하되 기록만 솎아낸다. 프레임마다 넣으면 고프레임에서
        // 버퍼가 delay초를 못 담는다(위 SampleRate 주석 참고).
        if (Time.time >= _nextSampleTime)
        {
            // 누적 기준으로 다음 시각을 잡아 드리프트를 막고, 프레임이 크게 밀렸으면 현재 시각에 재동기화
            _nextSampleTime = Mathf.Max(Time.time, _nextSampleTime + 1f / SampleRate);

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
            s.stamina = _stats != null ? _stats.Stamina : 0f;
            s.ammo = _shooter != null ? _shooter.CurrentAmmo : 0;
            s.valid = true;
        }

        if (_count == 0) return; // 아직 첫 샘플 전

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
        _nextSampleTime = Time.time; // 다음 프레임부터 곧바로 다시 쌓기 시작
        foreach (var s in _buf) s.valid = false;
        _shown = false;
        if (_ghost != null) _ghost.SetActive(false);
    }

    /// <summary>지원 사격 동안 고스트를 현재 위치/포즈에 고정하거나 해제.</summary>
    public void SetFrozen(bool frozen) => _frozen = frozen;

    /// <summary>
    /// 지원 사격 동안 고스트 Animator를 켜서 조준 파지 자세를 취하게 한다(끄면 다시 본 복사 재생).
    /// 과거 어떤 자세로 기록됐든 사격 중엔 총을 겨눈 모습이 되도록 한다.
    /// </summary>
    public void SetGhostAnimating(bool on)
    {
        if (_ghostAnimator == null || _ghostAnimator.runtimeAnimatorController == null) return;

        _ghostAnimator.enabled = on;
        if (!on) return;

        // 정지 상태 + 조준 → Base Layer는 대기(소총 파지) 자세
        _ghostAnimator.SetFloat("Speed", 0f);
        _ghostAnimator.SetBool("IsRunning", false);
        _ghostAnimator.SetBool("IsAiming", true);
        _ghostAnimator.Play("Idle", 0, 0f); // 달리던 중 기록됐어도 즉시 파지 자세로

        // 발사 모션은 상체 레이어에 있으므로 가중치를 올려야 보인다
        int upper = _ghostAnimator.GetLayerIndex("UpperBody");
        if (upper >= 0) _ghostAnimator.SetLayerWeight(upper, 1f);
    }

    /// <summary>고스트의 발사 모션 1회 재생(지원 사격 한 발마다 호출).</summary>
    public void TriggerGhostFire()
    {
        if (_ghostAnimator != null && _ghostAnimator.enabled)
            _ghostAnimator.SetTrigger("Fire");
    }

    /// <summary>
    /// 고스트를 목표 지점 쪽으로 수평 회전시킨다(지원 사격 시 실제로 적을 겨누는 모습).
    /// 고정(SetFrozen) 상태에서만 의미가 있다 — 재생 중이면 다음 프레임에 덮어써진다.
    /// </summary>
    public void AimGhostAt(Vector3 worldPoint)
    {
        if (_ghost == null || !_ghost.activeSelf) return;
        Vector3 dir = worldPoint - _ghost.transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) return;
        _ghost.transform.rotation = Quaternion.LookRotation(dir.normalized);
    }

    /// <summary>
    /// 고스트를 제자리에서 수평으로 degrees만큼 돌린다.
    /// 포즈가 비스듬한 상태에서도 총열이 목표를 향하도록 미세 조정할 때 쓴다
    /// (루트를 목표로 바로 돌리면 몸이 포즈 각도만큼 엉뚱한 데를 본다).
    /// </summary>
    public void RotateGhostYaw(float degrees)
    {
        if (_ghost == null || !_ghost.activeSelf) return;
        _ghost.transform.rotation = Quaternion.AngleAxis(degrees, Vector3.up) * _ghost.transform.rotation;
    }

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
        float nextAfterimage = 0f;
        while (el < duration)
        {
            el += Time.deltaTime;
            float k = Mathf.Clamp01(el / duration);
            float e = k * k * (3f - 2f * k); // easeInOut — 가속했다 감속하며 되감김
            var s = FindAtOrBefore(newest - span * e);
            if (s != null) ApplyToPlayer(s);

            // 지나온 자리에 잔상을 남긴다(포즈 적용 직후라 현재 자세 그대로 구워진다)
            if (el >= nextAfterimage)
            {
                nextAfterimage = el + Mathf.Max(0.01f, afterimageInterval);
                SpawnAfterimage();
            }
            yield return null;
        }

        // 최종(5초 전) 상태 적용: 위치·자세 + 체력·기력·탄약
        // (기력도 되돌려야 한다 — 굴러서 SP를 태운 직후 되감으면 몸은 과거로 갔는데
        //  기력만 현재값으로 비어 있어, 되감기 전과 같은 상황을 다시 넘길 수가 없다)
        var final = FindAtOrBefore(newest - span);
        if (final != null)
        {
            ApplyToPlayer(final);
            if (_stats != null)
            {
                _stats.RewindHealth(final.health);
                _stats.RewindStamina(final.stamina);
            }
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

    /// <summary>
    /// 현재 포즈를 그대로 구워(BakeMesh) 제자리에 잔상으로 남긴다.
    /// 캐릭터 하위의 모든 렌더러를 한 번에 훑어 상·하체(및 손에 든 총)가 빠짐없이 남는다.
    /// 스키닝 메시는 현재 자세로 굽고, 일반 메시는 원본 메시를 참조만 한다.
    /// (파티클/라인/트레일은 잔상 대상이 아니므로 제외)
    /// </summary>
    private void SpawnAfterimage()
    {
        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer)
                continue;

            if (r is SkinnedMeshRenderer smr)
            {
                if (smr.sharedMesh == null) continue;
                var baked = new Mesh();
                smr.BakeMesh(baked); // 렌더러 로컬 공간(스케일 미적용)으로 구움
                MakeAfterimagePiece(baked, ownsMesh: true, smr.transform);
            }
            else if (r.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
            {
                MakeAfterimagePiece(mf.sharedMesh, ownsMesh: false, r.transform);
            }
        }
    }

    /// <summary>원본 렌더러의 월드 TRS를 그대로 복사해 잔상 조각을 만든다(위치·크기 어긋남 방지).</summary>
    private void MakeAfterimagePiece(Mesh mesh, bool ownsMesh, Transform source)
    {
        var go = new GameObject("RewindAfterimage");
        go.transform.SetPositionAndRotation(source.position, source.rotation);
        go.transform.localScale = source.lossyScale;

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();

        // 이 캐릭터는 부위별로 머티리얼이 나뉘어 있어 메시가 여러 서브메시를 갖는다.
        // 슬롯 수만큼 채우지 않으면 서브메시 0번(몸통/팔)만 그려지고 머리·다리가 사라진다.
        var mat = new Material(Shader.Find("Sprites/Default")) { color = afterimageColor };
        int slots = Mathf.Max(1, mesh.subMeshCount);
        var mats = new Material[slots];
        for (int i = 0; i < slots; i++) mats[i] = mat; // 같은 인스턴스 공유 → 페이드/정리 1회
        mr.sharedMaterials = mats;

        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        go.AddComponent<RewindAfterimage>()
          .Init(mat, ownsMesh ? mesh : null, afterimageLife, afterimageColor);
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

/// <summary>시간역행 잔상 한 조각: 알파가 서서히 빠지고 사라진다(자기 머티리얼/메시 정리 포함).</summary>
public class RewindAfterimage : MonoBehaviour
{
    private Material _mat;
    private Mesh _ownedMesh;   // BakeMesh로 만든 것만 소유(총 메시는 원본 참조라 null)
    private float _life = 0.7f;
    private float _elapsed;
    private Color _color;

    public void Init(Material mat, Mesh ownedMesh, float life, Color color)
    {
        _mat = mat;
        _ownedMesh = ownedMesh;
        _life = Mathf.Max(0.05f, life);
        _color = color;
    }

    private void Update()
    {
        _elapsed += Time.deltaTime;
        float k = 1f - Mathf.Clamp01(_elapsed / _life);
        if (_mat != null)
        {
            var c = _color;
            c.a = _color.a * k * k; // 뒤로 갈수록 빠르게 옅어짐
            _mat.color = c;
        }
        if (_elapsed >= _life) Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (_mat != null) Destroy(_mat);
        if (_ownedMesh != null) Destroy(_ownedMesh);
    }
}
