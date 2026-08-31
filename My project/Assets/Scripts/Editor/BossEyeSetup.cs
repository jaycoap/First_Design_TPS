using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// 보스 눈빛의 위치를 <b>씬 오브젝트로</b> 잡게 해 주는 도구.
///
/// 눈 위치는 인스펙터의 <c>Eye Glow Offset</c>(머리 길이 비율)으로도 잡을 수 있지만,
/// 숫자를 넣고 재생해 보는 왕복은 미세 조정에 맞지 않는다. 이 도구는 머리 본 아래에
/// 빈 오브젝트 두 개를 만들어 <see cref="BossController"/>에 연결한다 — 이후에는
/// 씬 뷰에서 <b>끌어다 놓기만</b> 하면 되고, 오브젝트를 키우면 눈도 같이 커진다.
///
/// 처음 놓이는 자리는 오프셋으로 계산한 값이라, 대개는 앞뒤로 조금 미는 정도면 끝난다.
///
/// 메뉴: Tools/TPS/보스 눈 앵커 만들기
/// </summary>
public static class BossEyeSetup
{
    private const string LeftName = "EyeAnchor_L";
    private const string RightName = "EyeAnchor_R";

    [MenuItem("Tools/TPS/보스 눈 앵커 만들기")]
    public static void CreateAnchors()
    {
        var boss = Object.FindFirstObjectByType<BossController>();
        if (boss == null)
        {
            EditorUtility.DisplayDialog("보스 눈 앵커",
                "씬에서 BossController를 찾지 못했습니다.\n보스를 씬에 놓은 뒤 다시 실행하세요.", "확인");
            return;
        }

        var anim = boss.GetComponent<Animator>();
        Transform head = anim != null && anim.avatar != null && anim.avatar.isHuman
                       ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null)
        {
            EditorUtility.DisplayDialog("보스 눈 앵커",
                "머리 본을 찾지 못했습니다(휴머노이드 아바타가 아닙니다).\n" +
                "이 모델에서는 눈빛을 쓸 수 없습니다.", "확인");
            return;
        }

        var so = new SerializedObject(boss);
        Vector3 offset = so.FindProperty("eyeGlowOffset").vector3Value;

        Transform headTop = BossRig.FindHeadTop(head);
        BossFx.ResolveEyePositions(head, headTop, boss.transform, offset, 1f,
                                   out Vector3 left, out Vector3 right, out _);

        Transform l = MakeAnchor(head, LeftName, left);
        Transform r = MakeAnchor(head, RightName, right);

        so.FindProperty("eyeAnchorLeft").objectReferenceValue = l;
        so.FindProperty("eyeAnchorRight").objectReferenceValue = r;
        so.ApplyModifiedProperties();

        Selection.objects = new Object[] { l.gameObject, r.gameObject };
        EditorSceneManager.MarkSceneDirty(boss.gameObject.scene);

        Debug.Log($"[Boss] 눈 앵커를 '{head.name}' 아래에 만들고 연결했습니다. " +
                  "씬 뷰에서 끌어다 눈 위치에 맞추세요(오브젝트를 키우면 눈도 커집니다). " +
                  "보스를 선택하면 기즈모로 현재 눈 자리가 보입니다.");
    }

    /// <summary>이미 있으면 그것을 쓰고, 없을 때만 만든다(두 번 실행해도 늘어나지 않게).</summary>
    private static Transform MakeAnchor(Transform head, string name, Vector3 worldPos)
    {
        Transform t = head.Find(name);
        if (t == null)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "보스 눈 앵커 만들기");
            go.transform.SetParent(head, false);
            t = go.transform;
        }
        else Undo.RecordObject(t, "보스 눈 앵커 위치");

        t.position = worldPos;
        // 본 스케일이 실려 있으면 '1'이 눈 크기 배율 1이 아니게 된다 — 로컬 1로 맞춘다
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;
        return t;
    }
}
