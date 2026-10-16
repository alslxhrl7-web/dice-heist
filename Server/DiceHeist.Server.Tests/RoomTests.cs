using System.Text.Json;
using DiceHeist.Core;
using DiceHeist.Server.Multi;
using NUnit.Framework;

namespace DiceHeist.Server.Tests
{
    /// <summary>방 하나의 진행을 가짜 시계로 확인한다(DB·네트워크 없음).</summary>
    public class RoomTests
    {
        DateTimeOffset _now;
        Room _room;
        RoomSeat _a, _b;

        static readonly MultiTimings T = new MultiTimings
        {
            Countdown = TimeSpan.FromSeconds(3), Hold = TimeSpan.FromSeconds(30), Bet = TimeSpan.FromSeconds(20), Result = TimeSpan.FromSeconds(6),
        };

        [SetUp]
        public void SetUp()
        {
            _now = new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero);
            _room = new Room("1", "테스트", T, new SeededRandom(42));
            _a = Seat(1, "가");
            _b = Seat(2, "나");
            _room.Sit(_a);
            _room.Sit(_b);
        }

        static RoomSeat Seat(long id, string name) => new RoomSeat
        {
            UserId = id, Name = name, Stack = Room.BuyIn, SavedStack = Room.BuyIn, ConnectionId = "c" + id,
        };

        List<RoomSeat> Pass(TimeSpan t)
        {
            _now += t;
            return _room.Tick(_now);
        }

        void StartRound()
        {
            Pass(TimeSpan.Zero);
            Assert.AreEqual("countdown", _room.Phase);
            Pass(T.Countdown);
            Assert.AreEqual("hold", _room.Phase);
        }

        JsonElement State(RoomSeat viewer) =>
            JsonDocument.Parse(JsonSerializer.Serialize(_room.StateFor(viewer, _now))).RootElement;

        JsonElement SeatState(RoomSeat viewer, RoomSeat target) => State(viewer).GetProperty("seats")[_room.Seats.IndexOf(target)];

        RoomSeat CurrentBettor() => _room.Seats.First(s => SeatState(s, s).GetProperty("turn").GetBoolean());

        [Test]
        public void 한_명이면_시작하지_않고_두_명이면_카운트다운_뒤에_시작한다()
        {
            var solo = new Room("2", "혼자", T, new SeededRandom(1));
            solo.Sit(Seat(9, "혼자"));
            solo.Tick(_now);
            Assert.AreEqual("waiting", solo.Phase);

            StartRound();
            Assert.AreEqual(1, _room.RoundNo);
            Assert.AreEqual(Room.BuyIn - _room.Ante, _a.Stack, "앤티가 빠진다");
        }

        [Test]
        public void 레이즈_크기를_고르면_그만큼_올리고_범위_밖은_맞춘다()
        {
            StartRound();
            _room.Stand(_a, _now);
            _room.Stand(_b, _now);
            var first = CurrentBettor();
            var me = State(first).GetProperty("me");
            Assert.IsTrue(me.GetProperty("canRaise").GetBoolean());
            Assert.AreEqual(_room.Ante * 2, me.GetProperty("minRaise").GetInt32());
            Assert.AreEqual(_room.Ante * 10, me.GetProperty("maxRaise").GetInt32());

            long before = first.Stack;
            Assert.IsNull(_room.Bet(first, "raise", _now, _room.Ante * 7));
            Assert.AreEqual(before - _room.Ante * 7, first.Stack, "누적(앤티 제외) 7배가 되게 낸다");

            var other = CurrentBettor();
            var otherMe = State(other).GetProperty("me");
            Assert.AreEqual(0, otherMe.GetProperty("maxRaise").GetInt32(), "레이즈는 판당 한 번");
            Assert.AreEqual(_room.Ante * 7, otherMe.GetProperty("callCost").GetInt32(), "따라가려면 같은 만큼 낸다");
            Assert.IsNull(_room.Bet(other, "call", _now));
            Assert.AreEqual("result", _room.Phase);
            Assert.AreEqual(2 * Room.BuyIn, _a.Stack + _b.Stack, "칩 총량은 그대로");
        }

        [Test]
        public void 한도보다_크게_올리려_하면_최대_크기로_맞춘다()
        {
            StartRound();
            _room.Stand(_a, _now);
            _room.Stand(_b, _now);
            var first = CurrentBettor();
            long before = first.Stack;
            Assert.IsNull(_room.Bet(first, "raise", _now, int.MaxValue));
            Assert.AreEqual(before - _room.Ante * 10, first.Stack, "최대 10배");
        }

        /// <summary>한 판을 아무 조작 없이 시간 초과로만 넘긴다. actor가 있으면 그 사람만 확정·콜한다.</summary>
        void IdleRound(RoomSeat actor = null)
        {
            StartRound();
            if (actor != null) _room.Stand(actor, _now);
            Pass(T.Hold);
            while (_room.Phase == "betting")
            {
                var cur = CurrentBettor();
                if (cur == actor) _room.Bet(cur, "call", _now);
                else Pass(T.Bet);
            }
            Assert.AreEqual("result", _room.Phase);
            Pass(T.Result);
        }

        [Test]
        public void 두_판_연달아_아무것도_안_하면_자리_비움이_되어_앤티를_내지_않는다()
        {
            IdleRound(actor: _a);
            Assert.IsFalse(_b.Away, "한 판은 봐준다");
            IdleRound(actor: _a);
            Assert.IsTrue(_b.Away);
            Assert.IsFalse(_a.Away, "움직인 사람은 그대로");
            Assert.AreEqual(JsonValueKind.True, SeatState(_a, _b).GetProperty("away").ValueKind);

            int stack = _b.Stack;
            Pass(TimeSpan.Zero);
            Pass(T.Countdown);
            Assert.AreEqual("waiting", _room.Phase, "남은 한 명으로는 시작하지 않는다");
            Assert.AreEqual(stack, _b.Stack, "자리 비움 중에는 앤티가 나가지 않는다");

            Assert.IsNull(_room.Back(_b));
            StartRound();
            Assert.IsTrue(_room.InHand(_b), "돌아오면 다시 낀다");
        }

        [Test]
        public void 한_번이라도_움직이면_빈_판_수가_처음부터_다시_센다()
        {
            IdleRound(actor: _a);
            StartRound();
            _room.Stand(_b, _now);
            _room.Stand(_a, _now);
            while (_room.Phase == "betting") _room.Bet(CurrentBettor(), "call", _now);
            Pass(T.Result);
            IdleRound(actor: _a);
            Assert.IsFalse(_b.Away);
        }

        [Test]
        public void 잠깐_쉬기를_누르면_지금_판은_끝까지_하고_다음_판부터_빠진다()
        {
            StartRound();
            Assert.IsNull(_room.SitOut(_a));
            Assert.IsTrue(_room.InHand(_a), "지금 판은 그대로");
            Assert.IsNotNull(_room.SitOut(_a), "두 번은 안 된다");
            _room.Stand(_a, _now); _room.Stand(_b, _now);
            while (_room.Phase == "betting") _room.Bet(CurrentBettor(), "call", _now);
            Pass(T.Result);
            Pass(TimeSpan.Zero); Pass(T.Countdown);
            Assert.AreEqual("waiting", _room.Phase);
            Assert.IsTrue(_a.Away, "움직였어도 스스로 쉰 것은 그대로");
            Assert.IsNotNull(_room.Back(_b), "쉬지 않는 사람은 돌아올 것이 없다");
        }

        [Test]
        public void 남의_주사위는_쇼다운_전까지_보이지_않는다()
        {
            StartRound();
            Assert.AreEqual(JsonValueKind.Array, SeatState(_a, _a).GetProperty("dice").ValueKind, "내 주사위는 보인다");
            Assert.AreEqual(JsonValueKind.Null, SeatState(_a, _b).GetProperty("dice").ValueKind, "남의 주사위는 안 보인다");
            Assert.AreEqual(JsonValueKind.Null, SeatState(_a, _b).GetProperty("hand").ValueKind);
        }

        [Test]
        public void 모두_확정하고_콜하면_쇼다운까지_가고_칩_총량은_그대로()
        {
            StartRound();
            Assert.IsNull(_room.Stand(_a, _now));
            Assert.AreEqual("hold", _room.Phase, "한 명만 확정하면 기다린다");
            Assert.IsNull(_room.Stand(_b, _now));
            Assert.AreEqual("betting", _room.Phase);

            var first = CurrentBettor();
            var other = first == _a ? _b : _a;
            Assert.IsNotNull(_room.Bet(other, "call", _now), "내 차례가 아니면 거절");
            Assert.IsNull(_room.Bet(first, "call", _now));
            Assert.IsNull(_room.Bet(other, "call", _now));
            Assert.AreEqual("result", _room.Phase);
            Assert.AreEqual(2 * Room.BuyIn, _a.Stack + _b.Stack);

            // 쇼다운에서는 상대 주사위가 보인다
            Assert.AreEqual(JsonValueKind.Array, SeatState(_a, _b).GetProperty("dice").ValueKind);

            Pass(T.Result);
            Assert.AreEqual("waiting", _room.Phase);
        }

        [Test]
        public void 굴리기_시간이_지나면_모두_확정되고_베팅_시간이_지나면_폴드()
        {
            StartRound();
            Pass(T.Hold);
            Assert.AreEqual("betting", _room.Phase);
            var first = CurrentBettor();
            Pass(T.Bet);
            Assert.AreEqual("result", _room.Phase, "두 명 중 한 명이 폴드하면 바로 끝난다");
            Assert.IsTrue(_room.Seats.Single(s => s != first).Stack > Room.BuyIn, "남은 사람이 팟을 가져간다");
        }

        [Test]
        public void 다시_굴리기는_두_번까지이고_다_쓰면_자동_확정()
        {
            StartRound();
            var keep = new[] { true, true, false, false, false };
            Assert.IsNull(_room.Reroll(_a, keep, _now));
            Assert.IsNull(_room.Reroll(_a, keep, _now));
            Assert.IsNotNull(_room.Reroll(_a, keep, _now));
            Assert.IsTrue(SeatState(_a, _a).GetProperty("stood").GetBoolean());
        }

        [Test]
        public void 판_중에_나가면_이번_판은_폴드하고_끝나면_일어난다()
        {
            StartRound();
            Assert.IsFalse(_room.Leave(_a, _now), "판 중에는 바로 일어나지 않는다");
            Assert.IsNull(_room.Stand(_b, _now));
            Assert.AreEqual("result", _room.Phase, "나간 사람은 차례에 바로 폴드");
            var removed = Pass(T.Result);
            CollectionAssert.AreEqual(new[] { _a }, removed);
            Assert.AreEqual(1, _room.Seats.Count);
        }

        [Test]
        public void 여섯_명이_한_판을_끝까지_두고_칩은_그대로()
        {
            for (int i = 3; i <= Room.MaxSeats; i++) _room.Sit(Seat(i, "손님" + i));
            Assert.AreEqual(6, _room.Seats.Count);
            StartRound();
            foreach (var s in _room.Seats) Assert.IsNull(_room.Stand(s, _now));
            Assert.AreEqual("betting", _room.Phase);

            // 차례가 온 사람마다 콜한다
            for (int guard = 0; guard < 20 && _room.Phase == "betting"; guard++)
                Assert.IsNull(_room.Bet(CurrentBettor(), "call", _now));
            Assert.AreEqual("result", _room.Phase);
            Assert.AreEqual(6 * Room.BuyIn, _room.Seats.Sum(s => s.Stack));
        }

        [Test]
        public void 판_중에_들어온_사람은_다음_판부터()
        {
            StartRound();
            var late = Seat(9, "늦게온");
            _room.Sit(late);
            Assert.AreEqual(JsonValueKind.False, SeatState(late, late).GetProperty("inHand").ValueKind);
            Assert.IsNotNull(_room.Stand(late, _now), "이번 판에는 조작할 수 없다");
        }

        [Test]
        public void 대기_중에_나가면_바로_일어난다()
        {
            Assert.IsTrue(_room.Leave(_a, _now));
            Assert.AreEqual(1, _room.Seats.Count);
        }

        [Test]
        public void 선은_판마다_돈다()
        {
            StartRound();
            string first = State(_a).GetProperty("seats").EnumerateArray().First(s => s.GetProperty("starter").GetBoolean()).GetProperty("name").GetString();
            Pass(T.Hold);
            Pass(T.Bet);
            Pass(T.Result);
            Pass(TimeSpan.Zero);
            Pass(T.Countdown);
            string second = State(_a).GetProperty("seats").EnumerateArray().First(s => s.GetProperty("starter").GetBoolean()).GetProperty("name").GetString();
            Assert.AreNotEqual(first, second);
        }
    }
}
