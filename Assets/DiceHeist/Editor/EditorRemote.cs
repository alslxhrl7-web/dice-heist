using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace DiceHeist.EditorTools
{
    /// <summary>
    /// 열려 있는 에디터를 끄지 않고 밖에서 시키는 통로. 사람이 보는 에디터 화면에서 그대로 Play·테스트가 돈다.
    /// 프로젝트의 Temp/DiceHeist.cmd 파일에 한 줄 명령을 쓰면 읽고 지운 뒤 실행하고, 결과는 Temp/DiceHeist.result에 적는다.
    ///   refresh     스크립트·에셋 다시 읽기(코드를 고친 뒤)
    ///   play / stop Play 모드 켜기·끄기
    ///   test-edit / test-play   EditMode·PlayMode 테스트(PlayMode는 Game 창에서 실제로 돈다)
    /// 빌드는 하지 않는다(2026-10-16 사용자 결정: 빌드는 사용자가 직접 한다).
    /// </summary>
    [InitializeOnLoad]
    public static class EditorRemote
    {
        const string CommandFile = "Temp/DiceHeist.cmd";
        const string ResultFile = "Temp/DiceHeist.result";
        static double _next;

        static TestRunnerApi _api;

        static EditorRemote()
        {
            if (Application.isBatchMode) return;
            EditorApplication.update += Poll;
            // Play 모드로 들어가면 스크립트를 다시 읽어 등록이 사라지므로, 읽을 때마다 결과 받기를 다시 건다
            _api = ScriptableObject.CreateInstance<TestRunnerApi>();
            _api.RegisterCallbacks(new Callbacks());
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _next) return;
            _next = EditorApplication.timeSinceStartup + 0.5;
            if (!File.Exists(CommandFile)) return;
            string cmd;
            try
            {
                cmd = File.ReadAllText(CommandFile).Trim();
                File.Delete(CommandFile);
            }
            catch (IOException) { return; } // 쓰는 중이면 다음에
            Run(cmd);
        }

        static void Write(string text)
        {
            File.WriteAllText(ResultFile, $"{DateTime.Now:HH:mm:ss} {text}\n", Encoding.UTF8);
            Debug.Log("[DiceHeist 원격] " + text);
        }

        static void Run(string cmd)
        {
            Write("받음: " + cmd);
            switch (cmd)
            {
                case "refresh":
                    AssetDatabase.Refresh();
                    Write("done refresh");
                    break;
                case "play":
                    EditorApplication.isPlaying = true;
                    Write("done play");
                    break;
                case "stop":
                    EditorApplication.isPlaying = false;
                    Write("done stop");
                    break;
                case "test-edit":
                case "test-play":
                    _api.Execute(new ExecutionSettings(new Filter
                    {
                        testMode = cmd == "test-edit" ? TestMode.EditMode : TestMode.PlayMode,
                    }));
                    break;
                default:
                    // "only <테스트 전체 이름>": 그 테스트(또는 클래스)만. [Explicit] 테스트도 이름으로 부르면 돈다
                    // "only-play <이름>"은 PlayMode 테스트를 이름으로
                    if (cmd.StartsWith("only ") || cmd.StartsWith("only-play "))
                    {
                        bool play = cmd.StartsWith("only-play ");
                        _api.Execute(new ExecutionSettings(new Filter
                        {
                            testMode = play ? TestMode.PlayMode : TestMode.EditMode,
                            groupNames = new[] { "^" + System.Text.RegularExpressions.Regex.Escape(cmd.Substring(play ? 10 : 5).Trim()) },
                        }));
                        break;
                    }
                    Write("모르는 명령: " + cmd);
                    break;
            }
        }

        sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                var sb = new StringBuilder();
                sb.Append($"done test total={result.PassCount + result.FailCount + result.SkipCount} passed={result.PassCount} failed={result.FailCount}");
                Collect(result, sb);
                Write(sb.ToString());
            }

            static void Collect(ITestResultAdaptor r, StringBuilder sb)
            {
                if (!r.HasChildren && r.TestStatus == TestStatus.Failed)
                    sb.Append($"\nFAIL {r.Name}: {r.Message}");
                if (r.HasChildren)
                    foreach (var c in r.Children) Collect(c, sb);
            }
        }
    }
}
