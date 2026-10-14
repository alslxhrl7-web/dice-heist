using System;
using System.Collections.Generic;

namespace DiceHeist.Core
{
    public sealed class TableConfig
    {
        public int Ante = 10;
        /// <summary>올리기 금액 = 참가비 × 이 값.</summary>
        public int RaiseMultiplier = 3;
        public int MaxRerolls = 2;
        public int StartChips = 1000;
        public int RescueChips = 100;
        /// <summary>AI가 다시 굴릴 수 있는 횟수. 사람보다 적으면 그만큼 AI가 불리하다.</summary>
        public int AiMaxRerolls = 2;
        /// <summary>AI의 배짱. 1보다 작으면 스트레이트 밑의 패로는 이 확률로만 판에 남고 나머지는 포기한다.</summary>
        public double AiNerve = 1.0;

        /// <summary>
        /// 입문 테이블. 주사위 확률은 모두 같고 상대 AI만 서툴다(다시 굴리기 1회, 쉽게 포기).
        /// 사람이 AI와 같은 기준으로 남기고 따라가기만 해도 다섯 판에 네 판쯤(약 80%) 판돈을 딴다.
        /// </summary>
        public static TableConfig Bronze => new TableConfig { AiMaxRerolls = 1, AiNerve = 0.07 };
    }

    public sealed class ShowdownResult
    {
        public int Pot;
        /// <summary>나머지가 모두 포기해 주사위 공개 없이 끝났는지.</summary>
        public bool Uncontested;
        public List<PlayerState> Winners = new List<PlayerState>();
        public int Share;
        /// <summary>나누고 남은 칩. 첫 번째 승자가 가져간다.</summary>
        public int Remainder;
        public HandValue BestHand;
        /// <summary>공개된 사람들의 족보(강한 순).</summary>
        public List<(PlayerState player, HandValue hand)> Hands = new List<(PlayerState, HandValue)>();
    }

    /// <summary>
    /// 한 테이블의 규칙과 상태. 연출과 입력은 모르고, 호출 순서만 지키면 된다:
    /// StartRound → Reroll(각자 최대 2회) → BeginBetting → Betting.Apply 반복 → ResolveShowdown.
    /// dice-heist.html의 playRound / showdown.
    /// </summary>
    public sealed class DiceGame
    {
        readonly TableConfig _cfg;
        readonly IRandom _rng;
        readonly List<PlayerState> _players = new List<PlayerState>();

        public IReadOnlyList<PlayerState> Players => _players;
        public PlayerState Human => _players[0];
        public int Ante => _cfg.Ante;
        public int RaiseTotal => _cfg.Ante * _cfg.RaiseMultiplier;
        public int MaxRerolls => _cfg.MaxRerolls;
        public int RescueChips => _cfg.RescueChips;

        public int RoundNo { get; private set; }
        /// <summary>이번 판의 선. 판마다 한 자리씩 돌아간다.</summary>
        public int Starter { get; private set; }
        public int Pot { get; private set; }
        public BettingRound Betting { get; private set; }

        public DiceGame(TableConfig cfg, IRandom rng)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _players.Add(new PlayerState(0, "me", "나", cfg.StartChips, null));
            _players.Add(new PlayerState(1, "rac", "너구리", cfg.StartChips, AiProfile.Raccoon));
            _players.Add(new PlayerState(2, "cat", "고양이", cfg.StartChips, AiProfile.Cat));
        }

        public bool HumanIsBroke => Human.Chips < _cfg.Ante;

        public void Rescue() => Human.Chips += _cfg.RescueChips;

        public int ActiveCount()
        {
            int n = 0;
            foreach (var p in _players) if (!p.Folded) n++;
            return n;
        }

        /// <summary>칩이 모자란 AI는 지갑을 새로 채운다. 채운 AI 목록을 돌려준다.</summary>
        public List<PlayerState> RefillAi()
        {
            var refilled = new List<PlayerState>();
            foreach (var p in _players)
            {
                if (!p.IsAi || p.Chips >= _cfg.Ante * 4) continue;
                p.Chips = _cfg.StartChips;
                refilled.Add(p);
            }
            return refilled;
        }

        /// <summary>참가비를 걷고 모두의 주사위 5개를 굴린다.</summary>
        public void StartRound()
        {
            if (HumanIsBroke) throw new InvalidOperationException("참가비가 모자랍니다. Rescue를 먼저 호출하세요.");
            RoundNo++;
            Starter = (RoundNo - 1) % _players.Count;
            Pot = 0;
            Betting = null;
            foreach (var p in _players)
            {
                p.Chips -= _cfg.Ante;
                Pot += _cfg.Ante;
                p.ResetForRound();
            }
            foreach (var p in _players) WeightedDice.Roll(p.Dice, null, _rng);
        }

        public bool CanReroll(PlayerState p) => Betting == null && p.Rerolls < (p.IsAi ? _cfg.AiMaxRerolls : _cfg.MaxRerolls);

        /// <summary>고정하지 않은 주사위를 다시 굴린다. 다시 굴린 자리 번호를 돌려준다.</summary>
        public int[] Reroll(PlayerState p, IReadOnlyList<bool> held)
        {
            if (!CanReroll(p)) throw new InvalidOperationException("더 이상 다시 굴릴 수 없습니다.");
            var idxs = new List<int>();
            for (int i = 0; i < p.Dice.Length; i++) if (!held[i]) idxs.Add(i);
            if (idxs.Count == 0) throw new InvalidOperationException("모두 고정한 상태에서는 다시 굴릴 수 없습니다.");
            WeightedDice.Roll(p.Dice, held, _rng);
            p.Rerolls++;
            return idxs.ToArray();
        }

        public BettingRound BeginBetting()
        {
            Betting = new BettingRound(this, Starter);
            return Betting;
        }

        /// <summary>현재 걸기 상황에서 AI가 고를 행동.</summary>
        public BetAction DecideAi(PlayerState p)
        {
            if (!p.IsAi) throw new ArgumentException("AI 자리가 아닙니다.", nameof(p));
            bool scary = false;
            foreach (var q in _players) if (q != p && !q.Folded && q.Rerolls == 0) scary = true;
            var rank = HandEvaluator.Evaluate(p.Dice).Rank;
            return AiBrain.Decide(rank, p.Ai, Betting.RaiseUsed, Betting.Responding, scary, _rng, _cfg.AiNerve);
        }

        internal void Pay(PlayerState p, int amount)
        {
            p.Chips -= amount;
            p.Contrib += amount;
            Pot += amount;
        }

        public ShowdownResult ResolveShowdown()
        {
            if (Betting == null || !Betting.IsDone) throw new InvalidOperationException("걸기 단계가 끝나지 않았습니다.");
            var res = new ShowdownResult { Pot = Pot };
            var alive = new List<PlayerState>();
            foreach (var p in _players) if (!p.Folded) alive.Add(p);

            if (alive.Count == 1)
            {
                res.Uncontested = true;
                res.Winners.Add(alive[0]);
                res.Share = Pot;
            }
            else
            {
                foreach (var p in alive)
                {
                    p.Revealed = true;
                    res.Hands.Add((p, HandEvaluator.Evaluate(p.Dice)));
                }
                // List.Sort는 불안정 정렬이라, 동점이면 자리 순서를 유지하도록 직접 비교한다
                res.Hands.Sort((a, b) =>
                {
                    int c = HandEvaluator.Compare(b.hand, a.hand);
                    return c != 0 ? c : a.player.Index - b.player.Index;
                });
                res.BestHand = res.Hands[0].hand;
                foreach (var h in res.Hands)
                    if (HandEvaluator.Compare(h.hand, res.BestHand) == 0) res.Winners.Add(h.player);
                res.Share = Pot / res.Winners.Count;
                res.Remainder = Pot - res.Share * res.Winners.Count;
            }

            for (int i = 0; i < res.Winners.Count; i++)
            {
                res.Winners[i].Chips += res.Share + (i == 0 ? res.Remainder : 0);
                res.Winners[i].Winner = true;
            }
            Pot = 0;
            return res;
        }
    }
}
