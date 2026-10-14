using System;
using UnityEngine;
using UnityEngine.UI;

namespace DiceHeist.Game
{
    /// <summary>게임 전에 뜨는 시작 화면. 테이블을 가리는 전체 화면 UI이고, 시작을 누르면 닫힌다.</summary>
    public sealed class StartScreen : MonoBehaviour
    {
        static readonly Color Ink = new Color(0.12f, 0.16f, 0.15f);
        static readonly Color Muted = new Color(0.35f, 0.42f, 0.39f);
        static readonly Color Felt = new Color(0.81f, 0.89f, 0.84f, 0.93f);
        static readonly Color Card = new Color(0.98f, 0.99f, 0.97f, 0.96f);
        static readonly Color Accent = new Color(0.89f, 0.64f, 0.15f);
        static readonly Color Plain = new Color(0.88f, 0.92f, 0.89f);

        public Canvas Canvas { get; private set; }
        public event Action StartClicked;

        Font _font;

        public static StartScreen Build(Font font)
        {
            var go = new GameObject("StartScreen", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var screen = go.AddComponent<StartScreen>();
            screen._font = font;
            screen.Canvas = go.GetComponent<Canvas>();
            screen.Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            screen.Canvas.sortingOrder = 10;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            screen.BuildLayout();
            return screen;
        }

        void BuildLayout()
        {
            // 뒤의 테이블이 비치는 배경. 클릭이 주사위로 새지 않게 막는다
            var bg = NewRect("Background", transform);
            bg.anchorMin = Vector2.zero;
            bg.anchorMax = Vector2.one;
            bg.offsetMin = bg.offsetMax = Vector2.zero;
            bg.gameObject.AddComponent<Image>().color = Felt;

            var card = NewRect("Card", transform);
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(980, 640);
            card.gameObject.AddComponent<Image>().color = Card;

            Label(card, "다이스 하이스트", 104, Ink, 500, 140);
            Label(card, "동물들이 주사위 5개로 포커 족보를 겨루는 베팅 게임", 30, Muted, 392, 50);
            Label(card, "브론즈 테이블 · 참가비 10칩 · 나(토끼) vs 너구리 · 고양이", 26, Ink, 316, 44);
            Label(card, "굴리기  →  남기기(최대 2회)  →  걸기(포기 · 따라가기 · 올리기)  →  공개", 24, Muted, 268, 40);

            Button(card, "Btn_start", "게임 시작", Accent, new Vector2(0, 132), new Vector2(360, 92), 36, () => StartClicked?.Invoke());
            Button(card, "Btn_quit", "나가기", Plain, new Vector2(0, 46), new Vector2(220, 60), 24, Application.Quit);
        }

        public void Close() => Destroy(gameObject);

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>카드 아래쪽에서 bottom만큼 올라온 자리에 가운데 정렬한 한 줄.</summary>
        Text Label(RectTransform parent, string text, int size, Color color, float bottom, float height)
        {
            var rect = NewRect("Text", parent);
            rect.anchorMin = new Vector2(0, 0);
            rect.anchorMax = new Vector2(1, 0);
            rect.pivot = new Vector2(0.5f, 0);
            rect.anchoredPosition = new Vector2(0, bottom);
            rect.sizeDelta = new Vector2(0, height);
            var t = rect.gameObject.AddComponent<Text>();
            t.font = _font;
            t.fontSize = size;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = color;
            t.text = text;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        void Button(RectTransform parent, string name, string label, Color bg, Vector2 pos, Vector2 size, int fontSize, Action onClick)
        {
            var rect = NewRect(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0);
            rect.pivot = new Vector2(0.5f, 0);
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            var img = rect.gameObject.AddComponent<Image>();
            img.color = bg;
            var btn = rect.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());
            Label(rect, label, fontSize, Ink, 0, size.y);
        }
    }
}
