using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using NUnit.Framework;

namespace DiceHeist.Server.Tests
{
    /// <summary>소개 페이지(/), 멀티 화면(/play/), PC 게임 내려받기.</summary>
    public class SiteTests
    {
        WebApplicationFactory<Program> _factory;
        string _dbPath;
        string _zipPath;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"dh-site-{Guid.NewGuid():N}.db");
            _zipPath = _dbPath + ".zip";
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            {
                b.UseSetting("Db:Path", _dbPath);
                b.UseSetting("Db:BackupDir", _dbPath + "-backups");
                b.UseSetting("Site:WindowsZip", _zipPath);
            });
        }

        [TearDown]
        public void TearDown()
        {
            _factory.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm", _zipPath })
                try { File.Delete(f); } catch { }
            try { Directory.Delete(_dbPath + "-backups", true); } catch { }
        }

        [Test]
        public async Task 첫_화면은_소개_페이지이고_멀티는_play에_있다()
        {
            var http = _factory.CreateClient();
            var home = await http.GetStringAsync("/");
            StringAssert.Contains("한 판의 흐름", home);
            StringAssert.Contains("href=\"/play/\"", home);

            var play = await http.GetStringAsync("/play/");
            StringAssert.Contains("id=\"authForm\"", play);
            Assert.AreEqual(HttpStatusCode.OK, (await http.GetAsync("/play/app.js")).StatusCode);
        }

        [Test]
        public async Task 설정에_테이블_등급과_멀티_규칙이_있다()
        {
            var cfg = JsonDocument.Parse(await _factory.CreateClient().GetStringAsync("/api/config")).RootElement;
            var tiers = cfg.GetProperty("tiers").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToArray();
            CollectionAssert.AreEqual(new[] { "브론즈", "실버", "골드" }, tiers);
            Assert.AreEqual(6, cfg.GetProperty("multi").GetProperty("maxPlayers").GetInt32());
            Assert.AreEqual(50_000, cfg.GetProperty("multi").GetProperty("buyIn").GetInt32());
        }

        [Test]
        public async Task zip이_없으면_내려받기를_숨기고_있으면_내려준다()
        {
            var http = _factory.CreateClient();
            var site = JsonDocument.Parse(await http.GetStringAsync("/api/site")).RootElement;
            Assert.AreEqual(JsonValueKind.Null, site.GetProperty("windows").ValueKind);
            Assert.AreEqual(0, site.GetProperty("rooms").GetInt32());
            Assert.AreEqual(HttpStatusCode.NotFound, (await http.GetAsync("/download/windows")).StatusCode);

            File.WriteAllBytes(_zipPath, new byte[] { 0x50, 0x4b, 5, 6 });
            site = JsonDocument.Parse(await http.GetStringAsync("/api/site")).RootElement;
            Assert.AreEqual(4, site.GetProperty("windows").GetProperty("size").GetInt64());
            var res = await http.GetAsync("/download/windows");
            Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
            Assert.AreEqual("DiceHeist-Windows.zip", res.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
            CollectionAssert.AreEqual(new byte[] { 0x50, 0x4b, 5, 6 }, await res.Content.ReadAsByteArrayAsync());
        }
    }
}
