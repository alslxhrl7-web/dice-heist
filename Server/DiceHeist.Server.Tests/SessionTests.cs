using DiceHeist.Server.Data;
using NUnit.Framework;

namespace DiceHeist.Server.Tests
{
    /// <summary>로그인 토큰: 30일 기한, 기한이 지나면 정리된다.</summary>
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
    }
}
