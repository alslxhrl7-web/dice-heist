using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DiceHeist.Core;
using NUnit.Framework;

namespace DiceHeist.Tests
{
    /// <summary>
    /// 싱글 진행 플레이테스트 시뮬레이션: 브론즈 500칩에서 시작해 실버·골드를 거쳐 골드 한도(2,000칩)까지.
    /// 게임과 똑같이 SingleTable(Start → Reroll → Stand → Bet)로 진행하고, 사람 자리는 정해 둔 플레이 스타일로 고른다.
    /// 결과는 Logs/playtest_sim.md에 표로 남긴다. 오래 걸려서 평소 테스트에서는 빠지고 이름으로 불러야 돈다.
    /// </summary>
    [Explicit("수동 실행: 브론즈→골드 진행 시뮬레이션")]
    public class PlaytestSimulation
    {
        const int Runs = 200;
        const int MaxRounds = 4000;

        enum Style
        {
            /// <summary>남기기는 AI 기준, 베팅은 항상 콜(초보)</summary>
            CallOnly,
            /// <summary>원페어 이상 콜, 트리플 이상 레이즈(기본 3배), 노페어는 상대가 레이즈하면 폴드</summary>
            Basic,
            /// <summary>트리플 5배·풀하우스 이상 10배 레이즈, 투페어 이상 콜, 원페어는 레이즈를 받으면 폴드, 노페어 폴드</summary>
            Aggressive,
        }

        static readonly Dictionary<Style, string> StyleName = new Dictionary<Style, string>
        {
            { Style.CallOnly, "콜만 하는 초보" },
            { Style.Basic, "기본(원페어 콜·트리플 레이즈)" },
            { Style.Aggressive, "공격적(큰 족보 크게 레이즈)" },
        };

        sealed class TierStat
        {
            public int Rounds, Wins, Rescues;
            public long ChipDelta;
        }

        sealed class Run
        {
            public int? BronzeClear, SilverClear, GoldClear; // 그 한도에 처음 닿은 누적 판 수
            public int Drops; // 칩이 모자라 아래 등급으로 내려간 횟수
            public int BossTries; // 보스전에 앉은 횟수
            public int Rounds;
            public int MaxChips;
        }

        static (string action, int raiseTo) Decide(SingleView v, Style style)
        {
            var rank = HandEvaluator.Evaluate(v.seats[0].dice).Rank;
            bool facingRaise = v.raiseUsed || v.responding;
            switch (style)
            {
                case Style.CallOnly:
                    return ("call", 0);
                case Style.Basic:
                    if (rank >= HandRank.Triple && v.canRaise) return ("raise", 0);
                    if (rank == HandRank.NoPair && facingRaise) return ("fold", 0);
                    return ("call", 0);
                default:
                    if (rank >= HandRank.FullHouse && v.canRaise) return ("raise", v.maxRaise);
                    if (rank == HandRank.Triple && v.canRaise) return ("raise", Math.Min(v.maxRaise, v.ante * 5));
                    if (rank >= HandRank.TwoPair) return ("call", 0);
                    if (rank == HandRank.OnePair) return facingRaise ? ("fold", 0) : ("call", 0);
                    return ("fold", 0);
            }
        }

        /// <summary>등급 사이 이동: 한도에 닿으면 위로, 입장 칩 밑으로 떨어지면 아래로.</summary>
        static int NextTier(int tier, int chips)
        {
            var tiers = TableConfig.Tiers;
            if (tier < tiers.Length - 1 && chips >= tiers[tier].ChipCap && tiers[tier + 1].CanEnter(chips)) return tier + 1;
            if (tier > 0 && !tiers[tier].CanEnter(chips)) return tier - 1;
            return tier;
        }

        static Run Simulate(Style style, long seed, TierStat[] stats)
        {
            var rng = new SeededRandom(seed);
            var tiers = TableConfig.Tiers;
            var run = new Run();
            int tier = 0, chips = tiers[0].StartChips;
            var table = new SingleTable(tiers[0], rng, chips);
            void Sit(int t)
            {
                tier = t;
                table = new SingleTable(tiers[t], rng, chips);
                if (tiers[t].BossRounds > 0) run.BossTries++;
            }
            while (run.Rounds < MaxRounds)
            {
                var v = table.Start();
                if (v.phase == "broke")
                {
                    stats[tier].Rescues++;
                    table.Rescue();
                    v = table.Start();
                }
                int before = v.seats[0].chips + v.ante; // 앤티를 내기 전
                // 남기기: AI와 같은 기준
                while (v.phase == "hold" && v.canReroll)
                {
                    var hold = AiBrain.Hold(v.seats[0].dice);
                    if (hold.All(h => h)) break;
                    v = table.Reroll(hold);
                }
                v = table.Stand();
                while (v.phase == "bet")
                {
                    var (action, raiseTo) = Decide(v, style);
                    v = table.Bet(action, raiseTo);
                }
                Assert.AreEqual("result", v.phase, v.error);

                run.Rounds++;
                chips = v.seats[0].chips;
                stats[tier].Rounds++;
                stats[tier].ChipDelta += chips - before;
                if (v.result.winners.Contains(0)) stats[tier].Wins++;
                run.MaxChips = Math.Max(run.MaxChips, chips);

                if (tier == 0 && chips >= tiers[0].ChipCap) run.BronzeClear ??= run.Rounds;
                if (tier == 1 && chips >= tiers[1].ChipCap) run.SilverClear ??= run.Rounds;
                if (tier == 2 && table.BossWon) { run.GoldClear = run.Rounds; break; }

                int next = NextTier(tier, chips);
                if (next != tier)
                {
                    if (next < tier) run.Drops++;
                    Sit(next);
                }
                else if (table.BossOver)
                {
                    // 보스전에 졌다: 칩이 되면 바로 다시 도전, 모자라면 실버로
                    if (tiers[tier].CanEnter(chips)) Sit(tier);
                    else { run.Drops++; Sit(tier - 1); }
                }
            }
            return run;
        }

        static string Pct(int n, int total) => $"{100.0 * n / total:0}%";

        static string Median(IEnumerable<int?> xs)
        {
            var v = xs.Where(x => x.HasValue).Select(x => x.Value).OrderBy(x => x).ToList();
            if (v.Count == 0) return "-";
            int P(double q) => v[Math.Min(v.Count - 1, (int)(q * v.Count))];
            return $"{P(0.5)}판 (빠른 25% {P(0.25)} · 느린 25% {P(0.75)})";
        }

        [Test]
        public void 브론즈부터_골드까지_진행()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 플레이테스트 시뮬레이션: 브론즈 → 실버 → 골드");
            sb.AppendLine();
            sb.AppendLine($"- {DateTime.Now:yyyy-MM-dd HH:mm} · 스타일마다 {Runs}명 · 한 명당 최대 {MaxRounds}판 · 게임과 같은 `SingleTable` 코드");
            var tt = TableConfig.Tiers;
            sb.AppendLine($"- 진행 규칙: 브론즈 {tt[0].StartChips}칩 시작 → 한도({tt[0].ChipCap:N0})에 닿으면 실버(입장 {tt[1].MinChips:N0}), 실버 한도({tt[1].ChipCap:N0})에 닿으면 골드(입장 {tt[2].MinChips:N0}). " +
                (tt[2].BossRounds > 0 ? $"골드는 보스전: 앉은 뒤 {tt[2].BossRounds}판 안에 {tt[2].ChipCap:N0}칩이면 보스 격파(끝), 못 하면 다시 앉아 재도전. " : $"골드 한도({tt[2].ChipCap:N0})에 닿으면 끝. ") +
                "입장 칩 밑으로 떨어지면 한 등급 내려간다. 브론즈에서 바닥나면 구제 칩");
            sb.AppendLine("- 남기기는 모든 스타일이 AI와 같은 기준(`AiBrain.Hold`)");
            sb.AppendLine();

            foreach (Style style in Enum.GetValues(typeof(Style)))
            {
                var stats = new[] { new TierStat(), new TierStat(), new TierStat() };
                var runs = new List<Run>();
                for (int i = 0; i < Runs; i++) runs.Add(Simulate(style, 1000 + i, stats));

                sb.AppendLine($"## {StyleName[style]}");
                sb.AppendLine();
                sb.AppendLine("| 목표 | 도달한 비율 | 걸린 판 수(누적, 중앙값) |");
                sb.AppendLine("|---|---|---|");
                sb.AppendLine($"| 브론즈 한도 {tt[0].ChipCap:N0} | {Pct(runs.Count(r => r.BronzeClear.HasValue), Runs)} | {Median(runs.Select(r => r.BronzeClear))} |");
                sb.AppendLine($"| 실버 한도 {tt[1].ChipCap:N0} | {Pct(runs.Count(r => r.SilverClear.HasValue), Runs)} | {Median(runs.Select(r => r.SilverClear))} |");
                sb.AppendLine($"| 골드 보스 격파 | {Pct(runs.Count(r => r.GoldClear.HasValue), Runs)} | {Median(runs.Select(r => r.GoldClear))} |");
                sb.AppendLine();
                sb.AppendLine("| 테이블 | 판 수 합계 | 사람 승률 | 판당 평균 칩 | 구제 칩 |");
                sb.AppendLine("|---|---|---|---|---|");
                var tiers = TableConfig.Tiers;
                for (int t = 0; t < 3; t++)
                {
                    var s = stats[t];
                    string rate = s.Rounds > 0 ? Pct(s.Wins, s.Rounds) : "-";
                    string avg = s.Rounds > 0 ? $"{(double)s.ChipDelta / s.Rounds:+0.0;-0.0}" : "-";
                    sb.AppendLine($"| {tiers[t].Name} (앤티 {tiers[t].Ante}) | {s.Rounds:N0} | {rate} | {avg} | {s.Rescues} |");
                }
                sb.AppendLine();
                sb.AppendLine($"- 보스전 도전: 한 명당 평균 {runs.Average(r => r.BossTries):0.0}번 · 아래 등급으로 내려간 횟수: 한 명당 평균 {runs.Average(r => r.Drops):0.0}회 · 끝까지 못 간 사람(최대 {MaxRounds}판): {Pct(runs.Count(r => !r.GoldClear.HasValue), Runs)}");
                sb.AppendLine();
            }

            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs"); // Unity와 dotnet test 모두 프로젝트 폴더에서 돈다
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "playtest_sim.md"), sb.ToString(), new UTF8Encoding(false));
            TestContext.WriteLine(sb.ToString());
        }
    }
}
