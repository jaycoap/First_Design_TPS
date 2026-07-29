using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 시간역행 대상 컴포넌트. 붙이기만 하면 위치/회전(+선택: IRewindableExtra 상태, 예: 체력)을
/// 링버퍼에 기록하고, 시간역행 발동 시 과거로 되감기는 모습을 역재생한다.
/// 보스/적/기믹 등 되감고 싶은 모든 대상에 부착(보스 패턴의 위치·상태가 함께 과거로 돌아감).
/// 되감는 동안 같은 오브젝트의 다른 스크립트(AI 등)와 물리는 잠시 정지된다.
/// </summary>
[DefaultExecutionOrder(100)]
public class TimeRewindable : MonoBehaviour
{
    [Tooltip("기록 유지 시간(초). 시간역행 5초보다 여유 있게.")]
    [SerializeField] private float recordSeconds = 6.5f;

    private struct Snap
    {
        public float t;
        public Vector3 pos;
        public Quaternion rot;
        public float extra;
        public bool valid;
    }

    private Snap[] _buf;
    private int _head = -1, _count;
    private IRewindableExtra _extra;
    private static readonly List<TimeRewindable> All = new List<TimeRewindable>();

    public bool IsRewinding { get; private set; }

    private void Awake()
    {
        _buf = new Snap[Mathf.CeilToInt(recordSeconds * 90f)];
        _extra = GetComponent<IRewindableExtra>();
    }

    private void OnEnable() => All.Add(this);
    private void OnDisable() => All.Remove(this);

    private void LateUpdate()
    {
        if (IsRewinding) return;
        _head = (_head + 1) % _buf.Length;
        if (_count < _buf.Length) _count++;
        _buf[_head] = new Snap
        {
            t = Time.time,
            pos = transform.position,
            rot = transform.rotation,
            extra = _extra != null ? _extra.CaptureRewindExtra() : 0f,
            valid = true
        };
    }

    /// <summary>등록된 모든 대상에게 되감기 시작(secondsAgo 전 상태까지 duration 동안 역재생).</summary>
    public static void RewindAll(float secondsAgo, float duration)
    {
        foreach (var r in All.ToArray())
            if (r != null && r.isActiveAndEnabled) r.StartRewind(secondsAgo, duration);
    }

    public void StartRewind(float secondsAgo, float duration)
    {
        if (IsRewinding || _count == 0) return;
        StartCoroutine(RewindRoutine(secondsAgo, duration));
    }

    private IEnumerator RewindRoutine(float secondsAgo, float duration)
    {
        IsRewinding = true;

        // 되감는 동안 AI/물리 정지
        var disabled = new List<Behaviour>();
        foreach (var mb in GetComponents<MonoBehaviour>())
            if (mb != this && mb.enabled) { mb.enabled = false; disabled.Add(mb); }
        var rb = GetComponent<Rigidbody>();
        bool wasKinematic = false;
        if (rb != null) { wasKinematic = rb.isKinematic; rb.isKinematic = true; }

        float newest = _buf[_head].t;
        float oldest = _buf[(_head - (_count - 1) + _buf.Length * 2) % _buf.Length].t;
        float span = Mathf.Max(0.0001f, newest - Mathf.Max(oldest, newest - secondsAgo));

        float el = 0f;
        while (el < duration)
        {
            el += Time.deltaTime;
            float k = Mathf.Clamp01(el / duration);
            float e = k * k * (3f - 2f * k); // easeInOut
            ApplyAt(newest - span * e, applyExtra: false);
            yield return null;
        }
        ApplyAt(newest - span, applyExtra: true); // 최종 상태(체력 등 포함)

        // 복구 + 히스토리 초기화(미래가 된 기록은 무효)
        foreach (var mb in disabled) if (mb != null) mb.enabled = true;
        if (rb != null)
        {
            rb.isKinematic = wasKinematic;
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
        _head = -1;
        _count = 0;
        for (int i = 0; i < _buf.Length; i++) _buf[i].valid = false;
        IsRewinding = false;
    }

    private void ApplyAt(float t, bool applyExtra)
    {
        // 최신부터 훑어 t 이하 첫 스냅샷(없으면 가장 오래된 것)
        for (int i = 0; i < _count; i++)
        {
            var s = _buf[(_head - i + _buf.Length * 2) % _buf.Length];
            if (!s.valid) break;
            if (s.t <= t || i == _count - 1)
            {
                transform.SetPositionAndRotation(s.pos, s.rot);
                if (applyExtra && _extra != null) _extra.ApplyRewindExtra(s.extra);
                return;
            }
        }
    }
}

/// <summary>되감기 시 위치/회전 외의 상태(예: 체력)도 복원하고 싶은 컴포넌트가 구현.</summary>
public interface IRewindableExtra
{
    float CaptureRewindExtra();
    void ApplyRewindExtra(float value);
}
