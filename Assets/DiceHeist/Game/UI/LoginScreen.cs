using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DiceHeist.Game
{
    /// <summary>
    /// 서버 계정으로 로그인하는 첫 화면. 멀티 사이트와 같은 계정을 쓴다.
    /// 로그인·회원가입 요청은 GameBootstrap이 보내고, 이 화면은 입력과 결과 문구만 다룬다.
    /// </summary>
    public sealed class LoginScreen : MonoBehaviour
    {
        static readonly Color Ink = new Color(0.12f, 0.16f, 0.15f);
        static readonly Color Muted = new Color(0.35f, 0.42f, 0.39f);
        static readonly Color Felt = new Color(0.81f, 0.89f, 0.84f, 0.97f);
        static readonly Color Card = new Color(0.98f, 0.99f, 0.97f, 0.98f);
        static readonly Color Field = new Color(0.92f, 0.94f, 0.92f);
        static readonly Color Accent = new Color(0.89f, 0.64f, 0.15f);
        static readonly Color Plain = new Color(0.88f, 0.92f, 0.89f);
        static readonly Color Bad = new Color(0.70f, 0.25f, 0.18f);

        /// <summary>(회원가입인지, 아이디, 비밀번호)</summary>
        public event Action<bool, string, string> Submitted;
        public event Action OfflineChosen;

        TMP_FontAsset _font;
        TMP_InputField _name, _password;
        TextMeshProUGUI _message;
        Button _login, _register;

        public static LoginScreen Build(TMP_FontAsset font, string serverUrl)
        {
            var go = new GameObject("LoginScreen", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var screen = go.AddComponent<LoginScreen>();
            screen._font = font;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            screen.BuildLayout(serverUrl);
            return screen;
        }

        void BuildLayout(string serverUrl)
        {
            var bg = NewRect("Background", transform);
            bg.anchorMin = Vector2.zero;
            bg.anchorMax = Vector2.one;
            bg.offsetMin = bg.offsetMax = Vector2.zero;
            bg.gameObject.AddComponent<Image>().color = Felt;

            var card = NewRect("Card", transform);
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(760, 620);
            card.gameObject.AddComponent<Image>().color = Card;

            Label(card, "다이스 하이스트", 84, Ink, 500, 110);
            Label(card, "계정으로 로그인하면 칩이 서버에 저장되고, 브론즈를 지나면 멀티가 열립니다", 21, Muted, 462, 34);

            _name = Input(card, "Input_name", "아이디 (한글·영문·숫자 2~12자)", 386, false);
            _name.characterLimit = 12;
            _password = Input(card, "Input_password", "비밀번호 (6자 이상)", 316, true);

            _message = Label(card, "", 22, Bad, 268, 36);

            _login = Button(card, "Btn_login", "로그인", Accent, new Vector2(-130, 176), new Vector2(240, 76), 30, () => Submit(false));
            _register = Button(card, "Btn_register", "회원가입", Plain, new Vector2(130, 176), new Vector2(240, 76), 30, () => Submit(true));

            Label(card, "처음이면 아이디·비밀번호를 정하고 [회원가입]. 띄어쓰기·특수문자는 안 됩니다.", 19, Muted, 128, 30);
            Button(card, "Btn_offline", "서버 없이 연습 (칩은 이 PC에만 저장)", Plain, new Vector2(0, 44), new Vector2(460, 56), 20, () => OfflineChosen?.Invoke());
            Label(card, "서버: " + serverUrl, 15, Muted, 10, 24);

            _name.onSubmit.AddListener(_ => _password.ActivateInputField());
            _password.onSubmit.AddListener(_ => Submit(false));
            _name.ActivateInputField();
        }

        void Submit(bool register)
        {
            string name = _name.text.Trim();
            if (name.Length < 2) { ShowMessage("아이디를 2자 이상 입력해 주세요."); return; }
            if (_password.text.Length < 6) { ShowMessage("비밀번호를 6자 이상 입력해 주세요."); return; }
            SetBusy(true);
            ShowMessage(register ? "가입하는 중…" : "로그인하는 중…", Muted);
            Submitted?.Invoke(register, name, _password.text);
        }

        public void ShowMessage(string text, Color? color = null)
        {
            _message.color = color ?? Bad;
            _message.text = text;
        }

        public void SetBusy(bool busy)
        {
            _login.interactable = _register.interactable = !busy;
        }

        public void Close() => Destroy(gameObject);

        // ---------- 조립 도우미 ----------

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

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

        /// <summary>코드로 만든 TextMeshPro 입력 칸. 한글 입력(IME)이 된다.</summary>
        TMP_InputField Input(RectTransform parent, string name, string placeholder, float bottom, bool password)
        {
            var rect = NewRect(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0);
            rect.pivot = new Vector2(0.5f, 0);
            rect.anchoredPosition = new Vector2(0, bottom);
            rect.sizeDelta = new Vector2(520, 60);
            var bg = rect.gameObject.AddComponent<Image>();
            bg.color = Field;

            var area = NewRect("Text Area", rect);
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(18, 6);
            area.offsetMax = new Vector2(-18, -6);
            area.gameObject.AddComponent<RectMask2D>();

            TextMeshProUGUI Text(string n, Color c)
            {
                var r = NewRect(n, area);
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.one;
                r.offsetMin = r.offsetMax = Vector2.zero;
                var t = r.gameObject.AddComponent<TextMeshProUGUI>();
                t.font = _font;
                t.fontSize = 26;
                t.color = c;
                t.alignment = TextAlignmentOptions.MidlineLeft;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.richText = false;
                return t;
            }

            var ph = Text("Placeholder", new Color(Muted.r, Muted.g, Muted.b, 0.7f));
            ph.text = placeholder;
            var text = Text("Text", Ink);

            var input = rect.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = bg;
            input.textViewport = area;
            input.textComponent = text;
            input.placeholder = ph;
            input.fontAsset = _font;
            input.pointSize = 26;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = password ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard;
            input.richText = false;
            return input;
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
