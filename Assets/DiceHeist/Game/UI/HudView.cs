using System;
using System.Collections;
using System.Collections.Generic;
using DiceHeist.Core;
using TMPro;
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

        // 족보 표: 파이브 다이스부터 노페어까지. 확률은 WeightedDice.RankWeights

        static readonly string[] RankExamples =
        {
            "4 4 4 4 4", "6 6 6 6 2", "2 3 4 5 6", "5 5 5 3 3", "2 2 2 5 6", "6 6 3 3 1", "4 4 1 3 6", "1 2 4 5 6",
        };

        public Canvas Canvas { get; private set; }
        public TMP_FontAsset Font => _font;
        public event Action<string> ButtonClicked;

        TMP_FontAsset _font;
        Camera _cam;
        RectTransform _canvasRect;
        TextMeshProUGUI _sub, _stats, _hand, _handSub, _hint, _log, _bannerText, _potText;
        Image _bannerBg;
        RectTransform _buttonRow;
        readonly List<(Transform anchor, RectTransform rect)> _followers = new List<(Transform, RectTransform)>();
        readonly TextMeshProUGUI[] _seatTexts = new TextMeshProUGUI[3];
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

            hud._font = UiFont.Korean;

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
            var title = Label(top, "다이스 하이스트", 38, TextAlignmentOptions.TopLeft, Ink);
            Stretch(title.rectTransform, 18, 12, 18, 12);
            _sub = Label(top, "", 20, TextAlignmentOptions.TopLeft, Muted);
            Stretch(_sub.rectTransform, 18, 62, 18, 12);
            _stats = Label(top, "", 24, TextAlignmentOptions.BottomLeft, Ink);
            Stretch(_stats.rectTransform, 18, 12, 18, 14);

            // 오른쪽 위: 진행 기록
            var logPanel = Panel(root, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 20), new Vector2(470, 235));
            var logTitle = Label(logPanel, "진행 기록", 24, TextAlignmentOptions.TopLeft, Ink);
            Stretch(logTitle.rectTransform, 18, 12, 18, 12);
            _log = Label(logPanel, "", 20, TextAlignmentOptions.TopLeft, Muted);
            _log.textWrappingMode = TextWrappingModes.Normal;
            Stretch(_log.rectTransform, 18, 50, 18, 10);

            // 오른쪽 위: 족보 표 (높은 순). 왼쪽 위는 너구리 이름표 자리
            var ranks = Panel(root, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -20), new Vector2(336, 250));
            var ranksTitle = Label(ranks, "족보 (높은 순)", 24, TextAlignmentOptions.TopLeft, Ink);
            Stretch(ranksTitle.rectTransform, 18, 12, 18, 12);
            var ranksHead = Label(ranks, "1회 굴림 확률", 17, TextAlignmentOptions.TopRight, Muted);
            Stretch(ranksHead.rectTransform, 18, 18, 18, 12);
            var names = new List<string>();
            var odds = new List<string>();
            for (int r = 7; r >= 0; r--)
            {
                names.Add(HandEvaluator.NameOf((HandRank)r));
                odds.Add($"{WeightedDice.RankWeights[r] / 10.0:0.0}%");
            }
            var rankNames = Label(ranks, string.Join("\n", names), 19, TextAlignmentOptions.TopLeft, Ink);
            Stretch(rankNames.rectTransform, 18, 52, 18, 10);
            var rankExamples = Label(ranks, string.Join("\n", RankExamples), 19, TextAlignmentOptions.TopLeft, Muted);
            Stretch(rankExamples.rectTransform, 152, 52, 18, 10);
            var rankOdds = Label(ranks, string.Join("\n", odds), 19, TextAlignmentOptions.TopRight, Ink);
            Stretch(rankOdds.rectTransform, 18, 52, 18, 10);

            // 가운데 위: 결과 배너
            var banner = Panel(root, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -330), new Vector2(860, 92)); // 두 줄: 결과 + 이긴 이유. 펭귄 딜러 얼굴을 가리지 않게 테이블 가운데
            _bannerBg = banner.GetComponent<Image>();
            _bannerText = Label(banner, "", 30, TextAlignmentOptions.Center, Ink);
            Stretch(_bannerText.rectTransform, 10, 4, 10, 4);
            banner.gameObject.SetActive(false);

            // 아래 가운데: 내 족보, 안내, 버튼
            var bottom = Panel(root, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(940, 215));
            _hand = Label(bottom, "…", 44, TextAlignmentOptions.Top, Ink);
            Stretch(_hand.rectTransform, 10, 8, 10, 10);
            _handSub = Label(bottom, "지금 내 족보", 19, TextAlignmentOptions.Top, Muted);
            Stretch(_handSub.rectTransform, 10, 62, 10, 10);
            _hint = Label(bottom, "", 21, TextAlignmentOptions.Bottom, Muted);
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
                var tag = Panel(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(290, i == 0 ? 80 : 124));
                _seatBgs[i] = tag.GetComponent<Image>();
                _seatTexts[i] = Label(tag, "", 21, TextAlignmentOptions.Center, Ink);
                Stretch(_seatTexts[i].rectTransform, 8, 4, 8, 4);
                _followers.Add((seatAnchors[i], tag));
            }
            var pot = Panel(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(250, 52));
            _potText = Label(pot, "", 30, TextAlignmentOptions.Center, Ink);
            Stretch(_potText.rectTransform, 6, 2, 6, 2);
            _followers.Add((potAnchor, pot));
        }

        void LateUpdate()
        {
            Camera uiCam = Canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : Canvas.worldCamera;
            foreach (var (anchor, rect) in _followers)
            {
                if (anchor == null || rect == null) continue;
                Vector3 sp = _cam.WorldToScreenPoint(anchor.position);
                if (sp.z < 0f) { rect.anchoredPosition = new Vector2(100000f, 0f); continue; } // 카메라 뒤면 화면 밖으로
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, sp, uiCam, out var local))
                    rect.anchoredPosition = local;
            }
            _followers.RemoveAll(f => f.anchor == null || f.rect == null);
        }

        // ---------- 말풍선 ----------

        readonly Dictionary<Transform, RectTransform> _bubbles = new Dictionary<Transform, RectTransform>();

        /// <summary>anchor 위에 짧은 말풍선을 seconds 동안 띄운다. 같은 자리의 앞 말풍선은 바로 바뀐다.</summary>
        public void Say(Transform anchor, string text, float seconds = 1.8f)
        {
            if (anchor == null || string.IsNullOrEmpty(text)) return;
            if (_bubbles.TryGetValue(anchor, out var old) && old != null) Destroy(old.gameObject);

            float width = Mathf.Clamp(46 + text.Length * 24, 120, 420);
            var bubble = Panel(_canvasRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(width, 54));
            bubble.name = "Bubble";
            bubble.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.97f);
            // 아래로 삐죽 나온 꼬리
            var tail = NewRect("Tail", bubble);
            tail.anchorMin = tail.anchorMax = new Vector2(0.5f, 0f);
            tail.sizeDelta = new Vector2(16, 16);
            tail.anchoredPosition = new Vector2(0, -2);
            tail.localRotation = Quaternion.Euler(0, 0, 45f);
            var tailImg = tail.gameObject.AddComponent<Image>();
            tailImg.color = new Color(1f, 1f, 1f, 0.97f);
            tailImg.raycastTarget = false;
            tail.SetAsFirstSibling();

            var label = Label(bubble, text, 24, TextAlignmentOptions.Center, Ink);
            Stretch(label.rectTransform, 8, 2, 8, 2);
            _followers.Add((anchor, bubble));
            _bubbles[anchor] = bubble;
            StartCoroutine(PopBubble(bubble, seconds));
        }

        IEnumerator PopBubble(RectTransform bubble, float seconds)
        {
            // 톡 튀어나왔다가, 끝날 때 살짝 작아지며 사라진다
            for (float e = 0; e < 0.12f && bubble != null; e += Time.unscaledDeltaTime)
            {
                bubble.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.08f, e / 0.12f);
                yield return null;
            }
            if (bubble != null) bubble.localScale = Vector3.one;
            for (float e = 0; e < seconds && bubble != null; e += Time.deltaTime) yield return null;
            for (float e = 0; e < 0.15f && bubble != null; e += Time.deltaTime)
            {
                bubble.localScale = Vector3.one * Mathf.Lerp(1f, 0.7f, e / 0.15f);
                yield return null;
            }
            if (bubble != null) Destroy(bubble.gameObject);
        }

        // ---------- 갱신 ----------

        public void SetStats(int chips, int round, int winRatePercent, string bestHand, long seed)
        {
            _stats.text = $"내 칩 <b>{chips:N0}</b>   판 <b>{round}</b>   승률 <b>{winRatePercent}%</b>\n최고 족보 <b>{bestHand}</b>   <color=#7a8a84>시드 {seed}</color>";
        }

        public void SetTable(string text) => _sub.text = text;
        public void SetHand(string name) => _hand.text = name;
        public void SetHint(string hint) => _hint.text = hint;
        public void SetPot(int pot) => _potText.text = $"팟 (판돈) <b>{pot}</b>";
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
            _potText.transform.parent.gameObject.SetActive(false); // 배너가 팟 표시 자리에 뜬다(판이 끝나면 팟은 0)
            _bannerBg.color = win ? Good : Card;
            _bannerText.color = win ? Color.white : Ink;
            _bannerText.text = text;
        }

        public void HideBanner()
        {
            _bannerBg.gameObject.SetActive(false);
            _potText.transform.parent.gameObject.SetActive(true);
        }

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
                le.preferredWidth = spec.Label.Length <= 2 ? 72 : Mathf.Max(190, 40 + LongestLine(spec.Label) * 22); // +/- 같은 한 글자 버튼은 작게
                le.preferredHeight = 68;
                var btn = rect.gameObject.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.interactable = spec.Enabled;
                string id = spec.Id;
                btn.onClick.AddListener(() => ButtonClicked?.Invoke(id));
                var label = Label(rect, spec.Label, 27, TextAlignmentOptions.Center, spec.Style == ButtonStyle.Raise ? Color.white : Ink);
                Stretch(label.rectTransform, 4, 2, 4, 2);
                if (!spec.Enabled) label.color = new Color(label.color.r, label.color.g, label.color.b, 0.45f);
            }
        }

        // ---------- 조립 도우미 ----------

        /// <summary>서식 태그를 뺀 가장 긴 줄의 글자 수. 버튼 너비 계산용.</summary>
        static int LongestLine(string label)
        {
            int best = 0;
            foreach (var line in System.Text.RegularExpressions.Regex.Replace(label, "<[^>]*>", "").Split('\n'))
                best = Mathf.Max(best, line.Length);
            return best;
        }

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

        TextMeshProUGUI Label(Transform parent, string text, int size, TextAlignmentOptions align, Color color)
        {
            var rect = NewRect("Text", parent);
            var t = rect.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = _font;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.text = text;
            t.richText = true;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
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
