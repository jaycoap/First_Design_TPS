using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BossFX
{
    /// <summary>
    /// 바닥 경고 장판 하나의 설정값.
    /// 좌표 기준:
    ///   Circle / Ring / Cone → transform.position 이 '중심'(부채꼴은 꼭짓점)
    ///   Line                 → transform.position 이 '시작점', forward 방향으로 뻗음
    /// </summary>
    [System.Serializable]
    public class BossTelegraphSettings
    {
        [Header("모양")]
        public BossShape shape = BossShape.Circle;

        [Tooltip("원/링/부채꼴 = 반지름, 직선 = 길이 (월드 단위)")]
        public float radius = 4f;

        [Range(0f, 0.99f)]
        [Tooltip("링 안쪽 구멍 비율. 0.6 이면 안전지대가 60%")]
        public float innerRadius = 0.6f;

        [Range(0f, 360f)]
        public float coneAngle = 90f;

        [Tooltip("직선 두께 (월드 단위)")]
        public float lineWidth = 1.5f;

        [Header("타이밍")]
        public BossFillMode fillMode = BossFillMode.Radial;

        [Tooltip("경고가 0 → 100% 로 차오르는 시간")]
        public float chargeTime = 1.2f;

        [Tooltip("다 찬 뒤 발동까지 붙잡아두는 시간")]
        public float holdTime = 0.1f;

        public float fadeInTime = 0.12f;
        public float fadeOutTime = 0.25f;

        [Header("색")]
        public Color baseColor = new Color(0.35f, 0.12f, 0.75f, 1f);
        public Color hotColor  = new Color(1.00f, 0.18f, 0.35f, 1f);
        public Color edgeColor = new Color(0.85f, 0.55f, 1.00f, 1f);

        [Range(0f, 8f)] public float intensity = 1f;

        [Tooltip("Z-파이팅 방지용 바닥에서 띄우는 높이")]
        public float groundOffset = 0.05f;

        public BossTelegraphSettings Clone()
        {
            return (BossTelegraphSettings)MemberwiseClone();
        }
    }

    /// <summary>
    /// 경고 장판 인스턴스. 보통 직접 만들지 않고
    /// BossTelegraph.Spawn(...) 또는 BossPatternRunner 를 통해 사용합니다.
    /// </summary>
    [AddComponentMenu("BossFX/Boss Telegraph")]
    public class BossTelegraph : MonoBehaviour
    {
        MeshRenderer _renderer;
        MaterialPropertyBlock _mpb;
        Coroutine _routine;

        static Material _sharedMaterial;

        static Material SharedMaterial
        {
            get
            {
                if (_sharedMaterial == null)
                    _sharedMaterial = BossFXLibrary.CreateMaterial("BossFX/Telegraph");
                return _sharedMaterial;
            }
        }

        void Awake()
        {
            EnsureComponents();
        }

        void EnsureComponents()
        {
            if (_renderer != null) return;

            var mf = GetComponent<MeshFilter>();
            if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = BossFXLibrary.QuadXZ;

            _renderer = GetComponent<MeshRenderer>();
            if (_renderer == null) _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = SharedMaterial;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            _mpb = new MaterialPropertyBlock();
        }

        /// <summary>새 장판을 만들고 즉시 재생합니다.</summary>
        public static BossTelegraph Spawn(BossTelegraphSettings s, Vector3 position,
                                          Quaternion rotation, System.Action onFire = null)
        {
            var go = new GameObject("BossTelegraph");
            var tg = go.AddComponent<BossTelegraph>();
            tg.Play(s, position, rotation, onFire);
            return tg;
        }

        /// <summary>
        /// 장판을 배치하고 경고 → 발동 시퀀스를 재생합니다.
        /// onFire 는 경고가 다 찬 시점(= 판정 시점)에 호출됩니다.
        /// </summary>
        public void Play(BossTelegraphSettings s, Vector3 position, Quaternion rotation,
                         System.Action onFire = null)
        {
            EnsureComponents();

            // ---- 배치
            Vector3 pos = position + Vector3.up * s.groundOffset;
            if (s.shape == BossShape.Line)
            {
                // 직선은 시작점 기준이므로 중심을 앞으로 절반 밀어준다
                pos += (rotation * Vector3.forward) * (s.radius * 0.5f);
            }
            transform.SetPositionAndRotation(pos, rotation);

            // 쿼드는 1x1 이고 셰이더 좌표는 [-1,1] 이므로 지름 = 스케일
            float span = Mathf.Max(s.radius * 2f, 0.01f);
            if (s.shape == BossShape.Line) span = Mathf.Max(s.radius, 0.01f);
            transform.localScale = new Vector3(span, 1f, span);

            // ---- 셰이더 파라미터
            _mpb.Clear();
            _mpb.SetFloat(BossFXLibrary.PShape, (float)(int)s.shape);
            _mpb.SetFloat(BossFXLibrary.PInnerRadius, s.innerRadius);
            _mpb.SetFloat(BossFXLibrary.PConeAngle, s.coneAngle);
            _mpb.SetFloat(BossFXLibrary.PConeDirection, 0f);
            // 직선 두께는 정규화 값(월드 두께 / 길이)으로 넘긴다
            _mpb.SetFloat(BossFXLibrary.PLineWidth,
                          Mathf.Clamp(s.lineWidth / Mathf.Max(span, 1e-4f), 0.001f, 1f));
            _mpb.SetFloat(BossFXLibrary.PFillMode, (float)(int)s.fillMode);
            _mpb.SetColor(BossFXLibrary.PColorBase, s.baseColor);
            _mpb.SetColor(BossFXLibrary.PColorHot, s.hotColor);
            _mpb.SetColor(BossFXLibrary.PColorEdge, s.edgeColor);
            _mpb.SetFloat(BossFXLibrary.PIntensity, s.intensity);
            _mpb.SetFloat(BossFXLibrary.PFill, 0f);
            _mpb.SetFloat(BossFXLibrary.POpacity, 0f);
            _renderer.SetPropertyBlock(_mpb);

            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(Sequence(s, onFire));
        }

        IEnumerator Sequence(BossTelegraphSettings s, System.Action onFire)
        {
            // 1) 등장 페이드
            float t = 0f;
            while (t < s.fadeInTime)
            {
                t += Time.deltaTime;
                SetProps(0f, Mathf.Clamp01(t / Mathf.Max(s.fadeInTime, 1e-4f)));
                yield return null;
            }

            // 2) 경고 차오름
            t = 0f;
            while (t < s.chargeTime)
            {
                t += Time.deltaTime;
                SetProps(Mathf.Clamp01(t / Mathf.Max(s.chargeTime, 1e-4f)), 1f);
                yield return null;
            }
            SetProps(1f, 1f);

            // 3) 홀드
            if (s.holdTime > 0f) yield return new WaitForSeconds(s.holdTime);

            // 4) 발동 — 판정은 여기서
            onFire?.Invoke();

            // 5) 소멸 페이드
            t = 0f;
            while (t < s.fadeOutTime)
            {
                t += Time.deltaTime;
                SetProps(1f, 1f - Mathf.Clamp01(t / Mathf.Max(s.fadeOutTime, 1e-4f)));
                yield return null;
            }

            _routine = null;
            Destroy(gameObject);
        }

        void SetProps(float fill, float opacity)
        {
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(BossFXLibrary.PFill, fill);
            _mpb.SetFloat(BossFXLibrary.POpacity, opacity);
            _renderer.SetPropertyBlock(_mpb);
        }
    }

    /// <summary>
    /// 장판 모양과 똑같은 판정을 수행합니다.
    /// 경고로 보여준 영역과 실제 판정이 어긋나지 않도록,
    /// 셰이더와 같은 기준(중심/꼭짓점/시작점)을 씁니다.
    /// </summary>
    public static class BossHitTest
    {
        static readonly Collider[] _buffer = new Collider[64];

        /// <summary>해당 도형 안에 들어있는 콜라이더를 찾습니다.</summary>
        public static List<Collider> Query(BossTelegraphSettings s, Vector3 origin,
                                           Quaternion rotation, LayerMask mask,
                                           float verticalExtent = 3f)
        {
            var result = new List<Collider>();
            Vector3 forward = rotation * Vector3.forward;

            int count;
            if (s.shape == BossShape.Line)
            {
                Vector3 center = origin + forward * (s.radius * 0.5f);
                Vector3 half = new Vector3(s.lineWidth * 0.5f, verticalExtent, s.radius * 0.5f);
                count = Physics.OverlapBoxNonAlloc(center, half, _buffer, rotation, mask,
                                                   QueryTriggerInteraction.Collide);
                for (int i = 0; i < count; i++) result.Add(_buffer[i]);
                return result;
            }

            count = Physics.OverlapSphereNonAlloc(origin, s.radius, _buffer, mask,
                                                  QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var c = _buffer[i];
                Vector3 to = c.bounds.center - origin;
                to.y = 0f;
                float dist = to.magnitude;

                if (dist > s.radius) continue;

                if (s.shape == BossShape.Ring)
                {
                    if (dist < s.radius * s.innerRadius) continue;   // 가운데 안전지대
                }
                else if (s.shape == BossShape.Cone)
                {
                    if (dist > 1e-4f)
                    {
                        float ang = Vector3.Angle(forward, to.normalized);
                        if (ang > s.coneAngle * 0.5f) continue;
                    }
                }
                result.Add(c);
            }
            return result;
        }
    }
}
