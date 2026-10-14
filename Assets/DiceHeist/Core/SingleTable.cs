using System;
using System.Collections.Generic;

namespace DiceHeist.Core
{
    /// <summary>한 자리의 겉모습. 사람 주사위는 늘, AI 주사위는 쇼다운에서 공개된 뒤에만 들어 있다.</summary>
    [Serializable]
    public sealed class SeatView
    {
        public string name;
        public string tag;
        public bool ai;
        public int chips;
        public int rerolls;
        public int contrib;
        /// <summary>waiting / called / raised / folded</summary>
        public string status;
        public bool folded;
        public bool winner;
        public bool revealed;
        public int[] dice;
    }

    /// <summary>요청 하나를 처리하는 동안 일어난 일. 화면은 이 순서대로 연출한다.</summary>
    [Serializable]
    public sealed class TableEvent
    {
        /// <summary>refill / rescue / start / reroll / airoll / bet / showdown</summary>
        public string kind;
        public int seat;
        /// <summary>bet: fold / call / raise</summary>
        public string action;
        public int paid;
        /// <summary>airoll: 다시 굴린 횟수. refill·rescue: 받은 칩</summary>
        public int count;
        /// <summary>reroll: 다시 굴린 자리</summary>
        public int[] idxs;
    }

    [Serializable]
    public sealed class ResultView
    {
        public bool uncontested;
        public int[] winners;
        public string bestHand;
        public int share;
        public int pot;
        public int overCap;
        /// <summary>쇼다운에 남은 사람들의 자리와 족보(강한 순)</summary>
        public int[] handSeats;
        public string[] handNames;
    }

    /// <summary>
    /// 싱글 테이블의 지금 상태. 서버가 JSON으로 보내고 Unity가 JsonUtility로 읽는다(그래서 필드만 쓴다).
    /// </summary>
    [Serializable]
    public sealed class SingleView
    {
        public string error;
        public string table;
        public int ante;
        public int raiseTotal;
        public int chipCap;
        public int maxRerolls;
        public int rescueChips;
        public int roundNo;
        public int starter;
        public int pot;
        /// <summary>idle(판 사이) / broke(구제 칩 필요) / hold(남기기) / bet(내 베팅 차례) / result(판 끝)</summary>
        public string phase;
        public SeatView[] seats;
        public TableEvent[] events;
        public int rerollsLeft;
        public bool canReroll;
        public int callCost;
        public int raiseCost;
        public bool canCall;
        public bool canRaise;
        public bool raiseUsed;
        public bool responding;
        public ResultView result;
        /// <summary>서버가 채운다: 브론즈를 지났는지(멀티 열림)</summary>
        public bool bronzeCleared;
    }

    /// <summary>
    /// 싱글 한 테이블(나 + AI 너구리·고양이)을 요청 단위로 진행한다. 서버와 Unity 오프라인 모드가 같은 코드를 쓴다.
    /// 호출 순서: Start → Reroll(0~2회) → Stand → (Bet 반복) → result → 다시 Start.
    /// 실패하면 예외 대신 error가 채워진 SingleView를 돌려준다.
    /// </summary>
    public sealed class SingleTable
    {
        readonly DiceGame _g;
        readonly List<TableEvent> _events = new List<TableEvent>();
        ResultView _result;
        string _phase = "idle";

        public SingleTable(TableConfig cfg, IRandom rng, int chips)
        {
            _g = new DiceGame(cfg, rng, chips);
        }

        public DiceGame Game => _g;
        public TableConfig Config => _g.Config;
        public int Chips => _g.Human.Chips;
        public string Phase => _phase;
        /// <summary>판 중(남기기·베팅)인지. 이때 나가면 이번 판에 낸 칩은 잃는다.</summary>
        public bool InRound => _phase == "hold" || _phase == "bet";

        // ---------- 요청 ----------

        public SingleView Look() => View(null);

        public SingleView Start()
        {
            if (InRound) return View("이미 판이 진행 중입니다.");
            foreach (var p in _g.RefillAi()) Add(new TableEvent { kind = "refill", seat = p.Index, count = p.Chips });
            if (_g.HumanIsBroke)
            {
                _phase = "broke";
                return View(null);
            }
            _g.StartRound();
            _result = null;
            _phase = "hold";
            Add(new TableEvent { kind = "start", seat = _g.Starter });
            return View(null);
        }

        public SingleView Rescue()
        {
            if (!_g.HumanIsBroke) return View("칩이 남아 있어 구제 칩을 받을 수 없습니다.");
            _g.Rescue();
            _phase = "idle";
            Add(new TableEvent { kind = "rescue", seat = 0, count = _g.RescueChips });
            return View(null);
        }

        public SingleView Reroll(bool[] held)
        {
            var me = _g.Human;
            if (_phase != "hold") return View("지금은 다시 굴릴 수 없습니다.");
            if (held == null || held.Length != me.Dice.Length) return View("고정할 주사위 정보가 잘못됐습니다.");
            if (Array.TrueForAll(held, h => h)) return View("모두 고정하면 다시 굴릴 수 없습니다.");
            if (!_g.CanReroll(me)) return View("다시 굴리기를 다 썼습니다.");
            var idxs = _g.Reroll(me, held);
            Add(new TableEvent { kind = "reroll", seat = 0, idxs = idxs });
            return View(null);
        }

        /// <summary>내 주사위를 확정한다. AI가 다시 굴리고, 베팅이 내 차례가 오거나 끝날 때까지 진행한다.</summary>
        public SingleView Stand()
        {
            if (_phase != "hold") return View("지금은 확정할 수 없습니다.");
            foreach (var p in _g.Players)
            {
                if (!p.IsAi) continue;
                int count = 0;
                while (_g.CanReroll(p))
                {
                    var hold = AiBrain.Hold(p.Dice);
                    if (Array.TrueForAll(hold, h => h)) break;
                    _g.Reroll(p, hold);
                    count++;
                }
                Add(new TableEvent { kind = "airoll", seat = p.Index, count = count });
            }
            _g.BeginBetting();
            _phase = "bet";
            RunAi();
            return View(null);
        }

        public SingleView Bet(string action)
        {
            if (_phase != "bet" || _g.Betting.Current != _g.Human) return View("지금은 내 차례가 아닙니다.");
            BetAction request;
            switch (action)
            {
                case "fold": request = BetAction.Fold; break;
                case "call": request = BetAction.Call; break;
                case "raise": request = BetAction.Raise; break;
                default: return View("알 수 없는 베팅입니다.");
            }
            Apply(request);
            RunAi();
            return View(null);
        }

        // ---------- 진행 ----------

        void RunAi()
        {
            var bet = _g.Betting;
            while (!bet.IsDone && bet.Current.IsAi) Apply(_g.DecideAi(bet.Current));
            if (bet.IsDone) Showdown();
        }

        void Apply(BetAction request)
        {
            var p = _g.Betting.Current;
            var (act, paid) = _g.Betting.Apply(request);
            Add(new TableEvent { kind = "bet", seat = p.Index, action = act.ToString().ToLowerInvariant(), paid = paid });
        }

        void Showdown()
        {
            int pot = _g.Pot;
            var res = _g.ResolveShowdown();
            var winners = new int[res.Winners.Count];
            for (int i = 0; i < winners.Length; i++) winners[i] = res.Winners[i].Index;
            var seats = new int[res.Hands.Count];
            var names = new string[res.Hands.Count];
            for (int i = 0; i < seats.Length; i++)
            {
                seats[i] = res.Hands[i].player.Index;
                names[i] = HandEvaluator.NameOf(res.Hands[i].hand.Rank);
            }
            _result = new ResultView
            {
                uncontested = res.Uncontested,
                winners = winners,
                bestHand = res.Uncontested ? null : HandEvaluator.NameOf(res.BestHand.Rank),
                share = res.Share,
                pot = pot,
                overCap = res.OverCap,
                handSeats = seats,
                handNames = names,
            };
            _phase = "result";
            Add(new TableEvent { kind = "showdown" });
        }

        void Add(TableEvent e) => _events.Add(e);

        // ---------- 상태 ----------

        SingleView View(string error)
        {
            var cfg = _g.Config;
            var me = _g.Human;
            var bet = _g.Betting;
            bool myTurn = _phase == "bet" && bet != null && !bet.IsDone && bet.Current == me;
            var seats = new SeatView[_g.Players.Count];
            for (int i = 0; i < seats.Length; i++)
            {
                var p = _g.Players[i];
                bool show = !p.IsAi || p.Revealed;
                seats[i] = new SeatView
                {
                    name = p.Name,
                    tag = p.Ai?.Tag,
                    ai = p.IsAi,
                    chips = p.Chips,
                    rerolls = p.Rerolls,
                    contrib = p.Contrib,
                    status = p.Status.ToString().ToLowerInvariant(),
                    folded = p.Folded,
                    winner = p.Winner,
                    revealed = p.Revealed,
                    dice = show ? (int[])p.Dice.Clone() : null,
                };
            }
            var view = new SingleView
            {
                error = error,
                table = cfg.Name,
                ante = cfg.Ante,
                raiseTotal = _g.RaiseTotal,
                chipCap = cfg.ChipCap,
                maxRerolls = cfg.MaxRerolls,
                rescueChips = cfg.RescueChips,
                roundNo = _g.RoundNo,
                starter = _g.Starter,
                pot = _g.Pot,
                phase = _phase,
                seats = seats,
                events = _events.ToArray(),
                rerollsLeft = cfg.MaxRerolls - me.Rerolls,
                canReroll = _phase == "hold" && _g.CanReroll(me),
                callCost = myTurn ? bet.CallCost(me) : 0,
                raiseCost = myTurn ? bet.RaiseCost(me) : 0,
                canCall = myTurn && bet.CanCall(me),
                canRaise = myTurn && bet.CanRaise(me),
                raiseUsed = bet != null && bet.RaiseUsed,
                responding = bet != null && bet.Responding,
                result = _phase == "result" ? _result : null,
            };
            _events.Clear();
            return view;
        }
    }
}
