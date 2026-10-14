using System;
using System.Collections.Generic;
using DiceHeist.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace DiceHeist.Game
{
    public enum ButtonStyle
    {
        Plain,
        Primary,
        Raise,
    }

    public struct ButtonSpec
    {
        public string Id;
        public string Label;
        public ButtonStyle Style;
        public bool Enabled;

        public ButtonSpec(string id, string label, ButtonStyle style = ButtonStyle.Plain, bool enabled = true)
        {
            Id = id;
            Label = label;
            Style = style;
            Enabled = enabled;
        }
    }

    /// <summary>화면 UI 전부를 코드로 만든다. 칩·판돈·족보 이름·진행 기록·버튼, 그리고 자리 위에 뜨는 이름표.</summary>
    public sealed class HudView : MonoBehaviour
    {
        static readonly Color Ink = new Color(0.12f, 0.16f, 0.15f);
        static readonly Color Muted = new Color(0.35f, 0.42f, 0.39f);
        static readonly Color Card = new Color(0.98f, 0.99f, 0.97f, 0.94f);
        static readonly Color Accent = new Color(0.89f, 0.64f, 0.15f);
        static readonly Color Raise = new Color(0.77f, 0.26f, 0.42f);
        static readonly Color Good = new Color(0.17f, 0.54f, 0.34f);

        // 족보 표: 파이브 다이스부터 노페어까지. 확률은 7,776가지 중 해당 족보의 비율
        static readonly string[] RankExamples =
        {
            "4 4 4 4 4", "6 6 6 6 2", "2 3 4 5 6", "5 5 5 3 3", "2 2 2 5 6", "6 6 3 3 1", "4 4 1 3 6", "1 2 4 5 6",
        };
        static readonly string[] RankOdds =
        {
            "0.08%", "1.93%", "3.09%", "3.86%", "15.43%", "23.15%", "46.30%", "6.17%",
        };

        public Canvas Canvas { get; private set; }
        public Font Font => _font;
        public event Action<string> ButtonClicked;

        Font _font;
        Camera _cam;
        RectTransform _canvasRect;
        Text _stats, _hand, _handSub, _hint, _log, _bannerText, _potText;
        Image _bannerBg;
        RectTransform _buttonRow;
        readonly List<(Transform anchor, RectTransform rect)> _followers = new List<(Transform, RectTransform)>();
        readonly Text[] _seatTexts = new Text[3];
        readonly Image[] _seatBgs = new Image[3];

        public static HudView Build(Camera cam, Transform[] seatAnchors, Transform potAnchor)
        {
            var go = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var hud = go.AddComponent<HudView>();
            hud._cam = cam;
            hud.Canvas = go.GetComponent<Canvas>();
            hud.Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            hud._canvasRect = (RectTransform)go.transform;

            hud._font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" }, 32);
            if (hud._font == null) hud._font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var module = new GameObject("EventSystem", typeof(EventSystem)).AddComponent<InputSystemUIInputModule>();
                if (module.actionsAsset == null) module.AssignDefaultActions();
            }

            hud.BuildLayout(seatAnchors, potAnchor);
            return hud;
        }

        void BuildLayout(Transform[] seatAnchors, Transform potAnchor)
        {
            var root = _canvasRect;

            // 왼쪽 위: 제목과 통계
            var top = Panel(root, new Vector2(0, 0), new Vector2(0, 0), new Vector2(24, 20), new Vector2(430, 190));
            var title = Label(top, "다이스 하이스트", 38, TextAnchor.UpperLeft, Ink);
            Stretch(title.rectTransform, 18, 12, 18, 12);
            var sub = Label(top, "프로토타입 · 브론즈 테이블 · 참가비 10칩", 20, TextAnchor.UpperLeft, Muted);
            Stretch(sub.rectTransform, 18, 62, 18, 12);
            _stats = Label(top, "", 24, TextAnchor.LowerLeft, Ink);
            Stretch(_stats.rectTransform, 18, 12, 18, 14);

            // 오른쪽 위: 진행 기록
            var logPanel = Panel(root, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 20), new Vector2(470, 235));
            var logTitle = Label(logPanel, "진행 기록", 24, TextAnchor.UpperLeft, Ink);
            Stretch(logTitle.rectTransform, 18, 12, 18, 12);
            _log = Label(logPanel, "", 20, TextAnchor.UpperLeft, Muted);
            _log.horizontalOverflow = HorizontalWrapMode.Wrap;
            Stretch(_log.rectTransform, 18, 50, 18, 10);

            // 왼쪽 위: 족보 표 (높은 순)
            var ranks = Panel(root, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -20), new Vector2(336, 250));
            var ranksTitle = Label(ranks, "족보 (높은 순)", 24, TextAnchor.UpperLeft, Ink);
            Stretch(ranksTitle.rectTransform, 18, 12, 18, 12);
            var ranksHead = Label(ranks, "1회 굴림 확률", 17, TextAnchor.UpperRight, Muted);
            Stretch(ranksHead.rectTransform, 18, 18, 18, 12);
            var names = new List<string>();
            for (int r = 7; r >= 0; r--) names.Add(HandEvaluator.NameOf((HandRank)r));
            var rankNames = Label(ranks, string.Join("\n", names), 19, TextAnchor.UpperLeft, Ink);
            Stretch(rankNames.rectTransform, 18, 52, 18, 10);
            var rankExamples = Label(ranks, string.Join("\n", RankExamples), 19, TextAnchor.UpperLeft, Muted);
            Stretch(rankExamples.rectTransform, 152, 52, 18, 10);
            var rankOdds = Label(ranks, string.Join("\n", RankOdds), 19, TextAnchor.UpperRight, Ink);
            Stretch(rankOdds.rectTransform, 18, 52, 18, 10);

            // 가운데 위: 결과 배너
            var banner = Panel(root, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(820, 70));
            _bannerBg = banner.GetComponent<Image>();
            _bannerText = Label(banner, "", 30, TextAnchor.MiddleCenter, Ink);
            Stretch(_bannerText.rectTransform, 10, 4, 10, 4);
            banner.gameObject.SetActive(false);

            // 아래 가운데: 내 족보, 안내, 버튼
            var bottom = Panel(root, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(900, 215));
            _hand = Label(bottom, "…", 44, TextAnchor.UpperCenter, Ink);
            Stretch(_hand.rectTransform, 10, 8, 10, 10);
            _handSub = Label(bottom, "지금 내 족보", 19, TextAnchor.UpperCenter, Muted);
            Stretch(_handSub.rectTransform, 10, 62, 10, 10);
            _hint = Label(bottom, "", 21, TextAnchor.LowerCenter, Muted);
            Stretch(_hint.rectTransform, 10, 10, 10, 10);

            _buttonRow = NewRect("Buttons", bottom);
            _buttonRow.anchorMin = new Vector2(0, 0);
            _buttonRow.anchorMax = new Vector2(1, 0);
            _buttonRow.pivot = new Vector2(0.5f, 0);
            _buttonRow.anchoredPosition = new Vector2(0, 46);
            _buttonRow.sizeDelta = new Vector2(0, 74);
            var layout = _buttonRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 16;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            // 자리 이름표와 판돈: 3D 위치를 따라다닌다
            for (int i = 0; i < seatAnchors.Length; i++)
            {
                var tag = Panel(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300, i == 0 ? 80 : 124));
                _seatBgs[i] = tag.GetComponent<Image>();
                _seatTexts[i] = Label(tag, "", 21, TextAnchor.MiddleCenter, Ink);
                Stretch(_seatTexts[i].rectTransform, 8, 4, 8, 4);
                _followers.Add((seatAnchors[i], tag));
            }
            var pot = Panel(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(190, 52));
            _potText = Label(pot, "", 30, TextAnchor.MiddleCenter, Ink);
            Stretch(_potText.rectTransform, 6, 2, 6, 2);
            _followers.Add((potAnchor, pot));
        }

        void LateUpdate()
        {
            Camera uiCam = Canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : Canvas.worldCamera;
            foreach (var (anchor, rect) in _followers)
            {
                Vector3 sp = _cam.WorldToScreenPoint(anchor.position);
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, sp, uiCam, out var local))
                    rect.anchoredPosition = local;
            }
        }

        // ---------- 갱신 ----------

        public void SetStats(int chips, int round, int winRatePercent, string bestHand, long seed)
        {
            _stats.text = $"내 칩 <b>{chips:N0}</b>   판 <b>{round}</b>   승률 <b>{winRatePercent}%</b>\n최고 족보 <b>{bestHand}</b>   <color=#7a8a84>시드 {seed}</color>";
        }

        public void SetHand(string name) => _hand.text = name;
        public void SetHint(string hint) => _hint.text = hint;
        public void SetPot(int pot) => _potText.text = $"판돈 <b>{pot}</b>";
        public void SetLog(IEnumerable<string> lines) => _log.text = string.Join("\n", lines);

        public void SetSeat(int seat, string richText, bool turn, bool folded, bool winner)
        {
            _seatTexts[seat].text = richText;
            var c = winner ? new Color(0.78f, 0.93f, 0.83f, 0.96f) : turn ? new Color(1f, 0.93f, 0.72f, 0.96f) : Card;
            if (folded) c.a = 0.55f;
            _seatBgs[seat].color = c;
            _seatTexts[seat].color = folded ? Muted : Ink;
        }

        public void ShowBanner(string text, bool win)
        {
            _bannerBg.gameObject.SetActive(true);
            _bannerBg.color = win ? Good : Card;
            _bannerText.color = win ? Color.white : Ink;
            _bannerText.text = text;
        }

        public void HideBanner() => _bannerBg.gameObject.SetActive(false);

        public void ShowButtons(params ButtonSpec[] specs)
        {
            for (int i = _buttonRow.childCount - 1; i >= 0; i--) Destroy(_buttonRow.GetChild(i).gameObject);
            foreach (var spec in specs)
            {
                var rect = NewRect("Btn_" + spec.Id, _buttonRow);
                var img = rect.gameObject.AddComponent<Image>();
                Color bg = spec.Style == ButtonStyle.Primary ? Accent : spec.Style == ButtonStyle.Raise ? Raise : new Color(0.88f, 0.92f, 0.89f);
                img.color = bg;
                var le = rect.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = Mathf.Max(200, 60 + spec.Label.Length * 27);
                le.preferredHeight = 68;
                var btn = rect.gameObject.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.interactable = spec.Enabled;
                string id = spec.Id;
                btn.onClick.AddListener(() => ButtonClicked?.Invoke(id));
                var label = Label(rect, spec.Label, 27, TextAnchor.MiddleCenter, spec.Style == ButtonStyle.Raise ? Color.white : Ink);
                Stretch(label.rectTransform, 4, 2, 4, 2);
                if (!spec.Enabled) label.color = new Color(label.color.r, label.color.g, label.color.b, 0.45f);
            }
        }

        // ---------- 조립 도우미 ----------

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        RectTransform Panel(Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var rect = NewRect("Panel", parent);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            var img = rect.gameObject.AddComponent<Image>();
            img.color = Card;
            img.raycastTarget = false;
            return rect;
        }

        Text Label(Transform parent, string text, int size, TextAnchor align, Color color)
        {
            var rect = NewRect("Text", parent);
            var t = rect.gameObject.AddComponent<Text>();
            t.font = _font;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.text = text;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        static void Stretch(RectTransform rect, float left, float top, float right, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }
    }
}
