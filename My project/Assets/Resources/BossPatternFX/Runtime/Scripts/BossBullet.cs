using System.Collections.Generic;
using UnityEngine;

namespace BossFX
{
    /// <summary>탄환 한 발의 설정.</summary>
    [System.Serializable]
    public class BossBulletSettings
    {
        public float size = 0.6f;
        public float speed = 12f;

        [Tooltip("초당 속도 변화. 음수면 감속 후 역주행 같은 연출도 가능")]
        public float acceleration = 0f;

        [Tooltip("초당 진행 방향 회전(도). 0 이 아니면 휘어지는 탄")]
        public float curve = 0f;

        [Tooltip("타깃을 향해 돌아가는 정도(초당 도). 0 이면 유도 없음")]
        public float homing = 0f;

        public float lifeTime = 6f;
        public float radius = 0.35f;      // 판정 반지름

        public Color coreColor = new Color(1f, 0.85f, 1f, 1f);
        public Color edgeColor = new Color(0.6f, 0.2f, 1f, 1f);
        [Range(0f, 16f)] public float intensity = 4f;

        [Tooltip("맞았을 때 작은 폭발을 남길지")]
        public bool impactOnHit = true;
    }

    /// <summary>
    /// 탄환. 풀링되며, 맞으면 onHit 을 호출하고 스스로 반환됩니다.
    /// </summary>
    [AddComponentMenu("BossFX/Boss Bullet")]
    public class BossBullet : MonoBehaviour
    {
        static readonly Stack<BossBullet> _pool = new Stack<BossBullet>();
        static Material _sharedMaterial;
        static readonly Collider[] _hitBuffer = new Collider[8];

        static Material SharedMaterial
        {
            get
            {
                if (_sharedMaterial == null)
                    _sharedMaterial = BossFXLibrary.CreateMaterial("BossFX/Radial");
                return _sharedMaterial;
            }
        }

        MeshRenderer _renderer;
        MaterialPropertyBlock _mpb;

        BossBulletSettings _s;
        Vector3 _dir;
        float _speed;
        float _life;
        LayerMask _mask;
        Transform _target;
        System.Action<Collider> _onHit;
        bool _active;

        /// <summary>풀에서 하나 꺼내 발사합니다.</summary>
        public static BossBullet Fire(BossBulletSettings s, Vector3 position, Vector3 direction,
                                      LayerMask mask, Transform homingTarget = null,
                                      System.Action<Collider> onHit = null)
        {
            BossBullet b = _pool.Count > 0 ? _pool.Pop() : Create();
            b.gameObject.SetActive(true);
            b.Launch(s, position, direction, mask, homingTarget, onHit);
            return b;
        }

        static BossBullet Create()
        {
            var go = new GameObject("BossBullet");
            var b = go.AddComponent<BossBullet>();
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = BossFXLibrary.QuadXZ;
            b._renderer = go.AddComponent<MeshRenderer>();
            b._renderer.sharedMaterial = SharedMaterial;
            b._renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            b._renderer.receiveShadows = false;
            b._mpb = new MaterialPropertyBlock();
            return b;
        }

        void Launch(BossBulletSettings s, Vector3 position, Vector3 direction, LayerMask mask,
                    Transform homingTarget, System.Action<Collider> onHit)
        {
            _s = s;
            _dir = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
            _speed = s.speed;
            _life = s.lifeTime;
            _mask = mask;
            _target = homingTarget;
            _onHit = onHit;
            _active = true;

            transform.position = position;
            transform.localScale = Vector3.one * Mathf.Max(s.size, 0.01f) * 2f;

            _mpb.Clear();
            _mpb.SetFloat(BossFXLibrary.PMode, (float)(int)BossRadialMode.Orb);
            _mpb.SetColor(BossFXLibrary.PColorCore, s.coreColor);
            _mpb.SetColor(BossFXLibrary.PColorEdge, s.edgeColor);
            _mpb.SetFloat(BossFXLibrary.PThickness, 0.45f);
            _mpb.SetFloat(BossFXLibrary.PFalloff, 2.2f);
            _mpb.SetFloat(BossFXLibrary.PIntensity, s.intensity);
            _mpb.SetFloat(BossFXLibrary.POpacity, 1f);
            _renderer.SetPropertyBlock(_mpb);
        }

        void Update()
        {
            if (!_active) return;

            float dt = Time.deltaTime;
            _life -= dt;
            if (_life <= 0f) { Release(); return; }

            _speed += _s.acceleration * dt;

            if (Mathf.Abs(_s.curve) > 0.001f)
                _dir = Quaternion.Euler(0f, _s.curve * dt, 0f) * _dir;

            if (_s.homing > 0.001f && _target != null)
            {
                Vector3 want = (_target.position - transform.position);
                want.y = 0f;
                if (want.sqrMagnitude > 1e-4f)
                    _dir = Vector3.RotateTowards(_dir, want.normalized,
                                                 _s.homing * Mathf.Deg2Rad * dt, 0f).normalized;
            }

            transform.position += _dir * (_speed * dt);

            int n = Physics.OverlapSphereNonAlloc(transform.position, _s.radius, _hitBuffer,
                                                  _mask, QueryTriggerInteraction.Collide);
            if (n > 0)
            {
                _onHit?.Invoke(_hitBuffer[0]);
                if (_s.impactOnHit)
                {
                    BossImpactFX.Spawn(new BossImpactSettings
                    {
                        mode = BossRadialMode.Burst,
                        radius = _s.size * 3f,
                        duration = 0.22f,
                        coreColor = _s.coreColor,
                        edgeColor = _s.edgeColor,
                        intensity = _s.intensity * 1.4f,
                        flatOnGround = false
                    }, transform.position);
                }
                Release();
            }
        }

        void LateUpdate()
        {
            // 카메라를 향하게 (XZ 쿼드를 세워서 빌보드)
            if (!_active || Camera.main == null) return;
            Vector3 dir = Camera.main.transform.position - transform.position;
            if (dir.sqrMagnitude < 1e-4f) return;
            transform.rotation = Quaternion.LookRotation(dir.normalized) *
                                 Quaternion.Euler(90f, 0f, 0f);
        }

        void Release()
        {
            _active = false;
            _onHit = null;
            _target = null;
            gameObject.SetActive(false);
            _pool.Push(this);
        }

        /// <summary>씬 전환 등에서 풀을 비울 때.</summary>
        public static void ClearPool()
        {
            while (_pool.Count > 0)
            {
                var b = _pool.Pop();
                if (b != null) Destroy(b.gameObject);
            }
        }
    }
}
