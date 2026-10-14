using System;
using System.Collections.Generic;
using DiceHeist.Core;
using NUnit.Framework;

namespace DiceHeist.Tests
{
    /// <summary>정해 둔 실수만 차례로 내놓는 난수. 주사위는 항상 1.</summary>
    sealed class FixedRandom : IRandom
    {
        readonly Queue<double> _doubles;
        public FixedRandom(params double[] doubles) { _doubles = new Queue<double>(doubles); }
        public int Next(int maxExclusive) => 0;
        public double NextDouble() => _doubles.Count > 0 ? _doubles.Dequeue() : 0.999;
    }

    public class GameFlowTests
    {
        static void SetDice(PlayerState p, string s)
        {
            for (int i = 0; i < 5; i++) p.Dice[i] = s[i] - '0';
        }

        // 규칙 검사는 한도·약한 AI가 없는 기본 테이블(모두 1,000칩)에서 한다
        static DiceGame NewGame(TableConfig cfg = null) => new DiceGame(cfg ?? new TableConfig(), new FixedRandom());

        static DiceGame GameAtBetting(string me, string rac, string cat, TableConfig cfg = null)
        {
            var g = NewGame(cfg);
            g.StartRound();
            SetDice(g.Players[0], me);
            SetDice(g.Players[1], rac);
            SetDice(g.Players[2], cat);
            g.BeginBetting();
            return g;
        }

        [Test]
        public void 참가비는_자동으로_빠지고_선은_판마다_돌아간다()
        {
            var g = NewGame();
            for (int round = 1; round <= 4; round++)
            {
                g.StartRound();
                Assert.AreEqual((round - 1) % 3, g.Starter);
                Assert.AreEqual(30, g.Pot);
                var bet = g.BeginBetting();
                Assert.AreSame(g.Players[g.Starter], bet.Current, "선부터 고른다");
                while (!bet.IsDone) bet.Apply(BetAction.Fold);
                g.ResolveShowdown();
            }
        }

        [Test]
        public void 모두_따라가면_참가비만큼_더_낸다()
        {
            var g = GameAtBetting("66123", "55123", "44123");
            var bet = g.Betting;
            for (int i = 0; i < 3; i++)
            {
                var (act, paid) = bet.Apply(BetAction.Call);
                Assert.AreEqual(BetAction.Call, act);
                Assert.AreEqual(10, paid);
            }
            Assert.IsTrue(bet.IsDone);
            Assert.AreEqual(60, g.Pot);
            foreach (var p in g.Players) Assert.AreEqual(980, p.Chips);
        }

        [Test]
        public void 올리기가_나오면_앞서_따라간_사람은_차액을_낸다()
        {
            var g = GameAtBetting("66123", "55123", "44123");
            var bet = g.Betting;
            var me = g.Players[0]; var rac = g.Players[1]; var cat = g.Players[2];

            bet.Apply(BetAction.Call);
            Assert.AreEqual(30, bet.RaiseCost(rac));
            Assert.AreEqual((BetAction.Raise, 30), bet.Apply(BetAction.Raise));
            Assert.AreSame(rac, bet.Raiser);

            Assert.AreSame(cat, bet.Current);
            Assert.IsFalse(bet.Responding);
            Assert.AreEqual(30, bet.CallCost(cat));
            bet.Apply(BetAction.Call);

            Assert.AreSame(me, bet.Current, "먼저 따라간 사람에게 차액을 묻는다");
            Assert.IsTrue(bet.Responding);
            Assert.AreEqual(20, bet.CallCost(me));
            Assert.AreEqual((BetAction.Call, 20), bet.Apply(BetAction.Call));

            Assert.IsTrue(bet.IsDone);
            Assert.AreEqual(120, g.Pot);
            // 1인당 최대 = 참가비 10 + 올리기 30 = 참가비의 4배
            foreach (var p in g.Players) Assert.AreEqual(960, p.Chips);
        }

        [Test]
        public void 차액을_안_내면_포기()
        {
            var g = GameAtBetting("66123", "55123", "44123");
            var bet = g.Betting;
            bet.Apply(BetAction.Call);
            bet.Apply(BetAction.Raise);
            bet.Apply(BetAction.Fold);
            Assert.AreSame(g.Players[0], bet.Current);
            Assert.AreEqual((BetAction.Fold, 0), bet.Apply(BetAction.Fold));
            Assert.IsTrue(bet.IsDone);
            Assert.AreEqual(980, g.Players[0].Chips, "이미 낸 따라가기 10칩은 돌려받지 않는다");

            var res = g.ResolveShowdown();
            Assert.IsTrue(res.Uncontested);
            Assert.AreSame(g.Players[1], res.Winners[0]);
            Assert.AreEqual(70, res.Pot);
            Assert.AreEqual(960 + 70, g.Players[1].Chips);
        }

        [Test]
        public void 올리기는_판당_한_명만()
        {
            var g = GameAtBetting("66123", "55123", "44123");
            var bet = g.Betting;
            Assert.IsTrue(bet.CanRaise(g.Players[0]));
            bet.Apply(BetAction.Raise);
            Assert.IsFalse(bet.CanRaise(g.Players[1]));
            Assert.AreEqual((BetAction.Call, 30), bet.Apply(BetAction.Raise), "두 번째 올리기는 따라가기로 처리");
            bet.Apply(BetAction.Call);
            Assert.IsTrue(bet.IsDone, "올린 사람과 이미 맞춘 사람에게는 다시 묻지 않는다");
            Assert.AreEqual(120, g.Pot);
        }

        [Test]
        public void 한_명만_남으면_걸기가_바로_끝난다()
        {
            var g = GameAtBetting("12356", "12356", "66666");
            var bet = g.Betting;
            bet.Apply(BetAction.Fold);
            bet.Apply(BetAction.Fold);
            Assert.IsTrue(bet.IsDone, "마지막 남은 사람은 고를 필요가 없다");

            var res = g.ResolveShowdown();
            Assert.IsTrue(res.Uncontested);
            Assert.AreEqual(0, res.Hands.Count, "공개 없이 끝난다");
            Assert.IsFalse(g.Players[2].Revealed);
            Assert.AreEqual(990 + 30, g.Players[2].Chips);
        }

        [Test]
        public void 칩이_모자라면_올리기는_따라가기로_따라가기는_포기로()
        {
            var g = GameAtBetting("66123", "55123", "44123");
            g.Players[0].Chips = 25;
            Assert.IsFalse(g.Betting.CanRaise(g.Players[0]));
            Assert.AreEqual((BetAction.Call, 10), g.Betting.Apply(BetAction.Raise));

            g.Players[1].Chips = 5;
            Assert.IsFalse(g.Betting.CanCall(g.Players[1]));
            Assert.AreEqual((BetAction.Fold, 0), g.Betting.Apply(BetAction.Call));
            Assert.IsTrue(g.Players[1].Folded);
        }

        [Test]
        public void 높은_족보가_판돈을_가져간다()
        {
            var g = GameAtBetting("66123", "23456", "55522");
            while (!g.Betting.IsDone) g.Betting.Apply(BetAction.Call);
            var res = g.ResolveShowdown();

            Assert.IsFalse(res.Uncontested);
            Assert.AreEqual(1, res.Winners.Count);
            Assert.AreSame(g.Players[1], res.Winners[0], "스트레이트 > 풀하우스 > 원페어");
            Assert.AreEqual(HandRank.Straight, res.BestHand.Rank);
            Assert.AreEqual(980 + 60, g.Players[1].Chips);
            Assert.AreEqual(980, g.Players[0].Chips);
            Assert.IsTrue(g.Players[0].Revealed && g.Players[1].Revealed && g.Players[2].Revealed);
            Assert.AreEqual(0, g.Pot);
        }

        [Test]
        public void 포기한_사람은_족보가_높아도_못_받는다()
        {
            var g = GameAtBetting("66666", "44123", "33126");
            g.Betting.Apply(BetAction.Fold);
            g.Betting.Apply(BetAction.Call);
            g.Betting.Apply(BetAction.Call);
            var res = g.ResolveShowdown();
            Assert.AreSame(g.Players[1], res.Winners[0]);
            Assert.IsFalse(g.Players[0].Revealed);
            Assert.AreEqual(990, g.Players[0].Chips);
        }

        [Test]
        public void 완전_동점은_판돈을_나눈다()
        {
            var g = GameAtBetting("66331", "13636", "44123");
            while (!g.Betting.IsDone) g.Betting.Apply(BetAction.Call);
            var res = g.ResolveShowdown();

            Assert.AreEqual(2, res.Winners.Count);
            Assert.AreEqual(30, res.Share);
            Assert.AreEqual(0, res.Remainder);
            Assert.AreEqual(980 + 30, g.Players[0].Chips);
            Assert.AreEqual(980 + 30, g.Players[1].Chips);
            Assert.AreEqual(980, g.Players[2].Chips);
            Assert.IsTrue(g.Players[0].Winner && g.Players[1].Winner && !g.Players[2].Winner);
        }

        [Test]
        public void 셋_다_동점이면_셋이_나눈다()
        {
            var g = GameAtBetting("12345", "54321", "31245");
            while (!g.Betting.IsDone) g.Betting.Apply(BetAction.Call);
            var res = g.ResolveShowdown();
            Assert.AreEqual(3, res.Winners.Count);
            foreach (var p in g.Players) Assert.AreEqual(1000, p.Chips);
        }

        [Test]
        public void 나누고_남은_칩은_앞자리_승자가_가져간다()
        {
            var cfg = new TableConfig { Ante = 5 };
            var g = GameAtBetting("12356", "66331", "13636", cfg);
            g.Betting.Apply(BetAction.Fold);
            g.Betting.Apply(BetAction.Call);
            g.Betting.Apply(BetAction.Call);
            var res = g.ResolveShowdown();

            Assert.AreEqual(25, res.Pot);
            Assert.AreEqual(12, res.Share);
            Assert.AreEqual(1, res.Remainder);
            Assert.AreEqual(990 + 13, g.Players[1].Chips);
            Assert.AreEqual(990 + 12, g.Players[2].Chips);
        }

        [Test]
        public void 판이_끝나도_칩_총량은_그대로()
        {
            var g = new DiceGame(TableConfig.Bronze, new SeededRandom(7));
            for (int round = 0; round < 200; round++)
            {
                if (g.HumanIsBroke) break;
                g.RefillAi(); // 지갑 채우기는 칩이 늘어나는 유일한 경로라 비교 전에 먼저 처리
                int before = g.Players[0].Chips + g.Players[1].Chips + g.Players[2].Chips;
                var res = PlayAutoRound(g);
                int after = g.Players[0].Chips + g.Players[1].Chips + g.Players[2].Chips;
                // 한도를 넘어 못 받은 칩만큼만 줄어든다
                Assert.AreEqual(before, after + res.OverCap);
            }
        }

        [Test]
        public void 다시_굴리기는_최대_2회이고_고정한_주사위는_그대로()
        {
            var g = new DiceGame(TableConfig.Bronze, new SeededRandom(1234));
            g.StartRound();
            var me = g.Human;
            var held = new[] { true, false, true, false, true };
            var before = (int[])me.Dice.Clone();

            var idxs = g.Reroll(me, held);
            CollectionAssert.AreEqual(new[] { 1, 3 }, idxs);
            Assert.AreEqual(before[0], me.Dice[0]);
            Assert.AreEqual(before[2], me.Dice[2]);
            Assert.AreEqual(before[4], me.Dice[4]);
            Assert.AreEqual(1, me.Rerolls);

            Assert.Throws<InvalidOperationException>(() => g.Reroll(me, new[] { true, true, true, true, true }));
            g.Reroll(me, held);
            Assert.AreEqual(2, me.Rerolls);
            Assert.IsFalse(g.CanReroll(me));
            Assert.Throws<InvalidOperationException>(() => g.Reroll(me, held));
        }

        [Test]
        public void 같은_시드면_같은_판이_재현된다()
        {
            Assert.AreEqual(Transcript(20260101), Transcript(20260101));
            Assert.AreNotEqual(Transcript(20260101), Transcript(20260102));
        }

        static string Transcript(long seed)
        {
            var g = new DiceGame(TableConfig.Bronze, new SeededRandom(seed));
            var sb = new System.Text.StringBuilder();
            for (int round = 0; round < 40 && !g.HumanIsBroke; round++)
            {
                PlayAutoRound(g);
                foreach (var p in g.Players) sb.Append(string.Join("", p.Dice)).Append(':').Append(p.Chips).Append(' ');
                sb.Append('|');
            }
            return sb.ToString();
        }

        /// <summary>사람 자리도 AI 홀드 규칙으로 굴리고 항상 따라가는 자동 진행.</summary>
        static ShowdownResult PlayAutoRound(DiceGame g)
        {
            g.RefillAi();
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
            while (!bet.IsDone)
            {
                var p = bet.Current;
                bet.Apply(p.IsAi ? g.DecideAi(p) : BetAction.Call);
            }
            return g.ResolveShowdown();
        }

        [Test]
        public void 바닥나면_구제_칩()
        {
            var g = NewGame();
            g.Human.Chips = 9;
            Assert.IsTrue(g.HumanIsBroke);
            Assert.Throws<InvalidOperationException>(() => g.StartRound());
            g.Rescue();
            Assert.AreEqual(109, g.Human.Chips);
            Assert.IsFalse(g.HumanIsBroke);
        }

        [Test]
        public void AI는_칩이_참가비_4배_밑이면_지갑을_채운다()
        {
            var g = NewGame();
            g.Players[1].Chips = 39;
            g.Players[2].Chips = 40;
            var refilled = g.RefillAi();
            Assert.AreEqual(1, refilled.Count);
            Assert.AreEqual(1000, g.Players[1].Chips);
            Assert.AreEqual(40, g.Players[2].Chips);
        }
    }

    public class AiBrainTests
    {
        static int[] D(string s)
        {
            var d = new int[s.Length];
            for (int i = 0; i < s.Length; i++) d[i] = s[i] - '0';
            return d;
        }

        static bool[] M(string s)
        {
            var m = new bool[s.Length];
            for (int i = 0; i < s.Length; i++) m[i] = s[i] == 'o';
            return m;
        }

        [TestCase("44444", "ooooo", TestName = "홀드: 파이브 다이스는 전부")]
        [TestCase("23456", "ooooo", TestName = "홀드: 스트레이트는 전부")]
        [TestCase("55533", "ooooo", TestName = "홀드: 풀하우스는 전부")]
        [TestCase("66162", "oo.o.", TestName = "홀드: 트리플은 묶음만")]
        [TestCase("26666", ".oooo", TestName = "홀드: 포 다이스는 묶음만")]
        [TestCase("66331", "oooo.", TestName = "홀드: 투페어는 두 묶음")]
        [TestCase("44136", "oo...", TestName = "홀드: 원페어는 묶음만")]
        [TestCase("44356", "o.ooo", TestName = "홀드: 원페어라도 양쪽 열린 스트레이트 드로우면 드로우")]
        [TestCase("12356", ".oooo", TestName = "홀드: 노페어는 스트레이트 4개 드로우")]
        [TestCase("12346", ".oooo", TestName = "홀드: 두 드로우가 같으면 2-6 쪽")]
        public void 홀드(string dice, string expected)
        {
            CollectionAssert.AreEqual(M(expected), AiBrain.Hold(D(dice)));
        }

        static BetAction Decide(HandRank r, AiProfile p, bool raiseUsed, bool responding, bool scary, double roll) =>
            AiBrain.Decide(r, p, raiseUsed, responding, scary, new FixedRandom(roll));

        [Test]
        public void 풀하우스_이상은_올리고_이미_올렸으면_따라간다()
        {
            Assert.AreEqual(BetAction.Raise, Decide(HandRank.FullHouse, AiProfile.Cat, false, false, false, 0.99));
            Assert.AreEqual(BetAction.Call, Decide(HandRank.FiveDice, AiProfile.Cat, true, false, false, 0.99));
        }

        [Test]
        public void 차액_응답은_트리플_이상이면_무조건_따라간다()
        {
            Assert.AreEqual(BetAction.Call, Decide(HandRank.Triple, AiProfile.Cat, true, true, true, 0.99));
            Assert.AreEqual(BetAction.Fold, Decide(HandRank.NoPair, AiProfile.Cat, true, true, false, 0.99));
        }

        [Test]
        public void 너구리는_고양이보다_블러핑이_잦다()
        {
            // 같은 난수 0.2에서 너구리(0.25)는 노페어로 올리고 고양이(0.07)는 포기한다
            Assert.AreEqual(BetAction.Raise, Decide(HandRank.NoPair, AiProfile.Raccoon, false, false, false, 0.2));
            Assert.AreEqual(BetAction.Fold, Decide(HandRank.NoPair, AiProfile.Cat, false, false, false, 0.2));
        }

        [Test]
        public void 한_번도_안_굴린_상대가_있으면_조심스러워진다()
        {
            // 고양이 원페어: 따라갈 확률 0.75 → 상대가 무서우면 0.5625
            Assert.AreEqual(BetAction.Call, Decide(HandRank.OnePair, AiProfile.Cat, false, false, false, 0.6));
            Assert.AreEqual(BetAction.Fold, Decide(HandRank.OnePair, AiProfile.Cat, false, false, true, 0.6));
        }
    }

    public class SeededRandomTests
    {
        [Test]
        public void 같은_시드는_같은_수열()
        {
            var a = new SeededRandom(42);
            var b = new SeededRandom(42);
            for (int i = 0; i < 1000; i++) Assert.AreEqual(a.D6(), b.D6());
        }

        [Test]
        public void 주사위는_1에서_6_사이로_고르게_나온다()
        {
            var rng = new SeededRandom(99);
            var counts = new int[7];
            const int n = 60000;
            for (int i = 0; i < n; i++)
            {
                int v = rng.D6();
                Assert.That(v, Is.InRange(1, 6));
                counts[v]++;
            }
            for (int v = 1; v <= 6; v++) Assert.That(counts[v], Is.InRange(9500, 10500), $"눈 {v}");
        }

        [Test]
        public void 실수는_0_이상_1_미만()
        {
            var rng = new SeededRandom(5);
            for (int i = 0; i < 10000; i++) Assert.That(rng.NextDouble(), Is.InRange(0.0, 1.0).And.LessThan(1.0));
        }
    }
}
