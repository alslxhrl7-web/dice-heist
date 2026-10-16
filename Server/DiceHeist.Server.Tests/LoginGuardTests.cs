using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using NUnit.Framework;

namespace DiceHeist.Server.Tests
{
    /// <summary>비밀번호를 계속 틀린 아이디 잠그기, 가입 횟수 제한, 비밀번호 바꾸기.</summary>
    public class LoginGuardTests
    {
        DateTimeOffset _now = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);

        LoginGuard Guard() => new LoginGuard(() => _now) { MaxFailures = 3, Window = TimeSpan.FromMinutes(10), RegistrationsPerHour = 2 };

        [Test]
        public void 정해진_횟수를_틀리면_잠기고_실패가_창_밖으로_나가면_풀린다()
        {
            var g = Guard();
            g.Failed("토끼");
            _now += TimeSpan.FromMinutes(4);
            g.Failed("토끼");
            Assert.IsNull(g.LockedFor("토끼"));
            _now += TimeSpan.FromMinutes(1);
            g.Failed("토끼");
            Assert.AreEqual(TimeSpan.FromMinutes(5), g.LockedFor("토끼"), "첫 실패가 10분 지나면 풀린다");
            Assert.IsNull(g.LockedFor("너구리"), "다른 아이디는 상관없다");

            _now += TimeSpan.FromMinutes(5);
            Assert.IsNull(g.LockedFor("토끼"));
        }

        [Test]
        public void 맞히면_틀린_기록이_지워지고_영문은_대소문자를_가리지_않는다()
        {
            var g = Guard();
            g.Failed("Rabbit");
            g.Failed("rabbit");
            g.Succeeded("RABBIT");
            g.Failed("rabbit");
            g.Failed("rabbit");
            Assert.IsNull(g.LockedFor("rabbit"));
            g.Failed("RaBbIt");
            Assert.IsNotNull(g.LockedFor("rabbit"));
        }

        [Test]
        public void 가입은_한_시간에_정해진_수만_받는다()
        {
            var g = Guard();
            Assert.IsTrue(g.TryRegister());
            Assert.IsTrue(g.TryRegister());
            Assert.IsFalse(g.TryRegister());
            _now += TimeSpan.FromMinutes(61);
            Assert.IsTrue(g.TryRegister());
        }

        // ---------- HTTP ----------

        WebApplicationFactory<Program> _factory;
        string _dbPath;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"dh-guard-{Guid.NewGuid():N}.db");
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            {
                b.UseSetting("Db:Path", _dbPath);
                b.UseSetting("Db:BackupDir", _dbPath + "-backups");
                b.UseSetting("Security:LoginFailures", "3");
                b.UseSetting("Security:RegistrationsPerHour", "2");
            });
        }

        [TearDown]
        public void TearDown()
        {
            _factory.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
                try { File.Delete(f); } catch { }
            try { Directory.Delete(_dbPath + "-backups", true); } catch { }
        }

        static async Task<string> Error(HttpResponseMessage res) =>
            (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString();

        [Test]
        public async Task 비밀번호를_계속_틀리면_맞는_비밀번호로도_잠시_들어오지_못한다()
        {
            var http = _factory.CreateClient();
            Assert.IsTrue((await http.PostAsJsonAsync("/api/register", new { name = "토끼", password = "secret123" })).IsSuccessStatusCode);

            for (int i = 0; i < 3; i++)
                Assert.AreEqual(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/login", new { name = "토끼", password = "wrong" + i })).StatusCode);

            var locked = await http.PostAsJsonAsync("/api/login", new { name = "토끼", password = "secret123" });
            Assert.AreEqual(HttpStatusCode.TooManyRequests, locked.StatusCode);
            StringAssert.Contains("잠시 잠겼습니다", await Error(locked));

            // 없는 아이디는 몇 번을 틀려도 잠그지 않는다(가입 안내가 계속 나온다)
            for (int i = 0; i < 5; i++)
                StringAssert.Contains("가입된 계정이 없습니다", await Error(await http.PostAsJsonAsync("/api/login", new { name = "없는이", password = "whatever" })));
        }

        [Test]
        public async Task 비밀번호를_바꾸면_다른_기기의_로그인은_끊기고_새_비밀번호로_들어온다()
        {
            var phone = _factory.CreateClient();
            var pc = _factory.CreateClient();
            var reg = await (await phone.PostAsJsonAsync("/api/register", new { name = "토끼", password = "secret123" })).Content.ReadFromJsonAsync<JsonElement>();
            phone.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", reg.GetProperty("token").GetString());
            var login = await (await pc.PostAsJsonAsync("/api/login", new { name = "토끼", password = "secret123" })).Content.ReadFromJsonAsync<JsonElement>();
            pc.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", login.GetProperty("token").GetString());

            var wrong = await phone.PostAsJsonAsync("/api/password", new { current = "nope", next = "newpass99" });
            StringAssert.Contains("맞지 않습니다", await Error(wrong));
            StringAssert.Contains("6~64자", await Error(await phone.PostAsJsonAsync("/api/password", new { current = "secret123", next = "123" })));

            Assert.IsTrue((await phone.PostAsJsonAsync("/api/password", new { current = "secret123", next = "newpass99" })).IsSuccessStatusCode);
            Assert.AreEqual(HttpStatusCode.OK, (await phone.GetAsync("/api/me")).StatusCode, "바꾼 기기는 그대로");
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await pc.GetAsync("/api/me")).StatusCode, "다른 기기는 끊긴다");

            var anon = _factory.CreateClient();
            Assert.AreEqual(HttpStatusCode.BadRequest, (await anon.PostAsJsonAsync("/api/login", new { name = "토끼", password = "secret123" })).StatusCode);
            Assert.IsTrue((await anon.PostAsJsonAsync("/api/login", new { name = "토끼", password = "newpass99" })).IsSuccessStatusCode);
        }

        [Test]
        public async Task 비밀번호_바꾸기에서_틀려도_잠금_횟수에_든다()
        {
            var http = _factory.CreateClient();
            var reg = await (await http.PostAsJsonAsync("/api/register", new { name = "토끼", password = "secret123" })).Content.ReadFromJsonAsync<JsonElement>();
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", reg.GetProperty("token").GetString());
            for (int i = 0; i < 3; i++) await http.PostAsJsonAsync("/api/password", new { current = "guess" + i, next = "newpass99" });
            var res = await http.PostAsJsonAsync("/api/password", new { current = "secret123", next = "newpass99" });
            Assert.AreEqual(HttpStatusCode.TooManyRequests, res.StatusCode, "훔친 토큰으로 비밀번호를 맞혀 보는 것도 막는다");
        }

        [Test]
        public async Task 가입이_몰리면_잠시_막는다()
        {
            var http = _factory.CreateClient();
            Assert.IsTrue((await http.PostAsJsonAsync("/api/register", new { name = "하나", password = "secret123" })).IsSuccessStatusCode);
            StringAssert.Contains("이미 있는", await Error(await http.PostAsJsonAsync("/api/register", new { name = "하나", password = "secret123" })), "있는 아이디는 횟수를 쓰지 않는다");
            Assert.IsTrue((await http.PostAsJsonAsync("/api/register", new { name = "두울", password = "secret123" })).IsSuccessStatusCode);

            var res = await http.PostAsJsonAsync("/api/register", new { name = "세엣", password = "secret123" });
            Assert.AreEqual(HttpStatusCode.TooManyRequests, res.StatusCode);
            StringAssert.Contains("가입이 몰려", await Error(res));
        }
    }
}
