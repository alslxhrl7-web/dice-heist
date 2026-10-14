using System.Collections;
using System.Collections.Generic;
using System.IO;
using DiceHeist.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace DiceHeist.Tests
{
    /// <summary>자동 진행으로 실제 연출까지 포함해 여러 판을 돌려, 예외 없이 끝까지 가는지 본다. 단계별 화면도 Logs/Shots에 저장한다.</summary>
    public class TableSmokeTests
    {
        const int Rounds = 4;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            GameBootstrap.AutoPlay = false;
            // 다음 테스트에 테이블·UI가 겹쳐 남지 않게 치운다
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects()) Object.Destroy(go);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator 시작_화면에서_시작을_누르면_게임이_시작된다()
        {
            var boot = new GameObject("GameBootstrap").AddComponent<GameBootstrap>();
            boot.useRandomSeed = false;
            yield return null;
            yield return null;

            Assert.IsNotNull(boot.StartScreen, "시작 화면이 먼저 떠야 한다");
            Assert.AreEqual("idle", boot.Director.Phase, "시작을 누르기 전에는 게임이 진행되지 않는다");
            Assert.IsFalse(boot.Hud.Canvas.enabled);
            yield return Capture(boot.Cam, boot.StartScreen.Canvas, "start");

            boot.StartScreen.transform.Find("Card/Btn_start").GetComponent<Button>().onClick.Invoke();
            yield return null;
            yield return null;

            Assert.IsTrue(boot.StartScreen == null);
            Assert.IsTrue(boot.Hud.Canvas.enabled);
            Assert.AreNotEqual("idle", boot.Director.Phase);
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator 자동_진행으로_여러_판을_끝까지()
        {
            GameBootstrap.AutoPlay = true;
            Time.timeScale = 5f;
            var boot = new GameObject("GameBootstrap").AddComponent<GameBootstrap>();
            boot.useRandomSeed = false;
            boot.seed = 20261013;
            yield return null;
            yield return null;
            Assert.IsNotNull(boot.Director);

            var shot = new HashSet<string>();
            float deadline = Time.realtimeSinceStartup + 420f;
            while (!(boot.Director.RoundsFinished >= Rounds && boot.Director.Phase == "result"))
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, $"시간 초과: {boot.Director.Phase}, {boot.Director.RoundsFinished}판");
                string phase = boot.Director.Phase;
                string key = phase == "result" || phase == "hold" ? $"{phase}_{boot.Director.Game.RoundNo}" : phase;
                if (shot.Add(key))
                {
                    // 연출이 조금 진행된 뒤의 화면을 남긴다
                    yield return new WaitForSeconds(phase == "result" ? 0.3f : 0.65f);
                    yield return Capture(boot.Cam, boot.Hud.Canvas, key);
                }
                yield return null;
            }

            int total = 0;
            foreach (var p in boot.Director.Game.Players)
            {
                Assert.GreaterOrEqual(p.Chips, 0);
                total += p.Chips;
            }
            Assert.AreEqual(3000, total, "지갑 채우기가 없었다면 칩 총량은 그대로여야 한다");
        }

        static IEnumerator Capture(Camera cam, Canvas canvas, string name)
        {
            const int w = 1600, h = 900;
            var rt = new RenderTexture(w, h, 24);
            float scale = Time.timeScale;
            Time.timeScale = 0f;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            cam.targetTexture = rt;
            yield return null;
            yield return null;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            string dir = Path.Combine(Application.dataPath, "../Logs/Shots");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());

            cam.targetTexture = null;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Time.timeScale = scale;
            Object.Destroy(tex);
            rt.Release();
        }
    }
}
