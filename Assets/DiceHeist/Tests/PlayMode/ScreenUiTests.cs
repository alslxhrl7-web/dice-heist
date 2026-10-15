using System.Collections;
using DiceHeist.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace DiceHeist.Tests
{
    /// <summary>로그인 화면의 Tab 이동과 환경설정 음량.</summary>
    public class ScreenUiTests
    {
        GameObject _events;

        [SetUp]
        public void SetUp()
        {
            if (EventSystem.current == null)
                _events = new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
        }

        [TearDown]
        public void TearDown()
        {
            if (_events != null) Object.Destroy(_events);
        }

        [UnityTest]
        public IEnumerator 로그인_화면에서_Tab으로_아이디와_비밀번호를_오간다()
        {
            var login = LoginScreen.Build(UiFont.Korean, "http://localhost");
            yield return null;
            yield return null;
            Assert.AreEqual(0, login.FocusedField, "처음엔 아이디 칸");

            yield return Shot(login.GetComponent<Canvas>(), "login");
            login.MoveFocus(false);
            yield return null;
            Assert.AreEqual(1, login.FocusedField, "Tab → 비밀번호");

            login.MoveFocus(false);
            yield return null;
            Assert.AreEqual(0, login.FocusedField, "Tab → 다시 아이디");

            login.MoveFocus(true);
            yield return null;
            Assert.AreEqual(1, login.FocusedField, "Shift+Tab → 비밀번호");

            login.Close();
            yield return null;
        }

        [UnityTest]
        public IEnumerator 환경설정에서_음량을_바꾸면_바로_적용되고_저장된다()
        {
            float master = GameSettings.Master, sfx = GameSettings.Sfx;
            try
            {
                var panel = SettingsPanel.Open(UiFont.Korean);
                yield return null;

                yield return Shot(panel.GetComponent<Canvas>(), "settings");
                panel.MasterSlider.value = 0.3f;
                panel.SfxSlider.value = 0.5f;
                Assert.AreEqual(0.3f, GameSettings.Master, 1e-4f);
                Assert.AreEqual(0.3f, AudioListener.volume, 1e-4f, "전체 음량은 바로 반영");
                Assert.AreEqual(0.5f, GameSettings.Sfx, 1e-4f);
                Assert.AreEqual(0.4f, DiceAudio.Volume, 1e-4f, "효과음 = 0.8 × 설정");

                bool closed = false;
                panel.Closed += () => closed = true;
                panel.Close();
                yield return null;
                Assert.IsTrue(closed);
                Assert.IsTrue(panel == null, "닫으면 사라진다");
            }
            finally
            {
                // 사람의 설정을 그대로 돌려 놓는다
                GameSettings.Master = master;
                GameSettings.Sfx = sfx;
                GameSettings.Save();
            }
        }

        /// <summary>화면 하나를 Logs/Shots에 저장한다(눈으로 배치 확인용).</summary>
        static IEnumerator Shot(Canvas canvas, string name)
        {
            const int w = 1600, h = 900;
            var cam = new GameObject("ShotCam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.72f, 0.79f, 0.75f);
            var rt = new RenderTexture(w, h, 24);
            var mode = canvas.renderMode;
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
            string dir = System.IO.Path.Combine(Application.dataPath, "../Logs/Shots");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            canvas.renderMode = mode;
            cam.targetTexture = null;
            Object.Destroy(tex);
            Object.Destroy(cam.gameObject);
            rt.Release();
        }
    }
}
