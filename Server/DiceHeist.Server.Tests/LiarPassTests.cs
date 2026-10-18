using DiceHeist.Core;
using DiceHeist.Server.Liar;
using NUnit.Framework;

namespace DiceHeist.Server.Tests
{
    /// <summary>떠넘기기 카드: 레버를 잡은 사람이 돈을 내고 레버를 남에게 넘긴다.</summary>
    public class LiarPassTests
    {
        DateTimeOffset _now = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
        static readonly LiarTimings T = new LiarTimings { BotFillTo = 0 };
        LiarRoom _room;
        LiarSeat _a, _b, _c;

        static LiarSeat Seat(long id) => new LiarSeat { UserId = id, Name = "사람" + id, Stack = LiarRoom.BuyIn, SavedStack = LiarRoom.BuyIn, ConnectionId = "c" + id };

        [SetUp]
        public void SetUp()
        {
            _room = new LiarRoom("L1", "테스트", T, new SeededRandom(11));
            _a = Seat(1); _b = Seat(2); _c = Seat(3);
            _room.Sit(_a); _room.Sit(_b); _room.Sit(_c);
            _room.Tick(_now);
            _now += T.Countdown;
            _room.Tick(_now);
        }

        /// <summary>누군가 레버를 잡을 때까지(부르고 → 거짓말 → 컵 열기 4초).</summary>
        LiarSeat ToLever()
        {
            var t = _room.Seats.First(s => s.GameIndex == _room.Game.Turn);
            var m = _room.Game.MinimumRaise();
            _room.Bid(t, m.Quantity, m.Face, _now);
            var next = _room.Seats.First(s => s.GameIndex == _room.Game.Turn);
            _room.Challenge(next, _now);
            _now += T.Reveal;
            _room.Tick(_now);
            Assert.AreEqual("lever", _room.Phase);
            return _room.Seats.First(s => s.GameIndex == _room.Game.PendingPull);
        }

        [Test]
        public void 돈을_내고_레버를_넘기면_판돈이_늘고_넘겨받은_사람이_당긴다()
        {
            var holder = ToLever();
            var target = _room.Seats.First(s => s != holder);
            int pot = _room.Pot, stack = holder.Stack;
            Assert.IsNull(_room.Pass(holder, target.UserId, _now));
            Assert.AreEqual(stack - LiarRoom.PassBaseCost, holder.Stack);
            Assert.AreEqual(pot + LiarRoom.PassBaseCost, _room.Pot, "낸 돈은 판돈으로");
            Assert.AreEqual(target.GameIndex, _room.Game.PendingPull);
            Assert.IsNotNull(_room.Pull(holder, _now), "넘긴 사람은 이제 당길 수 없다");
            int pulls = _room.Game.Players[target.GameIndex].Pulls;
            Assert.IsNull(_room.Pull(target, _now));
            Assert.AreEqual(pulls + 1, _room.Game.Players[target.GameIndex].Pulls, "넘겨받은 사람의 레버가 한 칸 간다");
        }

        [Test]
        public void 같은_레버를_또_넘기면_비용이_두_배고_한_사람은_한_번만()
        {
            var first = ToLever();
            var second = _room.Seats.First(s => s != first);
            var third = _room.Seats.First(s => s != first && s != second);
            Assert.IsNull(_room.Pass(first, second.UserId, _now));
            Assert.AreEqual(2 * LiarRoom.PassBaseCost, _room.PassCost);
            int stack = second.Stack;
            Assert.IsNull(_room.Pass(second, third.UserId, _now), "넘겨받은 사람도 다시 넘길 수 있다");
            Assert.AreEqual(stack - 2 * LiarRoom.PassBaseCost, second.Stack);
            Assert.IsNull(_room.Pass(third, first.UserId, _now), "처음 넘긴 사람에게 되돌려 줄 수도 있다");
            Assert.IsNotNull(_room.Pass(first, second.UserId, _now), "처음 넘긴 사람은 이번 레버에서 다시 못 넘긴다");
            Assert.AreEqual(first.GameIndex, _room.Game.PendingPull);
        }

        [Test]
        public void 넘기면_레버_시간이_새로_시작한다()
        {
            var holder = ToLever();
            _now += TimeSpan.FromSeconds(10);
            var target = _room.Seats.First(s => s != holder);
            _room.Pass(holder, target.UserId, _now);
            _now += TimeSpan.FromSeconds(5);
            _room.Tick(_now);
            Assert.AreEqual("lever", _room.Phase, "넘겨받은 사람에게도 시간이 온전히 주어진다");
        }

        [Test]
        public void 돈이_모자라거나_떨어진_사람에게는_못_넘긴다()
        {
            var holder = ToLever();
            var target = _room.Seats.First(s => s != holder);
            holder.Stack = 9_999;
            Assert.IsNotNull(_room.Pass(holder, target.UserId, _now), "1만이 없다");
            holder.Stack = LiarRoom.BuyIn;
            Assert.IsNotNull(_room.Pass(holder, holder.UserId, _now), "자기에게");
            Assert.IsNotNull(_room.Pass(holder, 999, _now), "없는 사람");
        }

        [Test]
        public void 다음_레버에서는_비용이_처음으로_돌아간다()
        {
            var holder = ToLever();
            _room.Pass(holder, _room.Seats.First(s => s != holder).UserId, _now);
            _room.Pull(_room.Seats.First(s => s.GameIndex == _room.Game.PendingPull), _now);
            Assert.AreEqual(LiarRoom.PassBaseCost, _room.PassCost);
        }

        [Test]
        public void 봇은_확률이_높으면_떨어질_확률이_가장_높은_사람에게_넘긴다()
        {
            int passes = 0;
            for (int seed = 0; seed < 60; seed++)
            {
                var room = new LiarRoom("L1", "봇", new LiarTimings(), new SeededRandom(seed));
                var me = Seat(1);
                room.Sit(me);
                var now = _now;
                for (int i = 0; i < 3000 && room.Phase != "result"; i++)
                {
                    if (room.Phase == "bid" && room.Game.Turn == me.GameIndex && room.Game.Players[me.GameIndex].Alive)
                    {
                        if (room.Game.CanChallenge) room.Challenge(me, now); else { var m = room.Game.MinimumRaise(); room.Bid(me, m.Quantity, m.Face, now); }
                    }
                    else if (room.Phase == "lever" && room.Game.PendingPull == me.GameIndex) room.Pull(me, now);
                    now += TimeSpan.FromMilliseconds(250);
                    room.Tick(now);
                }
                passes += CountPasses(room); // 기록은 최근 10줄이라 덜 셀 수 있지만 0보다 크면 된다
            }
            Assert.Greater(passes, 0, "봇도 떠넘기기를 쓴다");
        }

        static int CountPasses(LiarRoom room)
        {
            var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(room.StateFor(room.Seats[0], DateTimeOffset.UtcNow)));
            return doc.RootElement.GetProperty("log").EnumerateArray().Count(l => l.GetString().Contains("떠넘겼다"));
        }
    }
}
