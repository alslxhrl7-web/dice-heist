using DiceHeist.Server.Data;
using NUnit.Framework;

namespace DiceHeist.Server.Tests
{
    /// <summary>DB 단위: 로그인 토큰 정리, 멀티 기록.</summary>
    public class SessionTests
    {
        string _dbPath;

        [SetUp]
        public void SetUp() => _dbPath = Path.Combine(Path.GetTempPath(), $"dh-session-{Guid.NewGuid():N}.db");

        [TearDown]
        public void TearDown()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
                try { File.Delete(f); } catch { }
        }

        [Test]
        public void 기한이_지난_토큰만_지운다()
        {
            var db = new Db(_dbPath);
            long id = db.CreateUser("토끼", "secret123").Value;
            var token = db.CreateSession(id);

            Assert.AreEqual(0, db.DeleteExpiredSessions(DateTimeOffset.UtcNow.AddDays(29)));
            Assert.AreEqual(id, db.UserIdByToken(token));
            Assert.AreEqual(1, db.DeleteExpiredSessions(DateTimeOffset.UtcNow.AddDays(31)));
            Assert.IsNull(db.UserIdByToken(token));
        }

        [Test]
        public void 멀티_기록은_판마다_더하고_최고값만_남긴다()
        {
            var db = new Db(_dbPath);
            long id = db.CreateUser("토끼", "secret123").Value;
            var u = db.GetUser(id);
            Assert.AreEqual((0, 0, -1, 0L), (u.Rounds, u.Wins, u.BestRank, u.BiggestPot), "기록이 없으면 0");

            db.RecordRounds(new[] { new RoundRecord(id, true, 6_000, 4) });
            db.RecordRounds(new[] { new RoundRecord(id, false, 0, 6) });
            db.RecordRounds(new[] { new RoundRecord(id, true, 2_000, -1) });
            u = db.GetUser(id);
            Assert.AreEqual((3, 2, 6, 6_000L), (u.Rounds, u.Wins, u.BestRank, u.BiggestPot));
        }
    }
}
