using System.Collections;
using UnityEngine;

namespace BossFX
{
    public enum BossBarrageShape
    {
        Ring,       // 360도 균등 발사
        Fan,        // 정면 부채꼴
        Spiral,     // 웨이브마다 각도를 틀어 나선
        Aimed,      // 타깃 조준 (산탄)
        RandomRain  // 머리 위에서 무작위 낙하
    }

    /// <summary>탄막 한 세트의 설정.</summary>
    [System.Serializable]
    public class BossBarrageSettings
    {
        public BossBarrageShape shape = BossBarrageShape.Ring;

        [Tooltip("한 웨이브당 탄 수")]
        public int countPerWave = 16;

        [Tooltip("웨이브 반복 횟수")]
        public int waves = 3;

        [Tooltip("웨이브 사이 간격(초)")]
        public float waveInterval = 0.25f;

        [Tooltip("Fan / Aimed 의 벌어진 각도")]
        public float arcAngle = 70f;

        [Tooltip("Spiral 에서 웨이브마다 더해지는 회전각")]
        public float spiralStep = 11f;

        [Tooltip("첫 웨이브 시작 각도")]
        public float startAngle = 0f;

        [Tooltip("발사 위치를 중심에서 띄우는 거리")]
        public float spawnRadius = 1.2f;

        [Tooltip("RandomRain 에서 탄이 생성되는 높이")]
        public float rainHeight = 14f;

        [Tooltip("RandomRain 이 뿌려지는 반경")]
        public float rainRadius = 12f;

        public BossBulletSettings bullet = new BossBulletSettings();
    }

    public static class BossBarrage
    {
        /// <summary>
        /// 탄막을 발사합니다. 코루틴이므로 MonoBehaviour 에서 StartCoroutine 으로 호출하세요.
        /// </summary>
        public static IEnumerator Emit(BossBarrageSettings s, Transform origin, Transform target,
                                       LayerMask mask, System.Action<Collider> onHit)
        {
            int waves = Mathf.Max(1, s.waves);
            int count = Mathf.Max(1, s.countPerWave);

            for (int w = 0; w < waves; w++)
            {
                if (origin == null) yield break;

                Vector3 center = origin.position;
                Vector3 forward = origin.forward;

                if (target != null &&
                    (s.shape == BossBarrageShape.Aimed || s.shape == BossBarrageShape.Fan))
                {
                    Vector3 to = target.position - center;
                    to.y = 0f;
                    if (to.sqrMagnitude > 1e-4f) forward = to.normalized;
                }

                float baseAngle = s.startAngle + s.spiralStep * w;

                for (int i = 0; i < count; i++)
                {
                    Vector3 dir;
                    Vector3 pos;

                    switch (s.shape)
                    {
                        case BossBarrageShape.Ring:
                        case BossBarrageShape.Spiral:
                        {
                            float a = baseAngle + 360f * i / count;
                            dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                            pos = center + dir * s.spawnRadius;
                            break;
                        }
                        case BossBarrageShape.Fan:
                        {
                            float t = count == 1 ? 0.5f : i / (float)(count - 1);
                            float a = Mathf.Lerp(-s.arcAngle * 0.5f, s.arcAngle * 0.5f, t)
                                      + baseAngle;
                            dir = Quaternion.Euler(0f, a, 0f) * forward;
                            pos = center + dir * s.spawnRadius;
                            break;
                        }
                        case BossBarrageShape.Aimed:
                        {
                            float a = Random.Range(-s.arcAngle * 0.5f, s.arcAngle * 0.5f);
                            dir = Quaternion.Euler(0f, a, 0f) * forward;
                            pos = center + dir * s.spawnRadius;
                            break;
                        }
                        default: // RandomRain
                        {
                            Vector2 c = Random.insideUnitCircle * s.rainRadius;
                            Vector3 anchor = target != null ? target.position : center;
                            pos = new Vector3(anchor.x + c.x, anchor.y + s.rainHeight,
                                              anchor.z + c.y);
                            dir = Vector3.down;
                            break;
                        }
                    }

                    BossBullet.Fire(s.bullet, pos, dir, mask, target, onHit);
                }

                if (w < waves - 1 && s.waveInterval > 0f)
                    yield return new WaitForSeconds(s.waveInterval);
            }
        }
    }
}
