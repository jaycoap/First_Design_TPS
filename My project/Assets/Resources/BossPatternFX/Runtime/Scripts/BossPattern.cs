using System.Collections.Generic;
using UnityEngine;

namespace BossFX
{
    public enum BossStepType
    {
        Telegraph,   // 바닥 경고 장판 → 발동 판정
        Beam,        // 레이저 빔
        Barrage,     // 탄막
        Impact,      // 이펙트만 (판정 없음)
        Wait         // 대기
    }

    /// <summary>공격이 생성될 위치 기준.</summary>
    public enum BossOrigin
    {
        Self,            // 보스 자신
        Target,          // 타깃(플레이어) 발밑
        TargetPredicted, // 타깃이 이동할 것으로 예상되는 지점
        ArenaCenter,     // 아레나 중앙
        RandomInArena    // 아레나 안 무작위
    }

    /// <summary>패턴을 이루는 한 단계.</summary>
    [System.Serializable]
    public class BossStep
    {
        [Tooltip("인스펙터에서 알아보기 위한 이름")]
        public string label = "Step";

        public BossStepType type = BossStepType.Telegraph;

        [Header("배치")]
        public BossOrigin origin = BossOrigin.Target;

        [Tooltip("타깃 쪽을 바라보게 회전시킬지")]
        public bool faceTarget = true;

        [Tooltip("기준점에서 forward 방향으로 밀어내는 거리")]
        public float forwardOffset = 0f;

        [Tooltip("Y축 고정 회전 오프셋(도). 팔각 벽 방향처럼 절대 각도를 쓸 때")]
        public float angleOffset = 0f;

        [Tooltip("빔/탄막이 발사되는 높이 (바닥 기준). 보스가 단상 위면 올려주세요)")]
        public float originHeight = 1f;

        [Tooltip("기준점 주변 무작위 반경")]
        public float randomOffset = 0f;

        [Tooltip("이 단계를 몇 번 반복할지 (부채꼴 3연타 같은 것)")]
        public int repeat = 1;

        [Tooltip("반복 사이 간격(초)")]
        public float repeatInterval = 0.25f;

        [Tooltip("반복할 때마다 Y축으로 더해지는 각도")]
        public float repeatAngleStep = 0f;

        [Header("진행")]
        [Tooltip("이 단계가 끝날 때까지 기다릴지. 끄면 다음 단계가 겹쳐서 시작됩니다")]
        public bool waitForCompletion = true;

        [Tooltip("이 단계 뒤 추가 대기 시간")]
        public float delayAfter = 0.4f;

        [Header("피해")]
        public float damage = 10f;

        [Header("내용물 (type 에 맞는 것만 사용됩니다)")]
        public BossTelegraphSettings telegraph = new BossTelegraphSettings();
        public BossBeamSettings beam = new BossBeamSettings();
        public BossBarrageSettings barrage = new BossBarrageSettings();
        public BossImpactSettings impact = new BossImpactSettings();

        [Tooltip("Telegraph 발동 시 함께 터뜨릴 충격파를 쓸지")]
        public bool impactOnFire = true;
    }

    /// <summary>
    /// 보스 패턴 하나. Assets 우클릭 → Create → BossFX → Boss Pattern 으로 만듭니다.
    /// </summary>
    [CreateAssetMenu(fileName = "BossPattern", menuName = "BossFX/Boss Pattern", order = 0)]
    public class BossPattern : ScriptableObject
    {
        [TextArea(1, 3)]
        public string description = "";

        [Tooltip("패턴이 끝난 뒤 쉬는 시간")]
        public float recoveryTime = 1.5f;

        public List<BossStep> steps = new List<BossStep>();
    }
}
