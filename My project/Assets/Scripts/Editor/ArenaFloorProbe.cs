using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 배경 메시를 실제로 훑어 "플레이어가 떨어지지 않고 다닐 수 있는 바닥"의 모양을 재는 도구.
///
/// 왜 필요한가
///  - 모델의 바운즈는 천장·외벽·장식까지 포함해서 발판 크기와 아무 상관이 없다.
///  - 위에서 아래로 한 번 쏘는 방식은 닫힌 실내에서 천장이나 중앙 단상을 바닥으로 오인한다.
///
/// 어떻게 재는가
///  1) 바닥 높이 후보를 모은다
///     - 기준점(플레이어)이 확실하면 그 발밑 하나만 쓴다
///     - 맵을 새로 깔 때처럼 기준점이 없으면, 위에서 아래로 만나는 면을 전부 후보로 삼고
///       각각 재 본 뒤 "가장 넓은 면들 중 가장 낮은 것"을 고른다(천장이 아니라 바닥을 잡기 위해)
///  2) 32~48방향으로 바닥을 따라 한 걸음씩 걸어 나간다. 발밑에 면이 이어지면 계속,
///     끊기면 거기가 가장자리
///  3) 방 한가운데 놓인 상자·기둥 때문에 끊긴 것은 가장자리가 아니므로,
///     짧은 거리 안에 같은 높이로 바닥이 다시 나타나면 뛰어넘어 계속 간다
///  4) 방향별 거리가 한쪽으로 치우쳐 있으면 그만큼 중심이 어긋난 것이므로 보정 후 다시 잰다
///
/// 결과는 방향별 반지름 배열(프로필)이라 원이 아닌 방(정사각형 격납고 등)도 그대로 표현된다.
/// </summary>
public static class ArenaFloorProbe
{
    /// <summary>천장·단상 후보를 몇 개까지 재 볼지.</summary>
    private const int MaxCandidates = 8;

    /// <summary>가장 넓은 후보의 이 비율 이상이면 "비슷하게 넓다"고 보고, 그 중 낮은 쪽을 바닥으로 친다.</summary>
    private const float TieRatio = 0.8f;

    /// <summary>측정 결과.</summary>
    public struct Floor
    {
        /// <summary>바닥 중심(월드). y는 바닥 높이.</summary>
        public Vector3 center;
        /// <summary>방향별 반지름(월드 단위). 0번이 +Z, 인덱스가 늘면 시계 방향.</summary>
        public float[] profile;
        /// <summary>가장 짧은 방향 / 가장 먼 방향.</summary>
        public float min, max;
    }

    /// <summary>바닥 모양을 잰다. 실패하면 false.</summary>
    /// <param name="stage">배경 루트(이 아래의 콜라이더만 바닥으로 인정한다).</param>
    /// <param name="seed">측정 시작점. 바닥 위 아무 데나면 된다(보통 플레이어 위치).</param>
    /// <param name="directions">몇 방향으로 잴지(그대로 벽 조각 수가 된다).</param>
    /// <param name="searchLevels">
    /// true면 기준점 아래위의 모든 면을 후보로 재 보고 제일 그럴듯한 바닥을 고른다.
    /// 맵을 새로 깔아 플레이어가 아직 그 위에 없을 때 쓴다.
    /// </param>
    public static bool Measure(GameObject stage, Vector3 seed, int directions, out Floor floor,
                               bool searchLevels = false)
    {
        floor = default;
        if (stage == null) return false;
        if (!TryGetBounds(stage, out Bounds b)) return false;

        float span = Mathf.Max(b.size.x, b.size.z);
        if (span <= 1e-5f) return false;

        directions = Mathf.Clamp(directions, 8, 128);

        float band = span * 0.01f;      // 한 걸음에 오르내릴 수 있는 높이(턱·경사 허용치)
        float step = span * 0.0025f;    // 걸음 간격
        float maxGap = span * 0.03f;    // 이만큼 안에 바닥이 다시 나오면 장애물로 보고 뛰어넘는다

        // --- 바닥 높이 후보 모으기 ---
        var levels = new List<float>();
        if (searchLevels)
        {
            CollectSurfaces(stage, new Vector2(seed.x, seed.z), b, span, band, levels);
            if (levels.Count == 0)
            {
                seed = b.center;
                CollectSurfaces(stage, new Vector2(seed.x, seed.z), b, span, band, levels);
            }
        }
        else if (TryFloorAt(stage, new Vector3(seed.x, seed.y + band * 2f, seed.z), span, out float y0))
        {
            levels.Add(y0);
        }

        if (levels.Count == 0)
        {
            // 마지막 수단: 콜라이더 바운즈 중앙 높이에서 아래로
            if (!TryFloorAt(stage, b.center, span, out float y1)) return false;
            seed = b.center;
            levels.Add(y1);
        }

        // --- 후보마다 재 본다 ---
        var results = new List<Floor>();
        foreach (float y in levels)
        {
            if (MeasureFrom(stage, new Vector3(seed.x, y, seed.z), span, band, step, maxGap, directions,
                            out Floor f))
                results.Add(f);
        }
        if (results.Count == 0) return false;

        // 넓은 쪽이 바닥이다. 비슷하게 넓은 면이 여럿이면(바닥과 천장처럼) 낮은 쪽을 고른다.
        float widest = 0f;
        foreach (var f in results) widest = Mathf.Max(widest, f.min);

        floor = results[0];
        bool picked = false;
        foreach (var f in results)
        {
            if (f.min < widest * TieRatio) continue;
            if (!picked || f.center.y < floor.center.y) { floor = f; picked = true; }
        }
        return true;
    }

    // ---------- 내부 ----------

    /// <summary>한 높이를 바닥으로 놓고 모양을 잰다(중심 보정 1회 포함).</summary>
    private static bool MeasureFrom(GameObject stage, Vector3 center, float span, float band, float step,
                                    float maxGap, int directions, out Floor floor)
    {
        floor = default;

        float[] profile = Sweep(stage, center, span, band, step, maxGap, directions, out Vector3 shift);
        float min = MinOf(profile);
        if (min <= step) return false;

        if (shift.magnitude > step && shift.magnitude < min)
        {
            Vector3 better = center + shift;
            if (TryFloorAt(stage, new Vector3(better.x, center.y + band * 2f, better.z), span, out float y2))
            {
                better.y = y2;
                float[] p2 = Sweep(stage, better, span, band, step, maxGap, directions, out _);
                if (MinOf(p2) > min) { center = better; profile = p2; min = MinOf(p2); }
            }
        }

        floor.center = center;
        floor.profile = profile;
        floor.min = min;
        floor.max = MaxOf(profile);
        return true;
    }

    /// <summary>중심에서 방향별로 바닥을 따라 걸어 나가며 가장자리까지의 거리를 잰다.</summary>
    private static float[] Sweep(GameObject stage, Vector3 center, float span, float band, float step,
                                 float maxGap, int directions, out Vector3 centerShift)
    {
        var profile = new float[directions];
        float maxRadius = span;
        Vector3 sum = Vector3.zero;

        for (int d = 0; d < directions; d++)
        {
            float a = 360f / directions * d * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));

            float y = center.y;
            float edge = 0f;

            for (float r = step; r <= maxRadius; r += step)
            {
                if (TryStepTo(stage, center + dir * r, y, band, out float hitY))
                {
                    y = hitY;                       // 완만한 경사·턱은 따라 오른다
                    edge = r;
                    continue;
                }

                // 발밑이 끊겼다. 방 한가운데 상자·기둥일 수도 있으니 조금 더 가 본다.
                bool resumed = false;
                for (float g = step; g <= maxGap; g += step)
                {
                    if (TryStepTo(stage, center + dir * (r + g), y, band, out float aheadY))
                    {
                        y = aheadY;
                        r += g;                     // 장애물을 뛰어넘어 계속
                        edge = r;
                        resumed = true;
                        break;
                    }
                }
                if (!resumed) break;                // 진짜 가장자리
            }

            profile[d] = edge;
            sum += dir * edge;
        }

        // 방향별 거리가 한쪽으로 치우쳤으면 그만큼 중심이 어긋나 있다는 뜻
        centerShift = -2f * (sum / directions);
        return profile;
    }

    /// <summary>기준 높이 y에서 xz 지점으로 한 걸음 갔을 때 발밑에 면이 있는가.</summary>
    private static bool TryStepTo(GameObject stage, Vector3 flat, float y, float band, out float hitY)
    {
        hitY = y;
        Vector3 from = new Vector3(flat.x, y + band, flat.z);
        if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, band * 3f, ~0, QueryTriggerInteraction.Ignore))
            return false;
        if (!hit.collider.transform.IsChildOf(stage.transform)) return false;
        hitY = hit.point.y;
        return true;
    }

    /// <summary>해당 xz에서 위에서 아래로 만나는 면들의 높이를 전부 모은다(높은 것부터, 비슷한 높이는 하나로).</summary>
    private static void CollectSurfaces(GameObject stage, Vector2 xz, Bounds b, float span, float band,
                                        List<float> levels)
    {
        Vector3 from = new Vector3(xz.x, b.max.y + span * 0.1f, xz.y);
        var hits = Physics.RaycastAll(from, Vector3.down, span * 3f, ~0, QueryTriggerInteraction.Ignore);

        var ys = new List<float>();
        foreach (var h in hits)
        {
            if (!h.collider.transform.IsChildOf(stage.transform)) continue;
            ys.Add(h.point.y);
        }
        ys.Sort();
        ys.Reverse();

        foreach (float y in ys)
        {
            bool near = false;
            foreach (float k in levels) if (Mathf.Abs(k - y) < band) { near = true; break; }
            if (near) continue;

            levels.Add(y);
            if (levels.Count >= MaxCandidates) return;
        }
    }

    /// <summary>해당 지점에서 아래로 쏴 배경의 바닥 높이를 얻는다(위에서 만나는 첫 면).</summary>
    private static bool TryFloorAt(GameObject stage, Vector3 from, float span, out float y)
    {
        y = 0f;
        var hits = Physics.RaycastAll(from, Vector3.down, span * 2f, ~0, QueryTriggerInteraction.Ignore);

        bool found = false;
        float best = float.MinValue;
        foreach (var h in hits)
        {
            if (!h.collider.transform.IsChildOf(stage.transform)) continue;
            if (h.point.y > best) { best = h.point.y; found = true; }
        }
        if (found) y = best;
        return found;
    }

    /// <summary>배경에 붙은 콜라이더 전체의 바운즈(렌더러가 아니라 콜라이더 기준).</summary>
    public static bool TryGetBounds(GameObject root, out Bounds bounds)
    {
        bounds = new Bounds();
        bool has = false;
        foreach (var c in root.GetComponentsInChildren<Collider>())
        {
            if (c.isTrigger) continue;
            if (!has) { bounds = c.bounds; has = true; }
            else bounds.Encapsulate(c.bounds);
        }
        return has;
    }

    private static float MinOf(float[] v)
    {
        float m = float.MaxValue;
        for (int i = 0; i < v.Length; i++) m = Mathf.Min(m, v[i]);
        return m == float.MaxValue ? 0f : m;
    }

    private static float MaxOf(float[] v)
    {
        float m = 0f;
        for (int i = 0; i < v.Length; i++) m = Mathf.Max(m, v[i]);
        return m;
    }
}
