using System.Collections;
using UnityEngine;

namespace BossFX
{
    /// <summary>충격파 / 폭발 섬광 설정.</summary>
    [System.Serializable]
    public class BossImpactSettings
    {
        public BossRadialMode mode = BossRadialMode.Ring;

        [Tooltip("최종 반지름 (월드 단위)")]
        public float radius = 6f;

        [Tooltip("퍼지는 데 걸리는 시간")]
        public float duration = 0.45f;

        [Range(0.001f, 1f)] public float thickness = 0.12f;
        [Range(0.5f, 8f)]   public float falloff = 2f;

        public Color coreColor = new Color(1f, 0.85f, 1f, 1f);
        public Color edgeColor = new Color(0.6f, 0.2f, 1f, 1f);

        [Range(0f, 16f)] public float intensity = 4f;

        [Tooltip("XZ 평면에 눕힐지(바닥 충격파) / 카메라를 향할지(공중 폭발)")]
        public bool flatOnGround = true;

        public float groundOffset = 0.08f;

        [Tooltip("퍼지는 속도 곡선. 처음 빠르고 뒤에 느려지는 게 자연스럽습니다")]
        public AnimationCurve expand = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    }

    /// <summary>충격파 링 / 구형 섬광 / 방사형 폭발.</summary>
    [AddComponentMenu("BossFX/Boss Impact FX")]
    public class BossImpactFX : MonoBehaviour
    {
        MeshRenderer _renderer;
        MaterialPropertyBlock _mpb;
        static Material _sharedMaterial;

        static Material SharedMaterial
        {
            get
            {
                if (_sharedMaterial == null)
                    _sharedMaterial = BossFXLibrary.CreateMaterial("BossFX/Radial");
                return _sharedMaterial;
            }
        }

        public static BossImpactFX Spawn(BossImpactSettings s, Vector3 position)
        {
            var go = new GameObject("BossImpactFX");
            var fx = go.AddComponent<BossImpactFX>();
            fx.Play(s, position);
            return fx;
        }

        void EnsureComponents()
        {
            if (_renderer != null) return;
            // ※ Unity Object 에 ?? 를 쓰면 안 됩니다.
            //   Unity 는 == 을 오버로드해 "파괴됨"을 null 로 취급하는데
            //   ?? 는 그 오버로드를 우회해서 파괴된 객체를 통과시킵니다.
            var mf = GetComponent<MeshFilter>();
            if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = BossFXLibrary.QuadXZ;

            _renderer = GetComponent<MeshRenderer>();
            if (_renderer == null) _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = SharedMaterial;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _mpb = new MaterialPropertyBlock();
        }

        public void Play(BossImpactSettings s, Vector3 position)
        {
            EnsureComponents();
            transform.position = position + Vector3.up * (s.flatOnGround ? s.groundOffset : 0f);
            if (!s.flatOnGround && Camera.main != null)
            {
                // 카메라를 향해 세움 (XZ 쿼드를 눕힌 상태에서 회전)
                Vector3 dir = Camera.main.transform.position - transform.position;
                if (dir.sqrMagnitude > 1e-4f)
                    transform.rotation = Quaternion.LookRotation(dir.normalized) *
                                         Quaternion.Euler(90f, 0f, 0f);
            }
            else
            {
                transform.rotation = Quaternion.identity;
            }

            _mpb.Clear();
            _mpb.SetFloat(BossFXLibrary.PMode, (float)(int)s.mode);
            _mpb.SetColor(BossFXLibrary.PColorCore, s.coreColor);
            _mpb.SetColor(BossFXLibrary.PColorEdge, s.edgeColor);
            _mpb.SetFloat(BossFXLibrary.PThickness, s.thickness);
            _mpb.SetFloat(BossFXLibrary.PFalloff, s.falloff);
            _mpb.SetFloat(BossFXLibrary.PIntensity, s.intensity);
            _renderer.SetPropertyBlock(_mpb);

            StartCoroutine(Sequence(s));
        }

        IEnumerator Sequence(BossImpactSettings s)
        {
            float t = 0f;
            float span = Mathf.Max(s.radius * 2f, 0.01f);
            transform.localScale = new Vector3(span, 1f, span);

            while (t < s.duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / Mathf.Max(s.duration, 1e-4f));
                float e = s.expand != null ? s.expand.Evaluate(k) : k;

                _renderer.GetPropertyBlock(_mpb);
                if (s.mode == BossRadialMode.Ring)
                {
                    // 링은 스케일 고정, 셰이더 안에서 반지름만 키운다 (테두리 두께 유지)
                    _mpb.SetFloat(BossFXLibrary.PRadius, Mathf.Lerp(0.02f, 0.98f, e));
                }
                else
                {
                    // 구/섬광은 오브젝트를 키운다
                    float sc = span * Mathf.Lerp(0.15f, 1f, e);
                    transform.localScale = new Vector3(sc, 1f, sc);
                }
                _mpb.SetFloat(BossFXLibrary.POpacity, 1f - k * k);   // 뒤로 갈수록 사라짐
                _renderer.SetPropertyBlock(_mpb);
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
