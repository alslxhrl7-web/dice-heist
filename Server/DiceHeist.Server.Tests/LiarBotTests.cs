using DiceHeist.Core;
using DiceHeist.Server.Data;
using DiceHeist.Server.Liar;
using NUnit.Framework;

namespace DiceHeist.Server.Tests
{
    /// <summary>빈자리를 채우는 봇과 빠른 시작.</summary>
    public class LiarBotTests
    {
        DateTimeOffset _now = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
        static readonly LiarTimings T = new LiarTimings();

        static LiarSeat Human(long id) => new LiarSeat
        {
            UserId = id, Name = "사람" + id, Stack = LiarRoom.BuyIn, SavedStack = LiarRoom.BuyIn, ConnectionId = "c" + id,
        };

        /// <summary>시계를 조금씩 넘기며 진행한다. 사람은 차례가 오면 거짓말(못 하면 가장 작게), 레버는 바로.</summary>
        void Run(LiarRoom room, LiarSeat me, Func<bool> until, int maxSteps = 4000)
        {
            for (int i = 0; i < maxSteps && !until(); i++)
            {
                if (room.Phase == "bid" && me.GameIndex == room.Game.Turn && room.Game.Players[me.GameIndex].Alive)
                {
                    if (room.Game.CanChallenge) room.Challenge(me, _now);
                    else { var m = room.Game.MinimumRaise(); room.Bid(me, m.Quantity, m.Face, _now); }
                }
                else if ((room.Phase == "lever" || room.Phase == "reveal") && room.Game.PendingPull == me.GameIndex) room.Pull(me, _now);
                else if (room.Phase == "double" && room.Find(me.UserId) != null && room.Seats.IndexOf(me) == WinnerSeat(room)) room.Cash(me, _now);
                _now += TimeSpan.FromMilliseconds(250);
                room.Tick(_now);
            }
        }

        static int WinnerSeat(LiarRoom room)
        {
            var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(room.StateFor(room.Seats[0], DateTimeOffset.UtcNow)));
            return doc.RootElement.GetProperty("winner").GetInt32();
        }

        [Test]
        public void 혼자_앉아도_봇이_채워서_바로_시작하고_끝까지_간다()
        {
            var room = new LiarRoom("L1", "빠른 시작", T, new SeededRandom(3));
            var me = Human(1);
            room.Sit(me);
            room.Tick(_now);
            Assert.AreEqual(LiarRoom.FillTo, room.Seats.Count, "봇이 4명까지 채운다");
            Assert.AreEqual(3, room.Seats.Count(s => s.IsBot));
            Assert.IsTrue(room.Seats.Where(s => s.IsBot).All(s => s.UserId < 0 && s.Name.StartsWith("봇")), "봇은 음수 id, 이름에 봇");

            Run(room, me, () => room.Phase == "bid");
            Assert.AreEqual(4 * LiarRoom.Ante, room.Pot);
            Run(room, me, () => room.Phase == "result");
            Assert.AreEqual("result", room.Phase, "봇도 부르고·거짓말하고·레버를 당겨 게임이 끝난다");
            Assert.IsEmpty(room.TakeRecords().Where(r => r.UserId < 0), "봇 기록은 DB로 가지 않는다");
        }

        [Test]
        public void 봇은_생각하는_시간을_두고_움직인다()
        {
            var room = new LiarRoom("L1", "빠른 시작", T, new SeededRandom(5));
            var me = Human(1);
            room.Sit(me);
            room.Tick(_now);
            _now += T.Countdown;
            room.Tick(_now);
            Assert.AreEqual("bid", room.Phase);
            // 사람 차례면 사람이 부르고 봇 차례를 만든다
            if (room.Game.Turn == me.GameIndex) { var m = room.Game.MinimumRaise(); room.Bid(me, m.Quantity, m.Face, _now); }
            var bot = room.Seats.First(s => s.GameIndex == room.Game.Turn);
            Assert.IsTrue(bot.IsBot);
            var before = room.Game.Bid;
            _now += TimeSpan.FromSeconds(0.5);
            room.Tick(_now);
            Assert.AreEqual(before, room.Game.Bid, "바로 움직이지 않는다");
            _now += LiarRoom.BotThink;
            room.Tick(_now);
            Assert.IsTrue(!room.Game.Bid.Equals(before) || room.Phase == "reveal", "생각한 뒤에는 부르거나 거짓말");
        }

        [Test]
        public void 사람이_모두_나가면_봇도_사라진다()
        {
            var room = new LiarRoom("L1", "빠른 시작", T, new SeededRandom(1));
            var me = Human(1);
            room.Sit(me);
            room.Tick(_now);
            Assert.AreEqual(3, room.Seats.Count(s => s.IsBot));
            Assert.IsTrue(room.Leave(me, _now), "카운트다운 중이면 바로 일어난다");
            room.Tick(_now);
            Assert.AreEqual(0, room.Seats.Count);
        }

        [Test]
        public void 봇_판단은_말이_안_되는_부르기에_거짓말을_외친다()
        {
            var g = new LiarGame(new[] { "a", "b", "c" }, new SeededRandom(2));
            g.StartRound();
            for (int i = 0; i < 5; i++) { g.Players[1].Dice[i] = 2; } // 봇(1번)은 6이 하나도 없다
            g.Raise(new LiarBid(12, 6)); // 15개 중 6이 12개 이상? 말이 안 된다
            int doubts = 0;
            for (int s = 0; s < 50; s++)
                if (LiarBot.Decide(g, 1, new SeededRandom(s)).challenge) doubts++;
            Assert.Greater(doubts, 40);
        }

        [Test]
        public void 봇_부르기는_늘_규칙에_맞다()
        {
            for (int seed = 0; seed < 200; seed++)
            {
                var rng = new SeededRandom(seed);
                var g = new LiarGame(new[] { "a", "b", "c", "d" }, rng);
                g.StartRound();
                for (int step = 0; step < 30; step++)
                {
                    var (challenge, bid) = LiarBot.Decide(g, g.Turn, rng);
                    if (challenge) { Assert.IsTrue(g.CanChallenge); break; }
                    Assert.IsNull(g.CheckBid(bid), bid.ToString());
                    g.Raise(bid);
                }
            }
        }

        // ---------- 빠른 시작(DB 포함) ----------

        string _dbPath;
        Db _db;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"dh-bot-{Guid.NewGuid():N}.db");
            _db = new Db(_dbPath);
        }

        [TearDown]
        public void TearDown()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
                try { File.Delete(f); } catch { }
        }

        [Test]
        public void 빠른_시작은_빈자리_있는_방에_앉히고_봇은_사람에게_자리를_비켜_준다()
        {
            var mgr = new LiarManager(_db, T, () => _now, new SeededRandom(9));
            var ids = Enumerable.Range(0, 6).Select(i => _db.CreateUser("사람" + i, "secret123").Value).ToList();

            Assert.IsNull(mgr.QuickStart(ids[0], "c0"));
            mgr.Tick();
            var room = mgr.RoomFor(ids[0]);
            Assert.AreEqual(4, room.Seats.Count, "혼자라 봇 3명");

            Assert.IsNull(mgr.QuickStart(ids[1], "c1"));
            Assert.AreSame(room, mgr.RoomFor(ids[1]), "같은 방으로 간다");
            mgr.Tick();
            Assert.AreEqual(4, room.Seats.Count, "사람 2 + 봇 2(채우는 인원은 그대로)");
            Assert.AreEqual(2, room.Seats.Count(s => s.IsBot));

            for (int i = 2; i < 5; i++) Assert.IsNull(mgr.QuickStart(ids[i], "c" + i));
            Assert.AreEqual(5, room.HumanCount, "사람 5명까지 한 방");
            Assert.IsNull(mgr.QuickStart(ids[5], "c5"));
            Assert.AreSame(room, mgr.RoomFor(ids[5]), "꽉 차도 판에 안 낀 봇이 비켜 주면 같은 방");
            Assert.LessOrEqual(room.Seats.Count, LiarRoom.MaxSeats);

            Assert.AreEqual(5 * 100_000L + 100_000L, _db.TotalMoney(), "봇 머니는 DB에 없다");
        }
    }
}
