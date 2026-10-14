using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DiceHeist.Core;
using UnityEngine;

namespace DiceHeist.Game
{
    /// <summary>
    /// DiceGame(규칙)을 한 단계씩 진행시키면서 그 사이에 3D 연출과 입력 대기를 끼워 넣는다.
    /// 규칙 판단은 전부 Core에 있고, 여기서는 순서와 화면만 다룬다.
    /// </summary>
    public sealed class TableDirector : MonoBehaviour
    {
        DiceGame _game;
        TableScene _scene;
        HudView _hud;
        long _seed;
        bool _auto;

        // 포커 용어 (우리말)
        const string Fold = "폴드 (포기)";
        const string Call = "콜 (따라가기)";
        const string Raise = "레이즈 (올리기)";
        const string Ante = "앤티 (참가비)";
        const string Pot = "팟 (판돈)";

        readonly bool[] _held = new bool[5];
        readonly List<string> _logs = new List<string>();
        string _choice;
        PlayerState _turn;
        bool _showHand;
        int _played, _won, _best = -1;
        bool _wired, _entered, _leave;

        int _lastChips = -1;

        /// <summary>결과 화면에서 테이블 선택을 눌러 자리에서 일어났을 때.</summary>
        public event Action Left;
        /// <summary>내 칩이 바뀔 때마다(앤티, 베팅, 쇼다운, 구제 칩). 저장에 쓴다.</summary>
        public event Action<int> ChipsChanged;

        /// <summary>지금 단계: entrance / rolling / hold / ai / betting / bet / respond / result / broke</summary>
        public string Phase { get; private set; } = "idle";
        public int RoundsFinished => _played;
        public DiceGame Game => _game;

        public void Begin(DiceGame game, TableScene scene, HudView hud, long seed, bool autoPlay)
        {
            _game = game;
            _scene = scene;
            _hud = hud;
            _seed = seed;
            _auto = autoPlay;
            if (!_wired)
            {
                _wired = true;
                _hud.ButtonClicked += id => _choice = id;
                foreach (var die in _scene.Seats[0].Dice) die.Clicked += OnDieClicked;
            }
            var cfg = game.Config;
            _hud.SetTable($"{cfg.Name} 테이블 · {Ante} {cfg.Ante}칩 · 한도 {cfg.ChipCap:N0}칩");
            Log($"{cfg.Name} 테이블에 앉았다 · {game.Human.Chips:N0}칩");
            StartCoroutine(Main());
        }

        IEnumerator Main()
        {
            if (!_entered)
            {
                Phase = "entrance";
                _hud.SetHint("동물들이 테이블로 나오는 중…");
                Refresh();
                yield return _scene.Entrance();
                _entered = true;
            }
            _leave = false;
            while (!_leave) yield return PlayRound();
            Phase = "idle";
            _hud.HideBanner();
            Left?.Invoke();
        }

        IEnumerator PlayRound()
        {
            var me = _game.Human;
            _hud.HideBanner();
            foreach (var p in _game.RefillAi()) Log($"{p.Name} · 지갑을 새로 채워 왔다 ({p.Chips:N0}칩)");

            if (_game.HumanIsBroke)
            {
                Phase = "broke";
                _hud.SetHint("칩이 바닥났습니다. 실제 게임에서는 싱글로 돌아가 다시 벌어야 합니다.");
                Refresh();
                yield return Choose(new ButtonSpec("rescue", $"구제 칩 받기 +{_game.RescueChips}", ButtonStyle.Primary));
                _game.Rescue();
                Log($"구제 칩 {_game.RescueChips}칩을 받았다");
            }

            // 1. 굴리기
            _game.StartRound();
            Log($"{_game.RoundNo}판 시작 · {Ante} {_game.Ante}칩씩");
            Phase = "rolling";
            _turn = null;
            _showHand = false;
            Array.Clear(_held, 0, _held.Length);
            _scene.ResetForRound();
            _hud.ShowButtons();
            _hud.SetHint("주사위를 굴리는 중…");
            Refresh();
            yield return Fx.All(this,
                _scene.ThrowMine(new[] { 0, 1, 2, 3, 4 }, me.Dice),
                _scene.ShakeCup(1),
                _scene.ShakeCup(2));
            _showHand = true;

            // 2. 남기기
            Phase = "hold";
            _turn = me;
            _hud.SetHint("남길 주사위를 눌러 고정하고, 나머지를 다시 굴리세요.");
            while (_game.CanReroll(me))
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
                int[] idxs = _game.Reroll(me, _held);
                yield return _scene.ThrowMine(idxs, me.Dice);
                _showHand = true;
            }
            SetPickable(false);
            Array.Clear(_held, 0, _held.Length);
            foreach (var die in _scene.Seats[0].Dice) die.Held = false;
            _hud.ShowButtons();
            Log($"나 · 리롤 {me.Rerolls}회");

            Phase = "ai";
            _hud.SetHint("상대가 주사위를 다시 굴리는 중…");
            foreach (var p in _game.Players.Where(q => q.IsAi))
            {
                _turn = p;
                Refresh();
                while (_game.CanReroll(p))
                {
                    bool[] hold = AiBrain.Hold(p.Dice);
                    if (hold.All(h => h)) break;
                    _game.Reroll(p, hold);
                    yield return _scene.ShakeCup(p.Index);
                    Refresh();
                    yield return new WaitForSeconds(0.22f);
                }
                Log($"{p.Name} · 리롤 {p.Rerolls}회");
                Refresh();
                yield return new WaitForSeconds(0.25f);
            }

            // 3. 걸기
            var bet = _game.BeginBetting();
            while (!bet.IsDone)
            {
                var p = bet.Current;
                _turn = p;
                BetAction request;
                if (p.IsAi)
                {
                    Phase = "betting";
                    _hud.SetHint($"{p.Name} 차례…");
                    Refresh();
                    yield return new WaitForSeconds(0.7f);
                    request = _game.DecideAi(p);
                }
                else
                {
                    Phase = bet.Responding ? "respond" : "bet";
                    int callCost = bet.CallCost(p);
                    var buttons = new List<ButtonSpec>
                    {
                        new ButtonSpec("fold", Fold),
                        new ButtonSpec("call", $"{Call}\n<size=20>+{callCost}칩 · {Times(bet.Level)}</size>", ButtonStyle.Primary, bet.CanCall(p)),
                    };
                    if (bet.Responding)
                        _hud.SetHint($"내 뒤에서 레이즈(올리기)가 나와 {Times(bet.Level)}가 됐습니다. 차액을 내고 콜(따라가기)할까요?");
                    else if (bet.RaiseUsed)
                        _hud.SetHint($"누군가 레이즈(올리기)해서 {Times(bet.Level)}가 됐습니다. 콜(따라가기)하려면 그만큼 내야 합니다.");
                    else
                    {
                        _hud.SetHint($"배수는 앤티(참가비) {_game.Ante}칩 기준. 레이즈(올리기)는 판당 한 명만, {Times(_game.RaiseTotal)}까지.");
                        buttons.Add(new ButtonSpec("raise", $"{Raise}\n<size=20>+{bet.RaiseCost(p)}칩 · {Times(_game.RaiseTotal)}</size>", ButtonStyle.Raise, bet.CanRaise(p)));
                    }
                    Refresh();
                    yield return Choose(buttons.ToArray());
                    request = _choice == "raise" ? BetAction.Raise : _choice == "call" ? BetAction.Call : BetAction.Fold;
                }

                var (act, paid) = bet.Apply(request);
                var animal = _scene.Seats[p.Index].Animal;
                switch (act)
                {
                    case BetAction.Fold:
                        Log($"{p.Name} · {Fold}");
                        animal.SetDroop(true);
                        break;
                    case BetAction.Call:
                        Log($"{p.Name} · {Call} +{paid} · {Times(p.Contrib)}");
                        break;
                    case BetAction.Raise:
                        Log($"{p.Name} · {Raise} +{paid} · {Times(p.Contrib)}");
                        StartCoroutine(animal.Bounce());
                        break;
                }
                Phase = "betting";
                Refresh();
            }
            _turn = null;

            // 4. 공개
            int pot = _game.Pot;
            var res = _game.ResolveShowdown();
            _played++;
            bool iWon = res.Winners.Contains(me);
            if (iWon) _won++;
            if (!me.Folded) _best = Math.Max(_best, (int)HandEvaluator.Evaluate(me.Dice).Rank);

            _hud.SetHint("");
            if (res.Uncontested)
            {
                var w = res.Winners[0];
                Log($"{w.Name} · {Pot} {pot}칩 획득 (쇼다운 없이)");
                _hud.ShowBanner($"{w.Name} 승리 · 나머지가 모두 폴드(포기) · +{pot}칩", iWon);
            }
            else
            {
                var reveals = _game.Players.Where(p => p.IsAi && p.Revealed)
                    .Select(p => _scene.Reveal(p.Index, p.Dice)).ToArray();
                Phase = "reveal";
                Refresh();
                yield return Fx.All(this, reveals);
                yield return new WaitForSeconds(0.45f);

                string hand = HandEvaluator.NameOf(res.BestHand.Rank);
                _hud.ShowBanner(res.Winners.Count > 1
                    ? $"무승부 · {string.Join(", ", res.Winners.Select(w => w.Name))} · {hand} · {res.Share}칩씩 나눔"
                    : $"{res.Winners[0].Name} 승리 · {hand} · +{pot}칩", iWon);
                Log("쇼다운 · " + string.Join(" / ", res.Hands.Select(h => $"{h.player.Name} {HandEvaluator.NameOf(h.hand.Rank)}")));
            }

            foreach (var p in _game.Players)
            {
                var animal = _scene.Seats[p.Index].Animal;
                if (p.Winner) StartCoroutine(animal.Cheer());
                else animal.SetDroop(true);
            }

            var cfg = _game.Config;
            if (res.OverCap > 0) Log($"{cfg.Name} 한도 {cfg.ChipCap:N0}칩 · 넘친 {res.OverCap}칩은 못 받음");
            var up = TableConfig.Tiers.FirstOrDefault(t => t.MinChips > cfg.MinChips && t.CanEnter(me.Chips));
            if (cfg.Name == TableConfig.Bronze.Name && TableConfig.ClearsBronze(me.Chips))
                _hud.SetHint("브론즈를 지났습니다! 멀티가 열렸어요. 칩을 사이버머니로 환전해 멀티에서 쓸 수 있습니다.");
            else if (me.Chips >= cfg.ChipCap)
                _hud.SetHint(up != null ? $"{cfg.Name} 한도에 닿았습니다. 테이블 선택에서 {up.Name}로 올라가세요." : $"{cfg.Name} 한도에 닿았습니다.");
            else if (up != null)
                _hud.SetHint($"{up.Name} 테이블에 앉을 수 있습니다 ({up.MinChips:N0}칩 이상). 테이블 선택에서 옮길 수 있어요.");

            Phase = "result";
            Refresh();
            yield return Choose(new ButtonSpec("next", "다음 판", ButtonStyle.Primary), new ButtonSpec("tables", "테이블 선택"));
            if (_choice == "tables") _leave = true;
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
            int left = _game.MaxRerolls - _game.Human.Rerolls;
            _hud.ShowButtons(
                new ButtonSpec("reroll", $"다시 굴리기 · {left}회 남음", ButtonStyle.Plain, !_held.All(h => h)),
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
            bool[] hold = AiBrain.Hold(_game.Human.Dice);
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
        string Times(int amount) => $"{amount / (double)_game.Ante:0.#}배";

        void Log(string line)
        {
            _logs.Insert(0, line);
            if (_logs.Count > 7) _logs.RemoveAt(_logs.Count - 1);
        }

        void Refresh()
        {
            var me = _game.Human;
            if (me.Chips != _lastChips)
            {
                _lastChips = me.Chips;
                ChipsChanged?.Invoke(me.Chips);
            }
            int rate = _played > 0 ? Mathf.RoundToInt(_won * 100f / _played) : 0;
            _hud.SetStats(me.Chips, _game.RoundNo, rate, _best >= 0 ? HandEvaluator.NameOf((HandRank)_best) : "-", _seed);
            _hud.SetHand(_showHand ? HandEvaluator.NameOf(HandEvaluator.Evaluate(me.Dice).Rank) : "…");
            _hud.SetPot(_game.Pot);
            _hud.SetLog(_logs);
            _scene.SetPot(_game.Pot, _game.Ante);

            foreach (var p in _game.Players)
            {
                string first = _game.RoundNo > 0 && _game.Starter == p.Index ? "  <color=#b07a00><b>선</b></color>" : "";
                string status = p.Status == SeatStatus.Raised ? $"<color=#c4426a><b>{Raise}</b> {Times(p.Contrib)}</color>"
                    : p.Status == SeatStatus.Called ? $"<color=#2c8a57><b>{Call}</b> {Times(p.Contrib)}</color>"
                    : p.Status == SeatStatus.Folded ? Fold : "대기";
                string text = $"<b>{p.Name}</b>{first}  {p.Chips:N0}칩\n리롤 {p.Rerolls}회 · {status}";
                // 상대 족보는 공개된 뒤에만 보여준다
                if (p.IsAi) text = $"<size=17><color=#5a6c64>{p.Ai.Tag}</color></size>\n" + text;
                if (p.IsAi && p.Revealed) text += $"\n<b>{HandEvaluator.NameOf(HandEvaluator.Evaluate(p.Dice).Rank)}</b>";
                _hud.SetSeat(p.Index, text, _turn == p, p.Folded, p.Winner);
            }
        }
    }
}
