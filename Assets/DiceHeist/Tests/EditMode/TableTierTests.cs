using DiceHeist.Core;
using NUnit.Framework;

namespace DiceHeist.Tests
{
    public class TableTierTests
    {
        static void SetDice(PlayerState p, string s)
        {
            for (int i = 0; i < 5; i++) p.Dice[i] = s[i] - '0';
        }

        /// <summary>사람이 파이브 다이스로 모두 따라가게 해서 판돈을 가져가는 한 판.</summary>
        static ShowdownResult HumanWinsRound(DiceGame g)
        {
            g.StartRound();
            SetDice(g.Players[0], "66666");
            SetDice(g.Players[1], "12456");
            SetDice(g.Players[2], "12356");
            var bet = g.BeginBetting();
            while (!bet.IsDone) bet.Apply(BetAction.Call);
            return g.ResolveShowdown();
        }

        [Test]
        public void 등급별_입장_조건과_한도()
        {
            var t = TableConfig.Tiers;
            Assert.AreEqual(new[] { "브론즈", "실버", "골드" }, new[] { t[0].Name, t[1].Name, t[2].Name });
            Assert.AreEqual(500, TableConfig.Bronze.StartChips);
            Assert.AreEqual(1500, TableConfig.Bronze.ChipCap); // 2026-10-16: 1,000 → 1,500
            Assert.AreEqual(1200, TableConfig.Silver.MinChips);
            Assert.AreEqual(2500, TableConfig.Silver.ChipCap);
            Assert.AreEqual(2000, TableConfig.Gold.MinChips);
            Assert.AreEqual(3000, TableConfig.Gold.ChipCap, "골드 보스전 목표");
            Assert.AreEqual(40, TableConfig.Gold.BossRounds);
            Assert.AreEqual(0, TableConfig.Silver.BossRounds);
        }

        [TestCase(500, true, false, false)]
        [TestCase(1199, true, false, false)]
        [TestCase(1200, true, true, false)]
        [TestCase(1999, true, true, false)]
        [TestCase(2000, true, true, true)]
        [TestCase(5, true, false, false)]
        public void 가진_칩으로_입장할_수_있는_테이블(int chips, bool bronze, bool silver, bool gold)
        {
            Assert.AreEqual(bronze, TableConfig.Bronze.CanEnter(chips), "브론즈");
            Assert.AreEqual(silver, TableConfig.Silver.CanEnter(chips), "실버");
            Assert.AreEqual(gold, TableConfig.Gold.CanEnter(chips), "골드");
        }

        [TestCase(1499, false)]
        [TestCase(1500, true)]
        public void 브론즈_한도에_닿으면_멀티가_열린다(int chips, bool open)
        {
            Assert.AreEqual(open, TableConfig.ClearsBronze(chips));
        }

        [Test]
        public void 브론즈는_500칩으로_시작한다()
        {
            var g = new DiceGame(TableConfig.Bronze, new SeededRandom(1));
            Assert.AreEqual(500, g.Human.Chips);
            Assert.AreEqual(500, g.Players[1].Chips, "AI도 500칩(2026-10-16 사용자 결정)");
        }

        [Test]
        public void 가지고_앉은_칩으로_시작한다()
        {
            var g = new DiceGame(TableConfig.Silver, new SeededRandom(1), 1320);
            Assert.AreEqual(1320, g.Human.Chips);
        }

        [Test]
        public void 한도를_넘게_따면_넘친_만큼은_못_받는다()
        {
            var g = new DiceGame(TableConfig.Bronze, new SeededRandom(1), 1490);
            var res = HumanWinsRound(g); // 앤티 10 + 콜 10씩, 팟 60
            Assert.AreEqual(1500, g.Human.Chips);
            Assert.AreEqual(1490 - 20 + 60 - 1500, res.OverCap);
        }

        [Test]
        public void 한도_밑에서는_다_받는다()
        {
            var g = new DiceGame(TableConfig.Bronze, new SeededRandom(1), 900);
            var res = HumanWinsRound(g);
            Assert.AreEqual(900 - 20 + 60, g.Human.Chips);
            Assert.AreEqual(0, res.OverCap);
        }

        [Test]
        public void 한도_위에서_앉으면_더_벌지는_못해도_줄지는_않는다()
        {
            var g = new DiceGame(TableConfig.Bronze, new SeededRandom(1), 1900);
            HumanWinsRound(g);
            Assert.AreEqual(1900, g.Human.Chips);
        }

        [Test]
        public void 실버와_골드의_한도()
        {
            var s = new DiceGame(TableConfig.Silver, new SeededRandom(1), 2490);
            HumanWinsRound(s);
            Assert.AreEqual(2500, s.Human.Chips);

            var gold = new DiceGame(TableConfig.Gold, new SeededRandom(1), 2990);
            HumanWinsRound(gold);
            Assert.AreEqual(3000, gold.Human.Chips);
        }
        [Test]
        public void 테이블마다_상대_동물과_딜러가_다르고_이름도_따라간다()
        {
            CollectionAssert.AreEqual(new[] { "raccoon", "cat" }, TableConfig.Bronze.Cast);
            CollectionAssert.AreEqual(new[] { "fox", "raccoon" }, TableConfig.Silver.Cast);
            CollectionAssert.AreEqual(new[] { "bear", "fox" }, TableConfig.Gold.Cast, "골드 보스는 곰");
            foreach (var t in TableConfig.Tiers)
            {
                Assert.AreEqual("penguin", t.Dealer);
                var v = new SingleTable(t, new SeededRandom(1), t.MinChips + t.Ante * 10).Look();
                Assert.AreEqual("rabbit", v.seats[0].character);
                Assert.AreEqual(t.Cast[0], v.seats[1].character);
                Assert.AreEqual(Characters.NameOf(t.Cast[1]), v.seats[2].name);
                Assert.AreEqual("penguin", v.dealer);
                foreach (var id in t.Cast) Assert.IsTrue(Characters.IsValid(id), id);
            }
        }
    }
}
