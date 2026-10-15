using System.Collections;
using UnityEngine;

namespace DiceHeist.Game
{
    /// <summary>
    /// 주사위 소리. 녹음 파일 없이 코드로 합성한다(재배포·라이선스 걱정이 없고, 나중에 녹음한 소리로 바꾸기 쉽다).
    /// - Impact: 굴린 주사위가 테이블·벽·다른 주사위에 부딪힐 때. DiceThrower가 물리 녹화에서 찾은 충돌 시점에 맞춰 부른다
    /// - CupRattle / CupSlam: 상대가 컵을 흔들고 내려놓을 때
    /// - Chips / Fold / Tock / Tick / Win / Lose: 베팅·쇼다운의 테이블 소리
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
        AudioClip[] _chips;
        AudioClip _swish, _tock, _win, _lose;
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
            _chips = new AudioClip[5];
            for (int i = 0; i < _chips.Length; i++) _chips[i] = Clack($"chip{i}", rng, 0.05f, 3200f, 7500f, 0.006f, 0.35f);
            _swish = Swish();
            _tock = Notes("tock", 0.12f, 0.03f, 0f, (820f, 0f));
            // 이기면 올라가는 4음(도-미-솔-높은 도), 지면 내려가는 2음
            _win = Notes("win", 0.9f, 0.22f, 0.35f, (523.3f, 0f), (659.3f, 0.09f), (784f, 0.18f), (1046.5f, 0.27f));
            _lose = Notes("lose", 0.7f, 0.3f, 0.15f, (392f, 0f), (311.1f, 0.2f));

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

        /// <summary>칩을 내는 소리. 많이 낼수록 여러 번 짤그랑거린다.</summary>
        public static void Chips(int count, float volume = 0.7f)
        {
            var a = Instance;
            a.StartCoroutine(a.ChipRun(Mathf.Clamp(count, 1, 8), volume));
        }

        IEnumerator ChipRun(int count, float volume)
        {
            for (int i = 0; i < count; i++)
            {
                Play(_chips[Random.Range(0, _chips.Length)], volume * Random.Range(0.7f, 1f), Random.Range(0.92f, 1.08f));
                yield return new WaitForSeconds(Random.Range(0.035f, 0.07f));
            }
        }

        /// <summary>폴드할 때 쉭 하고 밀어 내는 소리.</summary>
        public static void Fold() => Instance.Play(Instance._swish, 0.45f, 1f);

        /// <summary>컵을 열 때 나무 블록 소리.</summary>
        public static void Tock(float pitch = 1f) => Instance.Play(Instance._tock, 0.7f, pitch);

        /// <summary>마지막 공개 직전 긴장감용 작은 틱. 점점 빨라지게 여러 번 부른다.</summary>
        public static void Tick(float volume = 0.35f) => Instance.Play(Instance._tock, volume, 1.8f);

        public static void Win() => Instance.Play(Instance._win, 0.75f, 1f);
        public static void Lose() => Instance.Play(Instance._lose, 0.6f, 1f);

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

        /// <summary>음 여러 개를 이어 붙인 짧은 소리. notes는 (주파수, 시작 초). decay는 음마다의 잔향, harmonic은 2배음을 섞는 양.</summary>
        static AudioClip Notes(string name, float length, float decay, float harmonic, params (float freq, float start)[] notes)
        {
            int n = (int)(Rate * length);
            var data = new float[n];
            foreach (var (freq, start) in notes)
            {
                int s0 = (int)(start * Rate);
                for (int i = s0; i < n; i++)
                {
                    float t = (i - s0) / (float)Rate;
                    float env = Mathf.Min(1f, t / 0.004f) * Mathf.Exp(-t / decay);
                    data[i] += env * (Mathf.Sin(2f * Mathf.PI * freq * t) + harmonic * Mathf.Sin(4f * Mathf.PI * freq * t));
                }
            }
            float peak = 1e-6f;
            for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            for (int i = 0; i < n; i++) data[i] = data[i] / peak * 0.8f;
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>잡음을 점점 먹먹하게 걸러 만든 쉭 소리.</summary>
        static AudioClip Swish()
        {
            int n = (int)(Rate * 0.28f);
            var data = new float[n];
            var rng = new System.Random(11);
            float lp = 0f, peak = 1e-6f;
            for (int i = 0; i < n; i++)
            {
                float k = i / (float)n;
                float env = Mathf.Sin(Mathf.PI * k) * (1f - k * 0.5f);
                float x = ((float)rng.NextDouble() * 2f - 1f) * env;
                lp += Mathf.Lerp(0.5f, 0.06f, k) * (x - lp);
                data[i] = lp;
                peak = Mathf.Max(peak, Mathf.Abs(lp));
            }
            for (int i = 0; i < n; i++) data[i] = data[i] / peak * 0.7f;
            var clip = AudioClip.Create("swish", n, 1, Rate, false);
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
