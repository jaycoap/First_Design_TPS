using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 보스 몬스터(AlienMonster) AI.
///
/// 행동 흐름
///  - 추격      : 플레이어를 향해 걸어서 접근(달리기는 쓰지 않는다 — 먼 거리는 텔레포트가 담당)
///  - 근접 할퀴기: 사정거리 안이면 양팔을 번갈아 크게 휘둘러 연속으로 할퀸다(마지막 타는 더 크고 아프다)
///  - 레이저    : 왼팔을 앞으로 뻗고 검지 끝에서 빛이 일렁이다가, 발사 직전 일렁임이 빨라지고 발사
///                (발사 직전 조준이 고정되므로 구르기로 옆으로 빠지면 피할 수 있다)
///  - 돌진 할퀴기: 중거리에서 몸을 낮추고 양팔을 뒤로 당겼다가, 바닥에 그려진 통로를 따라
///                직선으로 달려들며 양팔을 X자로 교차해 후려친다. 방향이 예비동작 끝에
///                고정되므로 통로 밖으로 구르면 피할 수 있고, 빗나가면 크게 미끄러지며 굳는다(반격 기회)
///  - 텔레포트  : 플레이어가 멀어지면 쿨다운 없이 번쩍이며 사라졌다가 등 뒤(또는 정면)에 나타난다
///  - 분신 처형  : 체력 30%에서 1회. 맵 밖 원주에 진짜 포함 10기가 늘어서 일제히 충전하고,
///                진짜만 충전 색이 다르다. 제한 시간 안에 '과거의 나와의 협공(G)'으로 진짜를
///                때려야 파훼되며, 실패하면 전원이 시차 발사해 플레이어를 쓰러뜨린다.
///
/// 공격 전용 애니메이션 클립이 없으므로 팔 동작은 BossRig가 절차적으로 만들고,
/// 애니메이터는 대기/걷기/사망 로코모션만 담당한다.
/// 거리·속도 수치는 모두 "사람 1.8m 기준"이며, 실제 캐릭터 키에 맞춰 런타임에 자동 환산된다.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[DefaultExecutionOrder(60)] // BossRig(50)가 팔 포즈를 적용한 뒤 LateUpdate에서 손끝 위치를 읽는다
public class BossController : MonoBehaviour, IDamageable, IRewindableExtra, IRewindTimeAware
{
    public enum Phase { Intro, Idle, Chase, Melee, Rush, Laser, Teleport, Judgment, Aerial, Dead }

    [Header("참조")]
    [Tooltip("비우면 씬의 PlayerStats를 자동으로 찾는다.")]
    [SerializeField] private Transform target;
    [SerializeField] private Animator animator;

    [Header("체력")]
    [SerializeField] private float maxHealth = 800f;

    [Header("추격 (사람 1.8m 기준 수치)")]
    [Tooltip("걷기 속도. 보스는 달리지 않고 걸어서 다가오며, 멀어지면 텔레포트로 따라붙는다.")]
    [SerializeField] private float walkSpeed = 1.8f;
    [SerializeField] private float turnSpeed = 9f;
    [SerializeField] private float gravity = -20f;

    [Header("등장 컷신")]
    [Tooltip("게임을 시작하면 보스가 은신한 채로 기다렸다가, 컷신과 함께 모습을 드러낸다.\n" +
             "끄면 예전처럼 시작하자마자 바로 전투가 시작된다.")]
    [SerializeField] private bool playIntro = true;
    [Tooltip("시작 후 컷신이 걸리기까지의 여유(초). 플레이어가 화면을 인지할 시간.")]
    [SerializeField] private float introDelay = 0.8f;
    [Tooltip("아무것도 없는 자리를 비추며 뜸을 들이는 시간(초). 길수록 등장이 극적이다.")]
    [SerializeField] private float introHoldTime = 1.6f;
    [Tooltip("모습을 드러낸 뒤 포효하며 버티는 시간(초)")]
    [SerializeField] private float introRevealTime = 2.2f;

    [Header("체력 단계 (패턴 해금)")]
    [Tooltip("체력이 이 비율 아래로 떨어지면 2단계 — 돌진과 '하늘 레이저 강우'가 열린다.")]
    [SerializeField, Range(0.1f, 1f)] private float stage2HealthRatio = 0.7f;
    [Tooltip("체력이 이 비율 아래로 떨어지면 3단계 — 돌진이 3연타로 이어지고,\n" +
             "레이저는 구체 3연발로, 하늘에서 떨어지는 것도 구체로 바뀐다.")]
    [SerializeField, Range(0.05f, 1f)] private float stage3HealthRatio = 0.5f;

    [Header("근접 할퀴기")]
    [Tooltip("이 거리 안이면 할퀴기를 시도한다.")]
    [SerializeField] private float meleeRange = 1.7f;
    [Tooltip("정면 기준 판정 각도(도)")]
    [SerializeField] private float meleeAngle = 120f;
    [SerializeField] private float meleeDamage = 18f;
    [SerializeField] private float meleeCooldown = 2.4f;
    [Tooltip("팔을 뒤로 당기는 예비동작 시간(초) — 이 동안 플레이어가 반응할 수 있다.")]
    [SerializeField] private float meleeWindup = 0.5f;
    [Tooltip("팔이 호를 그리며 지나가는 시간(초)")]
    [SerializeField] private float meleeStrike = 0.22f;
    [SerializeField] private float meleeRecover = 0.45f;
    [Tooltip("강타 순간 앞으로 밀고 나가는 속도")]
    [SerializeField] private float meleeLunge = 3f;
    [Tooltip("2단계부터의 연속 할퀴기 횟수. 오른팔 → 왼팔 → 오른팔 순으로 번갈아 휘두른다.\n" +
             "1단계(체력 70% 이상)에서는 아래 Melee Hits Stage1 값을 쓴다.")]
    [SerializeField] private int meleeComboHits = 3;
    [Tooltip("1단계(체력 70% 이상)의 할퀴기 타수. 기본 1회 — 초반에는 한 대만 후려친다.")]
    [SerializeField] private int meleeHitsStage1 = 1;
    [Tooltip("연타 사이의 짧은 예비동작(초). 첫 타만 Melee Windup을 그대로 쓴다.")]
    [SerializeField] private float meleeComboInterval = 0.16f;
    [Tooltip("연타 1대당 피해 배율. 여러 번 맞으므로 한 대의 무게는 낮춘다(Melee Damage × 이 값).")]
    [SerializeField, Range(0.1f, 1f)] private float meleeComboDamageScale = 0.55f;
    [Tooltip("마지막 마무리 일격의 피해/전진 배율")]
    [SerializeField] private float meleeFinisherScale = 1.6f;
    [Tooltip("마무리 일격의 <b>사거리</b> 배율. 피해 배율과 따로 두는 이유:\n" +
             "하나로 묶으면 마무리가 세지는 동시에 사거리도 60% 늘어나, 팔이 닿지도 않는\n" +
             "거리에서 맞는 판정이 난다. 크게 휘두르는 만큼만 아주 조금 넓힌다.")]
    [SerializeField] private float meleeFinisherRangeScale = 1.15f;

    [Header("돌진 할퀴기 (중거리)")]
    [Tooltip("이 거리보다 멀어야 돌진한다. 너무 가까우면 그냥 할퀴는 편이 자연스럽다.")]
    [SerializeField] private float rushMinRange = 3.2f;
    [Tooltip("이 거리 안이면 돌진한다. 이보다 멀면 레이저/텔레포트가 담당한다.")]
    [SerializeField] private float rushMaxRange = 9f;
    [SerializeField] private float rushDamage = 24f;
    [SerializeField] private float rushCooldown = 7f;
    [Tooltip("몸을 낮추고 양팔을 뒤로 당기는 예비동작 시간(초). 이 동안 바닥에 돌진 통로가 그려진다.")]
    [SerializeField] private float rushWindup = 0.7f;
    [Tooltip("돌진 속도(사람 1.8m 기준 m/s). 걷기보다 훨씬 빨라야 '달려든다'로 읽힌다.")]
    [SerializeField] private float rushSpeed = 12f;
    [Tooltip("돌진 최대 지속 시간(초). 빗나가면 이 시간만큼 지나쳐 달린다.")]
    [SerializeField] private float rushTime = 0.6f;
    [Tooltip("돌진 중 받히는 판정 반경(사람 1.8m 기준). 이 폭이 바닥 통로로 그려진다.")]
    [SerializeField] private float rushRadius = 1.2f;
    [Tooltip("예비동작 끝에서 조준하는 지점을 목표 너머로 이만큼 더 잡는다(지나쳐 달리는 거리).")]
    [SerializeField] private float rushOvershoot = 2.5f;
    [Tooltip("돌진이 끝난 뒤 미끄러지며 굳어 있는 시간(초). 빗나가면 그대로 반격 기회가 된다.")]
    [SerializeField] private float rushRecover = 0.75f;

    [Header("레이저 (손끝 발사)")]
    [SerializeField] private float laserRange = 30f;
    [Tooltip("이 거리보다 가까우면 레이저 대신 할퀴기로 붙는다.")]
    [SerializeField] private float laserMinRange = 3.5f;
    [SerializeField] private float laserDamage = 26f;
    [SerializeField] private float laserCooldown = 5.5f;
    [Tooltip("왼손을 앞으로 뻗는 사전 동작 시간(초)")]
    [SerializeField] private float laserAimTime = 0.6f;
    [Tooltip("검지 끝에서 빛이 일렁이는 충전 시간(초). 끝으로 갈수록 일렁임이 빨라진다.")]
    [SerializeField] private float laserChargeTime = 1.25f;
    [Tooltip("발사 직전 조준이 고정되는 시간(초). 이 시간 안에 구르면 피할 수 있다.")]
    [SerializeField] private float laserLockTime = 0.4f;
    [Tooltip("광선이 남아있는 시간(초)")]
    [SerializeField] private float laserBeamTime = 0.42f;
    [Tooltip("광선 판정 반경 — 이 반경 밖으로 벗어나면 맞지 않는다.")]
    [SerializeField] private float laserRadius = 0.35f;
    [SerializeField] private float laserRecover = 0.5f;

    [Header("텔레포트")]
    [Tooltip("이 거리보다 멀어지면 텔레포트로 따라붙는다.")]
    [SerializeField] private float teleportDistance = 7f;
    [Tooltip("씬에 ArenaWall이 있으면 발동 거리를 '아레나 반지름 × 이 비율'로도 제한한다.\n" +
             "발동 거리가 아레나보다 넓으면 텔레포트가 영영 발동하지 않기 때문. 0이면 제한 없음.")]
    [SerializeField, Range(0f, 1f)] private float teleportArenaRatio = 0.8f;
    [Tooltip("멀어진 상태가 이 시간 이상 이어지면 (쿨다운 없이) 무조건 발동한다(초)")]
    [SerializeField] private float teleportDelay = 0.4f;
    [Tooltip("플레이어로부터 이만큼 떨어진 곳에 나타난다.")]
    [SerializeField] private float teleportAppearDistance = 2.8f;
    [Range(0f, 1f)]
    [Tooltip("등 뒤에 나타날 확률(나머지는 정면)")]
    [SerializeField] private float teleportBehindChance = 0.65f;
    [SerializeField] private float teleportVanishTime = 0.18f;
    [SerializeField] private float teleportAppearTime = 0.22f;

    [Header("텔레포트 충격파")]
    [Tooltip("등장 순간 플레이어 이동 속도 배율(0.7 = 30% 감소). 구르기에는 적용되지 않는다.")]
    [SerializeField, Range(0.1f, 1f)] private float teleportSlowFactor = 0.7f;
    [Tooltip("둔화 지속 시간(초)")]
    [SerializeField] private float teleportSlowTime = 1.5f;
    [Tooltip("등장과 함께 떨어지는 운석 수(0이면 사용 안 함)")]
    [SerializeField] private int meteorCount = 4;
    [Tooltip("운석이 떨어지는 범위 — 플레이어 주변 반경(사람 1.8m 기준)")]
    [SerializeField] private float meteorSpread = 5f;
    [Tooltip("운석 1발의 착탄 피해")]
    [SerializeField] private float meteorDamage = 16f;
    [Tooltip("착탄 피해 반경(사람 1.8m 기준)")]
    [SerializeField] private float meteorRadius = 1.6f;
    [Tooltip("예고가 뜬 뒤 떨어지기까지의 시간(초). 길수록 피하기 쉽다.")]
    [SerializeField] private float meteorFallTime = 1.1f;
    [Tooltip("운석과 운석 사이의 간격(초)")]
    [SerializeField] private float meteorInterval = 0.18f;
    [Tooltip("운석 폭풍 재사용 대기(초). 텔레포트는 쿨다운이 없어 이 값이 없으면 계속 쏟아진다.")]
    [SerializeField] private float meteorCooldown = 4f;

    [Header("하늘 레이저 강우 (2단계~)")]
    [Tooltip("하늘로 레이저를 쏜 뒤 그것이 플레이어 주변으로 떨어지는 패턴의 재사용 대기(초).")]
    [SerializeField] private float skyRainCooldown = 11f;
    [Tooltip("이 패턴으로 떨어지는 수. 텔레포트 충격파보다 넉넉하게 뿌린다.")]
    [SerializeField] private int skyRainCount = 7;
    [Tooltip("팔을 하늘로 들어 올리는 사전 동작 시간(초)")]
    [SerializeField] private float skyRainAimTime = 0.7f;
    [Tooltip("하늘로 쏘기 전 충전 시간(초)")]
    [SerializeField] private float skyRainChargeTime = 0.9f;
    [Tooltip("이 거리 안이면 강우를 쓰지 않는다(코앞에서 쓰면 피할 틈이 없다).")]
    [SerializeField] private float skyRainMinRange = 3f;

    [Header("공중 처형 (분신 처형 이후)")]
    [Tooltip("분신 처형이 끝난 뒤 이만큼 지나야 처음 발동한다(초).\n" +
             "파훼 보상인 경직 반격을 챙길 틈을 주기 위한 여유다.")]
    [SerializeField] private float aerialFirstDelay = 6f;
    [Tooltip("공중 처형 재사용 대기(초)")]
    [SerializeField] private float aerialCooldown = 18f;
    [Tooltip("아레나 중앙 바닥에서 이만큼 떠오른다 — 사람 기준 환산이 아니라 **실제 월드 미터**다.\n" +
             "다른 수치처럼 캐릭터 키에 비례시키면(7 × k ≈ 1.25m) 보스 키의 4배까지 올라가\n" +
             "화면 밖으로 사라진다. 보스가 보이는 높이는 제 키의 두 배 남짓이라 직접 지정한다.")]
    [SerializeField] private float aerialHeight = 0.6f;
    [Tooltip("떠오른 뒤 겨누는 시간(초). 이 동안 플레이어는 엄폐/거리를 잡을 수 있다.")]
    [SerializeField] private float aerialAimTime = 1.1f;
    [Tooltip("연속으로 쏟아붓는 구체 수")]
    [SerializeField] private int aerialBarrageCount = 10;
    [Tooltip("구체 사이의 발사 간격(초)")]
    [SerializeField] private float aerialBarrageInterval = 0.26f;
    [Tooltip("다 쏘고 나서 공중에 굳어 있는 시간(초) — 지상 복귀 전 반격 기회")]
    [SerializeField] private float aerialRecover = 1.2f;

    [Header("레이저 구체 (3단계 — 레이저 대체)")]
    [Tooltip("한 번에 쏘는 구체 수")]
    [SerializeField] private int orbBurstCount = 3;
    [Tooltip("구체 사이의 발사 간격(초)")]
    [SerializeField] private float orbInterval = 0.35f;
    [SerializeField] private float orbDamage = 20f;
    [Tooltip("구체 비행 속도(사람 1.8m 기준 m/s). 느릴수록 피하기 쉽다.")]
    [SerializeField] private float orbSpeed = 9f;
    [Tooltip("구체 폭발 반경(사람 1.8m 기준)")]
    [SerializeField] private float orbRadius = 1.3f;
    [Tooltip("구체가 스스로 터지기까지의 비행 시간(초)")]
    [SerializeField] private float orbLife = 4f;

    [Header("분신 처형 (체력 30% 패턴)")]
    [Tooltip("체력이 최대치의 이 비율 이하로 떨어지면 1회 발동한다.")]
    [SerializeField, Range(0.05f, 0.9f)] private float judgmentHealthRatio = 0.3f;
    [Tooltip("소환할 분신 수. 진짜 보스를 포함해 총 (이 값 + 1)기가 맵 밖 원형으로 늘어선다.")]
    [SerializeField] private int judgmentCloneCount = 9;
    [Tooltip("일제 사격까지의 충전 시간(초). 이 안에 진짜를 찾아 협공해야 한다.")]
    [SerializeField] private float judgmentChargeTime = 6f;
    [Tooltip("분신들이 늘어서는 원의 반지름 = 아레나 반지름 × 이 배율(맵 밖).")]
    [SerializeField] private float judgmentRingScale = 1.3f;
    [Tooltip("분신 1기당 레이저 피해. 파훼하지 못하면 여러 발이 겹쳐 치명적이다.")]
    [SerializeField] private float judgmentDamage = 34f;
    [Tooltip("일제 사격 간격(초). 구르기 무적으로 전부 흘릴 수 없도록 넉넉히 벌린다.")]
    [SerializeField] private float judgmentVolleyStagger = 0.12f;
    [Tooltip("파훼에 필요한 명중 횟수. 협공(과거의 나)과 일반 사격이 함께 이 횟수를 채워야 한다.\n" +
             "한 발로 끝나지 않게 하는 값이므로, 충전 시간 안에 퍼부을 수 있는 탄수를 보고 조절한다.")]
    [SerializeField] private int judgmentBreakHits = 45;
    [Tooltip("파훼에 성공한 순간 진짜 보스가 받는 피해(누적 명중은 체력을 깎지 않는다)")]
    [SerializeField] private float judgmentBreakDamage = 120f;
    [Tooltip("파훼 직후 무방비로 굳어 있는 시간(초)")]
    [SerializeField] private float judgmentStunTime = 1.4f;
    [Tooltip("진짜 보스의 충전 색. 분신(보스 색)과 확실히 달라야 찾을 수 있다.")]
    [SerializeField] private Color judgmentRealColor = new Color(1f, 0.42f, 0.08f);

    [Header("이펙트")]
    [Tooltip("레이저/텔레포트/발톱에 공통으로 쓰이는 보스 색")]
    [SerializeField] private Color bossColor = new Color(0.75f, 0.3f, 1f);
    [SerializeField] private Color hitFlashColor = new Color(1f, 0.35f, 0.35f);
    [Tooltip("장애물/지면 판정 마스크(자기 몸은 코드에서 제외)")]
    [SerializeField] private LayerMask obstacleMask = ~0;

    // ---- 런타임 상태 ----
    private CharacterController _cc;
    private BossRig _rig;
    private Transform _targetT;
    private IDamageable _targetDamage;
    private PlayerController _targetController;   // 구르기(회피) 여부 확인용
    private PlayerStats _targetStats;
    private ThirdPersonCamera _cam;

    private Phase _phase = Phase.Idle;
    private bool _busy;                 // 공격/텔레포트 코루틴 진행 중
    private float _health;
    private float _k = 1f;              // 사람 1.8m 대비 실제 크기 배율
    private float _verticalVelocity;
    private float _farTimer;
    private float _nextMelee, _nextLaser, _nextRush, _nextSkyRain;
    private float _nextAerial = float.MaxValue; // 분신 처형이 끝나야 열린다
    private float _teleportRetryTime;   // 설 자리를 못 찾았을 때의 재시도 대기
    private float _nextMeteorTime;      // 운석 폭풍 재사용 시각
    private float _teleportDist;        // 실제 발동 거리(월드 m) — 아레나 크기까지 반영한 값
    private float _animSpeed;           // 애니메이터 Speed(0=대기, 0.5=걷기)
    private Vector3 _lastSwingDir;      // 직전 할퀴기가 지나간 방향(연타 연결/후딜용)

    /// <summary>걷기 상태의 애니메이터 Speed 값(달리기 상태는 쓰지 않는다).</summary>
    private const float WalkAnimSpeed = 0.5f;

    /// <summary>광선이 화면에 남는 시간 = laserBeamTime × 이 값(판정은 발사 순간 한 번뿐).</summary>
    private const float BeamVisualStretch = 2.6f;

    // 레이저 연출 상태(코루틴이 채우고 LateUpdate가 그린다)
    private bool _orbOn, _beamFiring;
    private bool _aimLocked;            // 조준 고정 구간(예고선이 흰빛으로 깜빡인다)
    private float _orbCharge, _previewAlpha;
    private Vector3 _aimDir;

    // 분신 처형 패턴
    private bool _judgmentActive, _judgmentDone, _judgmentBroken;
    private float _judgmentEndTime;
    private int _judgmentHits;           // 이번 패턴에서 누적한 명중 횟수(협공 + 일반 사격)
    private int _judgmentIgnoreSession;  // 패턴 시작 시점에 이미 날아오던 협공(무효)
    private readonly List<BossClone> _clones = new List<BossClone>();

    // 이펙트 핸들
    private BossFx.ChargeOrb _orb;
    private BossFx.Beam _beam;
    private BossFx.Flash _flash;
    private BossFx.ClawTrail _claw, _clawLeft;
    private BossFx.ClawSlash _clawSlash; // 할퀸 순간 허공에 새겨지는 발톱 자국
    private BossFx.RushPath _rushPath;   // 돌진 예비동작 중 바닥에 그려지는 통로
    private GunFx.ImpactFx _impact;
    private ArenaWall _arena;            // 아레나(낙하 방지 벽) — 텔레포트 자리 제한에 쓴다
    private GunFx.ImpactFx _beamImpact;  // 레이저 착탄 전용(굵은 광선에 맞춘 큰 폭발)
    private GunFx.MuzzleFx _muzzleFx;    // 발사 순간 손끝에서 터지는 방출광
    private BossFx.ChargeOrb _realOrb;   // 분신 처형 전용(진짜만 다른 색)
    private BossFx.Beam _realBeam;

    // 피격 점멸
    private Renderer[] _renderers;
    private MaterialPropertyBlock _mpb;
    private float _flashUntil;
    private bool _flashApplied;
    private bool _hidden;

    // 부위 히트박스(없으면 몸 전체가 같은 피해를 받는 예전 동작 그대로)
    private BossHitbox[] _hitboxes = new BossHitbox[0];
    private CharacterController _playerCc;   // 히트박스를 물리 충돌에서 뺄 때만 쓴다

    private readonly RaycastHit[] _hitBuf = new RaycastHit[12];
    private readonly Collider[] _colBuf = new Collider[12];

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int DieHash = Animator.StringToHash("Die");

    /// <summary>씬에 살아있는 보스(HUD가 체력 바를 그리는 데 사용). 없으면 null.</summary>
    public static BossController Active { get; private set; }

    /// <summary>
    /// 컷신이 진행 중인가(전역). 플레이어 조작·사격·시간 능력이 이 동안 잠긴다.
    /// 카메라가 보스를 비추고 있으므로 조작을 열어 두면 화면 밖에서 움직이게 된다.
    /// </summary>
    public static bool CutsceneActive { get; private set; }

    /// <summary>등장 컷신이 끝나기 전인가(HUD 보스 바 / 전투 음악이 이때는 뜨지 않는다).</summary>
    public bool IntroPlaying => _phase == Phase.Intro;

    public float Health => _health;
    public float MaxHealth => maxHealth;
    public bool IsDead => _phase == Phase.Dead;
    public Phase CurrentPhase => _phase;

    /// <summary>
    /// 체력 단계. 낮아질수록 쓰는 패턴이 늘고 기존 패턴도 흉포해진다.
    ///  1단계(70%~)   : 할퀴기 1회 · 플레이어 조준 레이저 · 접근만 하는 텔레포트
    ///  2단계(~70%)   : + 돌진 할퀴기 · 하늘 레이저 강우 · 텔레포트 충격파(레이저 낙하)
    ///  3단계(~50%)   : 돌진이 3연타로 이어지고, 레이저는 구체 3연발, 낙하물도 구체로 바뀐다
    /// </summary>
    public int Stage => _health <= maxHealth * stage3HealthRatio ? 3
                      : _health <= maxHealth * stage2HealthRatio ? 2 : 1;

    /// <summary>분신 처형 패턴 진행 중인가(HUD 경고 표시용).</summary>
    public bool JudgmentActive => _judgmentActive;

    /// <summary>일제 사격까지 남은 비율 1→0(HUD 카운트다운 바).</summary>
    public float JudgmentRemain01 => _judgmentActive && judgmentChargeTime > 0.01f
        ? Mathf.Clamp01((_judgmentEndTime - Time.time) / judgmentChargeTime)
        : 0f;

    /// <summary>파훼까지 채운 명중 횟수(HUD 진행도 표시용).</summary>
    public int JudgmentHits => _judgmentHits;
    /// <summary>파훼에 필요한 총 명중 횟수(HUD 진행도 표시용).</summary>
    public int JudgmentBreakHits => judgmentBreakHits;
    /// <summary>파훼 진행도 0→1(HUD 게이지).</summary>
    public float JudgmentBreak01 => judgmentBreakHits > 0
        ? Mathf.Clamp01((float)_judgmentHits / judgmentBreakHits)
        : 0f;

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        // 절차적 팔 포즈는 휴머노이드 본을 다루므로 Animator와 같은 오브젝트에 있어야 한다
        _rig = GetComponentInChildren<BossRig>();
        if (_rig == null && animator != null)
        {
            _rig = animator.GetComponent<BossRig>();
            if (_rig == null) _rig = animator.gameObject.AddComponent<BossRig>();
        }
        // Animator조차 없는 구성이면 빈 리그를 붙여 둔다(포즈는 적용되지 않고 공격 판정만 동작)
        if (_rig == null) _rig = gameObject.AddComponent<BossRig>();

        // 로코모션 클립에 박힌 AnimationEvent(OnFootstep/OnLand) 수신부.
        // 없으면 "has no receiver!" 경고가 매 걸음 뜬다 → Animator와 같은 오브젝트에 보장해 둔다.
        if (animator != null && animator.GetComponent<BossFootsteps>() == null)
            animator.gameObject.AddComponent<BossFootsteps>();
        _health = maxHealth;

        // 실제 캐릭터 키를 사람 1.8m에 대비해 환산 → 모델 스케일과 무관하게 같은 감각으로 동작
        float worldHeight = _cc.height * Mathf.Abs(transform.lossyScale.y);
        _k = worldHeight > 1e-4f ? worldHeight / 1.8f : 1f;
        ResolveTeleportDistance();

        _renderers = GetComponentsInChildren<Renderer>();
        _mpb = new MaterialPropertyBlock();

        _hitboxes = GetComponentsInChildren<BossHitbox>(true);
        IgnoreHitboxCollisions();

        BattleMusic.Ensure(); // 보스가 있는 씬 = 전투 — 배경음악 재생기를 보장한다
        BuildFx();

        // 컷신을 쓸 거라면 이 시점부터 숨어 있어야 한다 — 한 프레임이라도 보이면
        // "없다가 나타난다"는 연출이 성립하지 않는다.
        if (playIntro)
        {
            _phase = Phase.Intro;
            SetHidden(true);
        }
    }

    private void Start()
    {
        if (playIntro) StartCoroutine(IntroRoutine());
    }

    /// <summary>
    /// 부위 히트박스는 '총알 판정 전용'이다. 실제 물리에서는 아무것도 밀지 않도록
    /// 보스 자신과 플레이어의 CharacterController에서 제외한다
    /// (팔 콜라이더가 제 몸을 막아 걸음이 끊기거나 플레이어를 밀어내는 사고 방지).
    /// 레이캐스트는 이 설정과 무관하게 그대로 맞는다.
    ///
    /// IgnoreCollision은 콜라이더를 껐다 켜면 풀리므로, 은신이 끝날 때마다 다시 건다.
    /// (BossHitboxSetup이 만드는 전용 레이어의 충돌 매트릭스가 1차 방어선이고, 이건 이중 안전장치)
    /// </summary>
    /// <summary>
    /// CharacterController를 끄고 순간이동시킨 뒤 다시 켠다.
    /// 콜라이더를 껐다 켜면 <see cref="Physics.IgnoreCollision"/> 설정이 전부 풀리므로,
    /// 켠 직후 반드시 다시 걸어야 한다 — 안 그러면 텔레포트 한 번에
    /// 플레이어와 서로 밀어내는 상태로 돌아가 공중에 뜨는 문제가 되살아난다.
    /// </summary>
    private void WarpTo(Vector3 position, Quaternion? rotation = null)
    {
        _cc.enabled = false;
        if (rotation.HasValue) transform.SetPositionAndRotation(position, rotation.Value);
        else transform.position = position;
        _cc.enabled = true;

        _verticalVelocity = 0f;
        IgnoreHitboxCollisions();
    }

    private void IgnoreHitboxCollisions()
    {
        // 플레이어를 아직 못 찾았으면 다음 호출에서 다시 시도한다
        // (찾지 못한 채 resolved로 표시하면 영영 다시 찾지 않는다)
        if (_playerCc == null)
        {
            var player = FindFirstObjectByType<PlayerController>();
            if (player != null) _playerCc = player.GetComponent<CharacterController>();
        }

        // 보스 본체 캡슐 ↔ 플레이어 캡슐: 서로 밀지 않게 한다.
        // CharacterController끼리 부딪히면 한쪽이 상대 캡슐 위로 타고 올라가(스텝 오프셋)
        // 공중에 뜬 채 서 있게 된다. 공격 판정은 전부 거리 계산이라 이걸 꺼도 그대로 동작하고,
        // 텔레포트 자리 검사도 원래 플레이어와는 겹쳐도 되는 것으로 처리하고 있다.
        if (_cc != null && _playerCc != null)
            Physics.IgnoreCollision(_cc, _playerCc, true);

        if (_hitboxes.Length == 0) return;

        foreach (var hb in _hitboxes)
        {
            Collider col = hb != null ? hb.Collider : null;
            if (col == null) continue;
            if (_cc != null) Physics.IgnoreCollision(col, _cc, true);
            if (_playerCc != null) Physics.IgnoreCollision(col, _playerCc, true);
        }
    }

    private void OnEnable()
    {
        if (Active == null || Active.IsDead) Active = this;
    }

    private void OnDisable()
    {
        // 시간역행이 이 컴포넌트를 잠시 끄면 진행 중이던 공격도 함께 정리한다
        StopAllCoroutines();
        ResetAttackState();
        AbortCutscene(); // 컷신 도중에 꺼졌다면 조작 잠금을 반드시 풀어 준다
        if (Active == this && !isActiveAndEnabled) Active = null;
    }

    /// <summary>
    /// 컷신을 중간에 끊고 정상 상태로 되돌린다.
    /// CutsceneActive는 전역 잠금이라, 코루틴이 죽은 채 true로 남으면
    /// 플레이어가 영영 조작할 수 없게 된다 — 어떤 경로로 끝나든 여기를 거치게 한다.
    /// </summary>
    private void AbortCutscene()
    {
        if (!CutsceneActive) return;

        CutsceneActive = false;
        _cam?.SetCinematicFocus(null);
        SetHidden(false);
        if (_phase == Phase.Intro) _phase = Phase.Idle;
    }

    private void OnDestroy()
    {
        if (_impact != null && _impact.Root != null) Destroy(_impact.Root);
        if (_beamImpact != null && _beamImpact.Root != null) Destroy(_beamImpact.Root);
        if (_muzzleFx != null && _muzzleFx.Root != null) Destroy(_muzzleFx.Root);
        if (_rushPath != null && _rushPath.Root != null) Destroy(_rushPath.Root);
        if (_clawSlash != null && _clawSlash.Root != null) Destroy(_clawSlash.Root);
        if (_flash != null && _flash.Root != null) Destroy(_flash.Root);
        AbortCutscene(); // 보스가 사라져도 조작 잠금은 남지 않게
        if (Active == this) Active = null;
    }

    private void BuildFx()
    {
        Transform tip = _rig != null ? _rig.IndexTip(BossRig.Arm.Left) : null;
        if (tip == null) tip = transform;
        _orb = BossFx.BuildChargeOrb(tip, _k, bossColor);
        _beam = BossFx.BuildBeam(transform, _k, bossColor);
        _flash = BossFx.BuildFlash(_k, bossColor);
        _impact = GunFx.BuildImpact(_k, bossColor);
        _beamImpact = GunFx.BuildImpact(_k * 3f, bossColor);
        _muzzleFx = GunFx.BuildMuzzleFlash(null, _k * 2.5f, bossColor);
        _rushPath = BossFx.BuildRushPath(_k, bossColor);
        _clawSlash = BossFx.BuildClawSlash(_k, bossColor);
        if (_rig != null)
        {
            // 연타는 양팔을 번갈아 쓰므로 궤적도 양쪽에 붙인다
            _claw = BossFx.BuildClawTrail(_rig.ClawTips(BossRig.Arm.Right), _k, bossColor);
            _clawLeft = BossFx.BuildClawTrail(_rig.ClawTips(BossRig.Arm.Left), _k, bossColor);
        }
    }

    // ---------- 메인 루프 ----------

    private void Update()
    {
        UpdateHitFlash();

        if (_phase == Phase.Dead) { ApplyGravity(Vector3.zero); return; }

        // 등장 컷신 동안에는 AI가 돌지 않는다(코루틴이 몸을 제어한다)
        if (_phase == Phase.Intro) return;

        if (_targetT == null && !AcquireTarget()) { ApplyGravity(Vector3.zero); SetAnimSpeed(0f); return; }

        // 플레이어가 죽었으면 전투를 멈춘다
        if (_targetStats != null && _targetStats.IsDead)
        {
            _phase = Phase.Idle;
            ApplyGravity(Vector3.zero);
            SetAnimSpeed(0f);
            return;
        }

        Vector3 flat = _targetT.position - transform.position;
        flat.y = 0f;
        float dist = flat.magnitude;
        Vector3 dir = dist > 1e-4f ? flat / dist : transform.forward;

        // 공격/텔레포트 중엔 코루틴이 몸을 제어한다(제자리 유지 + 중력만)
        if (_busy) return;

        // 어떤 경로로든 아레나 밖에 남았다면 스스로 안쪽으로 돌아온다.
        // 벽은 실제 콜라이더라 밖에서는 걸어 들어올 수 없어, 그대로 두면 영영 굳어 있는다.
        if (!IsInsideArena(transform.position, 0f) && TryPullInsideArena()) return;

        // --- 공중 처형: 분신 처형을 넘긴 뒤에만 열린다. 거리와 무관하게 최우선 ---
        // 체력 조건을 함께 보는 이유: 시간역행으로 체력이 되돌아가면 분신 처형은 이미
        // 끝난 것으로 남는데, 그 상태에서 체력이 넉넉한데도 마지막 패턴이 나오면 어색하다.
        if (Time.time >= _nextAerial && _health <= maxHealth * judgmentHealthRatio)
        {
            StartCoroutine(AerialBarrageRoutine());
            return;
        }

        // --- 텔레포트: 너무 멀어지면 쿨다운 없이 무조건 따라붙는다 ---
        if (dist > _teleportDist)
        {
            _farTimer += Time.deltaTime;
            if (_farTimer >= teleportDelay && Time.time >= _teleportRetryTime)
            {
                StartCoroutine(TeleportRoutine());
                return;
            }
        }
        else _farTimer = 0f;

        FaceDirection(dir, turnSpeed);

        // --- 근접 할퀴기 ---
        if (dist <= meleeRange * _k)
        {
            if (Time.time >= _nextMelee)
            {
                StartCoroutine(MeleeRoutine());
                return;
            }
            // 재사용 대기 중엔 밀어붙이지 않고 마주 본 채 버틴다(연속 타격 방지)
            _phase = Phase.Idle;
            HoldStill();
            return;
        }

        // --- 하늘 레이저 강우(2단계~): 붙어 있지 않을 때 넓게 압박한다 ---
        if (Stage >= 2 && dist >= skyRainMinRange * _k && Time.time >= _nextSkyRain)
        {
            StartCoroutine(SkyRainRoutine());
            return;
        }

        // --- 중거리 돌진 할퀴기(2단계~): 걸어서 붙기엔 멀고 텔레포트를 쓰기엔 가까운 거리대를 메운다 ---
        if (Stage >= 2 && dist >= rushMinRange * _k && dist <= rushMaxRange * _k
            && Time.time >= _nextRush && HasLineOfSight())
        {
            StartCoroutine(RushRoutine());
            return;
        }

        // --- 원거리: 1·2단계는 조준 레이저, 3단계는 구체 3연발로 바뀐다 ---
        if (dist <= laserRange * _k && dist >= laserMinRange * _k
            && Time.time >= _nextLaser && HasLineOfSight())
        {
            StartCoroutine(Stage >= 3 ? OrbBurstRoutine() : LaserRoutine());
            return;
        }

        // --- 추격: 걷기만 사용(먼 거리는 텔레포트가 담당) ---
        _phase = Phase.Chase;
        ApplyGravity(dir * (walkSpeed * _k));
        SetAnimSpeed(WalkAnimSpeed);
    }

    /// <summary>BossRig가 팔 포즈를 적용한 뒤(실행 순서 50) 손끝 기준 이펙트를 갱신한다.</summary>
    private void LateUpdate()
    {
        // 되감기로 되돌릴 수 있도록 패턴 쿨다운을 계속 기록해 둔다
        RecordPatternState();

        // 분신 처형 중에는 '진짜 전용' 색의 충전/광선을 쓴다(분신과 구분되는 유일한 단서)
        BossFx.ChargeOrb orb = _judgmentActive && _realOrb != null ? _realOrb : _orb;
        BossFx.Beam beam = _judgmentActive && _realBeam != null ? _realBeam : _beam;
        if (orb != _orb && _orb != null) _orb.Visible = false;

        if (orb != null)
        {
            orb.Visible = _orbOn;
            orb.Charge = _orbCharge;
        }
        if (beam == null) return;

        if (_previewAlpha > 0.001f)
        {
            Vector3 from = MuzzlePoint();
            beam.Preview(from, from + _aimDir * BeamLength(from, _aimDir), _previewAlpha, _aimLocked);
        }
        else if (!_beamFiring) beam.HidePreview();

        if (_beamFiring) beam.UpdateOrigin(MuzzlePoint());
    }

    // ---------- 근접 할퀴기 ----------

    private IEnumerator MeleeRoutine() => MeleeRoutine(MeleeHitsForStage());

    /// <summary>1단계는 한 대만, 2단계부터 연타로 늘어난다.</summary>
    private int MeleeHitsForStage() => Stage >= 2 ? Mathf.Max(1, meleeComboHits)
                                                  : Mathf.Max(1, meleeHitsStage1);

    private IEnumerator MeleeRoutine(int hitCount)
    {
        _busy = true;
        _phase = Phase.Melee;
        SetAnimSpeed(0f);

        int hits = Mathf.Max(1, hitCount);
        bool prevValid = false;
        Vector3 prevDir = transform.forward;
        BossRig.Arm prevArm = BossRig.Arm.Right;

        for (int i = 0; i < hits; i++)
        {
            // 오른팔 → 왼팔 → 오른팔 … 번갈아 휘둘러야 연타가 자연스럽다
            BossRig.Arm arm = (i % 2 == 0) ? BossRig.Arm.Right : BossRig.Arm.Left;
            bool finisher = i == hits - 1;
            float windup = i == 0 ? meleeWindup : meleeComboInterval;

            yield return Swipe(arm, finisher, windup, prevValid, prevArm, prevDir);

            prevArm = arm;
            prevDir = _lastSwingDir;
            prevValid = true;
        }

        // 후딜: 마지막에 휘두른 팔을 내리며 가중치를 푼다
        for (float t = 0f; t < meleeRecover; t += Time.deltaTime)
        {
            float w = 1f - Mathf.Clamp01(t / meleeRecover);
            _rig.AimArm(prevArm, prevDir, w);
            _rig.CurlHand(prevArm, 0.6f, w);
            TrackTarget(turnSpeed * 0.5f);
            HoldStill();
            yield return null;
        }

        _nextMelee = Time.time + meleeCooldown;
        _busy = false;
        _phase = Phase.Idle;
    }

    /// <summary>
    /// 할퀴기 한 번(예비동작 → 강타). 팔을 바깥·위로 당겼다가 몸을 가로질러 후려친다.
    /// 이전 타의 팔은 예비동작 동안 가중치를 빼며 자연스럽게 내려간다(연타가 끊겨 보이지 않게).
    /// </summary>
    private IEnumerator Swipe(BossRig.Arm arm, bool finisher, float windup,
                              bool hasPrev, BossRig.Arm prevArm, Vector3 prevDir)
    {
        // 오른팔은 오른쪽 뒤에서 왼쪽으로, 왼팔은 그 반대로 지나간다
        float side = arm == BossRig.Arm.Right ? 1f : -1f;
        float scale = finisher ? meleeFinisherScale : 1f;
        float startYaw = (finisher ? 115f : 95f) * side;
        float endYaw = (finisher ? -80f : -60f) * side;
        var claw = arm == BossRig.Arm.Right ? _claw : _clawLeft;

        // --- 예비동작 ---
        for (float t = 0f; t < windup; t += Time.deltaTime)
        {
            float k = Mathf.Clamp01(t / windup);
            float ease = k * k;
            _rig.AimArm(arm, SwingDir(Mathf.Lerp(0f, startYaw, ease), Mathf.Lerp(0f, 0.6f, ease)), ease);
            _rig.CurlHand(arm, ease, ease);
            _rig.TwistSpine(18f * side, ease);
            if (hasPrev) // 직전 타의 팔은 서서히 내려놓는다
            {
                float fade = 1f - ease;
                _rig.AimArm(prevArm, prevDir, fade);
                _rig.CurlHand(prevArm, 0.6f, fade);
            }
            TrackTarget(turnSpeed * 0.7f);
            HoldStill();
            yield return null;
        }

        // --- 강타 ---
        claw?.SetEmitting(true);
        GameSfx.PlayAt(Sfx.BossSwing, BodyCenter(), pitch: finisher ? 0.85f : 1f);
        bool hit = false;
        float strikeTime = meleeStrike * (finisher ? 1.25f : 1f);
        float damage = meleeDamage * meleeComboDamageScale * scale;

        bool slashed = false;
        for (float t = 0f; t < strikeTime; t += Time.deltaTime)
        {
            float k = Mathf.Clamp01(t / strikeTime);
            float ease = 1f - (1f - k) * (1f - k); // easeOut — 시작이 빠른 후려치기
            _lastSwingDir = SwingDir(Mathf.Lerp(startYaw, endYaw, ease), Mathf.Lerp(0.5f, -0.25f, ease));
            _rig.AimArm(arm, _lastSwingDir, 1f);
            _rig.CurlHand(arm, 0.7f, 1f);
            _rig.TwistSpine(Mathf.Lerp(18f, -14f, ease) * side, 1f);

            SetAnimSpeed(0f);
            ApplyGravity(transform.forward * (meleeLunge * _k * (finisher ? 1.3f : 1f)));

            // 발톱 자국: 팔이 정면을 지나기 직전에 한 번 새긴다(판정 순간과 맞물린다).
            // 반지름을 실제 판정 사거리에 맞춰야 "보이는 만큼 맞는다"가 성립한다.
            if (!slashed && ease > 0.25f)
            {
                slashed = true;
                float reach = meleeRange * _k * 1.1f * (finisher ? meleeFinisherRangeScale : 1f);
                _clawSlash?.Play(BodyCenter(),
                                 SwingDir(startYaw, 0.5f), SwingDir(endYaw, -0.25f),
                                 reach * (finisher ? 1f : 0.9f));
            }

            // 팔이 정면을 지나는 구간에서 한 번만 판정
            if (!hit && ease > 0.3f && TryMeleeHit(damage, finisher)) hit = true;
            yield return null;
        }
        claw?.SetEmitting(false);

        if (finisher && hit && _cam != null) _cam.AddFovKick(3f); // 마무리 타격감
    }

    /// <summary>스윙 방향(월드): 몸 정면을 기준으로 좌우 각도 + 상하 기울기.</summary>
    private Vector3 SwingDir(float yawDeg, float pitch)
        => (Quaternion.AngleAxis(yawDeg, Vector3.up) * transform.forward + Vector3.up * pitch).normalized;

    /// <summary>할퀴기 한 대의 명중 판정. rangeScale은 마무리 일격에서 사거리를 조금 넓힌다.</summary>
    private bool TryMeleeHit(float damage, bool finisher)
    {
        if (_targetT == null) return false;

        Vector3 to = _targetT.position - transform.position;
        float heightGap = Mathf.Abs(to.y);
        to.y = 0f;
        float dist = to.magnitude;

        // 판정 여유는 10%까지만 — 예전 25%는 눈에 보이는 팔 길이보다 한참 멀리서도 맞았다.
        // 마무리도 사거리는 거의 그대로다(피해만 세진다) — 예전엔 1.6배로 늘어나,
        // 돌진으로 지나친 자리에서 팔이 닿지도 않는데 맞는 판정이 났다.
        float range = meleeRange * _k * 1.1f * (finisher ? meleeFinisherRangeScale : 1f);
        if (dist > range) return false;
        if (heightGap > _cc.height * Mathf.Abs(transform.lossyScale.y)) return false;
        if (Vector3.Angle(transform.forward, to) > meleeAngle * 0.5f) return false;

        Vector3 point = _targetT.position + Vector3.up * (0.9f * _k);
        DamagePlayer(damage, point, strong: finisher);
        return true;
    }

    // ---------- 돌진 할퀴기 ----------

    /// <summary>
    /// 중거리 돌진. 몸을 낮추고 양팔을 뒤로 당기는 예비동작 동안 바닥에 돌진 통로가 그려지고,
    /// 예비동작 후반에 방향이 고정된 뒤 직선으로 달려들며 양팔을 X자로 교차해 후려친다.
    ///  - 방향이 고정되므로 통로 밖으로 구르면 피할 수 있다.
    ///  - 빗나가면 지나친 자리에서 더 크게 미끄러지며 굳는다(반격 기회).
    /// </summary>
    private IEnumerator RushRoutine()
    {
        _busy = true;
        _phase = Phase.Rush;
        SetAnimSpeed(0f);

        float half = rushRadius * _k;

        // --- 1) 예비동작: 활시위를 당기듯 양팔을 뒤·아래로 당긴다 ---
        for (float t = 0f; t < rushWindup; t += Time.deltaTime)
        {
            float ease = Mathf.Clamp01(t / rushWindup);
            ease *= ease;

            _rig.AimArm(BossRig.Arm.Right, SwingDir(150f, -0.15f), ease);
            _rig.AimArm(BossRig.Arm.Left, SwingDir(-150f, -0.15f), ease);
            _rig.CurlHand(BossRig.Arm.Right, 1f, ease);
            _rig.CurlHand(BossRig.Arm.Left, 1f, ease);

            // 예비동작 후반 40%부터 조준이 굳는다 — 이 시점에 옆으로 굴러야 피한다
            float lockStart = rushWindup * 0.6f;
            float track = rushWindup > lockStart
                ? 1f - Mathf.Clamp01((t - lockStart) / (rushWindup - lockStart)) : 0f;
            TrackTarget(turnSpeed * 1.5f * track);

            ShowRushPath(Mathf.Lerp(0.4f, 1f, ease), half);
            HoldStill();
            yield return null;
        }

        // --- 2) 돌진: 여기서 방향/거리가 고정된다 ---
        float dashLength = RushLength();
        _rushPath?.Hide();

        Vector3 dir = transform.forward;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
        dir.Normalize();
        float speed = rushSpeed * _k;

        _claw?.SetEmitting(true);
        _clawLeft?.SetEmitting(true);
        _impact?.Spawn(transform.position + Vector3.up * (0.1f * _k), Vector3.up); // 박차고 나가는 흙먼지
        GameSfx.PlayAt(Sfx.BossRush, BodyCenter());
        if (_cam != null) { _cam.AddShake(0.35f, 0.25f); _cam.AddFovKick(3.5f); }

        bool hit = false;
        float traveled = 0f;
        for (float t = 0f; t < rushTime && traveled < dashLength; t += Time.deltaTime)
        {
            // 벌렸던 팔이 점점 모인다 = X자 교차 직전
            float spread = Mathf.Lerp(55f, 14f, Mathf.Clamp01(t / rushTime));
            _rig.AimArm(BossRig.Arm.Right, SwingDir(spread, 0.1f), 1f);
            _rig.AimArm(BossRig.Arm.Left, SwingDir(-spread, 0.1f), 1f);
            _rig.CurlHand(BossRig.Arm.Right, 0.8f, 1f);
            _rig.CurlHand(BossRig.Arm.Left, 0.8f, 1f);

            Vector3 before = transform.position;
            SetAnimSpeed(0f);
            ApplyGravity(dir * speed);

            Vector3 moved = transform.position - before;
            moved.y = 0f;
            traveled += moved.magnitude;

            // 판정을 먼저 본다 — 플레이어 캡슐에 막혀 멈춘 경우에도 확실히 후려치도록
            if (!hit && TryRushHit()) { hit = true; break; }

            // 벽/장애물에 막혔으면 더 밀지 않고 마무리로 넘어간다
            if (t > 0.05f && moved.magnitude < speed * Time.deltaTime * 0.25f) break;

            yield return null;
        }

        // --- 3) 마무리: 미끄러지며 양팔을 X자로 교차해 후려친다 ---
        // 발톱이 지나가는 소리라 여기는 할퀴기 쪽(BossSwing)을 그대로 쓴다
        const float CrossTime = 0.18f;
        bool crossed = false;
        GameSfx.PlayAt(Sfx.BossSwing, BodyCenter(), pitch: 0.9f);
        for (float t = 0f; t < CrossTime; t += Time.deltaTime)
        {
            float k = Mathf.Clamp01(t / CrossTime);
            float ease = 1f - (1f - k) * (1f - k);
            _rig.AimArm(BossRig.Arm.Right, SwingDir(Mathf.Lerp(14f, -75f, ease), Mathf.Lerp(0.1f, -0.2f, ease)), 1f);
            _rig.AimArm(BossRig.Arm.Left, SwingDir(Mathf.Lerp(-14f, 75f, ease), Mathf.Lerp(0.1f, -0.2f, ease)), 1f);
            _rig.CurlHand(BossRig.Arm.Right, 0.9f, 1f);
            _rig.CurlHand(BossRig.Arm.Left, 0.9f, 1f);

            SetAnimSpeed(0f);
            ApplyGravity(dir * (speed * (1f - ease) * 0.5f)); // 관성으로 미끄러지며 정지

            // 교차하는 순간 X자로 겹치는 발톱 자국을 새긴다(오른팔 → 왼팔 순서로 한 번씩)
            if (!crossed && ease > 0.35f)
            {
                crossed = true;
                float reach = rushRadius * _k;
                _clawSlash?.Play(BodyCenter(), SwingDir(14f, 0.1f), SwingDir(-75f, -0.2f), reach);
            }

            // 스쳐 지나간 직후라도 발톱이 실제로 지나가는 순간 한 번 더 판정
            if (!hit && ease > 0.4f && TryRushHit()) hit = true;
            yield return null;
        }
        _claw?.SetEmitting(false);
        _clawLeft?.SetEmitting(false);

        // --- 3-b) 3단계: 달려든 자리에서 그대로 3연타로 몰아친다 ---
        // 여기서 뒷정리를 하지 않고 넘기는 이유는, MeleeRoutine이 자기 후딜과
        // _busy/_phase 해제를 그대로 이어받기 때문이다(연타 뒤에 후딜이 두 번 오지 않는다).
        if (Stage >= 3)
        {
            _nextRush = Time.time + rushCooldown;

            // 돌진은 목표를 지나쳐 달리므로(rushOvershoot) 멈춘 자리는 이미 사거리 밖이다.
            // 그대로 휘두르면 허공을 치거나, 사거리에 여유를 준 만큼만 억지로 맞는 판정이 난다.
            // 돌아서서 사거리 안으로 파고든 다음에 연타를 시작한다.
            for (float t = 0f; t < 0.5f; t += Time.deltaTime)
            {
                TrackTarget(turnSpeed * 2.5f);

                Vector3 toTarget = _targetT != null ? _targetT.position - transform.position : Vector3.zero;
                toTarget.y = 0f;
                if (toTarget.magnitude <= meleeRange * _k) { HoldStill(); break; }

                SetAnimSpeed(WalkAnimSpeed);
                ApplyGravity(toTarget.normalized * (walkSpeed * _k * 2f));
                yield return null;
            }

            yield return MeleeRoutine(Mathf.Max(2, meleeComboHits));
            yield break;
        }

        // --- 4) 후딜: 빗나갔으면 더 오래 굳는다(반격 기회) ---
        float recover = rushRecover * (hit ? 1f : 1.35f);
        for (float t = 0f; t < recover; t += Time.deltaTime)
        {
            float w = 1f - Mathf.Clamp01(t / recover);
            _rig.AimArm(BossRig.Arm.Right, SwingDir(-75f, -0.2f), w);
            _rig.AimArm(BossRig.Arm.Left, SwingDir(75f, -0.2f), w);
            _rig.CurlHand(BossRig.Arm.Right, 0.6f, w);
            _rig.CurlHand(BossRig.Arm.Left, 0.6f, w);
            TrackTarget(turnSpeed * 0.4f);
            HoldStill();
            yield return null;
        }

        _nextRush = Time.time + rushCooldown;
        // 돌진 직후 바로 할퀴면 피할 수 없는 연계가 된다 — 최소한의 숨 돌릴 틈
        _nextMelee = Mathf.Max(_nextMelee, Time.time + 0.4f);
        _busy = false;
        _phase = Phase.Idle;
    }

    /// <summary>돌진 거리(월드 m). 목표를 조금 지나칠 만큼 달리되, 최대 속도×시간을 넘지 않는다.</summary>
    private float RushLength()
    {
        float max = rushSpeed * _k * rushTime;
        if (_targetT == null) return max;

        Vector3 flat = _targetT.position - transform.position;
        flat.y = 0f;
        return Mathf.Clamp(flat.magnitude + rushOvershoot * _k, rushMinRange * _k, max);
    }

    /// <summary>바닥에 돌진 통로를 그린다(발밑에서 고정된 정면 방향으로).</summary>
    private void ShowRushPath(float alpha, float halfWidth)
    {
        if (_rushPath == null) return;
        Vector3 from = transform.position + Vector3.up * (0.05f * _k);
        _rushPath.Show(from, from + transform.forward * RushLength(), halfWidth, alpha);
    }

    /// <summary>돌진 중 받힘 판정. 몸 중심 기준 rushRadius 안이면 한 번만 후려친다.</summary>
    private bool TryRushHit()
    {
        if (_targetT == null) return false;

        Vector3 to = TargetCenter() - BodyCenter();
        float heightGap = Mathf.Abs(to.y);
        to.y = 0f;
        if (heightGap > _cc.height * Mathf.Abs(transform.lossyScale.y)) return false;

        float radius = rushRadius * _k + TargetRadius();
        if (to.sqrMagnitude > radius * radius) return false;

        // 이미 등 뒤로 완전히 지나쳐 버린 대상은 치지 않는다
        if (Vector3.Dot(to, transform.forward) < -radius * 0.5f) return false;

        DamagePlayer(rushDamage, TargetCenter(), strong: true);
        if (_cam != null) _cam.AddFovKick(5f);
        return true;
    }

    // ---------- 등장 컷신 ----------

    /// <summary>
    /// 게임 시작 연출. 보스는 은신한 채 서 있고, 카메라가 그 빈 자리를 천천히 돌며 비춘다.
    /// 아무것도 없는 자리를 먼저 보여줘야(introHoldTime) 나타나는 순간이 산다.
    ///
    /// 컷신 동안 플레이어 조작은 잠긴다(CutsceneActive) — 카메라가 보스를 보고 있는데
    /// 조작이 열려 있으면 화면 밖에서 움직이게 된다.
    /// </summary>
    private IEnumerator IntroRoutine()
    {
        _busy = true;
        CutsceneActive = true;
        SetHidden(true);
        SetAnimSpeed(0f);

        // Update가 Intro 동안 돌지 않으므로 여기서 직접 잡아 둔다(플레이어를 마주 보기 위해)
        AcquireTarget();
        if (_cam == null) _cam = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCamera>() : null;

        yield return new WaitForSeconds(introDelay);

        // --- 1) 빈 자리를 비춘다 ---
        _cam?.SetCinematicFocus(transform);
        yield return new WaitForSeconds(introHoldTime);

        // --- 2) 등장: 섬광과 함께 모습을 드러내며 포효한다 ---
        _flash?.Spawn(BodyCenter());
        SetHidden(false);
        GameSfx.PlayAt(Sfx.BossRoar, BodyCenter());
        if (_cam != null) { _cam.AddShake(0.9f, 0.6f); _cam.AddFovKick(6f); }

        // 위협하듯 양팔을 벌린 자세를 잡았다가 서서히 내린다
        for (float t = 0f; t < introRevealTime; t += Time.deltaTime)
        {
            float k = Mathf.Clamp01(t / introRevealTime);
            float w = k < 0.4f ? k / 0.4f : 1f - (k - 0.4f) / 0.6f; // 들었다가 내린다

            _rig.AimArm(BossRig.Arm.Right, SwingDir(70f, 0.45f), w);
            _rig.AimArm(BossRig.Arm.Left, SwingDir(-70f, 0.45f), w);
            _rig.CurlHand(BossRig.Arm.Right, 0.8f, w);
            _rig.CurlHand(BossRig.Arm.Left, 0.8f, w);

            TrackTarget(turnSpeed * 0.8f);
            HoldStill();
            yield return null;
        }

        // --- 3) 카메라를 플레이어에게 돌려주고 전투 시작 ---
        _cam?.SetCinematicFocus(null);
        yield return new WaitForSeconds(0.5f); // 카메라가 돌아오는 동안은 아직 잠금

        CutsceneActive = false;
        _busy = false;
        _phase = Phase.Idle;

        // 등장 직후 곧바로 덮치지 않도록 한 박자 준다
        _nextMelee = Time.time + 0.8f;
        _nextLaser = Time.time + 1.2f;
    }

    // ---------- 공중 처형 (분신 처형 이후) ----------

    /// <summary>
    /// 분신 처형을 넘긴 뒤에만 열리는 마지막 패턴.
    /// 아레나 한가운데 상공으로 텔레포트해 내려다보며 구체를 연달아 쏟아붓는다.
    ///
    /// 설계 의도
    ///  - 보스가 <b>공중에 멈춰 서므로</b> 근접·돌진이 전부 봉인된다. 대신 플레이어도
    ///    엄폐물이 없는 한가운데를 계속 노려보게 되어, 이 패턴 동안은 순수한 회피전이 된다.
    ///  - 구체는 하나씩 날아오므로 위치를 계속 바꾸며 흘려야 한다.
    ///  - 다 쏘고 나면 공중에서 잠시 굳는다 — 올려다보고 쏠 수 있는 확실한 반격 구간.
    /// </summary>
    private IEnumerator AerialBarrageRoutine()
    {
        _busy = true;
        _phase = Phase.Aerial;
        SetAnimSpeed(0f);

        // --- 1) 사라짐 ---
        _flash?.Spawn(BodyCenter());
        GameSfx.PlayAt(Sfx.BossTeleport, BodyCenter(), pitch: 1.15f);
        if (_cam != null) _cam.AddShake(0.3f, 0.25f);
        yield return new WaitForSeconds(teleportVanishTime * 0.5f);
        SetHidden(true);
        yield return new WaitForSeconds(teleportVanishTime * 0.5f);

        // --- 2) 아레나 한가운데 상공으로 ---
        // 높이는 아레나 오브젝트의 원점이 아니라 '실제 바닥'을 재서 올린다 —
        // 원점이 바닥과 다른 씬에서 땅에 박히거나 너무 높이 뜨는 것을 막는다.
        ResolveJudgmentRing(out Vector3 center, out _);
        Vector3 spot = center + Vector3.up * aerialHeight;
        if (TryFindFloor(center, out float floorY))
            spot.y = floorY + aerialHeight;

        WarpTo(spot);
        TrackTarget(999f); // 즉시 플레이어 쪽을 본다

        // --- 3) 등장 ---
        yield return new WaitForSeconds(teleportAppearTime * 0.5f);
        SetHidden(false);
        _flash?.Spawn(BodyCenter());
        GameSfx.PlayAt(Sfx.BossTeleport, BodyCenter(), pitch: 0.75f);
        if (_cam != null) { _cam.AddShake(0.6f, 0.4f); _cam.AddFovKick(4f); }

        // --- 4) 겨눔: 왼팔을 뻗고 충전 ---
        // 이 아래로는 ApplyGravity를 부르지 않는다 — 부르는 순간 추락한다.
        _aimDir = AimDirection();
        _orbOn = true;
        _orbCharge = 0f;
        GameSfx.PlayAt(Sfx.BossCharge, MuzzlePoint(), pitch: 1.1f);

        for (float t = 0f; t < aerialAimTime; t += Time.deltaTime)
        {
            float k = Mathf.Clamp01(t / aerialAimTime);
            _aimDir = TrackAim(_aimDir, 1f);
            _rig.AimArm(BossRig.Arm.Left, _aimDir, k);
            _rig.PointIndex(BossRig.Arm.Left, k);
            _orbCharge = k;
            TrackTarget(turnSpeed);
            SetAnimSpeed(0f);
            yield return null;
        }

        // --- 5) 연속 사격: 매 발 다시 겨눈다 ---
        int shots = Mathf.Max(1, aerialBarrageCount);
        for (int i = 0; i < shots; i++)
        {
            for (float t = 0f; t < aerialBarrageInterval; t += Time.deltaTime)
            {
                _aimDir = TrackAim(_aimDir, 1f);
                _rig.AimArm(BossRig.Arm.Left, _aimDir, 1f);
                _rig.PointIndex(BossRig.Arm.Left, 1f);
                _orbCharge = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(t / aerialBarrageInterval));
                TrackTarget(turnSpeed);
                SetAnimSpeed(0f);
                yield return null;
            }

            FireOrb(_aimDir);
            _orbCharge = 0.35f;
        }
        _orbOn = false;

        // --- 6) 후딜: 공중에 굳어 있다(반격 구간) ---
        for (float t = 0f; t < aerialRecover; t += Time.deltaTime)
        {
            float w = 1f - Mathf.Clamp01(t / aerialRecover);
            _rig.AimArm(BossRig.Arm.Left, _aimDir, w);
            _rig.PointIndex(BossRig.Arm.Left, w);
            TrackTarget(turnSpeed * 0.5f);
            SetAnimSpeed(0f);
            yield return null;
        }

        // --- 7) 지상 복귀 ---
        _flash?.Spawn(BodyCenter());
        GameSfx.PlayAt(Sfx.BossTeleport, BodyCenter(), pitch: 0.9f);
        SetHidden(true);
        yield return new WaitForSeconds(teleportVanishTime * 0.5f);
        ReturnToArena();
        SetHidden(false);
        if (_cam != null) _cam.AddShake(0.45f, 0.35f);

        _nextAerial = Time.time + aerialCooldown;
        _nextMelee = Mathf.Max(_nextMelee, Time.time + 0.5f);
        _busy = false;
        _phase = Phase.Idle;
    }

    /// <summary>지정한 지점 아래의 바닥 높이(자기 몸/플레이어는 무시). 못 찾으면 false.</summary>
    private bool TryFindFloor(Vector3 at, out float y)
    {
        y = 0f;
        float up = 20f * _k;
        int n = Physics.RaycastNonAlloc(at + Vector3.up * up, Vector3.down, _hitBuf,
                                        up * 2f, obstacleMask, QueryTriggerInteraction.Ignore);
        bool found = false;
        float best = float.MinValue;
        for (int i = 0; i < n; i++)
        {
            var h = _hitBuf[i];
            if (h.collider == null) continue;
            if (h.collider.transform.IsChildOf(transform)) continue;
            if (_targetT != null && h.collider.transform.IsChildOf(_targetT)) continue;
            if (h.point.y > best) { best = h.point.y; found = true; }
        }
        if (found) y = best;
        return found;
    }

    // ---------- 하늘 레이저 강우 (2단계~) ----------

    /// <summary>
    /// 팔을 하늘로 뻗어 레이저를 쏘아 올리면, 잠시 뒤 그것이 플레이어 주변으로 쏟아져 내린다.
    /// 낙하물은 텔레포트 충격파와 같은 예고(링 + 카운트다운 링 + 빛기둥)를 쓰므로,
    /// 플레이어가 이미 배운 규칙 그대로 읽고 피할 수 있다.
    /// 3단계에서는 떨어지는 것이 레이저 대신 구체가 된다.
    /// </summary>
    private IEnumerator SkyRainRoutine()
    {
        _busy = true;
        _phase = Phase.Laser;
        SetAnimSpeed(0f);

        Vector3 up = Vector3.up;
        _orbOn = true;
        _orbCharge = 0f;
        GameSfx.PlayAt(Sfx.BossCharge, MuzzlePoint(), pitch: 1.15f);

        // 1) 하늘을 향해 팔을 들어 올린다 — 조준이 아니라 '위'를 겨누는 것이 신호다
        for (float t = 0f; t < skyRainAimTime; t += Time.deltaTime)
        {
            float k = Mathf.Clamp01(t / skyRainAimTime);
            _rig.AimArm(BossRig.Arm.Left, up, k);
            _rig.PointIndex(BossRig.Arm.Left, k);
            _orbCharge = 0.15f * k;
            TrackTarget(turnSpeed * 0.6f);
            HoldStill();
            yield return null;
        }

        // 2) 충전
        for (float t = 0f; t < skyRainChargeTime; t += Time.deltaTime)
        {
            _rig.AimArm(BossRig.Arm.Left, up, 1f);
            _rig.PointIndex(BossRig.Arm.Left, 1f);
            _orbCharge = Mathf.Lerp(0.15f, 1f, Mathf.Clamp01(t / skyRainChargeTime));
            HoldStill();
            yield return null;
        }

        // 3) 하늘로 발사 — 이 광선이 곧 머리 위에서 되돌아온다
        Vector3 from = MuzzlePoint();
        if (_orb != null) _orb.Burst();
        if (_beam != null) _beam.Fire(from, from + up * (laserRange * _k), laserBeamTime * BeamVisualStretch);
        _muzzleFx?.Fire(from, up);
        GameSfx.PlayAt(Sfx.BossLaser, from, pitch: 0.85f);
        if (_cam != null) { _cam.AddShake(0.5f, 0.35f); _cam.AddFovKick(3.5f); }

        _beamFiring = true;
        for (float t = 0f; t < laserBeamTime; t += Time.deltaTime)
        {
            _rig.AimArm(BossRig.Arm.Left, up, 1f);
            _rig.PointIndex(BossRig.Arm.Left, 1f);
            _orbCharge = Mathf.Lerp(1f, 0f, t / laserBeamTime);
            HoldStill();
            yield return null;
        }
        _beamFiring = false;
        _orbOn = false;

        // 4) 쏟아진다. 뿌리는 동안 팔을 내리며 다음 행동으로 넘어간다
        StartCoroutine(RainRoutine(skyRainCount, FallStyle));

        for (float t = 0f; t < laserRecover; t += Time.deltaTime)
        {
            float w = 1f - Mathf.Clamp01(t / laserRecover);
            _rig.AimArm(BossRig.Arm.Left, up, w);
            _rig.PointIndex(BossRig.Arm.Left, w);
            TrackTarget(turnSpeed * 0.5f);
            HoldStill();
            yield return null;
        }

        _nextSkyRain = Time.time + skyRainCooldown;
        _nextLaser = Mathf.Max(_nextLaser, Time.time + 1f); // 강우 직후 레이저까지 겹치지 않게
        _busy = false;
        _phase = Phase.Idle;
    }

    /// <summary>단계에 따라 하늘에서 떨어지는 것의 모양이 바뀐다(2단계 레이저 / 3단계 구체).</summary>
    private BossMeteor.Style FallStyle => Stage >= 3 ? BossMeteor.Style.Orb : BossMeteor.Style.Beam;

    // ---------- 레이저 구체 (3단계 — 레이저 대체) ----------

    /// <summary>
    /// 3단계의 원거리 공격. 조준 레이저 대신 구체를 연달아 던진다.
    /// 레이저는 발사 순간에 판정이 끝나지만 구체는 <b>날아오는 동안 계속 보이므로</b>,
    /// "예고를 보고 피한다"에서 "날아오는 것을 보고 비킨다"로 회피 방식이 바뀐다.
    /// </summary>
    private IEnumerator OrbBurstRoutine()
    {
        _busy = true;
        _phase = Phase.Laser;
        SetAnimSpeed(0f);

        _aimDir = AimDirection();
        _orbOn = true;
        _orbCharge = 0f;
        GameSfx.PlayAt(Sfx.BossCharge, MuzzlePoint(), pitch: 1.25f);

        // 사전 동작: 왼손을 앞으로 뻗는다(레이저와 같은 자세라 3단계 진입이 바로 읽힌다)
        for (float t = 0f; t < laserAimTime; t += Time.deltaTime)
        {
            float k = Mathf.Clamp01(t / laserAimTime);
            _aimDir = TrackAim(_aimDir, 1f);
            _rig.AimArm(BossRig.Arm.Left, _aimDir, k);
            _rig.PointIndex(BossRig.Arm.Left, k);
            _orbCharge = 0.3f * k;
            TrackTarget(turnSpeed);
            HoldStill();
            yield return null;
        }

        int shots = Mathf.Max(1, orbBurstCount);
        for (int i = 0; i < shots; i++)
        {
            // 한 발마다 다시 겨눈다 — 옆으로 걷는 것만으로는 전부 흘릴 수 없다
            for (float t = 0f; t < orbInterval; t += Time.deltaTime)
            {
                _aimDir = TrackAim(_aimDir, 1f);
                _rig.AimArm(BossRig.Arm.Left, _aimDir, 1f);
                _rig.PointIndex(BossRig.Arm.Left, 1f);
                _orbCharge = Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(t / orbInterval));
                TrackTarget(turnSpeed);
                HoldStill();
                yield return null;
            }

            FireOrb(_aimDir);
            _orbCharge = 0.3f;
        }

        _orbOn = false;

        // 후딜: 팔을 내린다
        for (float t = 0f; t < laserRecover; t += Time.deltaTime)
        {
            float w = 1f - Mathf.Clamp01(t / laserRecover);
            _rig.AimArm(BossRig.Arm.Left, _aimDir, w);
            _rig.PointIndex(BossRig.Arm.Left, w);
            TrackTarget(turnSpeed * 0.5f);
            HoldStill();
            yield return null;
        }

        _nextLaser = Time.time + laserCooldown;
        _busy = false;
        _phase = Phase.Idle;
    }

    /// <summary>구체 한 발. 손끝에서 조준 방향으로 날아간다.</summary>
    private void FireOrb(Vector3 dir)
    {
        Vector3 from = MuzzlePoint();
        if (_orb != null) _orb.Burst();
        _muzzleFx?.Fire(from, dir);
        GameSfx.PlayAt(Sfx.BossLaser, from, pitch: 1.3f); // 레이저보다 가볍고 높게
        if (_cam != null) _cam.AddShake(0.18f, 0.18f);

        BossOrb.Launch(from, dir, _k, bossColor, orbDamage, orbRadius * _k,
                       orbSpeed * _k, orbLife, _targetT, transform, obstacleMask);
    }

    // ---------- 레이저 ----------

    private IEnumerator LaserRoutine()
    {
        _busy = true;
        _phase = Phase.Laser;
        SetAnimSpeed(0f);

        _aimDir = AimDirection();
        _orbOn = true;
        _orbCharge = 0f;
        _previewAlpha = 0f;
        GameSfx.PlayAt(Sfx.BossCharge, MuzzlePoint());

        // 1) 사전 동작: 왼손을 앞으로 뻗고 검지를 겨눈다(빛이 천천히 일렁이기 시작)
        for (float t = 0f; t < laserAimTime; t += Time.deltaTime)
        {
            float k = Mathf.Clamp01(t / laserAimTime);
            _aimDir = TrackAim(_aimDir, 1f);
            _rig.AimArm(BossRig.Arm.Left, _aimDir, k);
            _rig.PointIndex(BossRig.Arm.Left, k);
            _orbCharge = 0.12f * k;
            TrackTarget(turnSpeed);
            HoldStill();
            yield return null;
        }

        // 2) 충전: 일렁임이 점점 빨라지고 예고선이 진해진다.
        //    마지막 laserLockTime 동안엔 조준 추적이 멈춰(고정) 구르기로 피할 수 있다.
        bool lockCued = false;
        for (float t = 0f; t < laserChargeTime; t += Time.deltaTime)
        {
            float k = Mathf.Clamp01(t / laserChargeTime);
            float remain = laserChargeTime - t;
            float track = laserLockTime > 0.01f ? Mathf.Clamp01(remain / laserLockTime) : 1f;

            _aimDir = TrackAim(_aimDir, track);
            _rig.AimArm(BossRig.Arm.Left, _aimDir, 1f);
            _rig.PointIndex(BossRig.Arm.Left, 1f);
            _orbCharge = Mathf.Lerp(0.12f, 1f, k);
            _previewAlpha = Mathf.Lerp(0.3f, 1f, k * k);   // 예고선은 처음부터 또렷하게

            // 조준이 고정되는 순간을 흰 경고선 + 짧은 진동으로 확실히 알린다
            _aimLocked = track < 0.999f;
            if (_aimLocked && !lockCued)
            {
                lockCued = true;
                if (_cam != null) _cam.AddShake(0.12f, 0.15f);
            }

            TrackTarget(turnSpeed * track); // 조준이 고정되면 몸도 멈춘다
            HoldStill();
            yield return null;
        }

        // 3) 발사: 고정된 방향으로 광선이 나간다
        _previewAlpha = 0f;
        _aimLocked = false;
        _beamFiring = true;
        FireLaser(_aimDir);

        for (float t = 0f; t < laserBeamTime; t += Time.deltaTime)
        {
            _rig.AimArm(BossRig.Arm.Left, _aimDir, 1f);
            _rig.PointIndex(BossRig.Arm.Left, 1f);
            _orbCharge = Mathf.Lerp(1f, 0f, t / laserBeamTime);
            HoldStill();
            yield return null;
        }
        _beamFiring = false;
        _orbOn = false;

        // 4) 후딜: 팔을 내린다
        for (float t = 0f; t < laserRecover; t += Time.deltaTime)
        {
            float w = 1f - Mathf.Clamp01(t / laserRecover);
            _rig.AimArm(BossRig.Arm.Left, _aimDir, w);
            _rig.PointIndex(BossRig.Arm.Left, w);
            TrackTarget(turnSpeed * 0.5f);
            HoldStill();
            yield return null;
        }

        _nextLaser = Time.time + laserCooldown;
        _busy = false;
        _phase = Phase.Idle;
    }

    private void FireLaser(Vector3 dir)
    {
        Vector3 from = MuzzlePoint();
        float length = BeamLength(from, dir);
        Vector3 to = from + dir * length;

        if (_orb != null) _orb.Burst();
        if (_beam != null) _beam.Fire(from, to, laserBeamTime * BeamVisualStretch);
        _muzzleFx?.Fire(from, dir);
        if (_cam != null) { _cam.AddShake(0.55f, 0.35f); _cam.AddFovKick(4.5f); }
        GameSfx.PlayAt(Sfx.BossLaser, from);

        // 착탄 이펙트(벽에 맞았을 때) — 광선 굵기에 맞춰 큼직하게 터진다
        if (RaycastIgnoreSelf(from, dir, length, out RaycastHit wall))
            _beamImpact?.Spawn(wall.point, wall.normal);

        // 명중 판정: 광선 선분과 플레이어 중심의 거리로 본다.
        // 발사 직전 방향이 고정되므로, 구르기로 옆으로 빠지면 선분에서 벗어나 피할 수 있다.
        if (_targetT == null) return;
        Vector3 chest = TargetCenter();
        Vector3 closest = ClosestPointOnSegment(from, to, chest);
        float radius = laserRadius * _k + TargetRadius();
        if ((chest - closest).sqrMagnitude <= radius * radius)
            DamagePlayer(laserDamage, closest, strong: true);
    }

    /// <summary>손끝이 플레이어 상체를 향하는 방향.</summary>
    private Vector3 AimDirection()
    {
        Vector3 from = MuzzlePoint();
        Vector3 to = TargetCenter();
        Vector3 d = to - from;
        return d.sqrMagnitude > 1e-8f ? d.normalized : transform.forward;
    }

    /// <summary>현재 조준 방향을 목표 쪽으로 서서히 돌린다(track=0이면 고정).</summary>
    private Vector3 TrackAim(Vector3 current, float track)
    {
        if (track <= 0.001f) return current;
        Vector3 want = AimDirection();
        float step = 220f * track * Mathf.Deg2Rad * Time.deltaTime;
        return Vector3.RotateTowards(current, want, step, 0f);
    }

    /// <summary>레이저 발사 원점(왼손 검지 끝). 리그가 없으면 가슴 높이 정면.</summary>
    private Vector3 MuzzlePoint()
    {
        Transform tip = _rig != null ? _rig.IndexTip(BossRig.Arm.Left) : null;
        if (tip != null) return tip.position;
        return transform.position + Vector3.up * (1.4f * _k) + transform.forward * (0.4f * _k);
    }

    /// <summary>광선이 벽에 막히는 지점까지의 길이.</summary>
    private float BeamLength(Vector3 from, Vector3 dir)
    {
        float max = laserRange * _k * 1.5f;
        return RaycastIgnoreSelf(from, dir, max, out RaycastHit hit) ? hit.distance : max;
    }

    // ---------- 텔레포트 ----------

    private IEnumerator TeleportRoutine()
    {
        // 설 자리를 먼저 확보한다 — 자리가 없는데 사라지는 연출부터 하면
        // (쿨다운이 없으므로) 제자리에서 번쩍임만 반복하게 된다.
        if (!FindTeleportSpot(out Vector3 spot))
        {
            _teleportRetryTime = Time.time + 0.5f;
            _farTimer = 0f;
            yield break;
        }

        _busy = true;
        _phase = Phase.Teleport;
        _farTimer = 0f;
        SetAnimSpeed(0f);

        // 1) 사라짐: 제자리에서 번쩍인 뒤 모습을 감춘다
        _flash?.Spawn(BodyCenter());
        GameSfx.PlayAt(Sfx.BossTeleport, BodyCenter(), pitch: 1.15f); // 사라질 때는 높게
        if (_cam != null) _cam.AddShake(0.15f, 0.2f);
        yield return new WaitForSeconds(teleportVanishTime * 0.5f);
        SetHidden(true);
        yield return new WaitForSeconds(teleportVanishTime * 0.5f);

        // 2) 이동: 플레이어 등 뒤(기본) 또는 정면
        WarpTo(spot);
        if (_targetT != null)
        {
            Vector3 look = _targetT.position - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(look);
        }

        // 3) 등장: 도착 지점에서 다시 번쩍이며 나타난다
        yield return new WaitForSeconds(teleportAppearTime * 0.5f);
        SetHidden(false);
        _flash?.Spawn(BodyCenter());
        GameSfx.PlayAt(Sfx.BossTeleport, BodyCenter(), pitch: 0.8f); // 나타날 때는 낮고 무겁게
        if (_cam != null) { _cam.AddShake(0.45f, 0.35f); _cam.AddFovKick(-2.5f); }

        // 등장 충격파는 2단계부터다 — 1단계 텔레포트는 '접근'만 하고 아무것도 터뜨리지 않는다.
        // (초반부터 둔화 + 낙하물이 겹치면 패턴을 익힐 틈이 없다)
        if (Stage >= 2)
        {
            if (_targetController != null && teleportSlowFactor < 1f)
                _targetController.ApplyMoveSlow(teleportSlowFactor, teleportSlowTime);
            if (meteorCount > 0 && Time.time >= _nextMeteorTime)
            {
                _nextMeteorTime = Time.time + meteorCooldown;
                StartCoroutine(RainRoutine(meteorCount, FallStyle));
            }
        }

        yield return new WaitForSeconds(teleportAppearTime * 0.5f);

        // 등장 직후 바로 할퀴지 않도록 최소한의 여유를 준다(플레이어가 돌아볼 시간)
        _nextMelee = Mathf.Max(_nextMelee, Time.time + 0.45f);
        _busy = false;
        _phase = Phase.Idle;
    }

    /// <summary>
    /// 하늘에서 쏟아지는 것들(텔레포트 충격파 / 하늘 레이저 강우 공용).
    /// 플레이어 주변에 예고 링이 먼저 뜨고 잠시 뒤 떨어진다.
    /// 이동이 둔해진 상태라 그냥 걷기로는 빠져나가기 어렵고, 구르기로 회피해야 한다.
    /// 낙하물은 각자 알아서 낙하/착탄을 처리하므로 이 코루틴은 뿌리기만 한다.
    /// </summary>
    private IEnumerator RainRoutine(int count, BossMeteor.Style style)
    {
        for (int i = 0; i < count; i++)
        {
            if (_targetT == null) yield break;
            if (TryPickMeteorPoint(out Vector3 point))
                BossMeteor.Launch(point, _k, bossColor, meteorDamage, meteorRadius * _k,
                                  meteorFallTime, _targetT, style);
            yield return new WaitForSeconds(meteorInterval);
        }
    }

    /// <summary>플레이어 주변에서 실제로 바닥이 있는 착탄 지점 하나를 고른다(아레나 안으로 제한).</summary>
    private bool TryPickMeteorPoint(out Vector3 point)
    {
        point = default;
        if (_targetT == null) return false;

        Vector2 offset = Random.insideUnitCircle * (meteorSpread * _k);
        Vector3 candidate = _targetT.position + new Vector3(offset.x, 0f, offset.y);

        // 아레나가 있으면 벽 안쪽으로 끌어당긴다(밖에 떨어져 봐야 보이지도 않는다)
        var arena = FindFirstObjectByType<ArenaWall>();
        if (arena != null)
        {
            Vector3 c = arena.transform.position;
            Vector3 flat = candidate - c;
            flat.y = 0f;
            float max = arena.Radius * 0.92f;
            if (flat.magnitude > max) candidate = c + flat.normalized * max;
        }

        // 바닥 높이 확정(자기 몸/플레이어는 무시)
        float up = 12f * _k;
        int n = Physics.RaycastNonAlloc(candidate + Vector3.up * up, Vector3.down, _hitBuf,
                                        up * 2f, obstacleMask, QueryTriggerInteraction.Ignore);
        float best = float.MinValue;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            var h = _hitBuf[i];
            if (h.collider == null) continue;
            if (h.collider.transform.IsChildOf(transform)) continue;
            if (_targetT != null && h.collider.transform.IsChildOf(_targetT)) continue;
            if (h.point.y > best) { best = h.point.y; found = true; }
        }
        if (!found) return false;

        point = new Vector3(candidate.x, best, candidate.z);
        return true;
    }

    /// <summary>플레이어 등 뒤 → 정면 → 좌우 순으로 설 수 있는 자리를 찾는다.</summary>
    /// <summary>씬의 아레나(없으면 null). 못 찾았을 때만 다시 찾는다.</summary>
    private ArenaWall Arena
    {
        get
        {
            if (_arena == null) _arena = FindFirstObjectByType<ArenaWall>();
            return _arena;
        }
    }

    /// <summary>
    /// 아레나 안쪽 지점인가. margin만큼 벽에서 떨어져 있어야 인정한다(보스 캡슐 반지름).
    /// 아레나가 없는 씬이면 제한이 없으므로 항상 true.
    /// </summary>
    private bool IsInsideArena(Vector3 point, float margin)
    {
        var arena = Arena;
        if (arena == null) return true;

        Vector3 flat = point - arena.transform.position;
        flat.y = 0f;
        return flat.magnitude <= Mathf.Max(0.01f, arena.Radius - margin);
    }

    /// <summary>
    /// 아레나 밖에 있는 보스를 안쪽 가장자리로 되돌린다(성공하면 true).
    /// 벽 때문에 스스로 걸어 들어올 수 없으므로, 갇힌 것을 발견하면 순간이동으로 꺼내 준다.
    /// </summary>
    private bool TryPullInsideArena()
    {
        var arena = Arena;
        if (arena == null) return false;

        Vector3 center = arena.transform.position;
        Vector3 flat = transform.position - center;
        flat.y = 0f;
        if (flat.sqrMagnitude < 1e-6f) return false;

        // 벽에 다시 끼지 않도록 캡슐 지름만큼 안쪽으로 들어온다
        float margin = _cc.radius * Mathf.Abs(transform.lossyScale.x) * 2f;
        Vector3 spot = center + flat.normalized * Mathf.Max(0.01f, arena.Radius - margin);
        if (TryFindFloor(spot, out float floorY)) spot.y = floorY;

        _flash?.Spawn(BodyCenter());
        WarpTo(spot);
        _flash?.Spawn(BodyCenter());
        Debug.LogWarning("[Boss] 아레나 밖에 있어 안쪽으로 되돌렸습니다.");
        return true;
    }

    private bool FindTeleportSpot(out Vector3 spot)
    {
        spot = transform.position;
        if (_targetT == null) return false;

        Vector3 p = _targetT.position;
        Vector3 fwd = _targetT.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        float d = teleportAppearDistance * _k;

        bool behindFirst = Random.value < teleportBehindChance;
        var candidates = new[]
        {
            p + (behindFirst ? -fwd : fwd) * d,
            p + (behindFirst ? fwd : -fwd) * d,
            p + right * d,
            p - right * d,
        };

        foreach (var c in candidates)
            if (ValidateSpot(c, out spot)) return true;
        return false;
    }

    /// <summary>지면에 내려놓고, 보스 캡슐이 들어갈 공간이 있는지 확인한다.</summary>
    private bool ValidateSpot(Vector3 candidate, out Vector3 grounded)
    {
        grounded = candidate;
        float height = _cc.height * Mathf.Abs(transform.lossyScale.y);
        float radius = _cc.radius * Mathf.Abs(transform.lossyScale.x);

        // 아레나 밖은 거부한다. 벽(ArenaWall)은 실제 콜라이더라 한 번 밖으로 나가면
        // 다시 들어오지 못하고 벽에 붙어 제자리걸음만 하게 된다.
        // 플레이어가 벽 가까이 서 있으면 '등 뒤' 후보가 벽 너머로 잡히는데,
        // 그 자리는 바닥도 있고 캡슐도 안 겹쳐서 아래 검사들만으로는 통과해 버린다.
        if (!IsInsideArena(candidate, radius)) return false;

        if (!Physics.Raycast(candidate + Vector3.up * (height * 1.5f), Vector3.down,
                             out RaycastHit hit, height * 3f, obstacleMask, QueryTriggerInteraction.Ignore))
            return false;

        grounded = hit.point + Vector3.up * (_cc.skinWidth * Mathf.Abs(transform.lossyScale.y) + 0.01f * _k);

        Vector3 bottom = grounded + Vector3.up * radius;
        Vector3 top = grounded + Vector3.up * Mathf.Max(radius, height - radius);
        int n = Physics.OverlapCapsuleNonAlloc(bottom, top, radius * 0.85f, _colBuf,
                                               obstacleMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var col = _colBuf[i];
            if (col == null) continue;
            if (col.transform.IsChildOf(transform)) continue;                       // 자기 몸
            if (_targetT != null && col.transform.IsChildOf(_targetT)) continue;    // 플레이어와는 겹쳐도 밀려난다
            return false;
        }
        return true;
    }

    private void SetHidden(bool hidden)
    {
        if (_hidden == hidden) return;
        _hidden = hidden;
        foreach (var r in _renderers)
            if (r != null && !(r is ParticleSystemRenderer) && !(r is TrailRenderer))
                r.enabled = !hidden;

        // 안 보이는 동안엔 총알 판정도 사라져야 한다(빈 자리에 총알이 맞지 않도록)
        foreach (var hb in _hitboxes)
            if (hb != null && hb.Collider != null)
                hb.Collider.enabled = !hidden;

        // 콜라이더를 다시 켜면 IgnoreCollision 설정이 풀린다 → 다시 걸어 준다
        if (!hidden) IgnoreHitboxCollisions();
    }

    // ---------- 분신 처형(체력 30%) ----------

    /// <summary>
    /// 체력 30% 패턴. 맵 밖 원주에 진짜 보스를 포함한 (분신 수+1)기가 늘어서서
    /// 일제히 레이저를 충전한다. 진짜만 충전 색이 다르며, 제한 시간 안에
    /// 진짜에게 judgmentBreakHits발을 명중시켜야 파훼된다(협공 + 일반 사격 합산).
    /// 파훼하지 못하면 전원이 시차를 두고 발사해 플레이어를 확실히 쓰러뜨린다.
    /// </summary>
    private IEnumerator JudgmentRoutine()
    {
        _busy = true;
        _judgmentActive = true;
        _judgmentBroken = false;
        _judgmentHits = 0;
        _phase = Phase.Judgment;
        SetAnimSpeed(0f);

        // 진행 중이던 협공은 여기서 끊고 그 세션을 무효 처리한다 —
        // 패턴은 '다시 겨눠서 새로 발동한 협공'으로만 파훼된다.
        _judgmentIgnoreSession = TimeShiftController.SupportSession;
        TimeShiftController.CancelSupport();

        if (_targetT == null) AcquireTarget();
        if (_realOrb == null) BuildJudgmentFx();

        // --- 1) 배치: 아레나 밖 원주에 자리를 잡고 그중 하나에 진짜가 선다 ---
        int total = Mathf.Max(2, judgmentCloneCount + 1);
        ResolveJudgmentRing(out Vector3 center, out float ring);
        int realIndex = Random.Range(0, total);
        float angleOffset = Random.value * 360f;

        _flash?.Spawn(BodyCenter());
        SetHidden(true);
        if (_cam != null) { _cam.AddShake(0.7f, 0.5f); _cam.AddFovKick(5f); }
        yield return new WaitForSeconds(0.25f);

        // 분신은 이 몸을 그대로 복제하므로, 숨긴 상태에서 만들면 분신도 투명해진다.
        // 자리 배치와 같은 프레임에 되돌리므로 옛 자리에 보이는 프레임은 생기지 않는다.
        SetHidden(false);

        for (int i = 0; i < total; i++)
        {
            float a = (angleOffset + 360f / total * i) * Mathf.Deg2Rad;
            Vector3 pos = center + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * ring;
            Vector3 look = center - pos;
            look.y = 0f;
            Quaternion rot = look.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(look.normalized) : transform.rotation;

            if (i == realIndex)
            {
                WarpTo(pos, rot);
            }
            else
            {
                var clone = BossClone.Spawn(gameObject, pos, rot, _k, bossColor, obstacleMask);
                if (clone != null) _clones.Add(clone);
            }
        }

        _flash?.Spawn(BodyCenter());

        // --- 2) 일제 충전: 전원이 플레이어를 겨눈다. 진짜만 색이 다르다 ---
        foreach (var c in _clones) if (c != null) c.BeginCharge(_targetT);

        _judgmentEndTime = Time.time + judgmentChargeTime;
        _orbOn = true;
        _aimDir = AimDirection();
        // 충전음은 진짜 보스 것 하나만 울린다(분신까지 울리면 10겹으로 뭉개진다)
        GameSfx.PlayAt(Sfx.BossCharge, BodyCenter(), volume: 1.3f, pitch: 0.7f);

        while (Time.time < _judgmentEndTime && !_judgmentBroken)
        {
            float k01 = 1f - Mathf.Clamp01((_judgmentEndTime - Time.time) / judgmentChargeTime);
            foreach (var c in _clones) if (c != null) c.SetCharge(k01);

            _orbCharge = k01;
            _previewAlpha = Mathf.Lerp(0.3f, 0.95f, k01 * k01);
            _aimDir = Vector3.RotateTowards(_aimDir, AimDirection(), 360f * Mathf.Deg2Rad * Time.deltaTime, 0f);
            _rig.AimArm(BossRig.Arm.Left, _aimDir, 1f);
            _rig.PointIndex(BossRig.Arm.Left, 1f);

            TrackTarget(turnSpeed);
            // 맵 밖에는 발판이 없다. 중력을 주면 진짜만 추락해 정체가 드러나므로
            // 분신들과 똑같이 허공에 선 채로 버틴다.
            SetAnimSpeed(0f);
            yield return null;
        }

        _previewAlpha = 0f;

        if (_judgmentBroken) yield return JudgmentBreak();
        else yield return JudgmentVolley();

        // --- 4) 정리: 분신 소멸 후 진짜는 아레나 안으로 복귀 ---
        _orbOn = false;
        _orbCharge = 0f;
        _judgmentActive = false;
        DespawnClones();
        ReturnToArena();

        _busy = false;
        _phase = Phase.Idle;
        _nextMelee = Time.time + 0.6f;
        _nextLaser = Time.time + 1.5f;
        _farTimer = 0f;

        // 분신 처형이 끝난 직후부터 '공중 처형'이 열린다 — 첫 발동은 조금 뒤로 미뤄
        // 파훼 보상(경직 동안의 반격)을 챙길 틈을 준다.
        _nextAerial = Time.time + aerialFirstDelay;
    }

    /// <summary>파훼 성공: 분신이 일제히 흩어지고 진짜는 잠시 무방비로 굳는다.</summary>
    private IEnumerator JudgmentBreak()
    {
        // 여기서 패턴을 내려야 경직 동안 일반 사격이 통한다(진짜 반격 기회)
        _judgmentActive = false;
        if (_realOrb != null) _realOrb.Burst();
        if (_cam != null) { _cam.AddShake(0.9f, 0.6f); _cam.AddFovKick(-7f); }
        DespawnClones();
        _orbOn = false;

        // 팔이 풀리며 굳어 있는 시간(반격 기회)
        for (float t = 0f; t < judgmentStunTime; t += Time.deltaTime)
        {
            float w = 1f - Mathf.Clamp01(t / judgmentStunTime);
            _rig.AimArm(BossRig.Arm.Left, _aimDir, w);
            SetAnimSpeed(0f); // 아직 맵 밖이라 중력은 주지 않는다
            yield return null;
        }
        Debug.Log($"[Boss] 분신 처형 파훼 — 진짜에게 {judgmentBreakHits}발을 꽂았다.");
    }

    /// <summary>파훼 실패: 전원이 시차를 두고 발사한다(구르기 한 번으로는 다 흘릴 수 없다).</summary>
    private IEnumerator JudgmentVolley()
    {
        // 발사 순서를 섞어 어디서 날아올지 모르게 한다
        var shooters = new List<BossClone>(_clones);
        for (int i = shooters.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (shooters[i], shooters[j]) = (shooters[j], shooters[i]);
        }
        int realSlot = Random.Range(0, shooters.Count + 1);

        for (int i = 0; i <= shooters.Count; i++)
        {
            if (i == realSlot) FireJudgmentBeam();
            else
            {
                int idx = i < realSlot ? i : i - 1;
                if (idx < shooters.Count && shooters[idx] != null) shooters[idx].Fire(judgmentDamage);
            }
            if (_cam != null) _cam.AddShake(0.35f, 0.2f);
            yield return new WaitForSeconds(judgmentVolleyStagger);
        }
        Debug.Log("[Boss] 분신 처형 — 파훼 실패. 일제 사격.");
    }

    /// <summary>진짜 보스의 처형 광선(분신과 같은 피해, 색만 다르다).</summary>
    private void FireJudgmentBeam()
    {
        Vector3 from = MuzzlePoint();
        _aimDir = AimDirection();
        float length = BeamLength(from, _aimDir);

        if (_realOrb != null) _realOrb.Burst();
        if (_realBeam != null) _realBeam.Fire(from, from + _aimDir * length, 0.6f);
        _orbOn = false;

        if (_targetDamage != null && _targetT != null)
            DamagePlayer(judgmentDamage, TargetCenter(), strong: true);
    }

    /// <summary>진짜 보스 전용 충전/광선 이펙트(분신과 다른 색).</summary>
    private void BuildJudgmentFx()
    {
        Transform tip = _rig != null ? _rig.IndexTip(BossRig.Arm.Left) : null;
        _realOrb = BossFx.BuildChargeOrb(tip != null ? tip : transform, _k, judgmentRealColor);
        _realBeam = BossFx.BuildBeam(transform, _k, judgmentRealColor);
    }

    /// <summary>분신들이 늘어설 원(아레나 밖). ArenaWall이 없으면 텔레포트 거리를 기준으로 잡는다.</summary>
    private void ResolveJudgmentRing(out Vector3 center, out float radius)
    {
        var arena = FindFirstObjectByType<ArenaWall>();
        if (arena != null)
        {
            center = arena.transform.position;
            radius = arena.Radius * judgmentRingScale;
        }
        else
        {
            center = _targetT != null ? _targetT.position : transform.position;
            center.y = transform.position.y;
            radius = Mathf.Max(_teleportDist, teleportDistance * _k) * 1.5f;
        }
    }

    private void DespawnClones()
    {
        foreach (var c in _clones) if (c != null) c.Despawn();
        _clones.Clear();
    }

    /// <summary>패턴이 끝난 뒤 맵 밖에 남지 않도록 아레나 안(플레이어 근처)으로 복귀한다.</summary>
    private void ReturnToArena()
    {
        if (!FindTeleportSpot(out Vector3 spot))
        {
            // 최후 수단: 아레나 중앙(없으면 플레이어 발밑). 여기서 그냥 돌아가 버리면
            // 분신 처형 때 나갔던 맵 밖에 그대로 남아, 벽에 막혀 영영 못 돌아온다.
            var arena = Arena;
            spot = arena != null ? arena.transform.position
                 : _targetT != null ? _targetT.position
                 : transform.position;

            // 아레나 원점이 바닥과 다를 수 있으므로 실제 지면을 재서 내려놓는다
            if (TryFindFloor(spot, out float floorY)) spot.y = floorY;
        }

        _flash?.Spawn(BodyCenter());
        WarpTo(spot);
        _flash?.Spawn(BodyCenter());
    }

    // ---------- 피격 / 사망 ----------

    /// <summary>부위를 거치지 않은 직격(몸통 콜라이더/광역 피해 등).</summary>
    public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitNormal)
        => TakeDamage(amount, hitPoint, hitNormal, null);

    /// <summary>
    /// 피해 적용. part가 있으면 BossHitbox가 배율을 이미 곱해 넘긴 값이며,
    /// 여기서는 연출(약점은 점멸을 길게)에만 쓴다.
    /// </summary>
    public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitNormal, BossHitbox part)
    {
        if (_phase == Phase.Dead) return;

        // 분신 처형 중에는 체력이 깎이지 않는다 — '진짜'에게 명중시킨 횟수만 쌓이고,
        // judgmentBreakHits발을 채우는 순간 파훼된다. 협공 한 발로 즉시 끝나지 않으므로
        // 진짜를 찾아낸 뒤에도 남은 시간 동안 협공과 사격을 함께 퍼부어야 한다.
        if (_judgmentActive)
        {
            // 패턴이 시작되기 전에 이미 발동해 있던 협공은 진행도를 채우지 못한다.
            // (협공은 초당 여러 발이라, 체력을 30%로 만든 그 협공의 남은 탄이
            //  방금 시작된 패턴의 진행도를 공짜로 채워 버리는 '패턴 씹힘'이 생긴다)
            if (TimeShiftController.GhostDamageActive
                && TimeShiftController.SupportSession == _judgmentIgnoreSession) return;

            _judgmentHits++;
            _flashUntil = Time.time + 0.07f;
            if (_judgmentHits < judgmentBreakHits) return;

            _judgmentBroken = true;
            amount = judgmentBreakDamage;
        }

        _health = Mathf.Max(0f, _health - amount);
        // 약점(머리 등)에 맞으면 점멸을 길게 줘서 "잘 맞췄다"가 화면으로 드러나게 한다
        _flashUntil = Time.time + (part != null && part.IsWeakPoint ? 0.16f : 0.07f);

        if (_health <= 0f) { Die(); return; }

        // 체력 30% — 분신 처형 패턴(1회)
        if (!_judgmentDone && !_judgmentActive && _health <= maxHealth * judgmentHealthRatio)
        {
            _judgmentDone = true;
            StopAllCoroutines();
            ResetAttackState();
            StartCoroutine(JudgmentRoutine());
        }
    }

    private void Die()
    {
        AbortCutscene(); // 컷신 도중 죽는 구성이라도 잠금은 풀린다
        StopAllCoroutines();
        ResetAttackState();
        _phase = Phase.Dead;
        SetAnimSpeed(0f);
        GameSfx.PlayAt(Sfx.BossDeath, BodyCenter());
        if (animator != null && animator.runtimeAnimatorController != null)
            animator.SetTrigger(DieHash);
        Debug.Log("[Boss] AlienMonster 처치.");
    }

    /// <summary>피격 순간 몸을 붉게 점멸(MaterialPropertyBlock — 머티리얼을 복제하지 않는다).</summary>
    private void UpdateHitFlash()
    {
        bool want = Time.time < _flashUntil && !_hidden;
        if (want == _flashApplied) return;
        _flashApplied = want;

        foreach (var r in _renderers)
        {
            if (r == null || r is ParticleSystemRenderer || r is TrailRenderer) continue;
            r.GetPropertyBlock(_mpb);
            if (want)
            {
                _mpb.SetColor("_BaseColor", hitFlashColor);
                _mpb.SetColor("_Color", hitFlashColor);
            }
            else
            {
                _mpb.Clear();
            }
            r.SetPropertyBlock(_mpb);
        }
    }

    // ---------- 시간역행 연동 ----------

    // ---- 패턴 상태 되감기 ----
    // 위치·체력만 되돌리면 보스는 "과거 자리에 과거 체력으로 서서 현재 패턴을 이어가는" 상태가 된다.
    // 어떤 패턴이 언제 나올지는 쿨다운이 정하므로, 쿨다운도 함께 기록해 두었다가 되돌린다.

    private struct PatternSnap
    {
        public float t;
        public float melee, laser, rush, skyRain, aerial, meteor;
        public float farTimer;   // 텔레포트 발동까지 '멀어진 채 버틴 시간' 누적값
        public bool judgmentDone;
        public bool valid;
    }

    /// <summary>TimeRewindable과 같은 기록 주기·보관 시간(초당 90회 × 6.5초).</summary>
    private const float PatternSampleRate = 90f;
    private readonly PatternSnap[] _patternBuf = new PatternSnap[Mathf.CeilToInt(6.5f * PatternSampleRate)];
    private int _patternHead = -1, _patternCount;
    private float _nextPatternSample;

    /// <summary>쿨다운 상태를 링버퍼에 기록(LateUpdate에서 매 프레임이 아니라 일정 주기로).</summary>
    private void RecordPatternState()
    {
        if (Time.time < _nextPatternSample) return;
        _nextPatternSample = Mathf.Max(Time.time, _nextPatternSample + 1f / PatternSampleRate);

        _patternHead = (_patternHead + 1) % _patternBuf.Length;
        if (_patternCount < _patternBuf.Length) _patternCount++;
        _patternBuf[_patternHead] = new PatternSnap
        {
            t = Time.time,
            melee = _nextMelee,
            laser = _nextLaser,
            rush = _nextRush,
            skyRain = _nextSkyRain,
            aerial = _nextAerial,
            meteor = _nextMeteorTime,
            farTimer = _farTimer,
            judgmentDone = _judgmentDone,
            valid = true,
        };
    }

    /// <summary>
    /// 되감기가 끝난 뒤 호출된다. 쿨다운을 그 시점의 <b>남은 시간</b>으로 되돌린다.
    ///
    /// 절대 시각을 그대로 넣으면 안 된다 — 되감는 동안에도 Time.time은 계속 흘렀기 때문에,
    /// 과거의 "다음 발동 시각"은 이미 지나가 버려 모든 패턴이 즉시 사용 가능해진다.
    /// '그때 얼마나 남아 있었나'를 지금 시점에 다시 심어야 같은 순서로 패턴이 재생된다.
    /// </summary>
    public void OnRewoundTo(float pastTime)
    {
        if (_patternCount == 0) return;

        for (int i = 0; i < _patternCount; i++)
        {
            var s = _patternBuf[(_patternHead - i + _patternBuf.Length * 2) % _patternBuf.Length];
            if (!s.valid) break;
            if (s.t > pastTime && i != _patternCount - 1) continue;

            _nextMelee = Rearm(s.melee, s.t);
            _nextLaser = Rearm(s.laser, s.t);
            _nextRush = Rearm(s.rush, s.t);
            _nextSkyRain = Rearm(s.skyRain, s.t);
            _nextAerial = Rearm(s.aerial, s.t);
            _nextMeteorTime = Rearm(s.meteor, s.t);
            _farTimer = s.farTimer;  // 이건 누적 경과시간이라 그대로 되돌린다

            // 분신 처형은 1회성이라, 그 이전으로 돌아갔다면 다시 쓸 수 있어야 한다
            _judgmentDone = s.judgmentDone;
            break;
        }

        // 되감긴 뒤의 기록은 '오지 않은 미래'라 무효
        _patternHead = -1;
        _patternCount = 0;
        _nextPatternSample = Time.time;
    }

    /// <summary>과거 시점에 남아 있던 대기 시간을 지금 기준으로 다시 심는다.</summary>
    private static float Rearm(float nextTime, float snapTime)
    {
        if (nextTime >= float.MaxValue * 0.5f) return float.MaxValue; // 아직 열리지 않은 패턴
        return Time.time + Mathf.Max(0f, nextTime - snapTime);
    }

    public float CaptureRewindExtra() => _health;

    public void ApplyRewindExtra(float value)
    {
        _health = Mathf.Clamp(value, 0f, maxHealth);
        // 과거의 살아있던 시점으로 돌아왔다면 되살아난다(보스는 파괴되지 않으므로 완전 복원)
        if (_health > 0f && _phase == Phase.Dead)
        {
            _phase = Phase.Idle;
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                animator.ResetTrigger(DieHash);
                animator.Play("Idle", 0, 0f);
            }
        }
    }

    // ---------- 공통 ----------

    /// <summary>
    /// 텔레포트 발동 거리 결정. 기본은 teleportDistance(사람 기준 → 실제 크기 환산)지만,
    /// 아레나(ArenaWall)가 있으면 그 반지름 비율로도 제한한다.
    /// 좁은 아레나에서 발동 거리가 맵보다 넓어 "절대 텔레포트하지 않는" 상황을 막는다.
    /// </summary>
    private void ResolveTeleportDistance()
    {
        _teleportDist = teleportDistance * _k;

        if (teleportArenaRatio > 0f)
        {
            var arena = FindFirstObjectByType<ArenaWall>();
            if (arena != null)
                _teleportDist = Mathf.Min(_teleportDist, arena.Radius * teleportArenaRatio);
        }

        // 레이저를 쏠 수 있는 거리대(laserMinRange 바깥)는 남겨 둔다 —
        // 발동 거리가 너무 짧으면 멀어지는 즉시 붙어버려 원거리 패턴이 사라진다.
        _teleportDist = Mathf.Max(_teleportDist, laserMinRange * _k * 1.6f);
    }

    private bool AcquireTarget()
    {
        if (target != null) _targetT = target;
        else
        {
            _targetStats = FindFirstObjectByType<PlayerStats>();
            if (_targetStats == null) return false;
            _targetT = _targetStats.transform;
        }
        if (_targetStats == null) _targetStats = _targetT.GetComponentInParent<PlayerStats>();
        _targetDamage = _targetT.GetComponentInParent<IDamageable>();
        _targetController = _targetT.GetComponentInParent<PlayerController>();
        if (_cam == null) _cam = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCamera>() : null;

        // 플레이어를 이제 막 찾았다면 캡슐 충돌 무시도 여기서 확실히 걸어 둔다
        // (보스 Awake 시점에 플레이어가 아직 없던 구성이면 그때는 걸리지 않았다)
        if (_playerCc == null) IgnoreHitboxCollisions();
        return true;
    }

    private void TrackTarget(float speed)
    {
        if (_targetT == null || speed <= 0.001f) return;
        Vector3 to = _targetT.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude > 1e-6f) FaceDirection(to.normalized, speed);
    }

    private void FaceDirection(Vector3 dir, float speed)
    {
        if (dir.sqrMagnitude < 1e-6f) return;
        Quaternion want = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.Slerp(transform.rotation, want, speed * Time.deltaTime);
    }

    /// <summary>수평 이동 + 중력을 CharacterController로 적용.</summary>
    private void ApplyGravity(Vector3 horizontal)
    {
        if (!_cc.enabled) return;
        if (_cc.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f * _k;
        _verticalVelocity += gravity * _k * Time.deltaTime;
        _cc.Move((horizontal + Vector3.up * _verticalVelocity) * Time.deltaTime);
    }

    /// <summary>공격 중 제자리 유지: 로코모션을 대기로 되돌리고 중력만 적용.</summary>
    private void HoldStill()
    {
        SetAnimSpeed(0f);
        ApplyGravity(Vector3.zero);
    }

    private void SetAnimSpeed(float value)
    {
        _animSpeed = Mathf.MoveTowards(_animSpeed, value, 4f * Time.deltaTime);
        if (animator != null && animator.runtimeAnimatorController != null)
            animator.SetFloat(SpeedHash, _animSpeed);
    }

    private void DamagePlayer(float amount, Vector3 point, bool strong)
    {
        if (_targetDamage == null) return;

        bool dodged = _targetController != null && _targetController.IsRolling;
        Vector3 normal = (transform.position - point).normalized;
        _targetDamage.TakeDamage(amount, point, normal); // 구르기 중이면 PlayerStats가 무효 처리(회피)

        if (_cam != null)
        {
            if (dodged) _cam.AddShake(0.12f, 0.15f);
            else
            {
                _cam.AddShake(strong ? 0.6f : 0.3f, 0.35f);
                _cam.AddRoll(Random.Range(-4f, 4f));
            }
        }
        if (!dodged) _impact?.Spawn(point, normal);
    }

    /// <summary>보스 몸 중심(이펙트 기준점).</summary>
    private Vector3 BodyCenter()
        => transform.position + Vector3.up * (_cc.height * 0.5f * Mathf.Abs(transform.lossyScale.y));

    /// <summary>플레이어 몸 중심(조준/판정 기준점).</summary>
    private Vector3 TargetCenter()
    {
        if (_targetT == null) return transform.position;
        var cc = _targetT.GetComponentInParent<CharacterController>();
        if (cc != null) return _targetT.TransformPoint(cc.center);
        return _targetT.position + Vector3.up * (0.9f * _k);
    }

    private float TargetRadius()
    {
        var cc = _targetT != null ? _targetT.GetComponentInParent<CharacterController>() : null;
        return cc != null ? cc.radius * Mathf.Abs(_targetT.lossyScale.x) : 0.3f * _k;
    }

    /// <summary>플레이어가 보이는지(벽에 가려지지 않았는지).</summary>
    private bool HasLineOfSight()
    {
        if (_targetT == null) return false;
        Vector3 from = BodyCenter();
        Vector3 to = TargetCenter();
        Vector3 d = to - from;
        float dist = d.magnitude;
        if (dist < 1e-4f) return true;

        if (!RaycastIgnoreSelf(from, d / dist, dist, out RaycastHit hit)) return true;
        return hit.collider.transform.IsChildOf(_targetT) || _targetT.IsChildOf(hit.collider.transform);
    }

    /// <summary>자기 몸을 제외한 가장 가까운 충돌.</summary>
    private bool RaycastIgnoreSelf(Vector3 origin, Vector3 dir, float distance, out RaycastHit best)
    {
        best = default;
        int n = Physics.RaycastNonAlloc(origin, dir, _hitBuf, distance, obstacleMask, QueryTriggerInteraction.Ignore);
        float bestDist = float.MaxValue;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            var h = _hitBuf[i];
            if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
            if (h.distance < bestDist) { bestDist = h.distance; best = h; found = true; }
        }
        return found;
    }

    private static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ab = b - a;
        float len2 = ab.sqrMagnitude;
        if (len2 < 1e-8f) return a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
        return a + ab * t;
    }

    /// <summary>공격 연출/판정 상태를 초기화(중단·시간역행·사망 시).</summary>
    private void ResetAttackState()
    {
        _busy = false;
        _beamFiring = false;
        _orbOn = false;
        _orbCharge = 0f;
        _previewAlpha = 0f;
        _aimLocked = false;
        _farTimer = 0f;
        _judgmentActive = false;
        if (_orb != null) { _orb.Visible = false; _orb.Charge = 0f; }
        if (_realOrb != null) { _realOrb.Visible = false; _realOrb.Charge = 0f; }
        if (_beam != null) _beam.Hide();
        if (_realBeam != null) _realBeam.Hide();
        DespawnClones();
        _claw?.SetEmitting(false);
        _clawLeft?.SetEmitting(false);
        _clawSlash?.Hide();
        _rushPath?.Hide();
        SetHidden(false);
    }

    private void OnDrawGizmosSelected()
    {
        float k = Application.isPlaying ? _k : 1f;

        // 근접: 실제 판정 사거리를 그린다(설정값이 아니라 여유·마무리 배율까지 곱한 값).
        // 안쪽 원 = 일반 타, 바깥 원 = 마무리 타.
        Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, meleeRange * k * 1.1f);
        Gizmos.color = new Color(1f, 0.25f, 0.1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, meleeRange * k * 1.1f * meleeFinisherRangeScale);

        // 정면 판정 각도(meleeAngle)의 좌우 경계
        Gizmos.color = new Color(1f, 0.6f, 0.3f, 0.6f);
        float half = meleeAngle * 0.5f;
        float reach = meleeRange * k * 1.1f * meleeFinisherRangeScale;
        Gizmos.DrawRay(transform.position, Quaternion.AngleAxis(half, Vector3.up) * transform.forward * reach);
        Gizmos.DrawRay(transform.position, Quaternion.AngleAxis(-half, Vector3.up) * transform.forward * reach);
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.5f); // 돌진 발동 거리대
        Gizmos.DrawWireSphere(transform.position, rushMinRange * k);
        Gizmos.DrawWireSphere(transform.position, rushMaxRange * k);
        Gizmos.color = new Color(0.7f, 0.3f, 1f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, laserRange * k);
        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.3f);
        Gizmos.DrawWireSphere(transform.position,
            Application.isPlaying && _teleportDist > 0f ? _teleportDist : teleportDistance * k);
    }
}
