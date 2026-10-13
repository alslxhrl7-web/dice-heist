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

        /// <summary>테스트용: 사람 자리도 자동으로 진행한다.</summary>
        public static bool AutoPlay;

        public TableDirector Director { get; private set; }
        public TableScene Scene { get; private set; }
        public HudView Hud { get; private set; }
        public Camera Cam { get; private set; }

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

            var game = new DiceGame(TableConfig.Bronze, new SeededRandom(seed));
            Director = gameObject.AddComponent<TableDirector>();
            Director.Begin(game, Scene, Hud, seed, AutoPlay);
        }

        void OnDestroy()
        {
            Physics.simulationMode = _previousMode;
        }
    }
}
