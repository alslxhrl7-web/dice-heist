using System.IO;
using DiceHeist.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DiceHeist.EditorTools
{
    public static class DiceHeistSetup
    {
        const string ScenePath = "Assets/DiceHeist/Scenes/Table.unity";

        /// <summary>프로젝트를 열었을 때 빈 씬이면 테이블 씬을 대신 연다.</summary>
        [InitializeOnLoadMethod]
        static void OpenTableSceneOnLoad()
        {
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                var active = EditorSceneManager.GetActiveScene();
                if (string.IsNullOrEmpty(active.path) && !active.isDirty && File.Exists(ScenePath))
                    EditorSceneManager.OpenScene(ScenePath);
            };
        }

        /// <summary>카메라·조명·GameBootstrap만 있는 테이블 씬을 만들고 빌드 목록에 넣는다.</summary>
        [MenuItem("DiceHeist/테이블 씬 다시 만들기")]
        public static void CreateTableScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            new GameObject("GameBootstrap").AddComponent<GameBootstrap>();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            PlayerSettings.productName = "Dice Heist";
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            AssetDatabase.SaveAssets();
            Debug.Log("[DiceHeist] 테이블 씬 생성: " + ScenePath);
        }

        /// <summary>
        /// 색만 바꿔 쓰는 기준 머티리얼을 에셋으로 만든다. 코드에서만 만든 머티리얼은 빌드에서 셰이더가 빠져 분홍색으로 나온다.
        /// </summary>
        [MenuItem("DiceHeist/기준 머티리얼 만들기")]
        public static void EnsureBaseMaterial()
        {
            const string dir = "Assets/DiceHeist/Resources";
            string path = $"{dir}/{Fx.BaseMaterialName}.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;

            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var shader = tmp.GetComponent<Renderer>().sharedMaterial.shader;
            Object.DestroyImmediate(tmp);
            Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();
            AssetDatabase.CreateAsset(new Material(shader) { color = Color.white }, path);
            AssetDatabase.SaveAssets();
            Debug.Log("[DiceHeist] 기준 머티리얼 생성: " + path);
        }

        /// <summary>
        /// 빌드 폴더를 통째로 Builds/DiceHeist-Windows.zip으로 묶는다(이전 zip은 덮어씀). 받은 사람은 압축을 풀고 DiceHeist.exe를 실행한다.
        /// 빌드 폴더 안에 zip을 만들지 않는 이유: 다음 압축에 이전 zip이 섞여 들어간다.
        /// </summary>
        static string ZipBuild(string buildDir)
        {
            string zip = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(buildDir), "DiceHeist-Windows.zip"));
            if (File.Exists(zip)) File.Delete(zip);
            System.IO.Compression.ZipFile.CreateFromDirectory(Path.GetFullPath(buildDir), zip,
                System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: true);
            return zip;
        }

        /// <summary>Windows 실행 파일을 Builds/Windows에 만들고, 보내기 좋게 zip으로도 묶는다.</summary>
        [MenuItem("DiceHeist/Windows 빌드")]
        public static void BuildWindows()
        {
            const string output = "Builds/Windows/DiceHeist.exe";
            EnsureBaseMaterial();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            var summary = report.summary;
            Debug.Log($"[DiceHeist] 빌드 {summary.result}: {output} ({summary.totalSize / (1024 * 1024)} MB, 오류 {summary.totalErrors})");
            bool ok = summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
            string zip = null;
            if (ok)
            {
                zip = ZipBuild(Path.GetDirectoryName(output));
                Debug.Log($"[DiceHeist] 압축: {zip} ({new FileInfo(zip).Length / (1024 * 1024)} MB)");
            }
            if (Application.isBatchMode)
            {
                if (!ok) EditorApplication.Exit(1);
                return;
            }
            // 메뉴로 빌드했을 때 끝났는지 바로 알 수 있게 알린다. DiceHeist.exe는 Unity 실행기라 날짜가 잘 안 바뀌고, 바뀐 게임은 DiceHeist_Data에 들어간다
            string when = System.DateTime.Now.ToString("HH:mm:ss");
            if (ok && EditorUtility.DisplayDialog("빌드 성공",
                    $"{when}에 끝났습니다.\n보낼 파일: {zip}\n크기 {new FileInfo(zip).Length / (1024 * 1024)} MB · 걸린 시간 {summary.totalTime.TotalSeconds:0}초",
                    "폴더 열기", "닫기"))
                EditorUtility.RevealInFinder(zip);
            else if (!ok)
                EditorUtility.DisplayDialog("빌드 실패", $"{when} · 결과 {summary.result}, 오류 {summary.totalErrors}개.\nConsole 창의 빨간 줄을 확인하세요.", "닫기");
        }
    }
}
