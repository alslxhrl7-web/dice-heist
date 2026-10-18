using System.Linq;
using DiceHeist.Core;
using NUnit.Framework;

namespace DiceHeist.Tests
{
    /// <summary>함정 의자 라이어 다이스 규칙.</summary>
    public class LiarDiceTests
    {
        static LiarGame Game(int n = 3, int seed = 1)
        {
            var g = new LiarGame(Enumerable.Range(0, n).Select(i => "p" + i).ToList(), new SeededRandom(seed));
            g.StartRound();
            return g;
        }

        static void SetDice(LiarGame g, int player, params int[] dice)
        {
            for (int i = 0; i < dice.Length; i++) g.Players[player].Dice[i] = dice[i];
        }

        [Test]
        public void 부르기는_앞보다_높아야_한다()
        {
            var g = Game();
            Assert.IsNull(g.CheckBid(new LiarBid(2, 3)));
            g.Raise(new LiarBid(2, 3));
            Assert.AreEqual(1, g.Turn);
            Assert.IsNotNull(g.CheckBid(new LiarBid(2, 3)), "같으면 안 된다");
            Assert.IsNotNull(g.CheckBid(new LiarBid(2, 2)), "같은 개수에 작은 눈");
            Assert.IsNotNull(g.CheckBid(new LiarBid(1, 6)), "개수가 적으면 눈이 커도 안 된다");
            Assert.IsNull(g.CheckBid(new LiarBid(2, 4)));
            Assert.IsNull(g.CheckBid(new LiarBid(3, 2)));
            Assert.IsNotNull(g.CheckBid(new LiarBid(4, 1)), "1은 만능이라 부를 수 없다");
            Assert.IsNotNull(g.CheckBid(new LiarBid(16, 6)), "테이블 주사위는 15개");
        }

        [Test]
        public void 일은_만능으로_센다()
        {
            var g = Game();
            SetDice(g, 0, 1, 1, 5, 2, 3);
            SetDice(g, 1, 5, 5, 6, 6, 6);
            SetDice(g, 2, 2, 2, 2, 2, 1);
            Assert.AreEqual(6, g.CountFace(5), "5 셋 + 1 셋");
            Assert.AreEqual(8, g.CountFace(2), "2 다섯 + 1 셋");
        }

        [Test]
        public void 부르기가_맞으면_거짓말을_외친_사람이_진다()
        {
            var g = Game();
            SetDice(g, 0, 5, 5, 2, 2, 3);
            SetDice(g, 1, 6, 6, 6, 3, 3);
            SetDice(g, 2, 1, 4, 4, 4, 2);
            g.Raise(new LiarBid(3, 5)); // 5 둘 + 1 하나 = 3개, 맞다
            var r = g.Challenge();
            Assert.IsTrue(r.BidTrue);
            Assert.AreEqual(3, r.Count);
            Assert.AreEqual(1, r.Loser, "외친 사람");
            Assert.AreEqual(1, g.PendingPull);
            Assert.Throws<System.InvalidOperationException>(() => g.StartRound(), "레버를 당기기 전에는 다음 판이 없다");
        }

        [Test]
        public void 부르기가_틀리면_부른_사람이_진다()
        {
            var g = Game();
            SetDice(g, 0, 5, 2, 2, 2, 3);
            SetDice(g, 1, 6, 6, 6, 3, 3);
            SetDice(g, 2, 4, 4, 4, 4, 2);
            g.Raise(new LiarBid(4, 5)); // 5는 하나뿐
            var r = g.Challenge();
            Assert.IsFalse(r.BidTrue);
            Assert.AreEqual(0, r.Loser);
        }

        [Test]
        public void 레버는_여섯_번_안에_반드시_떨어지고_확률이_오른다()
        {
            for (int seed = 0; seed < 50; seed++)
            {
                var g = Game(2, seed);
                var p = g.Players[0];
                bool fell = false;
                for (int k = 0; k < LiarGame.Chambers && !fell; k++)
                {
                    Assert.AreEqual(1.0 / (6 - k), p.NextFallChance, 1e-9);
                    // 0번이 지게 만든다: 0번이 틀린 부르기를 하고 1번이 외친다
                    if (g.Turn != 0) { g.Raise(g.MinimumRaise()); }
                    for (int i = 0; i < 5; i++) { g.Players[0].Dice[i] = 3; g.Players[1].Dice[i] = 3; }
                    g.Raise(new LiarBid(g.DiceInPlay, 6));
                    var r = g.Challenge();
                    Assert.AreEqual(0, r.Loser);
                    var pull = g.Pull();
                    Assert.AreEqual(k + 1, pull.PullNo);
                    fell = pull.Fell;
                    if (!fell) g.StartRound();
                }
                Assert.IsTrue(fell, "6번째에는 반드시 떨어진다");
                Assert.IsTrue(g.Over);
                Assert.AreEqual(1, g.Winner.Index);
            }
        }

        [Test]
        public void 함정_칸은_고르게_정해진다()
        {
            var counts = new int[7];
            for (int seed = 0; seed < 6000; seed++)
            {
                var g = new LiarGame(new[] { "a", "b" }, new SeededRandom(seed));
                counts[GetTrap(g.Players[0])]++;
            }
            for (int k = 1; k <= 6; k++) Assert.That(counts[k], Is.InRange(850, 1150), $"{k}번째 칸");
        }

        /// <summary>TrapAt은 비밀(internal)이라 테스트 어셈블리에서는 리플렉션으로 읽는다.</summary>
        static int GetTrap(LiarPlayer p) =>
            (int)typeof(LiarPlayer).GetField("TrapAt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(p);

        [Test]
        public void 다음_판은_진_사람부터_부르고_탈락한_사람은_건너뛴다()
        {
            var g = Game(3, 4);
            SetDice(g, 0, 2, 2, 2, 2, 2);
            g.Raise(new LiarBid(15, 6)); // 0번 틀림
            g.Challenge();
            var pull = g.Pull();
            if (!pull.Fell)
            {
                Assert.AreEqual(0, g.Turn, "진 사람이 다음 판 첫 부르기");
                g.StartRound();
                Assert.AreEqual(0, g.Turn);
            }
            else Assert.AreEqual(1, g.Turn, "떨어졌으면 다음 사람");
        }

        [Test]
        public void 나가면_바로_탈락이고_남은_한_명이_이긴다()
        {
            var g = Game(2);
            g.Forfeit(1);
            Assert.IsTrue(g.Over);
            Assert.AreEqual(0, g.Winner.Index);
        }

        [Test]
        public void 첫_부르기에는_거짓말을_외칠_수_없다()
        {
            var g = Game();
            Assert.IsFalse(g.CanChallenge);
            g.Raise(new LiarBid(1, 2));
            Assert.IsTrue(g.CanChallenge);
        }
    }
}
