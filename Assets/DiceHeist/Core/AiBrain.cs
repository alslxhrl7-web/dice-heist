using System.Collections.Generic;

namespace DiceHeist.Core
{
    public enum BetAction
    {
        Fold,
        Call,
        Raise,
    }

    /// <summary>AI 성향. 족보 확률에는 영향을 주지 않고 베팅 빈도만 바꾼다.</summary>
    public sealed class AiProfile
    {
        public readonly double Bluff;
        public readonly double Loose;
        public readonly string Tag;

        public AiProfile(double bluff, double loose, string tag)
        {
            Bluff = bluff;
            Loose = loose;
            Tag = tag;
        }

        public static AiProfile Raccoon => new AiProfile(0.25, 0.7, "능글맞은 블러퍼");
        public static AiProfile Cat => new AiProfile(0.07, 0.45, "냉정한 계산형");
    }

    /// <summary>dice-heist.html의 straightDraw / aiHold / aiDecide.</summary>
    public static class AiBrain
    {
        public sealed class StraightDrawResult
        {
            public bool[] Held;
            public int Count;
            public bool Open;
        }

        static readonly int[][] StraightTargets =
        {
            new[] { 2, 3, 4, 5, 6 },
            new[] { 1, 2, 3, 4, 5 },
        };

        /// <summary>스트레이트에 4개 이상 걸친 경우 남길 주사위. 없으면 null.</summary>
        public static StraightDrawResult StraightDraw(IReadOnlyList<int> dice)
        {
            StraightDrawResult best = null;
            foreach (var target in StraightTargets)
            {
                var used = new HashSet<int>();
                var held = new bool[dice.Count];
                for (int i = 0; i < dice.Count; i++)
                {
                    int v = dice[i];
                    if (System.Array.IndexOf(target, v) >= 0 && used.Add(v)) held[i] = true;
                }
                if (used.Count < 4 || (best != null && used.Count <= best.Count)) continue;

                var miss = new List<int>();
                foreach (int v in target) if (!used.Contains(v)) miss.Add(v);
                best = new StraightDrawResult
                {
                    Held = held,
                    Count = used.Count,
                    Open = miss.Count == 1 && (miss[0] == target[0] || miss[0] == target[4]),
                };
            }
            return best;
        }

        public static bool[] Hold(IReadOnlyList<int> dice)
        {
            var e = HandEvaluator.Evaluate(dice);
            var c = new int[7];
            foreach (int v in dice) c[v]++;
            var held = new bool[dice.Count];

            switch (e.Rank)
            {
                case HandRank.FiveDice:
                case HandRank.Straight:
                case HandRank.FullHouse:
                    for (int i = 0; i < held.Length; i++) held[i] = true;
                    return held;
                case HandRank.FourDice:
                case HandRank.Triple:
                    for (int i = 0; i < held.Length; i++) held[i] = dice[i] == e.Tiebreak[0];
                    return held;
                case HandRank.TwoPair:
                    for (int i = 0; i < held.Length; i++) held[i] = c[dice[i]] == 2;
                    return held;
            }

            var draw = StraightDraw(dice);
            if (e.Rank == HandRank.OnePair)
            {
                if (draw != null && draw.Open) return draw.Held;
                for (int i = 0; i < held.Length; i++) held[i] = dice[i] == e.Tiebreak[0];
                return held;
            }
            return draw != null ? draw.Held : held;
        }

        /// <param name="scary">한 번도 다시 굴리지 않은 상대가 남아 있으면 true. 조심스러워진다.</param>
        public static BetAction Decide(HandRank rank, AiProfile p, bool raiseUsed, bool responding, bool scary, IRandom rng)
        {
            int r = (int)rank;
            double R = rng.NextDouble();
            double s = scary ? 0.75 : 1.0;

            if (responding)
            {
                if (r >= 3) return BetAction.Call;
                double pc = r == 2 ? p.Loose + 0.25 : r == 1 ? p.Loose * 0.5 : p.Bluff * 0.5;
                return R < pc * s ? BetAction.Call : BetAction.Fold;
            }

            if (r >= 4) return raiseUsed ? BetAction.Call : BetAction.Raise;
            if (!raiseUsed)
            {
                if (r == 3) return R < 0.35 * p.Loose + 0.1 ? BetAction.Raise : BetAction.Call;
                if (r <= 1 && R < p.Bluff) return BetAction.Raise;
                if (r == 2) return BetAction.Call;
                if (r == 1) return R < (p.Loose + 0.3) * s ? BetAction.Call : BetAction.Fold;
                return R < p.Loose * 0.3 ? BetAction.Call : BetAction.Fold;
            }

            if (r == 3) return BetAction.Call;
            if (r == 2) return R < (p.Loose + 0.2) * s ? BetAction.Call : BetAction.Fold;
            if (r == 1) return R < p.Loose * 0.5 * s ? BetAction.Call : BetAction.Fold;
            return R < p.Bluff * 0.4 ? BetAction.Call : BetAction.Fold;
        }
    }
}
