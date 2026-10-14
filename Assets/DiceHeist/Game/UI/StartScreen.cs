using System;
using DiceHeist.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DiceHeist.Game
{
    /// <summary>게임 전과 테이블을 옮길 때 뜨는 시작 화면. 가진 칩에 따라 앉을 수 있는 테이블(브론즈·실버·골드)을 고른다.</summary>
    public sealed class StartScreen : MonoBehaviour
    {
        static readonly Color Ink = new Color(0.12f, 0.16f, 0.15f);
        static readonly Color Muted = new Color(0.35f, 0.42f, 0.39f);
        static readonly Color Felt = new Color(0.81f, 0.89f, 0.84f, 0.93f);
        static readonly Color Card = new Color(0.98f, 0.99f, 0.97f, 0.96f);
        static readonly Color Accent = new Color(0.89f, 0.64f, 0.15f);
        static readonly Color Plain = new Color(0.88f, 0.92f, 0.89f);
        static readonly Color Locked = new Color(0.80f, 0.83f, 0.81f);

        public Canvas Canvas { get; private set; }
        public event Action<TableConfig> TableChosen;
        /// <summary>기록 지우기를 두 번 눌렀을 때.</summary>
        public event Action ResetRequested;
        /// <summary>환전 버튼: ChipExchange.Unit칩을 사이버머니로.</summary>
        public event Action ExchangeRequested;
        /// <summary>멀티 카드: 사이트(브라우저)를 연다.</summary>
        public event Action MultiRequested;
        /// <summary>로그아웃(서버 계정일 때).</summary>
        public event Action LogoutRequested;

        TMP_FontAsset _font;
        int _chips;
        long _money;
        bool _multiOpen;
        bool _online;
        string _account;
        string _notice;

        /// <param name="online">서버 계정이면 true. 오프라인이면 기록 지우기, 온라인이면 로그아웃 버튼이 나온다.</param>
        /// <param name="notice">맨 위에 띄울 알림(서버 오류 등). 없으면 null.</param>
        public static StartScreen Build(TMP_FontAsset font, Profile profile, bool online, string notice = null)
        {
            var go = new GameObject("StartScreen", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var screen = go.AddComponent<StartScreen>();
            screen._font = font;
            screen._chips = profile.chips;
            screen._money = profile.money;
            screen._multiOpen = profile.bronzeCleared;
            screen._online = online;
            screen._account = profile.name;
            screen._notice = notice;
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

            Label(card, "다이스 하이스트", 96, Ink, 516, 120);
            Label(card, "동물들이 주사위 5개로 포커 족보를 겨루는 베팅 게임 · 나(토끼) vs 너구리 · 고양이", 24, Muted, 476, 40);
            Label(card, "굴리기  →  남기기(최대 2회)  →  베팅  →  쇼다운(공개)", 22, Muted, 440, 34);
            Label(card, "베팅: 폴드(포기) · 콜(따라가기, 1배) · 레이즈(올리기, 3배)  ·  배수는 앤티 기준", 20, Muted, 410, 30);
            string who = _online ? $"<b>{_account}</b>  ·  " : "<color=#8b958f>오프라인</color>  ·  ";
            Label(card, $"{who}내 칩 <b>{_chips:N0}</b>  ·  사이버머니 <b>{_money:N0}</b>  ·  앉을 테이블을 고르세요", 28, Ink, 350, 48);
            if (!string.IsNullOrEmpty(_notice))
            {
                var note = Label(card, _notice, 22, new Color(0.70f, 0.25f, 0.18f), 648, 34); // 카드 바로 위
                note.textWrappingMode = TextWrappingModes.Normal;
            }

            // 테이블 고르기: 가진 칩이 입장 조건에 못 미치면 잠겨 있다
            var tiers = TableConfig.Tiers;
            for (int i = 0; i < tiers.Length; i++)
            {
                var cfg = tiers[i];
                bool open = cfg.CanEnter(_chips);
                string state = open ? "<color=#2c8a57>입장 가능</color>"
                    : cfg.MinChips > _chips ? $"<color=#8b958f>{cfg.MinChips:N0}칩부터 입장</color>"
                    : "<color=#8b958f>칩이 모자람</color>";
                string label = $"<size=36><b>{cfg.Name}</b></size>\n앤티 {cfg.Ante}칩 · 한도 {cfg.ChipCap:N0}칩\n{state}";
                var btn = Button(card, "Btn_table" + i, label, open ? Accent : Locked,
                    new Vector2(-345 + i * 230, 118), new Vector2(220, 170), 21, () => TableChosen?.Invoke(cfg));
                btn.interactable = open;
            }

            // 멀티: 브론즈 한도에 한 번이라도 닿으면 열린다. 사이버머니로 사람끼리 겨루는 곳(브라우저 사이트)
            bool multiOpen = _multiOpen && _online;
            string multiState = multiOpen ? "<color=#2c8a57>열림</color> · 사이트 열기"
                : !_online ? "<color=#8b958f>로그인해야 이용</color>"
                : $"<color=#8b958f>브론즈 {TableConfig.Bronze.ChipCap:N0}칩 달성 시</color>";
            var multi = Button(card, "Btn_multi", $"<size=36><b>멀티</b></size>\n사이버머니로 사람과\n{multiState}",
                multiOpen ? Accent : Locked, new Vector2(345, 118), new Vector2(220, 170), 21, () => MultiRequested?.Invoke());
            multi.interactable = multiOpen;

            Button(card, "Btn_quit", "나가기", Plain, new Vector2(-300, 30), new Vector2(180, 60), 24, Application.Quit);

            // 환전: 처음 500칩 위로 번 칩만, 100칩씩. 사이버머니는 멀티에서 쓴다
            bool canExchange = ChipExchange.Exchangeable(_chips) >= ChipExchange.Unit;
            var ex = Button(card, "Btn_exchange",
                $"환전 {ChipExchange.Unit}칩 → {ChipExchange.Unit * ChipExchange.Rate:N0}머니",
                canExchange ? Accent : Locked, new Vector2(0, 30), new Vector2(340, 60), 22, () => ExchangeRequested?.Invoke());
            ex.interactable = canExchange;

            if (_online)
            {
                Button(card, "Btn_logout", "로그아웃", Plain, new Vector2(300, 30), new Vector2(180, 60), 22, () => LogoutRequested?.Invoke());
                Label(card, $"칩과 사이버머니는 서버 계정에 저장 · 환전은 처음 {ChipExchange.Floor}칩 위로 번 칩만, 되돌릴 수 없음", 18, Muted, 92, 26);
            }
            else
            {
                // 실수로 지우지 않게 두 번 눌러야 한다
                TextMeshProUGUI resetLabel = null;
                bool armed = false;
                Button(card, "Btn_reset", "기록 지우기", Plain, new Vector2(300, 30), new Vector2(180, 60), 22, () =>
                {
                    if (armed) { ResetRequested?.Invoke(); return; }
                    armed = true;
                    resetLabel.text = $"한 번 더: {TableConfig.Bronze.StartChips}칩부터";
                });
                resetLabel = card.Find("Btn_reset").GetComponentInChildren<TextMeshProUGUI>();
                Label(card, $"오프라인 연습: 칩은 이 PC에만 저장되고 멀티에는 쓸 수 없습니다", 18, Muted, 92, 26);
            }
        }

        public void Close() => Destroy(gameObject);

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>카드 아래쪽에서 bottom만큼 올라온 자리에 가운데 정렬한 한 줄.</summary>
        TextMeshProUGUI Label(RectTransform parent, string text, int size, Color color, float bottom, float height)
        {
            var rect = NewRect("Text", parent);
            rect.anchorMin = new Vector2(0, 0);
            rect.anchorMax = new Vector2(1, 0);
            rect.pivot = new Vector2(0.5f, 0);
            rect.anchoredPosition = new Vector2(0, bottom);
            rect.sizeDelta = new Vector2(0, height);
            var t = rect.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = _font;
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.color = color;
            t.text = text;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        Button Button(RectTransform parent, string name, string label, Color bg, Vector2 pos, Vector2 size, int fontSize, Action onClick)
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
            return btn;
        }
    }
}
