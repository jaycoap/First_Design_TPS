using System.Collections.Generic;
using UnityEngine;

namespace BossFX
{
    /// <summary>
    /// 사이파이 지휘실(챔버) 맵의 실측 치수.
    /// scifi_command_chamber.py 의 CONFIG / build_platform / build_props 값과 일치합니다.
    ///
    /// 좌표계 주의 — Blender → Unity FBX 기본 임포트 기준입니다.
    ///   Blender (x, y, z)  →  Unity (x, z, y)
    ///   Blender +Y (개방부 방향) → Unity +Z
    /// 따라서 반지름 수치는 그대로, 높이만 Y 로 옮겨 적었습니다.
    /// 임포트 축 설정을 바꾸셨다면 OpenBayYaw 만 고치면 됩니다.
    /// </summary>
    public static class BossChamberLayout
    {
        /// <summary>중심 → 벽 안쪽면 (팔각형의 내접원 반지름)</summary>
        public const float RoomApothem = 26f;

        /// <summary>중심 → 팔각 모서리</summary>
        public const float RoomCircumradius = 28.1f;

        /// <summary>중앙 플랫폼 하부 드럼 반지름</summary>
        public const float PlatformRadius = 11f;

        /// <summary>플랫폼 최상단(코어) 높이</summary>
        public const float PlatformTopY = 2.19f;

        /// <summary>보스가 서 있기 좋은 3단 데크 높이</summary>
        public const float PlatformDeckY = 1.75f;

        /// <summary>플랫폼 둘레 인레이 링 (플레이어 활동 영역 시작)</summary>
        public const float InlayInner = 11.7f;
        public const float InlayOuter = 15.6f;

        /// <summary>콘솔 오벨리스크 8기가 놓인 반지름</summary>
        public const float ConsoleRadius = 14.2f;
        public const int ConsoleCount = 8;

        /// <summary>천장 높이</summary>
        public const float CeilingY = 17f;

        /// <summary>벽이 열려 있는 방향 (Unity +Z). 팔각형 한 변의 절반 폭</summary>
        public const float OpenBayYaw = 0f;
        public const float OpenBayHalfWidth = 10.77f;   // 26 * tan(22.5°)

        /// <summary>팔각형 한 변이 차지하는 각도</summary>
        public const float SideAngle = 45f;

        /// <summary>플레이어가 실제로 뛰어다니는 링의 중간 반지름</summary>
        public const float FloorRingMid = (InlayInner + RoomApothem) * 0.5f;
    }

    /// <summary>
    /// 챔버 맵 전용 보스 패턴 6종.
    /// 모든 수치가 위 BossChamberLayout 에서 나오므로,
    /// 맵 크기를 바꾸면 상수만 고치면 패턴이 따라옵니다.
    ///
    /// 런타임에서 쓰려면 BossChamberPatterns.All() 을,
    /// 에셋으로 굽고 싶으면 메뉴 BossFX ▸ Create Chamber Boss Patterns 를 쓰세요.
    /// </summary>
    public static class BossChamberPatterns
    {
        // ---- 챔버 색 (레퍼런스 이미지에서 추출) ----------------------------
        static readonly Color Warn   = new Color(0.35f, 0.12f, 0.75f);
        static readonly Color Danger = new Color(1.00f, 0.18f, 0.35f);
        static readonly Color Edge   = new Color(0.85f, 0.55f, 1.00f);
        static readonly Color CoreHot = new Color(1.00f, 0.70f, 1.00f);
        static readonly Color Glow   = new Color(0.55f, 0.20f, 1.00f);
        static readonly Color Alarm  = new Color(1.00f, 0.10f, 0.10f);

        static BossTelegraphSettings Warn2(BossShape shape, float radius, float charge,
                                           Color? hot = null)
        {
            return new BossTelegraphSettings
            {
                shape = shape,
                radius = radius,
                chargeTime = charge,
                baseColor = Warn,
                hotColor = hot ?? Danger,
                edgeColor = Edge,
                intensity = 1.15f,
                groundOffset = 0.12f     // 챔버 바닥 타일이 살짝 울퉁불퉁해서 조금 더 띄움
            };
        }

        static BossImpactSettings Shock(float radius, BossRadialMode mode = BossRadialMode.Ring)
        {
            return new BossImpactSettings
            {
                mode = mode,
                radius = radius,
                duration = 0.5f,
                thickness = 0.10f,
                coreColor = CoreHot,
                edgeColor = Glow,
                intensity = 5f,
                flatOnGround = true,
                groundOffset = 0.14f
            };
        }

        public static List<BossPattern> All()
        {
            return new List<BossPattern>
            {
                CoreOverload(),
                PlatformSlam(),
                ConsoleOverload(),
                SweepingArc(),
                OctagonalLattice(),
                CoreBarrage()
            };
        }

        static BossPattern New(string name, string desc, float recovery)
        {
            var p = ScriptableObject.CreateInstance<BossPattern>();
            p.name = name;
            p.description = desc;
            p.recoveryTime = recovery;
            return p;
        }

        // ================================================================ 1
        /// <summary>
        /// 코어 과부하 — 중앙 코어가 폭주해 바깥쪽 전체가 위험해집니다.
        /// 안전지대는 중앙 플랫폼 위. 플레이어를 안쪽으로 몰아넣는 패턴입니다.
        /// </summary>
        public static BossPattern CoreOverload()
        {
            var p = New("Chamber_1_코어과부하",
                        "바깥 전체가 위험. 중앙 플랫폼 위로 올라가야 산다.", 2.5f);

            var t = Warn2(BossShape.Ring, BossChamberLayout.RoomApothem, 2.4f);
            // 안전지대 = 플랫폼. 안쪽 반지름을 플랫폼 크기에 정확히 맞춥니다.
            t.innerRadius = BossChamberLayout.PlatformRadius / BossChamberLayout.RoomApothem;
            t.holdTime = 0.25f;
            t.fadeOutTime = 0.4f;

            p.steps.Add(new BossStep
            {
                label = "코어 충전",
                type = BossStepType.Telegraph,
                origin = BossOrigin.ArenaCenter,
                faceTarget = false,
                telegraph = t,
                impact = Shock(BossChamberLayout.RoomApothem),
                damage = 45f,
                delayAfter = 0.9f
            });
            return p;
        }

        // ================================================================ 2
        /// <summary>
        /// 플랫폼 슬램 — 플레이어 이동을 예측해서 원형 장판 4연타.
        /// 코어 과부하로 플랫폼에 몰아넣은 직후에 쓰면 좋습니다.
        /// </summary>
        public static BossPattern PlatformSlam()
        {
            var p = New("Chamber_2_플랫폼슬램",
                        "플레이어 예측 지점에 원형 장판 4연타.", 1.6f);

            var t = Warn2(BossShape.Circle, 4.2f, 0.85f);
            t.holdTime = 0.05f;

            p.steps.Add(new BossStep
            {
                label = "슬램",
                type = BossStepType.Telegraph,
                origin = BossOrigin.TargetPredicted,
                telegraph = t,
                impact = Shock(4.2f, BossRadialMode.Burst),
                repeat = 4,
                repeatInterval = 0.38f,
                randomOffset = 1.8f,
                damage = 20f,
                delayAfter = 0.6f
            });
            return p;
        }

        // ================================================================ 3
        /// <summary>
        /// 콘솔 과부하 — 맵에 실제로 배치된 콘솔 오벨리스크 8기 위치에서
        /// 시계방향으로 순차 폭발합니다. 맵 지형지물을 그대로 쓰는 패턴.
        /// </summary>
        public static BossPattern ConsoleOverload()
        {
            var p = New("Chamber_3_콘솔과부하",
                        "콘솔 8기 자리에서 시계방향 순차 폭발. 앞질러 뛰면 피할 수 있다.", 2f);

            var t = Warn2(BossShape.Circle, 4.6f, 0.55f);
            t.holdTime = 0f;
            t.fadeOutTime = 0.2f;

            p.steps.Add(new BossStep
            {
                label = "콘솔 순차 폭발",
                type = BossStepType.Telegraph,
                origin = BossOrigin.ArenaCenter,
                faceTarget = false,
                // 중앙에서 콘솔 반지름만큼 밀어내고, 45도씩 돌면서 8기를 훑습니다
                forwardOffset = BossChamberLayout.ConsoleRadius,
                angleOffset = 22.5f,                 // 콘솔이 변 중앙이 아니라 사이에 놓임
                repeat = BossChamberLayout.ConsoleCount,
                repeatAngleStep = 360f / BossChamberLayout.ConsoleCount,
                repeatInterval = 0.22f,
                telegraph = t,
                impact = Shock(4.6f, BossRadialMode.Burst),
                damage = 18f,
                delayAfter = 0.7f
            });
            return p;
        }

        // ================================================================ 4
        /// <summary>
        /// 회전 부채꼴 — 플랫폼 중앙에서 벽까지 닿는 부채꼴이 한 바퀴 돕니다.
        /// 각도 채우기(Angular)라 어느 쪽으로 도는지 눈으로 읽힙니다.
        /// </summary>
        public static BossPattern SweepingArc()
        {
            var p = New("Chamber_4_회전부채꼴",
                        "중앙에서 벽까지 닿는 부채꼴이 한 바퀴. 반대로 돌아 피한다.", 2f);

            // 사거리는 팔각 모서리까지 (구석에 숨어도 닿게)
            var t = Warn2(BossShape.Cone, BossChamberLayout.RoomCircumradius, 0.6f);
            t.coneAngle = 100f;
            t.fillMode = BossFillMode.Angular;
            t.holdTime = 0f;
            t.fadeOutTime = 0.18f;

            p.steps.Add(new BossStep
            {
                label = "부채꼴 회전",
                type = BossStepType.Telegraph,
                origin = BossOrigin.ArenaCenter,
                faceTarget = false,
                telegraph = t,
                impact = Shock(BossChamberLayout.RoomCircumradius),
                impactOnFire = false,           // 6연타라 충격파까지 겹치면 과합니다
                repeat = 6,
                repeatAngleStep = 62f,          // 6 x 62 = 372° → 한 바퀴 조금 넘게
                repeatInterval = 0.28f,
                damage = 24f,
                delayAfter = 0.8f
            });
            return p;
        }

        // ================================================================ 5
        /// <summary>
        /// 팔각 격자 레이저 — 팔각형 여덟 변 방향으로 레이저가 동시에 뻗습니다.
        /// 여덟 조각으로 갈린 사이 공간이 안전지대. 맵 형태를 그대로 쓴 패턴입니다.
        /// </summary>
        public static BossPattern OctagonalLattice()
        {
            // 주의: 레이저가 중앙에서 뻗으므로 플랫폼 위(중앙)는 전부 위험합니다.
            // 안전지대는 바깥 링에서 레이저 사이에 생기는 여덟 개의 쐐기 틈입니다.
            // 1번 패턴(코어 과부하)이 플레이어를 안으로 몰고, 이 패턴이 다시
            // 밖으로 밀어내는 구성이라 순서대로 돌리면 긴장이 생깁니다.
            var p = New("Chamber_5_팔각격자레이저",
                        "중앙 전체가 위험. 바깥 링의 레이저 사이 쐐기 틈으로 피한다.", 2.6f);

            float reach = BossChamberLayout.RoomCircumradius + 2f;

            // --- 경고선 8개 (동시에 뜨도록 waitForCompletion = false)
            for (int i = 0; i < 8; i++)
            {
                var t = Warn2(BossShape.Line, reach, 1.3f);
                t.lineWidth = 3.2f;
                t.fillMode = BossFillMode.Linear;
                t.holdTime = 0.1f;
                t.fadeOutTime = 0.15f;

                p.steps.Add(new BossStep
                {
                    label = $"격자 경고 {i}",
                    type = BossStepType.Telegraph,
                    origin = BossOrigin.ArenaCenter,
                    faceTarget = false,
                    angleOffset = i * BossChamberLayout.SideAngle,
                    telegraph = t,
                    impactOnFire = false,
                    damage = 0f,                 // 경고선 자체는 피해 없음
                    waitForCompletion = false,   // ← 8개가 겹쳐서 동시에 뜸
                    delayAfter = 0f
                });
            }

            // 경고가 다 차오를 때까지 기다렸다가
            p.steps.Add(new BossStep
            {
                label = "대기",
                type = BossStepType.Wait,
                waitForCompletion = true,
                delayAfter = 1.45f
            });

            // --- 레이저 8발 동시 발사
            for (int i = 0; i < 8; i++)
            {
                p.steps.Add(new BossStep
                {
                    label = $"격자 레이저 {i}",
                    type = BossStepType.Beam,
                    origin = BossOrigin.ArenaCenter,
                    faceTarget = false,
                    angleOffset = i * BossChamberLayout.SideAngle,
                    originHeight = BossChamberLayout.PlatformDeckY,
                    beam = new BossBeamSettings
                    {
                        length = reach,
                        width = 3.2f,
                        chargeTime = 0.1f,
                        extendTime = 0.06f,
                        fireTime = 1.1f,
                        fadeOutTime = 0.25f,
                        sweepAngle = 0f,
                        coreColor = CoreHot,
                        glowColor = Glow,
                        intensity = 4.5f,
                        hitInterval = 0.12f
                    },
                    damage = 9f,
                    waitForCompletion = (i == 7),   // 마지막 하나만 기다림
                    delayAfter = 0f
                });
            }
            return p;
        }

        // ================================================================ 6
        /// <summary>
        /// 코어 탄막 — 플랫폼 가장자리에서 나선 탄막이 퍼집니다.
        /// 발사 반지름을 플랫폼 크기에 맞춰서, 탄이 플랫폼 위에서 튀어나오는 것처럼 보입니다.
        /// </summary>
        public static BossPattern CoreBarrage()
        {
            var p = New("Chamber_6_코어탄막",
                        "플랫폼 가장자리에서 나선 탄막. 바깥 링을 돌며 피한다.", 2.4f);

            p.steps.Add(new BossStep
            {
                label = "나선 탄막",
                type = BossStepType.Barrage,
                origin = BossOrigin.ArenaCenter,
                faceTarget = false,
                originHeight = BossChamberLayout.PlatformTopY,
                barrage = new BossBarrageSettings
                {
                    shape = BossBarrageShape.Spiral,
                    countPerWave = 9,
                    waves = 18,
                    waveInterval = 0.14f,
                    spiralStep = 12f,
                    // 플랫폼 가장자리에서 생성 → 중앙 코어가 뿜는 것처럼 보임
                    spawnRadius = BossChamberLayout.PlatformRadius + 0.6f,
                    bullet = new BossBulletSettings
                    {
                        size = 0.55f,
                        speed = 8.5f,
                        // 벽(26m)까지 가고 조금 더. 여유 있게 잡아 중간에 사라지지 않게
                        lifeTime = 3.2f,
                        radius = 0.45f,
                        coreColor = CoreHot,
                        edgeColor = Glow,
                        intensity = 4.5f
                    }
                },
                damage = 14f,
                delayAfter = 0.6f
            });

            // 마무리로 개방부(+Z) 쪽 대형 부채꼴 한 방
            var t = Warn2(BossShape.Cone, BossChamberLayout.RoomApothem, 1.0f, Alarm);
            t.coneAngle = 75f;
            p.steps.Add(new BossStep
            {
                label = "개방부 방출",
                type = BossStepType.Telegraph,
                origin = BossOrigin.ArenaCenter,
                faceTarget = false,
                angleOffset = BossChamberLayout.OpenBayYaw,
                telegraph = t,
                impact = Shock(BossChamberLayout.RoomApothem),
                damage = 30f,
                delayAfter = 0.5f
            });
            return p;
        }
    }
}
