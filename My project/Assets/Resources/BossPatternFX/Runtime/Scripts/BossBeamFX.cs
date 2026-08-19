using System.Collections;
using UnityEngine;

namespace BossFX
{
    /// <summary>레이저 빔 설정. transform.position 에서 forward 방향으로 뻗습니다.</summary>
    [System.Serializable]
    public class BossBeamSettings
    {
        public float length = 30f;
        public float width = 1.6f;

        [Header("타이밍")]
        [Tooltip("가느다란 예열선이 보이는 시간")]
        public float chargeTime = 0.9f;

        [Tooltip("빔이 끝까지 뻗는 시간")]
        public float extendTime = 0.08f;

        [Tooltip("최대 폭으로 유지되는 시간 (이 동안 판정)")]
        public float fireTime = 0.5f;

        public float fadeOutTime = 0.2f;

        [Header("회전 스윕")]
        [Tooltip("발사 중 Y축으로 회전하는 각도. 0 이면 고정")]
        public float sweepAngle = 0f;

        [Header("색")]
        public Color coreColor = new Color(1f, 0.9f, 1f, 1f);
        public Color glowColor = new Color(0.55f, 0.2f, 1f, 1f);
        [Range(0f, 12f)] public float intensity = 3.5f;

        [Tooltip("카메라를 향해 빔 판을 회전시킬지 (3D 공간 빔이면 켜세요)")]
        public bool billboard = true;

        [Tooltip("판정 주기(초). 0.1 이면 초당 10번 검사")]
        public float hitInterval = 0.1f;
    }

    /// <summary>레이저 빔. 예열 → 발사 → 소멸.</summary>
    [AddComponentMenu("BossFX/Boss Beam FX")]
    public class BossBeamFX : MonoBehaviour
    {
        MeshRenderer _renderer;
        MaterialPropertyBlock _mpb;
        Transform _quad;
        static Material _sharedMaterial;

        static Material SharedMaterial
        {
            get
            {
                if (_sharedMaterial == null)
                    _sharedMaterial = BossFXLibrary.CreateMaterial("BossFX/Beam");
                return _sharedMaterial;
            }
        }

        public static BossBeamFX Spawn(BossBeamSettings s, Vector3 origin, Quaternion rotation,
                                       LayerMask hitMask, System.Action<Collider> onHit = null)
        {
            var go = new GameObject("BossBeamFX");
            var fx = go.AddComponent<BossBeamFX>();
            fx.Play(s, origin, rotation, hitMask, onHit);
            return fx;
        }

        void EnsureComponents()
        {
            if (_renderer != null) return;

            // 빔 판은 자식으로 둡니다. 부모는 방향만 담당하고
            // 자식이 카메라를 향해 축 회전(빌보드)합니다.
            var child = new GameObject("BeamQuad");
            _quad = child.transform;
            _quad.SetParent(transform, false);

            var mf = child.AddComponent<MeshFilter>();
            mf.sharedMesh = BossFXLibrary.QuadForward;
            _renderer = child.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = SharedMaterial;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _mpb = new MaterialPropertyBlock();
        }

        BossBeamSettings _s;
        bool _billboard;

        public void Play(BossBeamSettings s, Vector3 origin, Quaternion rotation,
                         LayerMask hitMask, System.Action<Collider> onHit = null)
        {
            EnsureComponents();
            _s = s;
            _billboard = s.billboard;

            transform.SetPositionAndRotation(origin, rotation);

            // QuadForward 는 +X 로 뻗으므로, 부모의 forward(+Z)에 맞춰 자식을 돌려둔다
            _quad.localRotation = Quaternion.Euler(0f, -90f, 0f);
            _quad.localScale = new Vector3(s.length, s.width, 1f);

            _mpb.Clear();
            _mpb.SetColor(BossFXLibrary.PColorCore, s.coreColor);
            _mpb.SetColor(BossFXLibrary.PColorGlow, s.glowColor);
            _mpb.SetFloat(BossFXLibrary.PIntensity, s.intensity);
            _mpb.SetFloat(BossFXLibrary.PCharge, 0f);
            _mpb.SetFloat(BossFXLibrary.PFire, 1f);
            _mpb.SetFloat(BossFXLibrary.POpacity, 0f);
            _renderer.SetPropertyBlock(_mpb);

            StartCoroutine(Sequence(s, hitMask, onHit));
        }

        void LateUpdate()
        {
            if (!_billboard || _quad == null || Camera.main == null) return;
            // 빔 축(부모 forward)을 유지한 채, 판이 카메라를 향하도록 축 중심 회전
            Vector3 axis = transform.forward;
            Vector3 toCam = Camera.main.transform.position - transform.position;
            Vector3 up = Vector3.Cross(axis, toCam);
            if (up.sqrMagnitude < 1e-6f) return;
            Quaternion world = Quaternion.LookRotation(axis, Vector3.Cross(up, axis).normalized);
            _quad.rotation = world * Quaternion.Euler(0f, -90f, 0f);
        }

        IEnumerator Sequence(BossBeamSettings s, LayerMask hitMask, System.Action<Collider> onHit)
        {
            // 1) 예열 — 가느다란 선
            float t = 0f;
            while (t < s.chargeTime)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / Mathf.Max(s.chargeTime, 1e-4f));
                SetProps(charge: 0f, fire: 1f, opacity: Mathf.Lerp(0.35f, 0.75f, k));
                yield return null;
            }

            // 2) 발사 — 폭 확장
            t = 0f;
            while (t < s.extendTime)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / Mathf.Max(s.extendTime, 1e-4f));
                SetProps(charge: k, fire: 1f, opacity: 1f);
                yield return null;
            }

            // 3) 유지 — 이 동안 판정 + 스윕
            float fired = 0f;
            float nextHit = 0f;
            float sweptSoFar = 0f;
            Quaternion startRot = transform.rotation;
            while (fired < s.fireTime)
            {
                fired += Time.deltaTime;
                SetProps(charge: 1f, fire: 1f, opacity: 1f);

                if (Mathf.Abs(s.sweepAngle) > 0.01f)
                {
                    float k = Mathf.Clamp01(fired / Mathf.Max(s.fireTime, 1e-4f));
                    sweptSoFar = s.sweepAngle * k;
                    transform.rotation = startRot * Quaternion.Euler(0f, sweptSoFar, 0f);
                }

                if (onHit != null && fired >= nextHit)
                {
                    nextHit = fired + Mathf.Max(s.hitInterval, 0.02f);
                    DoHit(s, hitMask, onHit);
                }
                yield return null;
            }

            // 4) 소멸
            t = 0f;
            while (t < s.fadeOutTime)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / Mathf.Max(s.fadeOutTime, 1e-4f));
                SetProps(charge: 1f, fire: 1f, opacity: 1f - k);
                yield return null;
            }
            Destroy(gameObject);
        }

        void DoHit(BossBeamSettings s, LayerMask mask, System.Action<Collider> onHit)
        {
            Vector3 center = transform.position + transform.forward * (s.length * 0.5f);
            Vector3 half = new Vector3(s.width * 0.5f, s.width * 0.5f, s.length * 0.5f);
            var hits = Physics.OverlapBox(center, half, transform.rotation, mask,
                                          QueryTriggerInteraction.Collide);
            for (int i = 0; i < hits.Length; i++) onHit(hits[i]);
        }

        void SetProps(float charge, float fire, float opacity)
        {
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(BossFXLibrary.PCharge, charge);
            _mpb.SetFloat(BossFXLibrary.PFire, fire);
            _mpb.SetFloat(BossFXLibrary.POpacity, opacity);
            _renderer.SetPropertyBlock(_mpb);
        }
    }
}
