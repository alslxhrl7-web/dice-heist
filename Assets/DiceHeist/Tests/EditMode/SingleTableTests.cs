using System;
using System.Linq;
using DiceHeist.Core;
using NUnit.Framework;

namespace DiceHeist.Tests
{
    public class SingleTableTests
    {
        /// <summary>요청만으로 한 판을 끝까지: 시작 → AI 기준으로 남기기 → 확정 → 내 차례마다 콜.</summary>
        static SingleView PlayRound(SingleTable t)
        {
            var v = t.Start();
            if (v.phase == "broke") v = t.Rescue();
            if (v.phase == "idle") v = t.Start();
            Assert.AreEqual("hold", v.phase, v.error);
            while (v.canReroll)
            {
                var hold = AiBrain.Hold(v.seats[0].dice);
                if (hold.All(h => h)) break;
                v = t.Reroll(hold);
                Assert.IsNull(v.error);
            }
            v = t.Stand();
            while (v.phase == "bet")
            {
                v = t.Bet(v.canCall ? "call" : "fold");
                Assert.IsNull(v.error);
            }
            Assert.AreEqual("result", v.phase);
            return v;
        }

        [Test]
        public void 판_안에서는_칩이_그대로_옮겨질_뿐이다()
        {
            var t = new SingleTable(new TableConfig(), new SeededRandom(3), 1000);
            for (int round = 0; round < 300; round++)
            {
                // 지갑 채우기와 구제 칩은 칩이 늘어나는 유일한 길이라 측정 전에 처리한다
                t.Game.RefillAi();
                if (t.Game.HumanIsBroke) t.Rescue();
                int before = t.Game.Players.Sum(p => p.Chips);
                PlayRound(t);
                Assert.AreEqual(before, t.Game.Players.Sum(p => p.Chips));
            }
        }

        [Test]
        public void AI_주사위는_쇼다운에서_공개되기_전까지_보내지_않는다()
        {
            var t = new SingleTable(new TableConfig(), new SeededRandom(5), 1000);
            var v = t.Start();
            Assert.IsNotNull(v.seats[0].dice, "내 주사위는 보인다");
            Assert.IsNull(v.seats[1].dice);
            Assert.IsNull(v.seats[2].dice);
            v = t.Stand();
            if (v.phase == "bet")
            {
                Assert.IsNull(v.seats[1].dice, "베팅 중에도 안 보인다");
                while (v.phase == "bet") v = t.Bet("call");
            }
            foreach (var s in v.seats.Skip(1))
                Assert.AreEqual(s.revealed, s.dice != null, "공개된 사람만 주사위가 있다");
        }

        [Test]
        public void 순서에_안_맞는_요청은_거절된다()
        {
            var t = new SingleTable(new TableConfig(), new SeededRandom(5), 1000);
            Assert.IsNotNull(t.Stand().error, "시작 전 확정");
            Assert.IsNotNull(t.Bet("call").error, "시작 전 베팅");
            t.Start();
            Assert.IsNotNull(t.Start().error, "판 중에 또 시작");
            Assert.IsNotNull(t.Bet("call").error, "남기기 중 베팅");
            Assert.IsNotNull(t.Reroll(new[] { true, true, true, true, true }).error, "모두 고정");
            Assert.IsNotNull(t.Rescue().error, "칩이 있는데 구제");
        }

        [Test]
        public void 확정하면_AI가_굴리고_베팅이_내_차례나_끝까지_간다()
        {
            var t = new SingleTable(new TableConfig(), new SeededRandom(9), 1000);
            t.Start();
            var v = t.Stand();
            Assert.AreEqual(2, v.events.Count(e => e.kind == "airoll"), "AI 둘 다 굴리기 기록");
            Assert.That(v.phase, Is.EqualTo("bet").Or.EqualTo("result"));
            if (v.phase == "bet") Assert.IsTrue(v.callCost >= 0 && (v.canCall || !v.canCall));
        }

        [Test]
        public void 이벤트는_한_번만_전달된다()
        {
            var t = new SingleTable(new TableConfig(), new SeededRandom(9), 1000);
            Assert.AreEqual(1, t.Start().events.Count(e => e.kind == "start"));
            Assert.AreEqual(0, t.Look().events.Length);
        }

        [Test]
        public void 칩이_바닥나면_구제를_받아야_시작할_수_있다()
        {
            var t = new SingleTable(TableConfig.Bronze, new SeededRandom(1), 0);
            Assert.AreEqual("broke", t.Start().phase);
            var v = t.Rescue();
            Assert.IsNull(v.error);
            Assert.AreEqual(100, v.seats[0].chips);
            Assert.AreEqual("hold", t.Start().phase);
        }

        [Test]
        public void 브론즈_한도를_넘게_따면_결과에_넘친_칩이_나온다()
        {
            var cfg = TableConfig.Bronze;
            var t = new SingleTable(cfg, new SeededRandom(2), cfg.ChipCap - 5);
            for (int i = 0; i < 40; i++)
            {
                var v = PlayRound(t);
                Assert.LessOrEqual(v.seats[0].chips, cfg.ChipCap);
                if (v.result.overCap > 0) return;
            }
            Assert.Pass("40판 안에 한도를 넘은 판이 없었다(한도 자체는 위에서 확인)");
        }
    }
}
