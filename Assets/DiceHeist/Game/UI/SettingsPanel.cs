using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DiceHeist.Game
{
    /// <summary>음량 같은 환경설정. 이 PC의 PlayerPrefs에 저장되고, 게임을 켤 때 GameBootstrap이 Apply를 부른다.</summary>
    public static class GameSettings
    {
        const string MasterKey = "DiceHeist.Volume.Master";
        const string SfxKey = "DiceHeist.Volume.Sfx";

        /// <summary>전체 음량 0~1. 모든 소리에 곱해진다.</summary>
        public static float Master
        {
            get => PlayerPrefs.GetFloat(MasterKey, 1f);
            set { PlayerPrefs.SetFloat(MasterKey, Mathf.Clamp01(value)); Apply(); }
        }

        /// <summary>효과음(주사위·칩·승패) 음량 0~1.</summary>
        public static float Sfx
        {
            get => PlayerPrefs.GetFloat(SfxKey, 1f);
            set { PlayerPrefs.SetFloat(SfxKey, Mathf.Clamp01(value)); Apply(); }
        }

        public static void Apply()
        {
            AudioListener.volume = Master;
            DiceAudio.Volume = 0.8f * Sfx;
        }

        public static void Save() => PlayerPrefs.Save();
    }

    /// <summary>시작 화면의 [설정]으로 여는 창. 전체 음량·효과음 슬라이더와 들어보기 버튼.</summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        static readonly Color Ink = new Color(0.12f, 0.16f, 0.15f);
        static readonly Color Muted = new Color(0.35f, 0.42f, 0.39f);
        static readonly Color Dim = new Color(0.10f, 0.14f, 0.12f, 0.55f);
        static readonly Color Card = new Color(0.98f, 0.99f, 0.97f, 1f);
        static readonly Color Track = new Color(0.86f, 0.90f, 0.87f);
        static readonly Color Accent = new Color(0.89f, 0.64f, 0.15f);
        static readonly Color Plain = new Color(0.88f, 0.92f, 0.89f);

        public event Action Closed;

        TMP_FontAsset _font;
        public Slider MasterSlider { get; private set; }
        public Slider SfxSlider { get; private set; }

        public static SettingsPanel Open(TMP_FontAsset font)
        {
            var go = new GameObject("SettingsPanel", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var panel = go.AddComponent<SettingsPanel>();
            panel._font = font;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40; // 시작 화면(10)·로그인(20)보다 위
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            panel.Build();
            return panel;
        }

        void Build()
        {
            // 뒤를 어둡게 덮고, 바깥을 누르면 닫힌다
            var dim = NewRect("Dim", transform);
            dim.anchorMin = Vector2.zero;
            dim.anchorMax = Vector2.one;
            dim.offsetMin = dim.offsetMax = Vector2.zero;
            dim.gameObject.AddComponent<Image>().color = Dim;
            dim.gameObject.AddComponent<Button>().onClick.AddListener(Close);

            var card = NewRect("Card", transform);
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(680, 430);
            card.gameObject.AddComponent<Image>().color = Card;

            Label(card, "환경설정", 48, Ink, new Vector2(0, 160), new Vector2(600, 70), TextAlignmentOptions.Center);

            MasterSlider = Row(card, "전체 음량", 70, GameSettings.Master, v => GameSettings.Master = v);
            SfxSlider = Row(card, "효과음", -20, GameSettings.Sfx, v => GameSettings.Sfx = v);
            Label(card, "주사위·칩·승패 소리 · 배경 음악은 아직 없습니다", 18, Muted, new Vector2(0, -75), new Vector2(600, 30), TextAlignmentOptions.Center);

            Button(card, "Btn_test", "소리 들어보기", Plain, new Vector2(-130, -155), new Vector2(230, 64), () =>
            {
                DiceAudio.Impact(0.8f);
                DiceAudio.Chips(3);
            });
            Button(card, "Btn_close", "닫기", Accent, new Vector2(130, -155), new Vector2(230, 64), Close);
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Close();
        }

        public void Close()
        {
            GameSettings.Save();
            Closed?.Invoke();
            Destroy(gameObject);
        }

        /// <summary>왼쪽 이름, 가운데 슬라이더, 오른쪽 퍼센트 한 줄.</summary>
        Slider Row(RectTransform card, string name, float y, float value, Action<float> onChange)
        {
            Label(card, name, 28, Ink, new Vector2(-230, y), new Vector2(160, 50), TextAlignmentOptions.MidlineLeft);
            var percent = Label(card, Percent(value), 26, Muted, new Vector2(255, y), new Vector2(90, 50), TextAlignmentOptions.MidlineRight);

            var root = NewRect("Slider_" + name, card);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = new Vector2(40, y);
            root.sizeDelta = new Vector2(340, 30);

            var bg = NewRect("Background", root);
            bg.anchorMin = new Vector2(0, 0.25f);
            bg.anchorMax = new Vector2(1, 0.75f);
            bg.offsetMin = bg.offsetMax = Vector2.zero;
            bg.gameObject.AddComponent<Image>().color = Track;

            var fillArea = NewRect("Fill Area", root);
            fillArea.anchorMin = new Vector2(0, 0.25f);
            fillArea.anchorMax = new Vector2(1, 0.75f);
            fillArea.offsetMin = new Vector2(0, 0);
            fillArea.offsetMax = new Vector2(-14, 0);
            var fill = NewRect("Fill", fillArea);
            fill.sizeDelta = new Vector2(14, 0);
            fill.gameObject.AddComponent<Image>().color = Accent;

            var handleArea = NewRect("Handle Slide Area", root);
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(14, 0);
            handleArea.offsetMax = new Vector2(-14, 0);
            var handle = NewRect("Handle", handleArea);
            handle.sizeDelta = new Vector2(28, 0);
            var handleImg = handle.gameObject.AddComponent<Image>();
            handleImg.color = Ink;

            var slider = root.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = value;
            slider.onValueChanged.AddListener(v =>
            {
                onChange(v);
                percent.text = Percent(v);
            });
            return slider;
        }

        static string Percent(float v) => $"{Mathf.RoundToInt(v * 100)}%";

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        TextMeshProUGUI Label(RectTransform parent, string text, int size, Color color, Vector2 pos, Vector2 box, TextAlignmentOptions align)
        {
            var rect = NewRect("Text", parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = box;
            var t = rect.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = _font;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.text = text;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        void Button(RectTransform parent, string name, string label, Color bg, Vector2 pos, Vector2 size, Action onClick)
        {
            var rect = NewRect(name, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            var img = rect.gameObject.AddComponent<Image>();
            img.color = bg;
            var btn = rect.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());
            var t = Label(rect, label, 26, Ink, Vector2.zero, size, TextAlignmentOptions.Center);
            t.rectTransform.anchorMin = Vector2.zero;
            t.rectTransform.anchorMax = Vector2.one;
            t.rectTransform.sizeDelta = Vector2.zero;
        }
    }
}
