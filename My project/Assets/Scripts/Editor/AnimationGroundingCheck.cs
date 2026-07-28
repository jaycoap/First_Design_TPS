using UnityEngine;
using UnityEditor;

/// <summary>
/// 로코모션 클립들이 씬의 Player 모델(아바타)로 리타게팅됐을 때
/// 발바닥이 지면(루트 Y)에서 얼마나 뜨는지 실측하는 진단 도구.
///
/// 각 클립을 실제 Player에 샘플링한 뒤 발/발끝 본 높이로 발바닥 위치를 추정해
/// "루트 대비 갭"을 클립별로 콘솔에 출력한다.
/// - 갭이 + : 발이 공중에 뜸 / - : 발이 지면에 파묻힘
/// - 모든 클립이 비슷한 +갭이면 모델 아바타 기준 문제(공통 오프셋),
///   특정 클립만 크면 그 클립의 임포트 설정 문제다.
///
/// 메뉴: Tools/TPS/Check Animation Grounding
/// </summary>
public static class AnimationGroundingCheck
{
    private const string AnimDir = "Assets/Resources/Player/Animation/";

    private static readonly string[] FbxPaths =
    {
        AnimDir + "Rifle Aiming Idle.fbx",
        AnimDir + "Walk Forward.fbx",
        AnimDir + "Idle To Running.fbx",
        AnimDir + "Rifle Run.fbx",
        AnimDir + "Running Dive Roll.fbx",
    };

    [MenuItem("Tools/TPS/Check Animation Grounding")]
    public static void Run()
    {
        var player = GameObject.Find("Player");
        if (player == null) { Debug.LogError("[TPS-Ground] 씬에 'Player'가 없습니다."); return; }

        var animator = player.GetComponentInChildren<Animator>();
        if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
        {
            Debug.LogError("[TPS-Ground] Player의 휴머노이드 Animator/Avatar를 찾지 못했습니다.");
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[TPS-Ground] 클립별 발-지면 갭 (단위: 월드, +면 공중에 뜸)");
        float ccHeight = player.TryGetComponent(out CharacterController cc) ? cc.height : 0f;

        AnimationMode.StartAnimationMode();
        try
        {
            foreach (string path in FbxPaths)
            {
                AnimationClip clip = LoadClip(path);
                if (clip == null) { sb.AppendLine($"  {path} : 클립 로드 실패"); continue; }

                float min = float.PositiveInfinity, max = float.NegativeInfinity, sum = 0f;
                const int samples = 8;
                for (int i = 0; i < samples; i++)
                {
                    float t = clip.length * i / samples;
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(player, clip, t);
                    AnimationMode.EndSampling();

                    float gap = SoleY(animator) - player.transform.position.y;
                    min = Mathf.Min(min, gap);
                    max = Mathf.Max(max, gap);
                    sum += gap;
                }

                float avg = sum / samples;
                string pct = ccHeight > 0f ? $" (키의 {avg / ccHeight * 100f:F1}%)" : "";
                sb.AppendLine($"  {clip.name,-22} 최저 {min:F4} / 평균 {avg:F4}{pct} / 최고 {max:F4}  ← {System.IO.Path.GetFileName(path)}");
            }
        }
        finally
        {
            AnimationMode.StopAnimationMode();
        }

        sb.AppendLine("  판독: '최저'가 0 근처(±0.002)면 정상. 전 클립이 비슷하게 +면 아바타 공통 오프셋.");
        Debug.Log(sb.ToString());
    }

    /// <summary>현재 포즈에서 발바닥 추정 높이(월드 Y). 발 본-발바닥 오프셋과 발끝 본 중 최저값.</summary>
    private static float SoleY(Animator animator)
    {
        float sole = float.PositiveInfinity;
        Transform lf = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        Transform rf = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        if (lf != null) sole = Mathf.Min(sole, lf.position.y - animator.leftFeetBottomHeight);
        if (rf != null) sole = Mathf.Min(sole, rf.position.y - animator.rightFeetBottomHeight);
        Transform lt = animator.GetBoneTransform(HumanBodyBones.LeftToes);
        Transform rt = animator.GetBoneTransform(HumanBodyBones.RightToes);
        if (lt != null) sole = Mathf.Min(sole, lt.position.y);
        if (rt != null) sole = Mathf.Min(sole, rt.position.y);
        return sole;
    }

    private static AnimationClip LoadClip(string path)
    {
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(path))
            if (a is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                return clip;
        return null;
    }
}
