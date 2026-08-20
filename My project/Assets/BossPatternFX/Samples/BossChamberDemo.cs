using UnityEngine;

namespace BossFX.Samples
{
    /// <summary>
    /// 챔버 맵에 보스 패턴을 바로 얹어보는 데모.
    ///
    /// 사용법
    ///   1) 챔버 FBX 를 씬에 배치 (중심이 원점, 스케일 1 이어야 합니다)
    ///   2) 빈 GameObject 에 이 스크립트를 붙이고 Play
    ///
    /// 에셋을 안 만들어도 코드에서 패턴 6종을 바로 생성해 돌립니다.
    /// 에셋으로 남기고 싶으면 메뉴 BossFX ▸ 1. 챔버 보스 패턴 에셋 만들기 를 쓰세요.
    /// </summary>
    [AddComponentMenu("BossFX/Samples/Boss Chamber Demo")]
    public class BossChamberDemo : MonoBehaviour
    {
        [Header("플레이어")]
        [Tooltip("비워두면 더미 캡슐을 만들어 WASD 로 움직입니다")]
        public Transform player;
        public float playerSpeed = 9f;

        [Tooltip("판정에 걸릴 레이어. 실제 프로젝트에선 플레이어 레이어만 지정하세요")]
        public LayerMask targetMask = ~0;

        [Header("연출")]
        [Tooltip("챔버가 없을 때 바닥 대용 원판을 깔지")]
        public bool createFallbackFloor = true;

        [Tooltip("패턴을 순서대로 / 무작위로")]
        public bool randomOrder = false;

        [Tooltip("특정 패턴 하나만 반복해서 보고 싶을 때 (0 = 전부)")]
        [Range(0, 6)] public int onlyPatternIndex = 0;

        BossPatternRunner _runner;

        void Start()
        {
            if (player == null) player = BuildDummyPlayer();
            if (createFallbackFloor && !SceneHasFloor()) BuildFallbackFloor();

            var bossGo = new GameObject("Boss");
            bossGo.transform.position =
                new Vector3(0f, BossChamberLayout.PlatformDeckY, 0f);

            var centerGo = new GameObject("ArenaCenter");
            centerGo.transform.position = Vector3.zero;   // 바닥 높이

            _runner = bossGo.AddComponent<BossPatternRunner>();
            _runner.target = player;
            _runner.targetMask = targetMask;
            _runner.arenaCenter = centerGo.transform;
            _runner.arenaRadius = BossChamberLayout.RoomApothem;
            _runner.randomOrder = randomOrder;
            _runner.loop = true;
            _runner.logSteps = true;

            var all = BossChamberPatterns.All();
            if (onlyPatternIndex > 0 && onlyPatternIndex <= all.Count)
            {
                _runner.patterns.Add(all[onlyPatternIndex - 1]);
            }
            else
            {
                _runner.patterns.AddRange(all);
            }

            _runner.onHit.AddListener(info =>
                Debug.Log($"[BossFX] HIT  {info.collider.name} ← {info.label}  " +
                          $"(damage {info.damage})"));
            _runner.onStepStart.AddListener(label =>
                Debug.Log($"[BossFX] ▶ {label}"));

            SetupCamera();
        }

        void Update()
        {
            if (player == null) return;
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            var move = new Vector3(h, 0f, v);
            if (move.sqrMagnitude > 1f) move.Normalize();
            player.position += move * (playerSpeed * Time.deltaTime);

            // 아레나 밖으로 못 나가게
            Vector3 p = player.position;
            Vector2 flat = new Vector2(p.x, p.z);
            float max = BossChamberLayout.RoomApothem - 1.5f;
            if (flat.magnitude > max)
            {
                flat = flat.normalized * max;
                player.position = new Vector3(flat.x, p.y, flat.y);
            }
        }

        // ------------------------------------------------------------------
        bool SceneHasFloor()
        {
            // 챔버가 이미 씬에 있으면 바닥을 또 깔지 않습니다
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>(
                         FindObjectsSortMode.None))
            {
                if (mr.name.Contains("Chamber") || mr.name.Contains("chamber"))
                    return true;
            }
            return false;
        }

        void BuildFallbackFloor()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            floor.name = "FallbackFloor";
            floor.transform.localScale =
                new Vector3(BossChamberLayout.RoomApothem * 2f, 0.1f,
                            BossChamberLayout.RoomApothem * 2f);
            floor.transform.position = new Vector3(0f, -0.1f, 0f);

            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (sh != null)
            {
                var m = new Material(sh) { color = new Color(0.05f, 0.045f, 0.07f) };
                floor.GetComponent<MeshRenderer>().sharedMaterial = m;
            }

            // 중앙 플랫폼 대용
            var plat = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            plat.name = "FallbackPlatform";
            plat.transform.localScale =
                new Vector3(BossChamberLayout.PlatformRadius * 2f,
                            BossChamberLayout.PlatformDeckY * 0.5f,
                            BossChamberLayout.PlatformRadius * 2f);
            plat.transform.position =
                new Vector3(0f, BossChamberLayout.PlatformDeckY * 0.5f, 0f);
            if (sh != null)
            {
                var m = new Material(sh) { color = new Color(0.09f, 0.08f, 0.12f) };
                plat.GetComponent<MeshRenderer>().sharedMaterial = m;
            }
        }

        Transform BuildDummyPlayer()
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            p.name = "DummyPlayer";
            // 플랫폼 바깥 링 위에 세웁니다
            p.transform.position =
                new Vector3(0f, 1f, -BossChamberLayout.FloorRingMid);
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (sh != null)
            {
                var m = new Material(sh) { color = new Color(0.3f, 0.85f, 1f) };
                p.GetComponent<MeshRenderer>().sharedMaterial = m;
            }
            return p.transform;
        }

        void SetupCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
            }
            // 챔버 개방부(+Z) 를 등지고 내려다보는 각도
            cam.transform.position = new Vector3(0f, 26f, -34f);
            cam.transform.rotation = Quaternion.Euler(38f, 0f, 0f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.012f, 0.008f, 0.025f);
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, 3000f);
        }
    }
}
