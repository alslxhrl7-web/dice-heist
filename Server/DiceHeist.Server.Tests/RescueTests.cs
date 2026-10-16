using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DiceHeist.Server.Data;
using DiceHeist.Server.Multi;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace DiceHeist.Server.Tests
{
    /// <summary>멀티 구제 머니: 바이인보다 적으면 하루(한국 시간) 한 번 바이인까지 채운다.</summary>
    public class RescueTests
    {
        string _dbPath;
        Db _db;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"dh-rescue-{Guid.NewGuid():N}.db");
            _db = new Db(_dbPath);
        }

        [TearDown]
        public void TearDown()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
                try { File.Delete(f); } catch { }
        }

        /// <summary>테이블에서 money만큼 잃은 계정을 만든다.</summary>
        long Broke(string name, long lose)
        {
            long id = _db.CreateUser(name, "secret123").Value;
            Assert.IsTrue(_db.BuyIn(id, "r", (int)lose));
            _db.UpdateStack(id, 0);
            _db.CashOut(id);
            return id;
        }

        static DateTimeOffset Kst(int day, int hour) => new DateTimeOffset(2026, 10, day, hour, 0, 0, TimeSpan.FromHours(9));

        [Test]
        public void 바이인까지만_채우고_하루에_한_번이며_한국_자정에_다시_열린다()
        {
            long id = Broke("빈털터리", 90_000); // 머니 10,000
            Assert.IsNull(_db.ClaimRescue(id, Room.BuyIn, Kst(16, 9)));
            Assert.AreEqual(Room.BuyIn, _db.GetUser(id).Money, "50,000까지 채운다");

            _db.BuyIn(id, "r", 50_000); _db.UpdateStack(id, 0); _db.CashOut(id);
            StringAssert.Contains("오늘은 이미", _db.ClaimRescue(id, Room.BuyIn, Kst(16, 23)));
            Assert.IsNull(_db.ClaimRescue(id, Room.BuyIn, Kst(17, 0)), "한국 시간 자정이 지나면 다시 받는다");
            Assert.AreEqual(Room.BuyIn, _db.GetUser(id).Money);
        }

        [Test]
        public void 머니가_넉넉하거나_테이블에_앉아_있으면_받지_못한다()
        {
            long rich = _db.CreateUser("부자", "secret123").Value;
            StringAssert.Contains("적을 때만", _db.ClaimRescue(rich, Room.BuyIn, Kst(16, 9)));

            long seated = _db.CreateUser("앉은이", "secret123").Value;
            _db.BuyIn(seated, "r", 90_000); // 머니 10,000, 테이블에 90,000
            StringAssert.Contains("일어난 뒤", _db.ClaimRescue(seated, Room.BuyIn, Kst(16, 9)));
            Assert.AreEqual(10_000, _db.GetUser(seated).Money);
        }

        [Test]
        public void 받을_수_있는지는_한국_날짜로_가린다()
        {
            var last = Kst(16, 23);
            Assert.IsFalse(Rescue.Available(0, last, 50_000, Kst(16, 23).AddMinutes(59)));
            Assert.IsTrue(Rescue.Available(0, last, 50_000, Kst(17, 0)));
            Assert.IsTrue(Rescue.Available(0, null, 50_000, Kst(16, 0)));
            Assert.IsFalse(Rescue.Available(50_000, null, 50_000, Kst(16, 0)), "바이인만큼 있으면 필요 없다");
        }

        [Test]
        public async Task API로_받으면_프로필에_반영되고_버튼이_사라진다()
        {
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            {
                b.UseSetting("Db:Path", _dbPath + "-api.db");
                b.UseSetting("Db:BackupDir", _dbPath + "-backups");
            });
            var http = factory.CreateClient();
            var reg = await (await http.PostAsJsonAsync("/api/register", new { name = "토끼", password = "secret123" })).Content.ReadFromJsonAsync<JsonElement>();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", reg.GetProperty("token").GetString());
            Assert.IsFalse(reg.GetProperty("profile").GetProperty("canRescue").GetBoolean(), "가입 직후에는 넉넉하다");
            Assert.AreEqual(HttpStatusCode.BadRequest, (await http.PostAsync("/api/rescue", null)).StatusCode);

            var db = factory.Services.GetRequiredService<Db>();
            db.BuyIn(1, "r", 70_000); db.UpdateStack(1, 0); db.CashOut(1);
            var me = await http.GetFromJsonAsync<JsonElement>("/api/me");
            Assert.IsTrue(me.GetProperty("canRescue").GetBoolean());

            var after = await (await http.PostAsync("/api/rescue", null)).Content.ReadFromJsonAsync<JsonElement>();
            Assert.AreEqual(Room.BuyIn, after.GetProperty("money").GetInt64());
            Assert.IsFalse(after.GetProperty("canRescue").GetBoolean());

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var f in new[] { _dbPath + "-api.db", _dbPath + "-api.db-wal", _dbPath + "-api.db-shm" })
                try { File.Delete(f); } catch { }
            try { Directory.Delete(_dbPath + "-backups", true); } catch { }
        }
    }
}
