using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DiceHeist.Core;
using DiceHeist.Server.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace DiceHeist.Server.Tests
{
    /// <summary>Unity가 부르는 싱글 API를 HTTP로 직접 불러 한 판씩 둔다.</summary>
    public class SingleApiTests
    {
        WebApplicationFactory<Program> _factory;
        string _dbPath;
        HttpClient _http;

        [SetUp]
        public async Task SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"dh-single-{Guid.NewGuid():N}.db");
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            {
                b.UseSetting("Db:Path", _dbPath);
                b.UseSetting("Db:BackupDir", _dbPath + "-backups"); // 테스트끼리 백업 이름이 겹치지 않게
            });
            _http = _factory.CreateClient();
            var res = await _http.PostAsJsonAsync("/api/register", new { name = "싱글러", password = "secret123" });
            var token = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        [TearDown]
        public void TearDown()
        {
            _http.Dispose();
            _factory.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
                try { File.Delete(f); } catch { }
            try { Directory.Delete(_dbPath + "-backups", true); } catch { }
        }

        async Task<JsonElement> Post(string path, object body = null)
        {
            var res = await _http.PostAsJsonAsync(path, body ?? new { });
            Assert.IsTrue(res.IsSuccessStatusCode, path + ": " + res.StatusCode);
            return await res.Content.ReadFromJsonAsync<JsonElement>();
        }

        static string Phase(JsonElement v) => v.GetProperty("phase").GetString();
        static bool HasError(JsonElement v) => v.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String;

        [Test]
        public async Task 브론즈에_앉아_여러_판을_두면_칩이_서버에_저장된다()
        {
            var v = await Post("/api/single/sit", new { table = "브론즈" });
            Assert.IsFalse(HasError(v), v.ToString());
            Assert.AreEqual(500, v.GetProperty("seats")[0].GetProperty("chips").GetInt32());

            for (int round = 0; round < 15; round++)
            {
                v = await Post("/api/single/start");
                if (Phase(v) == "broke") { await Post("/api/single/rescue"); v = await Post("/api/single/start"); }
                Assert.AreEqual("hold", Phase(v), v.ToString());
                Assert.AreEqual(JsonValueKind.Array, v.GetProperty("seats")[0].GetProperty("dice").ValueKind, "내 주사위");
                Assert.AreEqual(JsonValueKind.Null, v.GetProperty("seats")[1].GetProperty("dice").ValueKind, "AI 주사위는 숨김");

                var dice = v.GetProperty("seats")[0].GetProperty("dice").EnumerateArray().Select(x => x.GetInt32()).ToArray();
                var hold = AiBrain.Hold(dice);
                if (!hold.All(h => h)) v = await Post("/api/single/reroll", new { held = hold });
                v = await Post("/api/single/stand");
                while (Phase(v) == "bet")
                    v = await Post("/api/single/bet", new { action = v.GetProperty("canCall").GetBoolean() ? "call" : "fold" });
                Assert.AreEqual("result", Phase(v));

                int chips = v.GetProperty("seats")[0].GetProperty("chips").GetInt32();
                var db = _factory.Services.GetRequiredService<Db>();
                Assert.AreEqual(chips, db.GetUser(1).Chips, "판이 끝나면 칩이 DB에 있다");
            }
        }

        [Test]
        public async Task 칩이_모자라면_실버에_앉을_수_없다()
        {
            var v = await Post("/api/single/sit", new { table = "실버" });
            StringAssert.Contains("700", v.GetProperty("error").GetString());
        }

        [Test]
        public async Task 앉기_전이나_순서가_틀린_요청은_거절된다()
        {
            Assert.IsTrue(HasError(await Post("/api/single/start")), "앉기 전");
            await Post("/api/single/sit", new { table = "브론즈" });
            Assert.IsTrue(HasError(await Post("/api/single/stand")), "시작 전 확정");
        }

        [Test]
        public async Task 로그인하지_않으면_싱글을_쓸_수_없다()
        {
            var anon = _factory.CreateClient();
            var res = await anon.PostAsJsonAsync("/api/single/sit", new { table = "브론즈" });
            Assert.AreEqual(System.Net.HttpStatusCode.Unauthorized, res.StatusCode);
        }

        [Test]
        public async Task 브론즈_한도에_닿으면_멀티가_열린다()
        {
            var db = _factory.Services.GetRequiredService<Db>();
            Assert.IsFalse(db.SaveSingleChips(1, 999, TableConfig.ClearsBronze(999)));
            Assert.IsTrue(db.SaveSingleChips(1, 1000, TableConfig.ClearsBronze(1000)));
            Assert.IsTrue(db.SaveSingleChips(1, 300, TableConfig.ClearsBronze(300)), "한 번 열리면 칩이 줄어도 닫히지 않는다");
            var me = await (await _http.GetAsync("/api/me")).Content.ReadFromJsonAsync<JsonElement>();
            Assert.IsTrue(me.GetProperty("bronzeCleared").GetBoolean());
        }
    }
}
