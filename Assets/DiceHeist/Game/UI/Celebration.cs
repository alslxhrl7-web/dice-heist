using System.Collections;
using System.Collections.Generic;
using DiceHeist.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DiceHeist.Game
{
    /// <summary>
    /// 큰 족보(스트레이트, 포 다이스, 파이브 다이스)가 나왔을 때 화면 전체에 띄우는 축하 연출: 번쩍임, 튀어나오는 제목, 양쪽 아래에서 쏘는 색종이.
    /// 화면 위에 덮이지만 클릭은 막지 않는다(GraphicRaycaster 없음). 셰이더는 UI 기본과 TMP 기본만 쓴다.
    /// 연출용 난수(UnityEngine.Random)는 게임 난수와 따로다.
    /// </summary>
    public sealed class Celebration : MonoBehaviour
    {
        static readonly Color[] Paper =
        {
            new Color(0.89f, 0.64f, 0.15f), new Color(0.77f, 0.26f, 0.42f), new Color(0.17f, 0.54f, 0.34f),
            new Color(0.27f, 0.52f, 0.86f), new Color(0.98f, 0.85f, 0.35f), new Color(0.96f, 0.96f, 0.93f),
        };

        RectTransform _root;
        public Canvas Canvas { get; private set; }
        /// <summary>지금 연출 중인지.</summary>
        public bool Playing { get; private set; }

        public static Celebration Build()
        {
            var go = new GameObject("Celebration", typeof(Canvas), typeof(CanvasScaler));
            var fx = go.AddComponent<Celebration>();
            fx.Canvas = go.GetComponent<Canvas>();
            fx.Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            fx.Canvas.sortingOrder = 50; // HUD(0)·시작 화면(10) 위
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            fx._root = (RectTransform)go.transform;
            return fx;
        }

        /// <summary>이 족보면 축하한다(스트레이트 이상: 스트레이트, 포 다이스, 파이브 다이스).</summary>
        public static bool Worth(HandRank rank) => rank >= HandRank.Straight;

        /// <summary>연출을 시작하고 바로 돌아온다(게임 진행을 막지 않는다). 연출 중에 또 부르면 처음부터 다시 한다.</summary>
        public void Play(HandRank rank)
        {
            StopAllCoroutines();
            Clear();
            StartCoroutine(Run(rank));
        }

        void Clear()
        {
            for (int i = _root.childCount - 1; i >= 0; i--) Destroy(_root.GetChild(i).gameObject);
            Playing = false;
        }

        IEnumerator Run(HandRank rank)
        {
            Playing = true;
            bool top = rank == HandRank.FiveDice;
            bool four = rank == HandRank.FourDice;
            var size = _root.rect.size;
            if (size.x < 1f || size.y < 1f) size = new Vector2(1920, 1080); // 캔버스가 아직 배치 전이면 기준 해상도로

            // 1. 번쩍임
            var flash = NewImage("Flash", top ? new Color(1f, 0.92f, 0.6f, 0.55f) : new Color(1f, 1f, 1f, 0.4f));
            flash.rectTransform.anchorMin = Vector2.zero;
            flash.rectTransform.anchorMax = Vector2.one;
            flash.rectTransform.offsetMin = flash.rectTransform.offsetMax = Vector2.zero;

            // 2. 색종이 층: 제목보다 먼저 만들어 뒤에 그린다(글자를 가리지 않게)
            var paperLayer = NewLayer("ConfettiLayer");

            // 3. 제목
            var titleBg = NewImage("TitleBg", new Color(0.12f, 0.16f, 0.15f, 0.82f));
            titleBg.rectTransform.sizeDelta = new Vector2(size.x + 40, 250);
            titleBg.rectTransform.anchoredPosition = new Vector2(0, 90);
            var title = NewText("Title", HandEvaluator.NameOf(rank) + "!", top ? 150 : four ? 138 : 128,
                top ? new Color(1f, 0.82f, 0.3f) : four ? new Color(1f, 0.92f, 0.62f) : Color.white);
            title.rectTransform.anchoredPosition = new Vector2(0, 115);
            var sub = NewText("Sub", top ? "최고 족보! 주사위 다섯 개가 모두 같은 눈" : four ? "대단해요! 같은 눈이 네 개" : "축하합니다! 다섯 눈이 이어졌어요", 40, new Color(0.93f, 0.95f, 0.93f));
            sub.rectTransform.anchoredPosition = new Vector2(0, 10);

            // 4. 색종이: 왼쪽·오른쪽 아래에서 위로 쏜다. 파이브 다이스는 위에서도 쏟아진다
            var bits = new List<Bit>();
            int perSide = top ? 80 : four ? 68 : 55;
            for (int i = 0; i < perSide; i++)
            {
                bits.Add(NewBit(paperLayer, new Vector2(-size.x / 2 + 40, -size.y / 2), Random.Range(55f, 80f), Random.Range(1300f, 2100f)));
                bits.Add(NewBit(paperLayer, new Vector2(size.x / 2 - 40, -size.y / 2), Random.Range(100f, 125f), Random.Range(1300f, 2100f)));
            }
            if (top)
                for (int i = 0; i < 70; i++)
                    bits.Add(NewBit(paperLayer, new Vector2(Random.Range(-size.x / 2, size.x / 2), size.y / 2 + Random.Range(20f, 400f)), -90f, Random.Range(50f, 250f)));

            const float duration = 3.0f;
            const float gravity = 1900f;
            for (float t = 0; t < duration; t += Time.deltaTime)
            {
                float dt = Time.deltaTime;
                flash.color = new Color(flash.color.r, flash.color.g, flash.color.b, Mathf.Lerp(flash.color.a, 0f, 1f - Mathf.Exp(-dt * 7f)));

                // 제목: 0.35초 동안 0 → 1.2 → 1로 튀어나오고, 마지막 0.5초에 사라진다
                float pop = t < 0.22f ? Mathf.Lerp(0f, 1.2f, t / 0.22f) : t < 0.35f ? Mathf.Lerp(1.2f, 1f, (t - 0.22f) / 0.13f) : 1f + 0.03f * Mathf.Sin((t - 0.35f) * 6f);
                float fade = Mathf.Clamp01((duration - t) / 0.5f);
                title.rectTransform.localScale = Vector3.one * pop;
                sub.rectTransform.localScale = Vector3.one * Mathf.Clamp01(pop);
                title.alpha = sub.alpha = fade;
                titleBg.color = new Color(titleBg.color.r, titleBg.color.g, titleBg.color.b, 0.82f * fade * Mathf.Clamp01(t / 0.15f));

                foreach (var b in bits)
                {
                    b.Velocity.y -= gravity * dt;
                    b.Velocity.x *= 1f - 1.2f * dt; // 공기 저항
                    if (b.Velocity.y < -420f) b.Velocity.y = -420f; // 종이라 천천히 떨어진다
                    b.Rect.anchoredPosition += (b.Velocity + new Vector2(Mathf.Sin((t + b.Phase) * 7f) * 60f, 0)) * dt;
                    b.Rect.localRotation = Quaternion.Euler(0, 0, b.Rect.localEulerAngles.z + b.Spin * dt);
                    b.Rect.localScale = new Vector3(Mathf.Cos((t + b.Phase) * 9f), 1f, 1f); // 뒤집히며 반짝이는 느낌
                    b.Image.color = new Color(b.Image.color.r, b.Image.color.g, b.Image.color.b, fade);
                }
                yield return null;
            }
            Clear();
        }

        sealed class Bit
        {
            public RectTransform Rect;
            public Image Image;
            public Vector2 Velocity;
            public float Spin, Phase;
        }

        Bit NewBit(RectTransform layer, Vector2 from, float angleDeg, float speed)
        {
            var img = NewImage("Confetti", Paper[Random.Range(0, Paper.Length)], layer);
            img.rectTransform.sizeDelta = new Vector2(Random.Range(12f, 20f), Random.Range(20f, 32f));
            img.rectTransform.anchoredPosition = from;
            float a = angleDeg * Mathf.Deg2Rad;
            return new Bit
            {
                Rect = img.rectTransform, Image = img,
                Velocity = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * speed,
                Spin = Random.Range(-540f, 540f), Phase = Random.Range(0f, 10f),
            };
        }

        /// <summary>화면 전체를 덮는 빈 층. 먼저 만든 층이 뒤에 그려진다.</summary>
        RectTransform NewLayer(string name)
        {
            var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rt.SetParent(_root, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        Image NewImage(string name, Color color, RectTransform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent != null ? parent : _root, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        TextMeshProUGUI NewText(string name, string text, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(_root, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.font = UiFont.Korean;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            t.rectTransform.sizeDelta = new Vector2(1800, size * 1.4f);
            return t;
        }
    }
}
