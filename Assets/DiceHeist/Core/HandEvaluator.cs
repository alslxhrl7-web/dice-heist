using System;
using System.Collections.Generic;

namespace DiceHeist.Core
{
    public enum HandRank
    {
        NoPair = 0,
        OnePair = 1,
        TwoPair = 2,
        Triple = 3,
        FullHouse = 4,
        Straight = 5,
        FourDice = 6,
        FiveDice = 7,
    }

    /// <summary>족보 순위와 동점 비교용 눈 목록. dice-heist.html의 {rank, tb}.</summary>
    public readonly struct HandValue
    {
        public readonly HandRank Rank;
        public readonly int[] Tiebreak;

        public HandValue(HandRank rank, int[] tiebreak)
        {
            Rank = rank;
            Tiebreak = tiebreak;
        }

        public override string ToString() => $"{Rank}[{string.Join(",", Tiebreak)}]";
    }

    public static class HandEvaluator
    {
        public const int DiceCount = 5;

        static readonly string[] KoreanNames =
            { "노페어", "원페어", "투페어", "트리플", "풀하우스", "스트레이트", "포 다이스", "파이브 다이스" };

        public static string NameOf(HandRank rank) => KoreanNames[(int)rank];

        static int[] Counts(IReadOnlyList<int> dice)
        {
            var c = new int[7];
            for (int i = 0; i < dice.Count; i++)
            {
                int v = dice[i];
                if (v < 1 || v > 6) throw new ArgumentOutOfRangeException(nameof(dice), $"주사위 눈은 1~6이어야 합니다: {v}");
                c[v]++;
            }
            return c;
        }

        public static HandValue Evaluate(IReadOnlyList<int> dice)
        {
            if (dice == null || dice.Count != DiceCount) throw new ArgumentException("주사위는 5개여야 합니다.", nameof(dice));
            var c = Counts(dice);

            // 묶음을 개수 내림차순, 같으면 눈 내림차순으로 정렬
            var groups = new List<(int count, int value)>(DiceCount);
            for (int v = 6; v >= 1; v--) if (c[v] > 0) groups.Add((c[v], v));
            groups.Sort((a, b) => a.count != b.count ? b.count - a.count : b.value - a.value);

            var vals = new int[groups.Count];
            for (int i = 0; i < vals.Length; i++) vals[i] = groups[i].value;
            int first = groups[0].count;
            int second = groups.Count > 1 ? groups[1].count : 0;

            if (first == 5) return new HandValue(HandRank.FiveDice, new[] { vals[0] });
            if (first == 4) return new HandValue(HandRank.FourDice, vals);
            if (groups.Count == 5)
            {
                if (c[6] == 0) return new HandValue(HandRank.Straight, new[] { 5 }); // 1-2-3-4-5
                if (c[1] == 0) return new HandValue(HandRank.Straight, new[] { 6 }); // 2-3-4-5-6
                return new HandValue(HandRank.NoPair, vals);
            }
            if (first == 3 && second == 2) return new HandValue(HandRank.FullHouse, vals);
            if (first == 3) return new HandValue(HandRank.Triple, vals);
            if (first == 2 && second == 2) return new HandValue(HandRank.TwoPair, vals);
            return new HandValue(HandRank.OnePair, vals);
        }

        /// <summary>양수면 a 승, 음수면 b 승, 0이면 완전 동점.</summary>
        public static int Compare(HandValue a, HandValue b)
        {
            if (a.Rank != b.Rank) return (int)a.Rank - (int)b.Rank;
            int n = Math.Max(a.Tiebreak.Length, b.Tiebreak.Length);
            for (int i = 0; i < n; i++)
            {
                int x = i < a.Tiebreak.Length ? a.Tiebreak[i] : 0;
                int y = i < b.Tiebreak.Length ? b.Tiebreak[i] : 0;
                if (x != y) return x - y;
            }
            return 0;
        }

        public static int Compare(IReadOnlyList<int> a, IReadOnlyList<int> b) => Compare(Evaluate(a), Evaluate(b));

        /// <summary>족보를 이루는 주사위 표시용 마스크.</summary>
        public static bool[] PartMask(IReadOnlyList<int> dice)
        {
            var rank = Evaluate(dice).Rank;
            var c = Counts(dice);
            var mask = new bool[dice.Count];
            bool all = rank == HandRank.Straight || rank == HandRank.FiveDice;
            for (int i = 0; i < mask.Length; i++) mask[i] = all || c[dice[i]] >= 2;
            return mask;
        }
    }
}
