using System;
using System.Collections;
using System.Text;
using DiceHeist.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace DiceHeist.Game
{
    /// <summary>계정 정보. 서버의 /api/me와 같은 모양(JsonUtility로 읽는다).</summary>
    [Serializable]
    public sealed class Profile
    {
        public string error;
        public string name;
        public int chips;
        public long money;
        public bool bronzeCleared;
        public int exchangeable;
    }

    /// <summary>
    /// 싱글 테이블을 진행시키는 곳. 서버(RemoteBackend)든 이 PC(LocalBackend)든 화면은 똑같이 SingleView만 받는다.
    /// op: sit / look / start / rescue / reroll / stand / bet / leave
    /// </summary>
    public interface ISingleBackend
    {
        /// <summary>서버에 로그인한 상태인지. false면 오프라인(이 PC 저장).</summary>
        bool Online { get; }
        IEnumerator Send(string op, string bodyJson, Action<SingleView> done);
        IEnumerator GetProfile(Action<Profile> done);
        IEnumerator Exchange(int amount, Action<Profile> done);
    }

    [Serializable] public sealed class SitBody { public string table; }
    [Serializable] public sealed class RerollBody { public bool[] held; }
    [Serializable] public sealed class BetBody { public string action; }
    [Serializable] public sealed class ExchangeBody { public int amount; }
    [Serializable] public sealed class CredentialsBody { public string name; public string password; }

    [Serializable]
    public sealed class AuthResult
    {
        public string error;
        public string token;
        public Profile profile;
    }

    /// <summary>
    /// 서버에 로그인한 싱글. 판정과 칩 저장은 서버가 한다(이 PC에서는 칩을 바꿀 수 없다).
    /// </summary>
    public sealed class RemoteBackend : ISingleBackend
    {
        public const string TokenKey = "DiceHeist.Token";
        const int TimeoutSeconds = 15;

        readonly string _baseUrl;
        string _token;

        public RemoteBackend(string baseUrl, string token)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _token = token;
        }

        public bool Online => true;
        public bool Unauthorized { get; private set; }

        public static string SavedToken
        {
            get { try { return PlayerPrefs.GetString(TokenKey, ""); } catch { return ""; } }
            set { PlayerPrefs.SetString(TokenKey, value ?? ""); PlayerPrefs.Save(); }
        }

        /// <summary>로그인 또는 회원가입. 성공하면 토큰을 저장한다.</summary>
        public static IEnumerator Login(string baseUrl, bool register, string name, string password, Action<AuthResult> done)
        {
            var body = JsonUtility.ToJson(new CredentialsBody { name = name, password = password });
            AuthResult result = null;
            yield return Post(baseUrl.TrimEnd('/') + (register ? "/api/register" : "/api/login"), body, null, (code, text) =>
            {
                result = Parse<AuthResult>(code, text, (r, e) => r.error = e);
            });
            if (string.IsNullOrEmpty(result.error) && !string.IsNullOrEmpty(result.token)) SavedToken = result.token;
            done(result);
        }

        public IEnumerator Send(string op, string bodyJson, Action<SingleView> done)
        {
            SingleView view = null;
            yield return Post(_baseUrl + "/api/single/" + op, bodyJson ?? "{}", _token, (code, text) =>
            {
                if (code == 401) Unauthorized = true;
                view = Parse<SingleView>(code, text, (v, e) => v.error = e);
            });
            done(view);
        }

        public IEnumerator GetProfile(Action<Profile> done)
        {
            Profile p = null;
            yield return Get(_baseUrl + "/api/me", _token, (code, text) =>
            {
                if (code == 401) Unauthorized = true;
                p = Parse<Profile>(code, text, (r, e) => r.error = e);
            });
            done(p);
        }

        public IEnumerator Exchange(int amount, Action<Profile> done)
        {
            Profile p = null;
            yield return Post(_baseUrl + "/api/exchange", JsonUtility.ToJson(new ExchangeBody { amount = amount }), _token,
                (code, text) => p = Parse<Profile>(code, text, (r, e) => r.error = e));
            done(p);
        }

        public IEnumerator Logout()
        {
            yield return Post(_baseUrl + "/api/logout", "{}", _token, (_, __) => { });
            SavedToken = "";
        }

        // ---------- HTTP ----------

        /// <summary>응답을 읽는다. 연결 실패·서버 오류는 error에 사람이 읽을 문장을 넣는다.</summary>
        static T Parse<T>(long code, string text, Action<T, string> setError) where T : new()
        {
            if (code == 0)
            {
                var t = new T();
                setError(t, "서버에 연결할 수 없습니다. 인터넷 연결이나 서버가 켜져 있는지 확인해 주세요.");
                return t;
            }
            if (code == 401)
            {
                var t = new T();
                setError(t, "로그인이 만료됐습니다. 다시 로그인해 주세요.");
                return t;
            }
            T parsed;
            try { parsed = string.IsNullOrEmpty(text) ? new T() : JsonUtility.FromJson<T>(text); }
            catch { parsed = new T(); }
            if (code >= 400)
            {
                // 서버의 {"error": "..."}를 그대로 쓴다
                var err = JsonUtility.FromJson<ErrorOnly>(string.IsNullOrEmpty(text) ? "{}" : text);
                setError(parsed, string.IsNullOrEmpty(err?.error) ? $"서버 오류 ({code})" : err.error);
            }
            return parsed;
        }

        [Serializable] sealed class ErrorOnly { public string error; }

        static IEnumerator Post(string url, string json, string token, Action<long, string> done)
        {
            using var req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            if (!string.IsNullOrEmpty(token)) req.SetRequestHeader("Authorization", "Bearer " + token);
            req.timeout = TimeoutSeconds;
            yield return req.SendWebRequest();
            done(req.result == UnityWebRequest.Result.ConnectionError ? 0 : req.responseCode, req.downloadHandler?.text);
        }

        static IEnumerator Get(string url, string token, Action<long, string> done)
        {
            using var req = UnityWebRequest.Get(url);
            if (!string.IsNullOrEmpty(token)) req.SetRequestHeader("Authorization", "Bearer " + token);
            req.timeout = TimeoutSeconds;
            yield return req.SendWebRequest();
            done(req.result == UnityWebRequest.Result.ConnectionError ? 0 : req.responseCode, req.downloadHandler?.text);
        }
    }

    /// <summary>
    /// 서버 없이 이 PC에서 돈다(테스트·오프라인). 서버와 같은 Core SingleTable을 쓰고, 칩은 PlayerPrefs에 둔다.
    /// </summary>
    public sealed class LocalBackend : ISingleBackend
    {
        const string ChipsKey = "DiceHeist.Chips";
        const string MoneyKey = "DiceHeist.CyberMoney";
        const string ClearedKey = "DiceHeist.BronzeCleared";

        readonly IRandom _rng;
        readonly bool _save;
        SingleTable _table;

        public int Chips { get; private set; }
        public long Money { get; private set; }
        public bool BronzeCleared { get; private set; }
        public bool Online => false;

        public LocalBackend(IRandom rng, bool save)
        {
            _rng = rng;
            _save = save;
            Chips = save && PlayerPrefs.HasKey(ChipsKey) ? PlayerPrefs.GetInt(ChipsKey) : TableConfig.Bronze.StartChips;
            // PlayerPrefs에는 long이 없어 문자열로 둔다
            Money = save && long.TryParse(PlayerPrefs.GetString(MoneyKey, "0"), out var m) ? m : 0;
            BronzeCleared = save && PlayerPrefs.GetInt(ClearedKey, 0) == 1;
        }

        /// <summary>테스트용: 지금 앉은 테이블.</summary>
        public SingleTable Table => _table;

        public IEnumerator Send(string op, string bodyJson, Action<SingleView> done)
        {
            SingleView v;
            if (op == "sit")
            {
                var name = JsonUtility.FromJson<SitBody>(bodyJson).table;
                var cfg = Array.Find(TableConfig.Tiers, t => t.Name == name);
                if (cfg == null) v = new SingleView { error = "없는 테이블입니다." };
                else if (!cfg.CanEnter(Chips)) v = new SingleView { error = $"{cfg.Name}은(는) {cfg.MinChips:N0}칩 이상 있어야 앉을 수 있습니다." };
                else
                {
                    _table = new SingleTable(cfg, _rng, Chips);
                    v = _table.Look();
                }
            }
            else if (op == "leave")
            {
                _table = null;
                v = new SingleView { phase = "idle" };
            }
            else if (_table == null) v = new SingleView { error = "테이블에 앉아 있지 않습니다." };
            else
            {
                switch (op)
                {
                    case "look": v = _table.Look(); break;
                    case "start": v = _table.Start(); break;
                    case "rescue": v = _table.Rescue(); break;
                    case "reroll": v = _table.Reroll(JsonUtility.FromJson<RerollBody>(bodyJson).held); break;
                    case "stand": v = _table.Stand(); break;
                    case "bet": v = _table.Bet(JsonUtility.FromJson<BetBody>(bodyJson).action); break;
                    default: v = new SingleView { error = "알 수 없는 요청: " + op }; break;
                }
                Chips = _table.Chips;
                if (TableConfig.ClearsBronze(Chips)) BronzeCleared = true;
                Save();
            }
            v.bronzeCleared = BronzeCleared;
            done(v);
            yield break;
        }

        public IEnumerator GetProfile(Action<Profile> done)
        {
            done(Me());
            yield break;
        }

        public IEnumerator Exchange(int amount, Action<Profile> done)
        {
            if (amount > ChipExchange.Exchangeable(Chips))
            {
                var p = Me();
                p.error = $"처음 받은 {ChipExchange.Floor}칩 위로 번 칩만 바꿀 수 있습니다.";
                done(p);
                yield break;
            }
            var (left, money) = ChipExchange.Exchange(Chips, amount);
            Chips = left;
            Money += money;
            Save();
            done(Me());
        }

        /// <summary>오프라인 기록을 처음(500칩)으로 되돌린다.</summary>
        public void Reset()
        {
            Chips = TableConfig.Bronze.StartChips;
            Money = 0;
            BronzeCleared = false;
            _table = null;
            Save();
        }

        Profile Me() => new Profile
        {
            name = "오프라인", chips = Chips, money = Money, bronzeCleared = BronzeCleared, exchangeable = ChipExchange.Exchangeable(Chips),
        };

        void Save()
        {
            if (!_save) return;
            PlayerPrefs.SetInt(ChipsKey, Chips);
            PlayerPrefs.SetInt(ClearedKey, BronzeCleared ? 1 : 0);
            PlayerPrefs.SetString(MoneyKey, Money.ToString());
            PlayerPrefs.Save();
        }
    }
}
