using System;
using DiceHeist.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DiceHeist.Game
{
    /// <summary>씬에 이 컴포넌트 하나만 있으면 테이블·동물·UI를 조립하고 게임을 시작한다.</summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Tooltip("켜면 실행할 때마다 새 시드를 쓴다. 끄면 아래 시드로 같은 판이 재현된다.")]
        public bool useRandomSeed = true;
        public long seed = 20261013;

        /// <summary>테스트용: 사람 자리도 자동으로 진행한다. 켜면 저장도 하지 않는다.</summary>
        public static bool AutoPlay;
        /// <summary>끄면 저장된 칩을 읽지도 쓰지도 않는다(테스트가 사람의 저장을 건드리지 않게).</summary>
        public static bool UseSave = true;

        /// <summary>환전으로 모은 사이버머니(멀티용).</summary>
        public long CyberMoney { get; private set; }

        const string SaveKey = "DiceHeist.Chips";
        const string MoneyKey = "DiceHeist.CyberMoney";
        bool Saving => UseSave && !AutoPlay;

        public TableDirector Director { get; private set; }
        public TableScene Scene { get; private set; }
        public HudView Hud { get; private set; }
        public Camera Cam { get; private set; }
        /// <summary>시작(테이블 선택) 화면. 테이블에 앉아 있는 동안은 null.</summary>
        public StartScreen StartScreen { get; private set; }
        /// <summary>테이블 밖에서 들고 다니는 내 칩. 테이블에서 일어날 때 갱신된다.</summary>
        public int Chips { get; private set; }

        IRandom _rng;

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

            _rng = new SeededRandom(seed);
            Chips = Saving && PlayerPrefs.HasKey(SaveKey) ? PlayerPrefs.GetInt(SaveKey) : TableConfig.Bronze.StartChips;
            // PlayerPrefs에는 long이 없어 문자열로 둔다
            CyberMoney = Saving && long.TryParse(PlayerPrefs.GetString(MoneyKey, "0"), out var m) ? m : 0;
            Director = gameObject.AddComponent<TableDirector>();
            Director.ChipsChanged += chips =>
            {
                Chips = chips;
                Save();
            };
            Director.Left += () =>
            {
                Chips = Director.Game.Human.Chips;
                Save();
                ShowTableSelect();
            };

            if (AutoPlay) Sit(TableConfig.Bronze);
            else ShowTableSelect();
        }

        /// <summary>시작 화면을 띄운다. 테이블을 고르면 그 자리에 앉는다.</summary>
        void ShowTableSelect()
        {
            Hud.Canvas.enabled = false;
            StartScreen = StartScreen.Build(Hud.Font, Chips, CyberMoney);
            StartScreen.TableChosen += Sit;
            StartScreen.ExchangeRequested += () =>
            {
                var (left, money) = ChipExchange.Exchange(Chips, ChipExchange.Unit);
                Chips = left;
                CyberMoney += money;
                Save();
                StartScreen.Close();
                ShowTableSelect();
            };
            StartScreen.ResetRequested += () =>
            {
                Chips = TableConfig.Bronze.StartChips;
                CyberMoney = 0;
                Save();
                StartScreen.Close();
                ShowTableSelect();
            };
        }

        /// <summary>내 칩을 이 PC에 저장한다(PlayerPrefs, Windows에서는 레지스트리). 칩이 바뀔 때마다 바로 쓴다.</summary>
        void Save()
        {
            if (!Saving) return;
            PlayerPrefs.SetInt(SaveKey, Chips);
            PlayerPrefs.SetString(MoneyKey, CyberMoney.ToString());
            PlayerPrefs.Save();
        }

        void Sit(TableConfig cfg)
        {
            if (StartScreen != null)
            {
                StartScreen.Close();
                StartScreen = null;
            }
            Hud.Canvas.enabled = true;
            Director.Begin(new DiceGame(cfg, _rng, Chips), Scene, Hud, seed, AutoPlay);
        }

        void OnDestroy()
        {
            Physics.simulationMode = _previousMode;
        }
    }
}
