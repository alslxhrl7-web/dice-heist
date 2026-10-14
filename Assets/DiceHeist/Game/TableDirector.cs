using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DiceHeist.Core;
using UnityEngine;

namespace DiceHeist.Game
{
    /// <summary>
    /// 싱글 한 테이블의 연출. 규칙은 백엔드(서버 또는 이 PC의 Core SingleTable)가 판정하고,
    /// 여기서는 요청을 보내고 돌아온 이벤트(누가 굴렸고 누가 콜했는지)를 순서대로 3D·UI로 보여 준다.
    /// </summary>
    public sealed class TableDirector : MonoBehaviour
    {
        ISingleBackend _backend;
        TableScene _scene;
        HudView _hud;
        long _seed;
        bool _auto;
        string _tableName;

        // 포커 용어 (우리말)
        const string Fold = "폴드 (포기)";
        const string Call = "콜 (따라가기)";
        const string Raise = "레이즈 (올리기)";
        const string Ante = "앤티 (참가비)";
        const string Pot = "팟 (판돈)";

        readonly bool[] _held = new bool[5];
        readonly List<string> _logs = new List<string>();
        string _choice;
        int _turnSeat = -1;
        bool _showHand;
        int _played, _won, _best = -1;
        bool _wired, _entered, _leave;
        Celebration _celebration;
        /// <summary>이번 판에 이미 축하한 가장 높은 족보. 같은 족보로 두 번 축하하지 않는다.</summary>
        HandRank? _celebrated;

        /// <summary>서버가 마지막으로 돌려준 상태.</summary>
        SingleView _v;
        /// <summary>지금 화면에 보이는 자리 상태. 이벤트를 하나씩 재생하며 _v 쪽으로 따라간다.</summary>
        SeatView[] _shown;
        int _shownPot;

        /// <summary>결과 화면에서 테이블 선택을 눌렀거나, 서버 오류로 테이블을 떠났을 때. 인자는 알릴 말(없으면 null).</summary>
        public event Action<string> Left;

        /// <summary>지금 단계: entrance / connecting / rolling / hold / ai / betting / bet / respond / reveal / result / broke / error</summary>
        public string Phase { get; private set; } = "idle";
        public int RoundsFinished => _played;
        /// <summary>마지막으로 받은 테이블 상태.</summary>
        public SingleView View => _v;
        /// <summary>축하 연출을 띄운 횟수(테스트용).</summary>
        public int Celebrations { get; private set; }
        public Celebration Celebration => _celebration;

        public void Begin(ISingleBackend backend, string tableName, TableScene scene, HudView hud, long seed, bool autoPlay)
        {
            _backend = backend;
            _tableName = tableName;
            _scene = scene;
            _hud = hud;
            _seed = seed;
            _auto = autoPlay;
            if (!_wired)
            {
                _wired = true;
                _celebration = Celebration.Build();
                _hud.ButtonClicked += id => _choice = id;
                foreach (var die in _scene.Seats[0].Dice) die.Clicked += OnDieClicked;
            }
            StartCoroutine(Main());
        }

        IEnumerator Main()
        {
            _leave = false;
            string leaveMessage = null;

            Phase = "connecting";
            _hud.SetHint("테이블에 앉는 중…");
            yield return Request("sit", JsonUtility.ToJson(new SitBody { table = _tableName }));
            if (!string.IsNullOrEmpty(_v.error))
            {
                leaveMessage = _v.error;
                _leave = true;
            }
            else
            {
                _hud.SetTable($"{_v.table} 테이블 · {Ante} {_v.ante}칩 · 한도 {_v.chipCap:N0}칩" + (_backend.Online ? "" : " · 오프라인"));
                Log($"{_v.table} 테이블에 앉았다 · {_v.seats[0].chips:N0}칩");
                Refresh();

                if (!_entered)
                {
                    Phase = "entrance";
                    _hud.SetHint("동물들이 테이블로 나오는 중…");
                    yield return _scene.Entrance();
                    _entered = true;
                }
            }

            while (!_leave)
            {
                var error = new string[1];
                yield return PlayRound(error);
                if (error[0] != null)
                {
                    // 서버 오류: 다시 시도하거나 테이블 선택으로
                    Phase = "error";
                    _hud.SetHint(error[0]);
                    yield return Choose(new ButtonSpec("retry", "다시 시도", ButtonStyle.Primary), new ButtonSpec("tables", "테이블 선택"));
                    if (_choice == "tables") _leave = true;
                    else yield return Request("look", null);
                }
            }

            yield return _backend.Send("leave", null, _ => { });
            Phase = "idle";
            _hud.HideBanner();
            _hud.ShowButtons();
            Left?.Invoke(leaveMessage);
        }

        /// <summary>요청을 보내고 결과를 _v에 둔다. 실패해도 _v.error만 채워지고 화면 상태는 그대로다.</summary>
        IEnumerator Request(string op, string body)
        {
            SingleView res = null;
            yield return _backend.Send(op, body, r => res = r);
            if (res == null) res = new SingleView { error = "응답이 없습니다." };
            if (!string.IsNullOrEmpty(res.error) && _v != null)
            {
                _v.error = res.error;
                yield break;
            }
            _v = res;
        }

        static bool Has(int[] dice) => dice != null && dice.Length == 5;

        IEnumerator PlayRound(string[] error)
        {
            _hud.HideBanner();

            // 1. 시작 (칩이 바닥났으면 구제 칩부터)
            yield return Request("start", null);
            if (!string.IsNullOrEmpty(_v.error)) { error[0] = _v.error; yield break; }
            PlayInstantEvents();
            if (_v.phase == "broke")
            {
                Phase = "broke";
                _hud.SetHint("칩이 바닥났습니다. 구제 칩을 받아 다시 시작하세요.");
                Refresh();
                yield return Choose(new ButtonSpec("rescue", $"구제 칩 받기 +{_v.rescueChips}", ButtonStyle.Primary));
                yield return Request("rescue", null);
                if (!string.IsNullOrEmpty(_v.error)) { error[0] = _v.error; yield break; }
                PlayInstantEvents();
                yield return Request("start", null);
                if (!string.IsNullOrEmpty(_v.error)) { error[0] = _v.error; yield break; }
                PlayInstantEvents();
            }

            Log($"{_v.roundNo}판 시작 · {Ante} {_v.ante}칩씩");
            Phase = "rolling";
            _turnSeat = -1;
            _showHand = false;
            Array.Clear(_held, 0, _held.Length);
            _scene.ResetForRound();
            SyncShown();
            _hud.ShowButtons();
            _hud.SetHint("주사위를 굴리는 중…");
            Refresh();
            yield return Fx.All(this,
                _scene.ThrowMine(new[] { 0, 1, 2, 3, 4 }, _v.seats[0].dice),
                _scene.ShakeCup(1),
                _scene.ShakeCup(2));
            _showHand = true;
            _celebrated = null;
            CelebrateIfGreat();

            // 2. 남기기
            Phase = "hold";
            _turnSeat = 0;
            _hud.SetHint("남길 주사위를 눌러 고정하고, 나머지를 다시 굴리세요.");
            while (_v.canReroll)
            {
                SetPickable(true);
                ShowHoldButtons();
                Refresh();
                if (_auto) yield return AutoHold();
                yield return WaitChoice();
                SetPickable(false);
                if (_choice != "reroll" || _held.All(h => h)) break;

                _hud.ShowButtons();
                _showHand = false;
                Refresh();
                yield return Request("reroll", JsonUtility.ToJson(new RerollBody { held = (bool[])_held.Clone() }));
                if (!string.IsNullOrEmpty(_v.error)) { error[0] = _v.error; yield break; }
                var ev = _v.events.FirstOrDefault(e => e.kind == "reroll");
                SyncShown();
                if (ev != null) yield return _scene.ThrowMine(ev.idxs, _v.seats[0].dice);
                _showHand = true;
                CelebrateIfGreat();
            }
            SetPickable(false);
            Array.Clear(_held, 0, _held.Length);
            foreach (var die in _scene.Seats[0].Dice) die.Held = false;
            _hud.ShowButtons();
            Log($"나 · 리롤 {_v.seats[0].rerolls}회");

            // 3. 확정 → AI가 굴리고, 내 차례가 오거나 끝날 때까지 베팅이 진행된다
            Phase = "ai";
            _hud.SetHint("상대가 주사위를 다시 굴리는 중…");
            Refresh();
            var before = Snapshot();
            yield return Request("stand", null);
            if (!string.IsNullOrEmpty(_v.error)) { error[0] = _v.error; yield break; }
            yield return PlayEvents(before);

            while (_v.phase == "bet")
            {
                Phase = _v.responding ? "respond" : "bet";
                _turnSeat = 0;
                var buttons = new List<ButtonSpec>
                {
                    new ButtonSpec("fold", Fold),
                    new ButtonSpec("call", $"{Call}\n<size=20>+{_v.callCost}칩 · {Times(_v.seats[0].contrib + _v.callCost)}</size>", ButtonStyle.Primary, _v.canCall),
                };
                if (_v.responding)
                    _hud.SetHint($"내 뒤에서 레이즈(올리기)가 나와 {Times(_v.seats[0].contrib + _v.callCost)}가 됐습니다. 차액을 내고 콜(따라가기)할까요?");
                else if (_v.raiseUsed)
                    _hud.SetHint($"누군가 레이즈(올리기)해서 {Times(_v.seats[0].contrib + _v.callCost)}가 됐습니다. 콜(따라가기)하려면 그만큼 내야 합니다.");
                else
                {
                    _hud.SetHint($"배수는 앤티(참가비) {_v.ante}칩 기준. 레이즈(올리기)는 판당 한 명만, {Times(_v.raiseTotal)}까지.");
                    buttons.Add(new ButtonSpec("raise", $"{Raise}\n<size=20>+{_v.raiseCost}칩 · {Times(_v.raiseTotal)}</size>", ButtonStyle.Raise, _v.canRaise));
                }
                Refresh();
                yield return Choose(buttons.ToArray());
                string action = _choice == "raise" ? "raise" : _choice == "call" ? "call" : "fold";

                before = Snapshot();
                yield return Request("bet", JsonUtility.ToJson(new BetBody { action = action }));
                if (!string.IsNullOrEmpty(_v.error)) { error[0] = _v.error; yield break; }
                yield return PlayEvents(before);
            }
            _turnSeat = -1;

            if (_v.phase != "result") { error[0] = "판이 예상과 다르게 끝났습니다. 다시 시도해 주세요."; yield break; }
            yield return Showdown();
        }

        IEnumerator Showdown()
        {
            var res = _v.result;
            int me = 0;
            bool iWon = res.winners != null && res.winners.Contains(me);
            _played++;
            if (iWon) _won++;
            if (!_v.seats[me].folded && Has(_v.seats[me].dice))
                _best = Math.Max(_best, (int)HandEvaluator.Evaluate(_v.seats[me].dice).Rank);

            _hud.SetHint("");
            string Names(int[] seats) => string.Join(", ", seats.Select(i => _v.seats[i].name));
            if (res.uncontested)
            {
                var w = _v.seats[res.winners[0]];
                Log($"{w.name} · {Pot} {res.pot}칩 획득 (쇼다운 없이)");
                _hud.ShowBanner($"{w.name} 승리 · 나머지가 모두 폴드(포기) · +{res.pot}칩", iWon);
            }
            else
            {
                var reveals = new List<IEnumerator>();
                for (int i = 1; i < _v.seats.Length; i++)
                    if (_v.seats[i].revealed && Has(_v.seats[i].dice)) reveals.Add(_scene.Reveal(i, _v.seats[i].dice));
                Phase = "reveal";
                SyncShown();
                Refresh();
                yield return Fx.All(this, reveals.ToArray());
                yield return new WaitForSeconds(0.45f);

                _hud.ShowBanner(res.winners.Length > 1
                    ? $"무승부 · {Names(res.winners)} · {res.bestHand} · {res.share}칩씩 나눔"
                    : $"{_v.seats[res.winners[0]].name} 승리 · {res.bestHand} · +{res.pot}칩", iWon);
                Log("쇼다운 · " + string.Join(" / ", res.handSeats.Select((s, k) => $"{_v.seats[s].name} {res.handNames[k]}")));
            }

            for (int i = 0; i < _v.seats.Length; i++)
            {
                var animal = _scene.Seats[i].Animal;
                if (_v.seats[i].winner) StartCoroutine(animal.Cheer());
                else animal.SetDroop(true);
            }

            int chips = _v.seats[me].chips;
            if (res.overCap > 0) Log($"{_v.table} 한도 {_v.chipCap:N0}칩 · 넘친 {res.overCap}칩은 못 받음");
            var cfg = TableConfig.Tiers.FirstOrDefault(t => t.Name == _v.table) ?? TableConfig.Bronze;
            var up = TableConfig.Tiers.FirstOrDefault(t => t.MinChips > cfg.MinChips && t.CanEnter(chips));
            if (cfg.Name == TableConfig.Bronze.Name && TableConfig.ClearsBronze(chips))
                _hud.SetHint("브론즈를 지났습니다! 멀티가 열렸어요. 칩을 사이버머니로 환전해 멀티에서 쓸 수 있습니다.");
            else if (chips >= _v.chipCap)
                _hud.SetHint(up != null ? $"{cfg.Name} 한도에 닿았습니다. 테이블 선택에서 {up.Name}로 올라가세요." : $"{cfg.Name} 한도에 닿았습니다.");
            else if (up != null)
                _hud.SetHint($"{up.Name} 테이블에 앉을 수 있습니다 ({up.MinChips:N0}칩 이상). 테이블 선택에서 옮길 수 있어요.");

            Phase = "result";
            SyncShown();
            Refresh();
            yield return Choose(new ButtonSpec("next", "다음 판", ButtonStyle.Primary), new ButtonSpec("tables", "테이블 선택"));
            if (_choice == "tables") _leave = true;
        }

        // ---------- 이벤트 재생 ----------

        /// <summary>연출 없이 기록만 남기는 이벤트(지갑 채우기, 구제 칩).</summary>
        void PlayInstantEvents()
        {
            foreach (var e in _v.events)
            {
                if (e.kind == "refill") Log($"{_v.seats[e.seat].name} · 지갑을 새로 채워 왔다 ({e.count:N0}칩)");
                else if (e.kind == "rescue") Log($"구제 칩 {e.count}칩을 받았다");
            }
            SyncShown();
        }

        /// <summary>AI의 다시 굴리기와 베팅을 하나씩 보여 준다. 화면의 칩·상태는 이벤트마다 바뀐다.</summary>
        IEnumerator PlayEvents((SeatView[] seats, int pot) before)
        {
            _shown = before.seats;
            _shownPot = before.pot;
            foreach (var e in _v.events)
            {
                switch (e.kind)
                {
                    case "airoll":
                        Phase = "ai";
                        _turnSeat = e.seat;
                        _hud.SetHint("상대가 주사위를 다시 굴리는 중…");
                        Refresh();
                        for (int k = 0; k < e.count; k++)
                        {
                            yield return _scene.ShakeCup(e.seat);
                            _shown[e.seat].rerolls++;
                            Refresh();
                            yield return new WaitForSeconds(0.22f);
                        }
                        Log($"{_shown[e.seat].name} · 리롤 {e.count}회");
                        yield return new WaitForSeconds(0.25f);
                        break;
                    case "bet":
                    {
                        var s = _shown[e.seat];
                        _turnSeat = e.seat;
                        if (e.seat != 0)
                        {
                            Phase = "betting";
                            _hud.SetHint($"{s.name} 차례…");
                            Refresh();
                            yield return new WaitForSeconds(0.7f);
                        }
                        s.chips -= e.paid;
                        s.contrib += e.paid;
                        _shownPot += e.paid;
                        var animal = _scene.Seats[e.seat].Animal;
                        switch (e.action)
                        {
                            case "fold":
                                s.status = "folded";
                                s.folded = true;
                                Log($"{s.name} · {Fold}");
                                animal.SetDroop(true);
                                break;
                            case "call":
                                s.status = "called";
                                Log($"{s.name} · {Call} +{e.paid} · {Times(s.contrib)}");
                                break;
                            case "raise":
                                s.status = "raised";
                                Log($"{s.name} · {Raise} +{e.paid} · {Times(s.contrib)}");
                                StartCoroutine(animal.Bounce());
                                break;
                        }
                        Phase = "betting";
                        Refresh();
                        break;
                    }
                }
            }
            SyncShown();
            Refresh();
        }

        /// <summary>내 주사위가 멈췄을 때 스트레이트·파이브 다이스면 축하한다. 이번 판에 축하한 족보보다 높을 때만.</summary>
        void CelebrateIfGreat()
        {
            if (!Has(_v.seats[0].dice)) return;
            var rank = HandEvaluator.Evaluate(_v.seats[0].dice).Rank;
            if (!Celebration.Worth(rank) || (_celebrated.HasValue && rank <= _celebrated.Value)) return;
            _celebrated = rank;
            Celebrations++;
            Log($"나 · {HandEvaluator.NameOf(rank)}!");
            Refresh();
            _celebration.Play(rank);
            StartCoroutine(_scene.Seats[0].Animal.Cheer());
        }

        (SeatView[] seats, int pot) Snapshot() => (Copy(_v.seats), _v.pot);

        static SeatView[] Copy(SeatView[] seats) => seats.Select(s => new SeatView
        {
            name = s.name, tag = s.tag, ai = s.ai, chips = s.chips, rerolls = s.rerolls, contrib = s.contrib, status = s.status,
            folded = s.folded, winner = s.winner, revealed = s.revealed, dice = s.dice,
        }).ToArray();

        void SyncShown()
        {
            _shown = Copy(_v.seats);
            _shownPot = _v.pot;
        }

        // ---------- 입력 ----------

        void OnDieClicked(DieView die)
        {
            if (Phase != "hold") return;
            _held[die.Index] = !_held[die.Index];
            die.Held = _held[die.Index];
            ShowHoldButtons();
        }

        void SetPickable(bool on)
        {
            foreach (var die in _scene.Seats[0].Dice) die.Pickable = on;
        }

        void ShowHoldButtons()
        {
            _hud.ShowButtons(
                new ButtonSpec("reroll", $"다시 굴리기 · {_v.rerollsLeft}회 남음", ButtonStyle.Plain, !_held.All(h => h)),
                new ButtonSpec("stand", "이대로 확정", ButtonStyle.Primary));
        }

        IEnumerator Choose(params ButtonSpec[] buttons)
        {
            _hud.ShowButtons(buttons);
            if (_auto)
            {
                // 자동 진행: 따라갈 수 있으면 따라가고, 아니면 첫 버튼
                yield return new WaitForSeconds(0.4f);
                var pick = buttons.FirstOrDefault(b => b.Id == "call" && b.Enabled);
                _choice = pick.Id ?? buttons[0].Id;
            }
            else yield return WaitChoice();
            _hud.ShowButtons();
        }

        IEnumerator WaitChoice()
        {
            if (_auto && _choice != null) yield break;
            _choice = null;
            while (_choice == null) yield return null;
        }

        /// <summary>자동 진행용: AI와 같은 기준으로 고정하고 다시 굴린다.</summary>
        IEnumerator AutoHold()
        {
            yield return new WaitForSeconds(0.4f);
            bool[] hold = AiBrain.Hold(_v.seats[0].dice);
            for (int i = 0; i < 5; i++)
            {
                _held[i] = hold[i];
                _scene.Seats[0].Dice[i].Held = hold[i];
            }
            yield return new WaitForSeconds(0.4f);
            _choice = hold.All(h => h) ? "stand" : "reroll";
        }

        // ---------- 화면 갱신 ----------

        /// <summary>걸기 금액을 앤티 기준 배수로. 앤티 10칩일 때 10칩은 1배, 30칩은 3배.</summary>
        string Times(int amount) => $"{amount / (double)Math.Max(1, _v.ante):0.#}배";

        void Log(string line)
        {
            _logs.Insert(0, line);
            if (_logs.Count > 7) _logs.RemoveAt(_logs.Count - 1);
        }

        void Refresh()
        {
            if (_v == null || _v.seats == null || _v.seats.Length == 0) return;
            var seats = _shown ?? _v.seats;
            var me = seats[0];
            int rate = _played > 0 ? Mathf.RoundToInt(_won * 100f / _played) : 0;
            _hud.SetStats(me.chips, _v.roundNo, rate, _best >= 0 ? HandEvaluator.NameOf((HandRank)_best) : "-", _seed);
            _hud.SetHand(_showHand && Has(_v.seats[0].dice) ? HandEvaluator.NameOf(HandEvaluator.Evaluate(_v.seats[0].dice).Rank) : "…");
            _hud.SetPot(_shownPot);
            _hud.SetLog(_logs);
            _scene.SetPot(_shownPot, _v.ante);

            for (int i = 0; i < seats.Length; i++)
            {
                var p = seats[i];
                string first = _v.roundNo > 0 && _v.starter == i ? "  <color=#b07a00><b>선</b></color>" : "";
                string status = p.status == "raised" ? $"<color=#c4426a><b>{Raise}</b> {Times(p.contrib)}</color>"
                    : p.status == "called" ? $"<color=#2c8a57><b>{Call}</b> {Times(p.contrib)}</color>"
                    : p.status == "folded" ? Fold : "대기";
                string text = $"<b>{p.name}</b>{first}  {p.chips:N0}칩\n리롤 {p.rerolls}회 · {status}";
                // 상대 족보는 공개된 뒤에만 보여준다
                if (p.ai) text = $"<size=17><color=#5a6c64>{p.tag}</color></size>\n" + text;
                if (p.ai && p.revealed && Has(p.dice)) text += $"\n<b>{HandEvaluator.NameOf(HandEvaluator.Evaluate(p.dice).Rank)}</b>";
                _hud.SetSeat(i, text, _turnSeat == i, p.folded, p.winner);
            }
        }
    }
}
