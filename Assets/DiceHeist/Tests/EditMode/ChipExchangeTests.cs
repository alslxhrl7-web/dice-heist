using System;
using DiceHeist.Core;
using NUnit.Framework;

namespace DiceHeist.Tests
{
    public class ChipExchangeTests
    {
        [TestCase(0, 0)]
        [TestCase(100, 0)]
        [TestCase(500, 0)]
        [TestCase(599, 0)]
        [TestCase(600, 100)]
        [TestCase(1000, 500)]
        [TestCase(1550, 1000)]
        public void 처음_500칩_위로_번_칩만_100칩_단위로_바꿀_수_있다(int chips, int expected)
        {
            Assert.AreEqual(expected, ChipExchange.Exchangeable(chips));
        }

        [Test]
        public void 칩_1개는_사이버머니_100()
        {
            var (left, money) = ChipExchange.Exchange(1000, 300);
            Assert.AreEqual(700, left);
            Assert.AreEqual(30000, money);
        }

        [Test]
        public void 남겨야_할_칩을_넘거나_단위가_안_맞으면_못_바꾼다()
        {
            Assert.Throws<InvalidOperationException>(() => ChipExchange.Exchange(1000, 600));
            Assert.Throws<ArgumentException>(() => ChipExchange.Exchange(1000, 150));
            Assert.Throws<ArgumentException>(() => ChipExchange.Exchange(1000, 0));
        }

        [Test]
        public void 구제_칩은_사이버머니가_되지_않는다()
        {
            var g = new DiceGame(TableConfig.Bronze, new SeededRandom(1), 0);
            g.Rescue();
            Assert.AreEqual(0, ChipExchange.Exchangeable(g.Human.Chips));
        }
    }
}
