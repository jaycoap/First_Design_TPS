#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BossFX.EditorTools
{
    /// <summary>
    /// 챔버 전용 보스 패턴을 .asset 파일로 구워냅니다.
    /// GUID 문제 없이 프로젝트 안에서 만들어지므로 그냥 메뉴만 누르면 됩니다.
    /// </summary>
    public static class BossPatternBuilder
    {
        const string Root = "Assets/BossPatternFX";
        const string PatternDir = Root + "/Patterns";

        [MenuItem("BossFX/1. 챔버 보스 패턴 에셋 만들기", false, 10)]
        public static void CreateChamberPatterns()
        {
            EnsureFolder(PatternDir);

            var made = BossChamberPatterns.All();
            int n = 0;
            foreach (var p in made)
            {
                string path = $"{PatternDir}/{p.name}.asset";

                // 이미 있으면 내용만 갈아끼워서 씬의 참조를 유지합니다
                var existing = AssetDatabase.LoadAssetAtPath<BossPattern>(path);
                if (existing != null)
                {
                    EditorUtility.CopySerialized(p, existing);
                    EditorUtility.SetDirty(existing);
                    Object.DestroyImmediate(p);
                }
                else
                {
                    AssetDatabase.CreateAsset(p, path);
                }
                n++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[BossFX] 패턴 {n}개를 {PatternDir} 에 만들었습니다.");
            EditorUtility.FocusProjectWindow();
            var first = AssetDatabase.LoadAssetAtPath<Object>(
                $"{PatternDir}/{made[0].name}.asset");
            if (first != null) Selection.activeObject = first;
        }

        [MenuItem("BossFX/2. 씬에 보스 배치하기", false, 11)]
        public static void CreateBossInScene()
        {
            // 패턴이 없으면 먼저 만듭니다
            var guids = AssetDatabase.FindAssets("t:BossPattern", new[] { PatternDir });
            if (guids.Length == 0)
            {
                CreateChamberPatterns();
                guids = AssetDatabase.FindAssets("t:BossPattern", new[] { PatternDir });
            }

            var go = new GameObject("Boss");
            // 챔버 중앙 플랫폼 위
            go.transform.position = new Vector3(0f, BossChamberLayout.PlatformDeckY, 0f);

            var runner = go.AddComponent<BossPatternRunner>();
            runner.arenaRadius = BossChamberLayout.RoomApothem;
            runner.loop = true;
            runner.logSteps = true;
            runner.patterns.Clear();
            foreach (var g in guids)
            {
                var p = AssetDatabase.LoadAssetAtPath<BossPattern>(
                    AssetDatabase.GUIDToAssetPath(g));
                if (p != null) runner.patterns.Add(p);
            }
            runner.patterns.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            // 아레나 중심 = 바닥 높이 기준점
            var center = new GameObject("ArenaCenter");
            center.transform.SetParent(go.transform, false);
            center.transform.position = Vector3.zero;
            runner.arenaCenter = center.transform;

            Selection.activeGameObject = go;
            Debug.Log("[BossFX] 보스를 배치했습니다. " +
                      "인스펙터에서 target(플레이어)과 targetMask 를 지정하세요.");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
