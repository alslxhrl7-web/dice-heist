using System;
using System.Collections;
using DiceHeist.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DiceHeist.Game
{
    /// <summary>
    /// 씬에 이 컴포넌트 하나만 있으면 테이블·동물·UI를 조립하고 게임을 시작한다.
    /// 흐름: 로그인(서버 계정) → 시작 화면(테이블 고르기·환전·멀티) → 테이블 → 테이블 선택으로 돌아오기.
    /// 서버 계정이면 판정과 칩 저장을 서버가 하고, "서버 없이 연습"이면 이 PC에서 같은 규칙으로 돈다.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Tooltip("켜면 실행할 때마다 새 시드를 쓴다. 끄면 아래 시드로 같은 판이 재현된다(오프라인에서만).")]
        public bool useRandomSeed = true;
        public long seed = 20261013;

        [Tooltip("게임 서버 주소. 같은 주소가 멀티 사이트다.")]
        public string serverUrl = "https://desktop-5omono1.tailab2121.ts.net";

        /// <summary>테스트용: 사람 자리도 자동으로 진행한다. 켜면 서버를 쓰지 않고 저장도 하지 않는다.</summary>
        public static bool AutoPlay;
        /// <summary>끄면 오프라인 기록(PlayerPrefs)을 읽지도 쓰지도 않는다(테스트가 사람의 저장을 건드리지 않게).</summary>
        public static bool UseSave = true;
        /// <summary>끄면 로그인 없이 바로 오프라인으로 시작한다(테스트용).</summary>
        public static bool UseServer = true;

        public TableDirector Director { get; private set; }
        public TableScene Scene { get; private set; }
        public HudView Hud { get; private set; }
        public Camera Cam { get; private set; }
        /// <summary>시작(테이블 선택) 화면. 테이블에 앉아 있거나 로그인 중이면 null.</summary>
        public StartScreen StartScreen { get; private set; }
        public LoginScreen LoginScreen { get; private set; }
        public ISingleBackend Backend { get; private set; }
        /// <summary>마지막으로 받은 계정 정보(칩·사이버머니·멀티 열림).</summary>
        public Profile Profile { get; private set; }

        SimulationMode _previousMode;

        void Start()
        {
            if (useRandomSeed) seed = DateTime.UtcNow.Ticks % 1000000000L;

            // 주사위 물리는 DiceThrower가 필요할 때만 직접 돌린다
            _previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;

            Cam = Camera.main;
            if (Cam == null)
            {
                Cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
                Cam.tag = "MainCamera";
            }
            Cam.transform.position = new Vector3(0f, 12.5f, -11.5f);
            Cam.transform.LookAt(new Vector3(0f, 0f, -1.6f));
            Cam.fieldOfView = 42f;
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = new Color(0.72f, 0.79f, 0.75f);
            if (Cam.GetComponent<PhysicsRaycaster>() == null) Cam.gameObject.AddComponent<PhysicsRaycaster>();

            if (FindAnyObjectByType<Light>() == null)
            {
                var light = new GameObject("Sun", typeof(Light)).GetComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.15f;
                light.shadows = LightShadows.Soft;
                light.transform.rotation = Quaternion.Euler(52f, -28f, 0f);
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.36f, 0.38f, 0.37f);

            Scene = TableScene.Build();
            var anchors = new Transform[Scene.Seats.Length];
            for (int i = 0; i < anchors.Length; i++) anchors[i] = Scene.Seats[i].LabelAnchor;
            Hud = HudView.Build(Cam, anchors, Scene.PotAnchor);
            Hud.Canvas.enabled = false;

            Director = gameObject.AddComponent<TableDirector>();
            Director.Left += message => StartCoroutine(ShowTableSelect(message));

            if (AutoPlay)
            {
                Backend = new LocalBackend(new SeededRandom(seed), false);
                Sit(TableConfig.Bronze);
            }
            else if (!UseServer) GoOffline();
            else StartCoroutine(TryAutoLogin());
        }

        // ---------- 로그인 ----------

        /// <summary>저장된 토큰이 있으면 그대로 들어가고, 없거나 만료됐으면 로그인 화면.</summary>
        IEnumerator TryAutoLogin()
        {
            var token = RemoteBackend.SavedToken;
            if (!string.IsNullOrEmpty(token))
            {
                var remote = new RemoteBackend(serverUrl, token);
                Profile p = null;
                yield return remote.GetProfile(r => p = r);
                if (string.IsNullOrEmpty(p.error))
                {
                    Backend = remote;
                    Profile = p;
                    yield return ShowTableSelect(null);
                    yield break;
                }
                if (remote.Unauthorized) RemoteBackend.SavedToken = "";
                ShowLogin(p.error);
                yield break;
            }
            ShowLogin(null);
        }

        void ShowLogin(string message)
        {
            CloseScreens();
            LoginScreen = LoginScreen.Build(Hud.Font, serverUrl);
            if (!string.IsNullOrEmpty(message)) LoginScreen.ShowMessage(message);
            LoginScreen.Submitted += (register, name, password) => StartCoroutine(Login(register, name, password));
            LoginScreen.OfflineChosen += GoOffline;
        }

        IEnumerator Login(bool register, string name, string password)
        {
            AuthResult result = null;
            yield return RemoteBackend.Login(serverUrl, register, name, password, r => result = r);
            if (!string.IsNullOrEmpty(result.error) || string.IsNullOrEmpty(result.token))
            {
                LoginScreen.SetBusy(false);
                LoginScreen.ShowMessage(string.IsNullOrEmpty(result.error) ? "로그인하지 못했습니다." : result.error);
                yield break;
            }
            Backend = new RemoteBackend(serverUrl, result.token);
            Profile = result.profile;
            yield return ShowTableSelect(null);
        }

        void GoOffline()
        {
            Backend = new LocalBackend(new SeededRandom(seed), UseSave);
            StartCoroutine(ShowTableSelect(null));
        }

        // ---------- 시작 화면 ----------

        /// <summary>계정 정보를 새로 받아 시작 화면을 띄운다. 테이블을 고르면 그 자리에 앉는다.</summary>
        IEnumerator ShowTableSelect(string notice)
        {
            Profile p = null;
            yield return Backend.GetProfile(r => p = r);
            if (!string.IsNullOrEmpty(p.error))
            {
                if (Backend is RemoteBackend remote && remote.Unauthorized)
                {
                    RemoteBackend.SavedToken = "";
                    ShowLogin(p.error);
                    yield break;
                }
                // 서버에 잠깐 못 닿았으면 마지막으로 받은 정보로 보여 주고 알린다
                notice = p.error;
                p = Profile ?? new Profile { name = "?", chips = 0 };
            }
            Profile = p;

            CloseScreens();
            Hud.Canvas.enabled = false;
            StartScreen = StartScreen.Build(Hud.Font, Profile, Backend.Online, notice);
            StartScreen.TableChosen += Sit;
            StartScreen.ExchangeRequested += () => StartCoroutine(Exchange());
            StartScreen.MultiRequested += () => Application.OpenURL(serverUrl);
            StartScreen.LogoutRequested += () => StartCoroutine(Logout());
            StartScreen.ResetRequested += () =>
            {
                if (Backend is LocalBackend local) local.Reset();
                StartCoroutine(ShowTableSelect(null));
            };
        }

        IEnumerator Exchange()
        {
            Profile p = null;
            yield return Backend.Exchange(ChipExchange.Unit, r => p = r);
            if (string.IsNullOrEmpty(p.error)) Profile = p;
            yield return ShowTableSelect(string.IsNullOrEmpty(p.error) ? null : p.error);
        }

        IEnumerator Logout()
        {
            if (Backend is RemoteBackend remote) yield return remote.Logout();
            Backend = null;
            Profile = null;
            ShowLogin("로그아웃했습니다.");
        }

        void Sit(TableConfig cfg)
        {
            CloseScreens();
            Hud.Canvas.enabled = true;
            Director.Begin(Backend, cfg.Name, Scene, Hud, seed, AutoPlay);
        }

        void CloseScreens()
        {
            if (StartScreen != null)
            {
                StartScreen.Close();
                StartScreen = null;
            }
            if (LoginScreen != null)
            {
                LoginScreen.Close();
                LoginScreen = null;
            }
        }

        void OnDestroy()
        {
            Physics.simulationMode = _previousMode;
        }
    }
}
