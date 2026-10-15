using System.Collections;
using UnityEngine;

namespace DiceHeist.Game
{
    /// <summary>
    /// 주사위 소리. 녹음 파일 없이 코드로 합성한다(재배포·라이선스 걱정이 없고, 나중에 녹음한 소리로 바꾸기 쉽다).
    /// - Impact: 굴린 주사위가 테이블·벽·다른 주사위에 부딪힐 때. DiceThrower가 물리 녹화에서 찾은 충돌 시점에 맞춰 부른다
    /// - CupRattle / CupSlam: 상대가 컵을 흔들고 내려놓을 때
    /// </summary>
    public sealed class DiceAudio : MonoBehaviour
    {
        const int Rate = 44100;
        const int Voices = 10;

        /// <summary>전체 음량(0~1).</summary>
        public static float Volume = 0.8f;

        static DiceAudio _instance;

        AudioClip[] _clacks;
        AudioClip[] _muffled;
        AudioClip _thud;
        AudioSource[] _voices;
        int _next;

        static DiceAudio Instance
        {
            get
            {
                if (_instance == null) _instance = new GameObject("DiceAudio").AddComponent<DiceAudio>();
                return _instance;
            }
        }

        void Awake()
        {
            var rng = new System.Random(7);
            _clacks = new AudioClip[8];
            for (int i = 0; i < _clacks.Length; i++) _clacks[i] = Clack($"clack{i}", rng, 0.075f, 1700f, 5200f, 0.010f, 0f);
            _muffled = new AudioClip[6];
            for (int i = 0; i < _muffled.Length; i++) _muffled[i] = Clack($"muffled{i}", rng, 0.06f, 900f, 2600f, 0.008f, 0.82f);
            _thud = Thud();

            _voices = new AudioSource[Voices];
            for (int i = 0; i < Voices; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                _voices[i] = s;
            }
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        void Play(AudioClip clip, float volume, float pitch)
        {
            var s = _voices[_next];
            _next = (_next + 1) % _voices.Length;
            s.pitch = pitch;
            s.PlayOneShot(clip, Mathf.Clamp01(volume) * Volume);
        }

        /// <summary>주사위 한 번 부딪힘. strength는 0~1(충돌 세기).</summary>
        public static void Impact(float strength)
        {
            var a = Instance;
            float v = Mathf.Lerp(0.15f, 1f, Mathf.Clamp01(strength));
            a.Play(a._clacks[Random.Range(0, a._clacks.Length)], v, Random.Range(0.9f, 1.12f));
        }

        /// <summary>컵 안에서 주사위가 달그락거리는 소리. seconds 동안 이어진다.</summary>
        public static void CupRattle(float seconds, float volume = 0.6f)
        {
            var a = Instance;
            a.StartCoroutine(a.Rattle(seconds, volume));
        }

        /// <summary>컵을 테이블에 탁 내려놓는 소리.</summary>
        public static void CupSlam(float volume = 0.7f)
        {
            var a = Instance;
            a.Play(a._thud, volume, Random.Range(0.95f, 1.05f));
            a.Play(a._muffled[Random.Range(0, a._muffled.Length)], volume * 0.8f, 1f);
        }

        IEnumerator Rattle(float seconds, float volume)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                Play(_muffled[Random.Range(0, _muffled.Length)], volume * Random.Range(0.5f, 1f), Random.Range(0.9f, 1.15f));
                yield return new WaitForSeconds(Random.Range(0.03f, 0.075f));
            }
        }

        // ---------- 합성 ----------

        /// <summary>
        /// 딱딱한 플라스틱이 부딪히는 짧은 소리: 높은 공명 몇 개 + 짧은 잡음, 빠르게 사라진다.
        /// lowpass(0~1)가 크면 천이나 컵 안처럼 먹먹해진다.
        /// </summary>
        static AudioClip Clack(string name, System.Random rng, float length, float fLow, float fHigh, float decay, float lowpass)
        {
            int n = (int)(Rate * length);
            var data = new float[n];
            const int modes = 4;
            var freq = new float[modes];
            var amp = new float[modes];
            for (int k = 0; k < modes; k++)
            {
                freq[k] = Mathf.Lerp(fLow, fHigh, (float)rng.NextDouble());
                amp[k] = 0.35f + 0.65f * (float)rng.NextDouble();
            }
            float lp = 0f, peak = 1e-6f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Exp(-t / decay) * Mathf.Min(1f, t / 0.0004f);
                float tone = 0f;
                for (int k = 0; k < modes; k++) tone += amp[k] * Mathf.Sin(2f * Mathf.PI * freq[k] * t) * Mathf.Exp(-t / (decay * (0.6f + k * 0.25f)));
                float noise = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t / (decay * 0.35f));
                float x = env * (0.55f * tone / modes + 0.6f * noise);
                lp = lowpass > 0f ? lp + (1f - lowpass) * (x - lp) : x;
                data[i] = lp;
                peak = Mathf.Max(peak, Mathf.Abs(lp));
            }
            for (int i = 0; i < n; i++) data[i] = data[i] / peak * 0.9f;
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>낮게 둥 울리는 내려놓는 소리.</summary>
        static AudioClip Thud()
        {
            int n = (int)(Rate * 0.16f);
            var data = new float[n];
            var rng = new System.Random(3);
            float lp = 0f, peak = 1e-6f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = Mathf.Lerp(150f, 90f, t / 0.16f);
                float x = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t / 0.045f)
                          + ((float)rng.NextDouble() * 2f - 1f) * 0.5f * Mathf.Exp(-t / 0.012f);
                lp += 0.25f * (x - lp);
                data[i] = lp;
                peak = Mathf.Max(peak, Mathf.Abs(lp));
            }
            for (int i = 0; i < n; i++) data[i] = data[i] / peak * 0.9f;
            var clip = AudioClip.Create("thud", n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
