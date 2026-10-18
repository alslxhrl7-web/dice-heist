using System.Text.Json;
using DiceHeist.Core;
using DiceHeist.Server.Liar;
using NUnit.Framework;

namespace DiceHeist.Server.Tests
{
    /// <summary>함정 의자 라이어 다이스 방 하나를 가짜 시계로 끝까지 진행한다(DB·네트워크 없음).</summary>
    public class LiarRoomTests
    {
        DateTimeOffset _now;
        LiarRoom _room;
        LiarSeat _a, _b, _c;
        static readonly LiarTimings T = new LiarTimings();

        [SetUp]
        public void SetUp()
        {
            _now = new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
            _room = new LiarRoom("L1", "테스트", T, new SeededRandom(7));
            _a = Seat(1, "가");
            _b = Seat(2, "나");
            _c = Seat(3, "다");
            _room.Sit(_a);
            _room.Sit(_b);
            _room.Sit(_c);
        }

        static LiarSeat Seat(long id, string name) => new LiarSeat
        {
            UserId = id, Name = name, Stack = LiarRoom.BuyIn, SavedStack = LiarRoom.BuyIn, ConnectionId = "c" + id,
        };

        List<LiarSeat> Pass(TimeSpan t)
        {
            _now += t;
            return _room.Tick(_now);
        }

        void StartGame()
        {
            Pass(TimeSpan.Zero);
            Assert.AreEqual("countdown", _room.Phase);
            Pass(T.Countdown);
            Assert.AreEqual("bid", _room.Phase);
        }

        JsonElement State(LiarSeat viewer) => JsonDocument.Parse(JsonSerializer.Serialize(_room.StateFor(viewer, _now))).RootElement;

        LiarSeat Turn() => _room.Seats.First(s => s.GameIndex == _room.Game.Turn);

        int TotalStacks() => _room.Seats.Sum(s => s.Stack);

        /// <summary>한 게임을 끝까지: 차례마다 앞 부르기가 있으면 거짓말, 없으면 가장 작게 부른다. 레버는 바로 당긴다.</summary>
        void PlayToWinner()
        {
            for (int guard = 0; guard < 500 && _room.Phase != "double" && _room.Phase != "result"; guard++)
            {
                switch (_room.Phase)
                {
                    case "bid":
                        var t = Turn();
                        if (_room.Game.CanChallenge) Assert.IsNull(_room.Challenge(t, _now));
                        else { var m = _room.Game.MinimumRaise(); Assert.IsNull(_room.Bid(t, m.Quantity, m.Face, _now)); }
                        break;
                    case "reveal":
                    case "lever":
                        var loser = _room.Seats.First(s => s.GameIndex == _room.Game.PendingPull);
                        Assert.IsNull(_room.Pull(loser, _now));
                        break;
                    case "fall":
                        Pass(T.Fall);
                        break;
                    default:
                        Assert.Fail("모르는 단계 " + _room.Phase);
                        break;
                }
            }
        }

        [Test]
        public void 참가비가_판돈이_되고_마지막_생존자가_가져간다()
        {
            int before = TotalStacks();
            StartGame();
            Assert.AreEqual(3 * LiarRoom.Ante, _room.Pot);
            Assert.AreEqual(before - 3 * LiarRoom.Ante, TotalStacks());
            PlayToWinner();
            Assert.AreEqual("double", _room.Phase, "이긴 사람은 더블 오어 낫싱을 고른다");
            Assert.AreEqual(3 * LiarRoom.Ante, _room.Vault);
            var winner = _room.Seats[State(_a).GetProperty("winner").GetInt32()];
            Assert.IsNull(_room.Cash(winner, _now));
            Assert.AreEqual("result", _room.Phase);
            Assert.AreEqual(before, TotalStacks(), "관전 베팅·더블이 없으면 머니 총합은 그대로");
            Assert.AreEqual(1, _room.Game.AliveCount());
        }

        [Test]
        public void 남의_주사위는_컵을_열_때만_보인다()
        {
            StartGame();
            var s = State(_a).GetProperty("seats");
            Assert.AreEqual(JsonValueKind.Array, s[0].GetProperty("dice").ValueKind, "내 주사위");
            Assert.AreEqual(JsonValueKind.Null, s[1].GetProperty("dice").ValueKind);
            var t = Turn();
            var m = _room.Game.MinimumRaise();
            _room.Bid(t, m.Quantity, m.Face, _now);
            Assert.IsNull(_room.Challenge(Turn(), _now));
            Assert.AreEqual("reveal", _room.Phase);
            s = State(_a).GetProperty("seats");
            Assert.AreEqual(JsonValueKind.Array, s[1].GetProperty("dice").ValueKind, "열면 모두 보인다");
            Assert.AreEqual(JsonValueKind.Object, State(_a).GetProperty("reveal").ValueKind);
        }

        [Test]
        public void 차례가_아니면_부를_수_없고_시간이_지나면_자동으로_진행된다()
        {
            StartGame();
            var t = Turn();
            var other = _room.Seats.First(s => s != t);
            Assert.IsNotNull(_room.Bid(other, 1, 2, _now));
            Pass(T.Turn);
            Assert.AreEqual("bid", _room.Phase, "첫 차례 시간 초과는 가장 작게 부른다");
            Assert.IsTrue(_room.Game.CanChallenge);
            Pass(T.Turn);
            Assert.AreEqual("reveal", _room.Phase, "앞 부르기가 있으면 시간 초과는 거짓말");
            Pass(T.Reveal);
            Assert.AreEqual("lever", _room.Phase);
            Pass(T.Lever);
            Assert.AreEqual("fall", _room.Phase, "레버도 시간이 지나면 자동으로 당긴다");
        }

        [Test]
        public void 떨어진_사람은_살아남을_사람에게_관전_베팅을_건다()
        {
            StartGame();
            // 누군가 떨어질 때까지
            for (int guard = 0; guard < 300 && _room.Game.AliveCount() == 3; guard++)
            {
                if (_room.Phase == "bid")
                {
                    var t = Turn();
                    if (_room.Game.CanChallenge) _room.Challenge(t, _now);
                    else { var m = _room.Game.MinimumRaise(); _room.Bid(t, m.Quantity, m.Face, _now); }
                }
                else if (_room.Phase == "reveal" || _room.Phase == "lever") _room.Pull(_room.Seats.First(s => s.GameIndex == _room.Game.PendingPull), _now);
                else if (_room.Phase == "fall") Pass(T.Fall);
            }
            var dead = _room.Seats.First(s => !_room.Game.Players[s.GameIndex].Alive);
            var alive = _room.Seats.First(s => _room.Game.Players[s.GameIndex].Alive);
            Assert.IsNotNull(_room.SideBet(alive, dead.UserId, 1000), "살아 있으면 못 건다");
            Assert.IsNotNull(_room.SideBet(dead, dead.UserId, 1000), "떨어진 사람에게는 못 건다");
            Assert.IsNotNull(_room.SideBet(dead, alive.UserId, 1234), "정해진 금액만");
            int before = dead.Stack;
            Assert.IsNull(_room.SideBet(dead, alive.UserId, 5000));
            Assert.AreEqual(before - 5000, dead.Stack);
            Assert.AreEqual(2, dead.SideOdds, "건 때 생존자 2명 → 2배");
            Assert.IsNotNull(_room.SideBet(dead, alive.UserId, 1000), "게임마다 한 번");

            PlayToWinner();
            var winner = _room.Seats[State(_a).GetProperty("winner").GetInt32()];
            Assert.AreEqual(winner == alive ? before - 5000 + 10000 : before - 5000, dead.Stack, "맞히면 건 금액 × 배당");
        }

        [Test]
        public void 더블_오어_낫싱은_두_배_아니면_0이고_최대_세_번()
        {
            int doubled = 0, busted = 0;
            for (int seed = 0; seed < 40; seed++)
            {
                SetUp();
                _room = new LiarRoom("L1", "테스트", T, new SeededRandom(seed));
                _room.Sit(_a); _room.Sit(_b);
                foreach (var s in new[] { _a, _b }) { s.Stack = LiarRoom.BuyIn; s.GameIndex = -1; }
                StartGame();
                PlayToWinner();
                var w = _room.Seats[State(_a).GetProperty("winner").GetInt32()];
                int vault = _room.Vault;
                int stackBefore = w.Stack;
                int tries = 0;
                while (_room.Phase == "double")
                {
                    Assert.IsNull(_room.Double(w, _now));
                    tries++;
                    Assert.AreEqual("doubleRoll", _room.Phase);
                    Assert.That(_room.Vault, Is.EqualTo(vault * 2).Or.EqualTo(0));
                    if (_room.Vault == 0) { busted++; break; }
                    vault = _room.Vault;
                    doubled++;
                    Pass(T.DoubleRoll);
                }
                Assert.LessOrEqual(tries, LiarRoom.MaxDoubles);
                if (_room.Phase == "doubleRoll") Pass(T.DoubleRoll);
                Assert.AreEqual("result", _room.Phase);
                Assert.AreEqual(stackBefore + _room.Vault, w.Stack, "금고에 남은 만큼 받는다");
            }
            Assert.Greater(doubled, 0);
            Assert.Greater(busted, 0);
        }

        [Test]
        public void 게임_중에_나가면_탈락이고_끝나면_일어난다()
        {
            StartGame();
            Assert.IsFalse(_room.Leave(_c, _now), "게임 중이라 바로 일어나지 않는다");
            Assert.IsFalse(_room.Game.Players[_c.GameIndex].Alive);
            PlayToWinner();
            if (_room.Phase == "double") _room.Cash(_room.Seats[State(_a).GetProperty("winner").GetInt32()], _now);
            var removed = Pass(T.Result);
            Assert.Contains(_c, removed);
            Assert.AreEqual(2, _room.Seats.Count);
        }

        [Test]
        public void 둘만_있다가_한_명이_나가면_남은_사람이_이긴다()
        {
            _room.Leave(_c, _now);
            StartGame();
            _room.Leave(_b, _now);
            Assert.That(_room.Phase, Is.EqualTo("double").Or.EqualTo("result"));
            Assert.AreEqual(_room.Seats.IndexOf(_a), State(_a).GetProperty("winner").GetInt32());
        }
    }
}
