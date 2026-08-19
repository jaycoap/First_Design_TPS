using System.Collections.Generic;
using UnityEngine;

namespace BossFX.Samples
{
    /// <summary>
    /// 원클릭 데모.
    /// 빈 GameObject 에 이 스크립트만 붙이고 Play 를 누르면
    /// 바닥 / 더미 플레이어 / 보스 / 패턴 5종이 전부 코드로 생성됩니다.
    ///
    /// 플레이어는 WASD 로 움직입니다. 장판에 맞으면 콘솔에 로그가 찍힙니다.
    /// 마음에 드는 패턴이 있으면 BuildPatterns() 의 값을 그대로
    /// BossPattern 에셋에 옮겨 담으면 됩니다.
    /// </summary>
    [AddComponentMenu("BossFX/Samples/Boss FX Demo")]
    public class BossFXDemo : MonoBehaviour
    {
        [Header("데모 구성")]
        public bool createFloor = true;
        public bool createDummyPlayer = true;
        public float arenaRadius = 16f;
        public float playerSpeed = 8f;

        [Header("색")]
        public Color warnColor = new Color(0.35f, 0.12f, 0.75f);
        public Color dangerColor = new Color(1f, 0.18f, 0.35f);
        public Color edgeColor = new Color(0.85f, 0.55f, 1f);

        Transform _player;
        BossPatternRunner _runner;

        void Start()
        {
            if (createFloor) BuildFloor();
            if (createDummyPlayer) _player = BuildPlayer();

            var bossGo = new GameObject("Boss");
            bossGo.transform.position = Vector3.zero;

            _runner = bossGo.AddComponent<BossPatternRunner>();
            _runner.target = _player;
            _runner.arenaRadius = arenaRadius;
            _runner.loop = true;
            _runner.logSteps = true;
            // 데모에서는 Default 레이어의 플레이어를 맞히기 위해 전부 허용.
            // 실제 프로젝트에서는 반드시 플레이어 레이어만 지정하세요.
            _runner.targetMask = ~0;
            _runner.patterns = BuildPatterns();
            _runner.onHit.AddListener(info =>
                Debug.Log($"[BossFX] HIT! {info.collider.name} ← {info.label} " +
                          $"(damage {info.damage})"));

            if (Camera.main == null)
            {
                var cam = new GameObject("Main Camera").AddComponent<Camera>();
                cam.tag = "MainCamera";
                cam.transform.position = new Vector3(0f, 22f, -22f);
                cam.transform.rotation = Quaternion.Euler(42f, 0f, 0f);
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.015f, 0.01f, 0.03f);
            }
        }

        void Update()
        {
            if (_player == null) return;
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            var move = new Vector3(h, 0f, v);
            if (move.sqrMagnitude > 1f) move.Normalize();
            _player.position += move * (playerSpeed * Time.deltaTime);
        }

        // ------------------------------------------------------------ 씬 구성
        void BuildFloor()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "DemoFloor";
            floor.transform.localScale = Vector3.one * (arenaRadius * 0.25f);
            var mr = floor.GetComponent<MeshRenderer>();
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (sh != null)
            {
                var m = new Material(sh);
                m.color = new Color(0.05f, 0.045f, 0.07f);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.35f);
                mr.sharedMaterial = m;
            }
        }

        Transform BuildPlayer()
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            p.name = "DummyPlayer";
            p.transform.position = new Vector3(0f, 1f, -6f);
            var col = p.GetComponent<Collider>();
            col.isTrigger = false;
            var mr = p.GetComponent<MeshRenderer>();
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (sh != null)
            {
                var m = new Material(sh);
                m.color = new Color(0.3f, 0.8f, 1f);
                mr.sharedMaterial = m;
            }
            return p.transform;
        }

        // ------------------------------------------------------------ 패턴
        List<BossPattern> BuildPatterns()
        {
            var list = new List<BossPattern>
            {
                MakeSlam(),
                MakeDonut(),
                MakeConeSweep(),
                MakeLaserSweep(),
                MakeSpiralBarrage()
            };
            return list;
        }

        BossTelegraphSettings Warn(BossShape shape, float radius, float charge)
        {
            return new BossTelegraphSettings
            {
                shape = shape,
                radius = radius,
                chargeTime = charge,
                baseColor = warnColor,
                hotColor = dangerColor,
                edgeColor = edgeColor,
                intensity = 1.1f
            };
        }

        BossPattern MakeSlam()
        {
            var p = ScriptableObject.CreateInstance<BossPattern>();
            p.name = "1_슬램 (플레이어 위치 원형 3연타)";
            p.description = "플레이어 발밑을 예측해서 원형 장판 3연타";
            p.recoveryTime = 1.2f;
            p.steps.Add(new BossStep
            {
                label = "슬램",
                type = BossStepType.Telegraph,
                origin = BossOrigin.TargetPredicted,
                telegraph = Warn(BossShape.Circle, 4f, 1.0f),
                repeat = 3,
                repeatInterval = 0.45f,
                randomOffset = 1.5f,
                damage = 18f,
                delayAfter = 0.5f
            });
            return p;
        }

        BossPattern MakeDonut()
        {
            var p = ScriptableObject.CreateInstance<BossPattern>();
            p.name = "2_도넛 (가운데로 피하기)";
            p.description = "바깥이 위험, 중앙이 안전지대";
            var t = Warn(BossShape.Ring, 14f, 1.6f);
            t.innerRadius = 0.35f;
            p.steps.Add(new BossStep
            {
                label = "도넛",
                type = BossStepType.Telegraph,
                origin = BossOrigin.ArenaCenter,
                faceTarget = false,
                telegraph = t,
                damage = 30f,
                delayAfter = 0.8f
            });
            return p;
        }

        BossPattern MakeConeSweep()
        {
            var p = ScriptableObject.CreateInstance<BossPattern>();
            p.name = "3_부채꼴 스윕 (좌→우 훑기)";
            var t = Warn(BossShape.Cone, 13f, 0.7f);
            t.coneAngle = 70f;
            t.fillMode = BossFillMode.Angular;
            p.steps.Add(new BossStep
            {
                label = "부채꼴",
                type = BossStepType.Telegraph,
                origin = BossOrigin.Self,
                faceTarget = true,
                telegraph = t,
                repeat = 4,
                repeatInterval = 0.3f,
                repeatAngleStep = 55f,     // 반복할 때마다 옆으로 훑음
                damage = 22f,
                delayAfter = 0.6f
            });
            return p;
        }

        BossPattern MakeLaserSweep()
        {
            var p = ScriptableObject.CreateInstance<BossPattern>();
            p.name = "4_레이저 (경고선 → 회전 빔)";
            var t = Warn(BossShape.Line, 34f, 0.9f);
            t.lineWidth = 2.2f;
            t.fillMode = BossFillMode.Linear;

            p.steps.Add(new BossStep
            {
                label = "레이저 경고",
                type = BossStepType.Telegraph,
                origin = BossOrigin.Self,
                telegraph = t,
                impactOnFire = false,
                damage = 0f,
                delayAfter = 0f
            });
            p.steps.Add(new BossStep
            {
                label = "레이저 발사",
                type = BossStepType.Beam,
                origin = BossOrigin.Self,
                beam = new BossBeamSettings
                {
                    length = 34f,
                    width = 2.2f,
                    chargeTime = 0.25f,
                    fireTime = 1.6f,
                    sweepAngle = 120f,
                    coreColor = new Color(1f, 0.9f, 1f),
                    glowColor = new Color(0.55f, 0.2f, 1f),
                    intensity = 4f
                },
                damage = 8f,
                delayAfter = 0.5f
            });
            return p;
        }

        BossPattern MakeSpiralBarrage()
        {
            var p = ScriptableObject.CreateInstance<BossPattern>();
            p.name = "5_나선 탄막";
            p.recoveryTime = 2f;
            p.steps.Add(new BossStep
            {
                label = "나선",
                type = BossStepType.Barrage,
                origin = BossOrigin.Self,
                faceTarget = false,
                barrage = new BossBarrageSettings
                {
                    shape = BossBarrageShape.Spiral,
                    countPerWave = 8,
                    waves = 14,
                    waveInterval = 0.13f,
                    spiralStep = 13f,
                    bullet = new BossBulletSettings
                    {
                        size = 0.5f,
                        speed = 9f,
                        lifeTime = 5f,
                        radius = 0.4f,
                        coreColor = new Color(1f, 0.85f, 1f),
                        edgeColor = new Color(0.6f, 0.2f, 1f),
                        intensity = 4f
                    }
                },
                damage = 12f,
                delayAfter = 0.8f
            });
            return p;
        }
    }
}
