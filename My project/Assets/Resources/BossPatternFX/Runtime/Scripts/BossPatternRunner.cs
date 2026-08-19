using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace BossFX
{
    /// <summary>판정이 발생했을 때 넘어오는 정보.</summary>
    public struct BossHitInfo
    {
        public Collider collider;
        public float damage;
        public string label;
        public Vector3 point;
    }

    [System.Serializable]
    public class BossHitEvent : UnityEvent<BossHitInfo> { }

    // UnityEvent<T> 는 제네릭 그대로는 인스펙터에 노출되지 않습니다.
    // 반드시 구체 서브클래스를 만들어야 합니다.
    [System.Serializable]
    public class BossStringEvent : UnityEvent<string> { }

    /// <summary>
    /// BossPattern 을 순서대로 실행합니다.
    /// 빈 GameObject 에 붙이고 pattern / target / targetMask 만 채우면 동작합니다.
    /// 실제 데미지 처리는 onHit 이벤트로 빼두었으니 프로젝트 쪽 체력 시스템에 연결하세요.
    /// </summary>
    [AddComponentMenu("BossFX/Boss Pattern Runner")]
    public class BossPatternRunner : MonoBehaviour
    {
        [Header("패턴")]
        public List<BossPattern> patterns = new List<BossPattern>();

        [Tooltip("순서대로 / 무작위로 고를지")]
        public bool randomOrder = false;

        public bool loop = true;
        public bool playOnStart = true;

        [Header("대상")]
        public Transform target;

        [Tooltip("판정에 걸릴 레이어 (플레이어 레이어를 지정하세요)")]
        public LayerMask targetMask = ~0;

        [Header("아레나")]
        public Transform arenaCenter;
        public float arenaRadius = 18f;

        [Header("이벤트")]
        public BossHitEvent onHit = new BossHitEvent();
        public BossStringEvent onStepStart = new BossStringEvent();
        public UnityEvent onPatternComplete = new UnityEvent();

        [Header("디버그")]
        public bool logSteps = false;

        int _index = -1;
        Coroutine _loop;
        readonly List<Coroutine> _running = new List<Coroutine>();

        public bool IsPlaying => _loop != null;

        void Start()
        {
            if (playOnStart) Play();
        }

        public void Play()
        {
            Stop();
            _loop = StartCoroutine(RunLoop());
        }

        public void Stop()
        {
            if (_loop != null) { StopCoroutine(_loop); _loop = null; }
            for (int i = 0; i < _running.Count; i++)
                if (_running[i] != null) StopCoroutine(_running[i]);
            _running.Clear();
        }

        IEnumerator RunLoop()
        {
            if (patterns == null || patterns.Count == 0)
            {
                Debug.LogWarning("[BossFX] 실행할 패턴이 없습니다.", this);
                yield break;
            }

            do
            {
                BossPattern p = NextPattern();
                if (p != null)
                {
                    yield return RunPattern(p);
                    onPatternComplete.Invoke();
                    if (p.recoveryTime > 0f) yield return new WaitForSeconds(p.recoveryTime);
                }
                else
                {
                    yield return null;
                }
            }
            while (loop);

            _loop = null;
        }

        BossPattern NextPattern()
        {
            if (patterns.Count == 0) return null;
            if (randomOrder) return patterns[Random.Range(0, patterns.Count)];
            _index = (_index + 1) % patterns.Count;
            return patterns[_index];
        }

        /// <summary>패턴 하나를 즉시 실행합니다 (외부에서 호출 가능).</summary>
        public IEnumerator RunPattern(BossPattern pattern)
        {
            if (pattern == null || pattern.steps == null) yield break;

            for (int i = 0; i < pattern.steps.Count; i++)
            {
                var step = pattern.steps[i];
                if (step == null) continue;

                if (logSteps) Debug.Log($"[BossFX] step {i}: {step.label} ({step.type})", this);
                onStepStart.Invoke(step.label);

                if (step.waitForCompletion) yield return RunStep(step);
                else _running.Add(StartCoroutine(RunStep(step)));

                if (step.delayAfter > 0f) yield return new WaitForSeconds(step.delayAfter);
            }
        }

        IEnumerator RunStep(BossStep step)
        {
            int repeat = Mathf.Max(1, step.repeat);

            for (int r = 0; r < repeat; r++)
            {
                ResolvePlacement(step, r, out Vector3 pos, out Quaternion rot);

                switch (step.type)
                {
                    case BossStepType.Telegraph:
                        yield return DoTelegraph(step, pos, rot);
                        break;

                    case BossStepType.Beam:
                        yield return DoBeam(step, pos, rot);
                        break;

                    case BossStepType.Barrage:
                        yield return DoBarrage(step, pos, rot);
                        break;

                    case BossStepType.Impact:
                        BossImpactFX.Spawn(step.impact, pos);
                        yield return new WaitForSeconds(step.impact.duration);
                        break;

                    case BossStepType.Wait:
                        yield return null;
                        break;
                }

                if (r < repeat - 1 && step.repeatInterval > 0f)
                    yield return new WaitForSeconds(step.repeatInterval);
            }
        }

        // ---------------------------------------------------------------- 배치
        void ResolvePlacement(BossStep step, int repeatIndex, out Vector3 pos, out Quaternion rot)
        {
            Vector3 center = arenaCenter != null ? arenaCenter.position : transform.position;

            switch (step.origin)
            {
                case BossOrigin.Self:
                    pos = transform.position;
                    break;
                case BossOrigin.Target:
                    pos = target != null ? target.position : transform.position;
                    break;
                case BossOrigin.TargetPredicted:
                    pos = PredictTarget();
                    break;
                case BossOrigin.ArenaCenter:
                    pos = center;
                    break;
                default:
                {
                    Vector2 c = Random.insideUnitCircle * arenaRadius;
                    pos = center + new Vector3(c.x, 0f, c.y);
                    break;
                }
            }

            if (step.randomOffset > 0f)
            {
                Vector2 c = Random.insideUnitCircle * step.randomOffset;
                pos += new Vector3(c.x, 0f, c.y);
            }

            // 회전
            if (step.faceTarget && target != null)
            {
                Vector3 to = target.position - pos;
                to.y = 0f;
                rot = to.sqrMagnitude > 1e-4f
                    ? Quaternion.LookRotation(to.normalized)
                    : transform.rotation;
            }
            else
            {
                rot = transform.rotation;
            }

            float yaw = step.angleOffset + step.repeatAngleStep * repeatIndex;
            if (Mathf.Abs(yaw) > 0.001f)
                rot = rot * Quaternion.Euler(0f, yaw, 0f);

            if (Mathf.Abs(step.forwardOffset) > 0.001f)
                pos += (rot * Vector3.forward) * step.forwardOffset;

            // 바닥에 붙이기 (아레나 높이 기준)
            pos.y = center.y;
        }

        Vector3 PredictTarget()
        {
            if (target == null) return transform.position;
            var rb = target.GetComponent<Rigidbody>();
            if (rb != null)
            {
                const float lead = 0.6f;
#if UNITY_6000_0_OR_NEWER
                Vector3 v = rb.linearVelocity;
#else
                Vector3 v = rb.velocity;
#endif
                return target.position + new Vector3(v.x, 0f, v.z) * lead;
            }
            var cc = target.GetComponent<CharacterController>();
            if (cc != null)
                return target.position + new Vector3(cc.velocity.x, 0f, cc.velocity.z) * 0.6f;
            return target.position;
        }

        // ---------------------------------------------------------------- 실행
        IEnumerator DoTelegraph(BossStep step, Vector3 pos, Quaternion rot)
        {
            bool fired = false;
            var settings = step.telegraph;

            BossTelegraph.Spawn(settings, pos, rot, () =>
            {
                fired = true;

                // 경고와 똑같은 모양으로 판정
                var hits = BossHitTest.Query(settings, pos, rot, targetMask);
                for (int i = 0; i < hits.Count; i++)
                {
                    onHit.Invoke(new BossHitInfo
                    {
                        collider = hits[i],
                        damage = step.damage,
                        label = step.label,
                        point = hits[i].bounds.center
                    });
                }

                if (step.impactOnFire)
                {
                    var im = step.impact;
                    // 장판 크기에 맞춰 충격파 반지름 자동 조정
                    var scaled = new BossImpactSettings
                    {
                        mode = im.mode,
                        radius = settings.shape == BossShape.Line
                                 ? settings.radius * 0.5f : settings.radius,
                        duration = im.duration,
                        thickness = im.thickness,
                        falloff = im.falloff,
                        coreColor = im.coreColor,
                        edgeColor = im.edgeColor,
                        intensity = im.intensity,
                        flatOnGround = true,
                        groundOffset = im.groundOffset,
                        expand = im.expand
                    };
                    BossImpactFX.Spawn(scaled, pos);
                }
            });

            float total = settings.fadeInTime + settings.chargeTime + settings.holdTime;
            float t = 0f;
            while (!fired && t < total + 1f)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }

        IEnumerator DoBeam(BossStep step, Vector3 pos, Quaternion rot)
        {
            var s = step.beam;
            BossBeamFX.Spawn(s, pos + Vector3.up * step.originHeight, rot, targetMask, col =>
            {
                onHit.Invoke(new BossHitInfo
                {
                    collider = col,
                    damage = step.damage,
                    label = step.label,
                    point = col.bounds.center
                });
            });

            yield return new WaitForSeconds(s.chargeTime + s.extendTime + s.fireTime);
        }

        IEnumerator DoBarrage(BossStep step, Vector3 pos, Quaternion rot)
        {
            // 탄막은 발사 원점이 필요하므로 임시 Transform 을 만든다
            var go = new GameObject("BarrageOrigin");
            go.transform.SetPositionAndRotation(pos + Vector3.up * step.originHeight, rot);

            yield return BossBarrage.Emit(step.barrage, go.transform, target, targetMask, col =>
            {
                onHit.Invoke(new BossHitInfo
                {
                    collider = col,
                    damage = step.damage,
                    label = step.label,
                    point = col.bounds.center
                });
            });

            Destroy(go);
        }

        void OnDrawGizmosSelected()
        {
            Vector3 c = arenaCenter != null ? arenaCenter.position : transform.position;
            Gizmos.color = new Color(0.6f, 0.3f, 1f, 0.5f);
            const int seg = 48;
            Vector3 prev = c + new Vector3(arenaRadius, 0f, 0f);
            for (int i = 1; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                Vector3 p = c + new Vector3(Mathf.Cos(a) * arenaRadius, 0f,
                                            Mathf.Sin(a) * arenaRadius);
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }
    }
}
