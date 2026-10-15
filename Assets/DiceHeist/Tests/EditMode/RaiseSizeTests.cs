using DiceHeist.Core;
using NUnit.Framework;

namespace DiceHeist.Tests
{
    /// <summary>사람이 레이즈 크기를 고르는 규칙과, 브론즈에서 모두 500칩으로 시작하는지.</summary>
    public class RaiseSizeTests
    {
        static void SetDice(PlayerState p, string s)
        {
            for (int i = 0; i < 5; i++) p.Dice[i] = s[i] - '0';
        }

        static DiceGame AtBetting(TableConfig cfg = null)
        {
            var g = new DiceGame(cfg ?? TableConfig.Bronze, new SeededRandom(1));
            g.StartRound();
            SetDice(g.Players[0], "66123");
            SetDice(g.Players[1], "55123");
            SetDice(g.Players[2], "44123");
            g.BeginBetting();
            return g;
        }

        [Test]
        public void 브론즈는_사람도_AI도_500칩으로_시작한다()
        {
            var g = new DiceGame(TableConfig.Bronze, new SeededRandom(1));
            foreach (var p in g.Players) Assert.AreEqual(500, p.Chips, p.Name);
        }

        [Test]
        public void 브론즈_AI는_칩이_떨어지면_500칩으로_채운다()
        {
            var g = new DiceGame(TableConfig.Bronze, new SeededRandom(1));
            g.Players[1].Chips = 30;
            g.RefillAi();
            Assert.AreEqual(500, g.Players[1].Chips);
        }

        [Test]
        public void 레이즈_크기는_앤티_2배에서_10배까지_고른다()
        {
            var g = AtBetting();
            var bet = g.Betting;
            var me = g.Players[0];
            Assert.AreEqual(20, bet.MinRaiseFor(me));
            Assert.AreEqual(100, bet.MaxRaiseFor(me));
            Assert.IsTrue(bet.CanRaiseAny(me));

            Assert.AreEqual((BetAction.Raise, 70), bet.Apply(BetAction.Raise, 70));
            Assert.AreEqual(70, bet.Level);
            Assert.AreEqual(70, bet.CallCost(g.Players[1]), "뒤 사람은 고른 크기만큼 맞춰야 한다");
        }

        [Test]
        public void 범위를_넘는_크기는_한도에_맞춘다()
        {
            var g = AtBetting();
            Assert.AreEqual((BetAction.Raise, 100), g.Betting.Apply(BetAction.Raise, 5000));

            var g2 = AtBetting();
            Assert.AreEqual((BetAction.Raise, 20), g2.Betting.Apply(BetAction.Raise, 1));
        }

        [Test]
        public void 크기를_안_고르면_예전처럼_앤티_3배()
        {
            var g = AtBetting();
            Assert.AreEqual((BetAction.Raise, 30), g.Betting.Apply(BetAction.Raise));
            Assert.AreEqual(30, g.Betting.Level);
        }

        [Test]
        public void 가진_칩보다_크게는_못_올린다()
        {
            var g = AtBetting();
            var me = g.Players[0];
            me.Chips = 45;
            Assert.AreEqual(45, g.Betting.MaxRaiseFor(me));
            Assert.AreEqual((BetAction.Call, 10), g.Betting.Apply(BetAction.Raise, 60), "모자라면 콜로 낮춘다");
        }

        [Test]
        public void 앞사람이_콜한_뒤에도_레이즈_크기를_고를_수_있다()
        {
            var g = AtBetting();
            var bet = g.Betting;
            bet.Apply(BetAction.Call);
            var rac = g.Players[1];
            Assert.AreEqual(20, bet.MinRaiseFor(rac));
            Assert.AreEqual((BetAction.Raise, 50), bet.Apply(BetAction.Raise, 50));
            bet.Apply(BetAction.Call);
            Assert.AreSame(g.Players[0], bet.Current);
            Assert.AreEqual(40, bet.CallCost(g.Players[0]), "먼저 콜한 10칩을 빼고 차액 40");
        }

        [Test]
        public void 싱글_테이블_요청으로_레이즈_크기를_보낸다()
        {
            var t = new SingleTable(TableConfig.Bronze, new SeededRandom(5), 500);
            var v = t.Start();
            v = t.Stand();
            // 첫 판은 내가 선이라 처음 고르는 사람이 나다
            Assert.AreEqual("bet", v.phase);
            Assert.IsTrue(v.canRaise);
            Assert.AreEqual(20, v.minRaise);
            Assert.AreEqual(100, v.maxRaise);
            v = t.Bet("raise", 80);
            Assert.IsNull(v.error);
            var mine = System.Array.Find(v.events, e => e.kind == "bet" && e.seat == 0);
            Assert.AreEqual("raise", mine.action);
            Assert.AreEqual(80, mine.paid);
        }

        [Test]
        public void 기본보다_큰_레이즈에는_약한_패로_덜_따라온다()
        {
            // 같은 판을 크기만 바꿔 여러 번: 큰 레이즈일수록 AI가 따라오는 횟수가 줄어야 한다
            int Calls(int raiseTo)
            {
                int calls = 0;
                for (int seed = 0; seed < 400; seed++)
                {
                    var cfg = TableConfig.Gold; // 거꾸로 읽기가 없는 제 실력 AI
                    var g = new DiceGame(cfg, new SeededRandom(seed));
                    g.StartRound();
                    SetDice(g.Players[0], "66123");
                    SetDice(g.Players[1], "55124");
                    SetDice(g.Players[2], "44125");
                    var bet = g.BeginBetting();
                    bet.Apply(BetAction.Raise, raiseTo);
                    while (!bet.IsDone)
                    {
                        var (act, _) = bet.Apply(g.DecideAi(bet.Current));
                        if (act == BetAction.Call) calls++;
                    }
                }
                return calls;
            }

            int small = Calls(90), big = Calls(300);
            Assert.Greater(small, big, $"앤티 3배 {small}회 vs 10배 {big}회");
        }
    }
}
