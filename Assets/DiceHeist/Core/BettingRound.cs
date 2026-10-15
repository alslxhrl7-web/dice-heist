using System;
using System.Collections.Generic;

namespace DiceHeist.Core
{
    /// <summary>
    /// 걸기 단계. 선부터 한 번씩 고르고, 올리기가 나오면 차액을 안 낸 사람만 따라가기/포기를 다시 고른다.
    /// dice-heist.html의 betting / applyAct.
    /// </summary>
    public sealed class BettingRound
    {
        readonly DiceGame _game;
        readonly List<PlayerState> _order;
        int _cursor;
        bool _responsePass;

        /// <summary>지금 따라가려면 맞춰야 하는 금액(참가비 제외).</summary>
        public int Level { get; private set; }
        public PlayerState Raiser { get; private set; }
        public bool RaiseUsed => Raiser != null;
        /// <summary>올리기 뒤 차액을 묻는 차례인지.</summary>
        public bool Responding => _responsePass;
        /// <summary>지금 고를 차례인 사람. 걸기가 끝났으면 null.</summary>
        public PlayerState Current { get; private set; }
        public bool IsDone => Current == null;
        public IReadOnlyList<PlayerState> Order => _order;

        internal BettingRound(DiceGame game, int starter)
        {
            _game = game;
            int n = game.Players.Count;
            _order = new List<PlayerState>(n);
            for (int i = 0; i < n; i++) _order.Add(game.Players[(starter + i) % n]);
            Level = game.Ante;
            _cursor = -1;
            Advance();
        }

        public int CallCost(PlayerState p) => Level - p.Contrib;
        /// <summary>기본 크기(참가비 × RaiseMultiplier)로 올릴 때 낼 금액.</summary>
        public int RaiseCost(PlayerState p) => RaiseCostTo(p, _game.RaiseTotal);
        /// <summary>이번 판 누적이 total이 되도록 올릴 때 낼 금액.</summary>
        public int RaiseCostTo(PlayerState p, int total) => total - p.Contrib;
        public bool CanCall(PlayerState p) => p.Chips >= CallCost(p);
        /// <summary>기본 크기로 올릴 수 있는지.</summary>
        public bool CanRaise(PlayerState p) => !RaiseUsed && !_responsePass && p.Chips >= RaiseCost(p);
        /// <summary>p가 고를 수 있는 레이즈 크기의 범위(이번 판 누적). 올릴 수 없으면 max가 min보다 작다.</summary>
        public int MinRaiseFor(PlayerState p) => Math.Max(_game.MinRaiseTotal, Level + _game.Ante);
        public int MaxRaiseFor(PlayerState p) => Math.Min(_game.MaxRaiseTotal, p.Chips + p.Contrib);
        /// <summary>어떤 크기로든 올릴 수 있는지.</summary>
        public bool CanRaiseAny(PlayerState p) => !RaiseUsed && !_responsePass && MaxRaiseFor(p) >= MinRaiseFor(p);

        /// <summary>현재 차례의 선택을 적용한다. 올리기는 기본 크기. 칩이 모자라면 올리기→따라가기→포기로 낮춰진다.</summary>
        public (BetAction action, int paid) Apply(BetAction requested) => Apply(requested, _game.RaiseTotal);

        /// <summary>
        /// 현재 차례의 선택을 적용한다. 올리기면 이번 판 누적이 raiseTo가 되게 올린다(허용 범위로 맞춤).
        /// 칩이 모자라면 올리기→따라가기→포기로 낮춰지고, 실제 적용된 행동과 낸 금액을 돌려준다.
        /// </summary>
        public (BetAction action, int paid) Apply(BetAction requested, int raiseTo)
        {
            var p = Current ?? throw new InvalidOperationException("걸기 단계가 이미 끝났습니다.");
            var act = requested;
            raiseTo = Math.Max(MinRaiseFor(p), Math.Min(_game.MaxRaiseTotal, raiseTo));
            if (act == BetAction.Raise && (RaiseUsed || _responsePass)) act = BetAction.Call;
            if (act == BetAction.Raise && p.Chips < RaiseCostTo(p, raiseTo)) act = BetAction.Call;
            if (act == BetAction.Call && p.Chips < CallCost(p)) act = BetAction.Fold;

            int paid = 0;
            switch (act)
            {
                case BetAction.Fold:
                    p.Folded = true;
                    p.Status = SeatStatus.Folded;
                    break;
                case BetAction.Call:
                    paid = CallCost(p);
                    _game.Pay(p, paid);
                    p.Status = SeatStatus.Called;
                    break;
                case BetAction.Raise:
                    paid = RaiseCostTo(p, raiseTo);
                    _game.Pay(p, paid);
                    p.Status = SeatStatus.Raised;
                    Level = raiseTo;
                    Raiser = p;
                    break;
            }
            Advance();
            return (act, paid);
        }

        void Advance()
        {
            Current = null;
            while (true)
            {
                _cursor++;
                if (_cursor >= _order.Count)
                {
                    if (_responsePass || Raiser == null) return;
                    _responsePass = true;
                    _cursor = 0;
                }
                if (_game.ActiveCount() == 1) return;

                var p = _order[_cursor];
                if (p.Folded) continue;
                if (_responsePass && (p == Raiser || p.Contrib >= Level)) continue;
                Current = p;
                return;
            }
        }
    }
}
