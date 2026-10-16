using DiceHeist.Core;
using NUnit.Framework;

namespace DiceHeist.Tests
{
    /// <summary>골드 보스전: 앉은 뒤 BossRounds판 안에 한도까지 벌어야 보스를 이긴다.</summary>
    public class BossTests
    {
        /// <summary>남기기 없이 확정하고, 베팅은 action으로 끝까지 한 판.</summary>
        static SingleView Round(SingleTable t, string action)
        {
            var v = t.Start();
            if (!string.IsNullOrEmpty(v.error)) return v;
            v = t.Stand();
            while (v.phase == "bet") v = t.Bet(action);
            return v;
        }

        [Test]
        public void 판_수를_다_쓰면_보스전에_지고_그_자리에서는_더_못_한다()
        {
            var cfg = TableConfig.Gold;
            var t = new SingleTable(cfg, new SeededRandom(3), 2000);
            SingleView v = null;
            for (int i = 0; i < cfg.BossRounds; i++)
            {
                v = Round(t, "fold"); // 폴드만 하면 앤티만 잃어 한도에 못 닿는다
                Assert.AreEqual(i + 1, v.bossRound);
                if (i < cfg.BossRounds - 1) Assert.AreEqual("", v.bossResult);
            }
            Assert.AreEqual("lost", v.bossResult);
            Assert.IsTrue(t.BossOver);
            Assert.IsFalse(t.BossWon);
            StringAssert.Contains("보스전이 끝났습니다", t.Start().error);
        }

        [Test]
        public void 판_수_안에_한도에_닿으면_보스를_이긴다()
        {
            // 한도 바로 밑에서 앉아 콜만 해서 한 판이라도 이기면 닿는다. 이기는 시드를 찾아 확인
            bool seen = false;
            for (int seed = 0; seed < 50 && !seen; seed++)
            {
                var t = new SingleTable(TableConfig.Gold, new SeededRandom(seed), 2990);
                for (int i = 0; i < TableConfig.Gold.BossRounds && !t.BossOver; i++)
                {
                    var v = Round(t, "call");
                    if (v.bossResult == "won")
                    {
                        Assert.IsTrue(t.BossWon);
                        Assert.AreEqual(3000, v.seats[0].chips);
                        StringAssert.Contains("보스전이 끝났습니다", t.Start().error, "이긴 자리도 일어나서 다시 앉아야 한다");
                        seen = true;
                    }
                }
            }
            Assert.IsTrue(seen, "50개 시드 중 한 번은 이겨야 한다");
        }

        [Test]
        public void 보스를_이긴_사람은_보스전_없이_한도_없이_앉는다()
        {
            var v = new SingleTable(TableConfig.Gold, new SeededRandom(1), 2500, bossCleared: true).Look();
            Assert.AreEqual(0, v.bossRounds);
            Assert.AreEqual(int.MaxValue, v.chipCap);
            Assert.AreEqual(3000, TableConfig.Gold.ChipCap, "원래 설정은 그대로");
        }

        [Test]
        public void 보스전은_골드에만_있다()
        {
            Assert.AreEqual(0, new SingleTable(TableConfig.Bronze, new SeededRandom(1), 500).Look().bossRounds);
            Assert.AreEqual(0, new SingleTable(TableConfig.Silver, new SeededRandom(1), 1500).Look().bossRounds);
            Assert.AreEqual(40, new SingleTable(TableConfig.Gold, new SeededRandom(1), 2000).Look().bossRounds);
        }
    }
}
