using System;
using DiceHeist.Core;
using NUnit.Framework;

namespace DiceHeist.Tests
{
    public class WeightedDiceTests
    {
        [Test]
        public void 확률표는_낮은_족보일수록_높고_합은_100퍼센트()
        {
            var w = WeightedDice.RankWeights;
            Assert.AreEqual(8, w.Length);
            int sum = 0;
            for (int r = 0; r < w.Length; r++)
            {
                sum += w[r];
                if (r > 0) Assert.Less(w[r], w[r - 1], $"{(HandRank)r}는 {(HandRank)(r - 1)}보다 드물어야 한다");
            }
            Assert.AreEqual(1000, sum);
        }

        [Test]
        public void 첫_굴림은_확률표대로_나온다()
        {
            const int n = 40000;
            var rng = new SeededRandom(3);
            var d = new int[5];
            var counts = new int[8];
            for (int i = 0; i < n; i++)
            {
                WeightedDice.Roll(d, null, rng);
                counts[(int)HandEvaluator.Evaluate(d).Rank]++;
            }
            for (int r = 0; r < 8; r++)
                Assert.AreEqual(WeightedDice.RankWeights[r] / 1000.0, counts[r] / (double)n, 0.01, ((HandRank)r).ToString());
        }

        [Test]
        public void 고정한_주사위는_그대로이고_나올_수_없는_족보는_안_나온다()
        {
            var rng = new SeededRandom(8);
            var held = new[] { true, true, false, false, false };
            for (int i = 0; i < 2000; i++)
            {
                var d = new[] { 4, 4, 1, 2, 3 };
                WeightedDice.Roll(d, held, rng);
                Assert.AreEqual(4, d[0]);
                Assert.AreEqual(4, d[1]);
                foreach (int v in d) Assert.That(v, Is.InRange(1, 6));
                var rank = HandEvaluator.Evaluate(d).Rank;
                Assert.AreNotEqual(HandRank.NoPair, rank);
                Assert.AreNotEqual(HandRank.Straight, rank);
            }
        }

        [Test]
        public void 브론즈에서는_사람이_다섯_판에_네_판쯤_딴다()
        {
            // 사람도 AI와 같은 기준으로 남기고 항상 따라가는 단순한 방식으로 친다
            const int n = 3000;
            var g = new DiceGame(TableConfig.Bronze, new SeededRandom(2026));
            int won = 0;
            long net = 0;
            for (int round = 0; round < n; round++)
            {
                g.RefillAi();
                g.Human.Chips = 500; // 한도에 막히지 않게 매 판 같은 칩으로
                int before = g.Human.Chips;
                g.StartRound();
                foreach (var p in g.Players)
                {
                    while (g.CanReroll(p))
                    {
                        var h = AiBrain.Hold(p.Dice);
                        if (Array.TrueForAll(h, x => x)) break;
                        g.Reroll(p, h);
                    }
                }
                var bet = g.BeginBetting();
                while (!bet.IsDone) bet.Apply(bet.Current.IsAi ? g.DecideAi(bet.Current) : BetAction.Call);
                if (g.ResolveShowdown().Winners.Contains(g.Human)) won++;
                net += g.Human.Chips - before;
            }
            Assert.That(won / (double)n, Is.InRange(0.74, 0.86), "승률");
            // AI가 쉽게 폴드하지 않아 팟이 커야 한다: 판당 평균 앤티 2배 이상
            Assert.That(net / (double)n, Is.GreaterThan(20), "판당 평균 수익");
        }

        [Test]
        public void 브론즈_AI는_한_번만_다시_굴린다()
        {
            var g = new DiceGame(TableConfig.Bronze, new SeededRandom(5));
            g.StartRound();
            var rac = g.Players[1];
            g.Reroll(rac, new[] { false, false, false, false, false });
            Assert.IsFalse(g.CanReroll(rac));
            Assert.IsTrue(g.CanReroll(g.Human));
        }

        [Test]
        public void 배짱이_1이면_기존_판단과_같다()
        {
            for (int r = 0; r < 8; r++)
            for (double roll = 0.05; roll < 1; roll += 0.1)
            {
                var a = AiBrain.Decide((HandRank)r, AiProfile.Raccoon, false, false, false, new FixedRandom(roll));
                var b = AiBrain.Decide((HandRank)r, AiProfile.Raccoon, false, false, false, new FixedRandom(roll), 1.0);
                Assert.AreEqual(a, b);
            }
        }

        [Test]
        public void 배짱이_낮으면_약한_패로는_겁먹고_포기하지만_스트레이트_이상은_버틴다()
        {
            // 첫 실수 0.05는 원래 판단(올리기), 두 번째 0.5는 버틸지 여부
            Assert.AreEqual(BetAction.Fold, AiBrain.Decide(HandRank.FullHouse, AiProfile.Cat, false, false, false, new FixedRandom(0.05, 0.5), 0.07));
            Assert.AreEqual(BetAction.Raise, AiBrain.Decide(HandRank.FullHouse, AiProfile.Cat, false, false, false, new FixedRandom(0.05, 0.01), 0.07));
            Assert.AreEqual(BetAction.Raise, AiBrain.Decide(HandRank.Straight, AiProfile.Cat, false, false, false, new FixedRandom(0.05, 0.5), 0.07));
        }
    }
}
