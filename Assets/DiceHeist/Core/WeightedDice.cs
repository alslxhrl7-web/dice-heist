using System.Collections.Generic;

namespace DiceHeist.Core
{
    /// <summary>
    /// 족보 확률표에 맞춰 굴리는 주사위. 굴릴 때마다 "굴린 뒤 5개가 이루는 족보"를 확률표로 먼저 뽑고,
    /// 그 족보가 되는 눈 조합 가운데 하나를 고르게 고른다. 고정한 주사위 때문에 나올 수 없는 족보는 빼고 뽑는다.
    /// 공정한 주사위는 원페어(46%)가 노페어(6%)보다 흔해서, 낮은 족보일수록 흔하도록 일부러 기울였다.
    /// </summary>
    public static class WeightedDice
    {
        /// <summary>족보별 확률(천분율). 순서는 HandRank: 노페어부터 파이브 다이스까지.</summary>
        public static readonly int[] RankWeights = { 360, 280, 160, 100, 50, 30, 16, 4 };

        static readonly int[] Pow6 = { 1, 6, 36, 216, 1296, 7776 };

        // 5개 눈 조합(6진수 부호) → 족보
        static readonly byte[] RankOf = BuildRankTable();

        static byte[] BuildRankTable()
        {
            var table = new byte[Pow6[5]];
            var d = new int[HandEvaluator.DiceCount];
            for (int code = 0; code < table.Length; code++)
            {
                for (int i = 0, x = code; i < d.Length; i++, x /= 6) d[i] = 1 + x % 6;
                table[code] = (byte)HandEvaluator.Evaluate(d).Rank;
            }
            return table;
        }

        /// <summary>held가 false인 자리만 다시 굴린다. held가 null이면 전부 굴린다.</summary>
        public static void Roll(int[] dice, IReadOnlyList<bool> held, IRandom rng)
        {
            var free = new List<int>(dice.Length);
            int fixedCode = 0;
            for (int i = 0; i < dice.Length; i++)
            {
                if (held == null || !held[i]) free.Add(i);
                else fixedCode += (dice[i] - 1) * Pow6[i];
            }
            if (free.Count == 0) return;

            int combos = Pow6[free.Count];
            var counts = new int[RankWeights.Length];
            for (int c = 0; c < combos; c++) counts[RankOf[Code(fixedCode, free, c)]]++;

            int total = 0;
            for (int r = 0; r < counts.Length; r++) if (counts[r] > 0) total += RankWeights[r];
            int pick = rng.Next(total);
            int rank = 0;
            for (int r = 0; r < counts.Length; r++)
            {
                if (counts[r] == 0) continue;
                if (pick < RankWeights[r]) { rank = r; break; }
                pick -= RankWeights[r];
            }

            int nth = rng.Next(counts[rank]);
            for (int c = 0; c < combos; c++)
            {
                if (RankOf[Code(fixedCode, free, c)] != rank || nth-- > 0) continue;
                for (int k = 0, x = c; k < free.Count; k++, x /= 6) dice[free[k]] = 1 + x % 6;
                return;
            }
        }

        static int Code(int fixedCode, List<int> free, int combo)
        {
            int code = fixedCode;
            for (int k = 0; k < free.Count; k++, combo /= 6) code += combo % 6 * Pow6[free[k]];
            return code;
        }
    }
}
