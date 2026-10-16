using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DiceHeist.Server.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace DiceHeist.Server.Tests
{
    /// <summary>실제 서버(메모리 안)에 두 사람이 가입·접속해 방을 만들고 한 판을 끝까지 둔다.</summary>
    public class ServerFlowTests
    {
        WebApplicationFactory<Program> _factory;
        string _dbPath;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"dh-test-{Guid.NewGuid():N}.db");
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            {
                b.UseSetting("Db:Path", _dbPath);
                // 테스트마다 따로 둔다. 같은 폴더면 같은 초에 만든 사본 이름이 겹쳐 백업 실패가 이벤트 로그에 쌓인다
                b.UseSetting("Db:BackupDir", _dbPath + "-backups");
                b.UseSetting("Multi:CountdownSeconds", "0.2");
                b.UseSetting("Multi:HoldSeconds", "30");
                b.UseSetting("Multi:BetSeconds", "30");
                b.UseSetting("Multi:ResultSeconds", "0.5");
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

        sealed class Player
        {
            public string Token;
            public HubConnection Hub;
            public JsonElement? State;
            public JsonElement? Profile;
            public readonly SemaphoreSlim Changed = new SemaphoreSlim(0);
        }

        async Task<Player> SignUp(string name)
        {
            var http = _factory.CreateClient();
            var res = await http.PostAsJsonAsync("/api/register", new { name, password = "secret123" });
            Assert.IsTrue(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
            var token = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
            // 테스트 머니 없이 가입 때 받는 시작 머니만으로 앉는다
            var p = new Player { Token = token };
            var server = _factory.Server;
            p.Hub = new HubConnectionBuilder()
                .WithUrl(new Uri(server.BaseAddress, "hub"), o =>
                {
                    o.AccessTokenProvider = () => Task.FromResult(token);
                    o.HttpMessageHandlerFactory = _ => server.CreateHandler();
                    o.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                })
                .Build();
            p.Hub.On<JsonElement>("state", s => { p.State = s; p.Changed.Release(); });
            p.Hub.On<JsonElement>("profile", s => { p.Profile = s; p.Changed.Release(); });
            await p.Hub.StartAsync();
            return p;
        }

        static async Task<JsonElement> WaitFor(Player p, Func<JsonElement, bool> ok, string what)
        {
            var until = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < until)
            {
                if (p.State is JsonElement s && ok(s)) return s;
                await p.Changed.WaitAsync(TimeSpan.FromMilliseconds(200));
            }
            Assert.Fail("기다리다 시간 초과: " + what);
            return default;
        }

        static string Phase(JsonElement s) => s.GetProperty("phase").GetString();
        static bool MyTurn(JsonElement s) => s.GetProperty("me").ValueKind == JsonValueKind.Object && s.GetProperty("me").GetProperty("myTurn").GetBoolean();

        [Test, Timeout(60000)]
        public async Task 두_사람이_방에서_한_판을_끝까지_두고_머니는_새지_않는다()
        {
            var a = await SignUp("가나");
            var b = await SignUp("다라");
            var db = _factory.Services.GetRequiredService<Db>();
            long total = db.TotalMoney();
            Assert.AreEqual(200_000, total);

            Assert.IsNull(await a.Hub.InvokeAsync<string>("CreateRoom", "첫 방"));
            var lobbyRoom = (await WaitFor(a, s => true, "방 상태")).GetProperty("room").GetProperty("id").GetString();
            Assert.IsNull(await b.Hub.InvokeAsync<string>("JoinRoom", lobbyRoom));
            Assert.AreEqual(total, db.TotalMoney(), "바이인은 머니를 테이블로 옮길 뿐");

            await WaitFor(a, s => Phase(s) == "hold", "굴리기 시작");
            await WaitFor(b, s => Phase(s) == "hold", "굴리기 시작");
            Assert.IsNull(await a.Hub.InvokeAsync<string>("Reroll", new[] { true, true, false, false, false }));
            Assert.IsNull(await a.Hub.InvokeAsync<string>("Stand"));
            Assert.IsNull(await b.Hub.InvokeAsync<string>("Stand"));

            // 처음 차례가 온 사람은 앤티 5배로 올리고, 그다음부터는 콜한다. 결과가 나올 때까지
            var until = DateTime.UtcNow.AddSeconds(20);
            bool raised = false;
            while (DateTime.UtcNow < until)
            {
                if (a.State is JsonElement sa && Phase(sa) == "result") break;
                foreach (var p in new[] { a, b })
                    if (p.State is JsonElement s && Phase(s) == "betting" && MyTurn(s))
                    {
                        if (!raised && s.GetProperty("me").GetProperty("canRaise").GetBoolean())
                        {
                            raised = true;
                            Assert.IsNull(await p.Hub.InvokeAsync<string>("RaiseTo", 5_000));
                        }
                        else await p.Hub.InvokeAsync<string>("Bet", "call");
                    }
                await Task.Delay(50);
            }
            Assert.IsTrue(raised, "레이즈 크기를 골라 올렸다");
            var result = await WaitFor(a, s => Phase(s) == "result", "결과");
            Assert.IsNotNull(result.GetProperty("banner").GetString());
            Assert.AreEqual(total, db.TotalMoney(), "판이 끝나도 머니 총량은 그대로");

            await WaitFor(a, s => Phase(s) == "waiting" || Phase(s) == "countdown" || s.GetProperty("roundNo").GetInt32() > 1, "다음 판 준비");
            Assert.IsNull(await a.Hub.InvokeAsync<string>("LeaveRoom"));
            Assert.IsNull(await b.Hub.InvokeAsync<string>("LeaveRoom"));
            await Task.Delay(300);
            Assert.AreEqual(total, db.TotalMoney(), "모두 일어나면 머니로 돌아온다");
            Assert.AreEqual(0, db.TotalMoney() - (db.GetUser(1).Money + db.GetUser(2).Money), "테이블에 남은 머니가 없다");
        }

        [Test, Timeout(60000)]
        public async Task 방에는_여섯_명까지_앉고_일곱_번째는_거절된다()
        {
            var players = new List<Player>();
            for (int i = 0; i < 7; i++) players.Add(await SignUp("손님" + i));
            Assert.IsNull(await players[0].Hub.InvokeAsync<string>("CreateRoom", "큰 방"));
            var roomId = (await WaitFor(players[0], s => true, "방 상태")).GetProperty("room").GetProperty("id").GetString();
            for (int i = 1; i < 6; i++)
                Assert.IsNull(await players[i].Hub.InvokeAsync<string>("JoinRoom", roomId), $"{i + 1}번째 사람");
            StringAssert.Contains("꽉", await players[6].Hub.InvokeAsync<string>("JoinRoom", roomId));
            var s0 = await WaitFor(players[0], s => s.GetProperty("seats").GetArrayLength() == 6, "6명");
            Assert.AreEqual(6, s0.GetProperty("seats").GetArrayLength());
        }

        [Test]
        public async Task 가입하면_시작_머니를_받고_싱글_없이_바로_멀티에_앉는다()
        {
            var p = await SignUp("새내기");
            var http = _factory.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", p.Token);
            var me = await http.GetFromJsonAsync<JsonElement>("/api/me");
            Assert.AreEqual(Data.Db.StarterMoney, me.GetProperty("money").GetInt64());
            Assert.IsFalse(me.GetProperty("bronzeCleared").GetBoolean());
            Assert.IsNull(await p.Hub.InvokeAsync<string>("CreateRoom", "방"));
        }

        [Test]
        public void 시작_머니가_생기기_전에_가입한_계정도_한_번만_받는다()
        {
            var path = Path.Combine(Path.GetTempPath(), $"dh-starter-{Guid.NewGuid():N}.db");
            try
            {
                var db = new Data.Db(path);
                long id = db.CreateUser("예전사람", "secret123").Value;
                // 예전 계정처럼 시작 머니 기록을 지운다
                using (var c = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path))
                {
                    c.Open();
                    using var cmd = c.CreateCommand();
                    cmd.CommandText = "UPDATE users SET money = 0; DELETE FROM ledger WHERE kind = 'starter';";
                    cmd.ExecuteNonQuery();
                }
                new Data.Db(path);
                new Data.Db(path); // 다시 켜도 또 주지 않는다
                Assert.AreEqual(Data.Db.StarterMoney, new Data.Db(path).GetUser(id).Money);
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                foreach (var f in new[] { path, path + "-wal", path + "-shm" })
                    try { File.Delete(f); } catch { }
            }
        }

        [TestCase("토끼", true)]
        [TestCase("rabbit", true)]
        [TestCase("토끼Rabbit7", true)]
        [TestCase("ㅋㅋ왕", true)]
        [TestCase("  토끼2  ", true, TestName = "앞뒤 공백은 지운다")]
        [TestCase("토", false)]
        [TestCase("토끼 rabbit", false)]
        [TestCase("rabbit!", false)]
        [TestCase("열세글자아이디입니다아아아", false)]
        [TestCase("열두글자아이디입니다아아", true)]
        public async Task 아이디는_한글_영문_숫자를_섞어_쓸_수_있다(string name, bool ok)
        {
            var http = _factory.CreateClient();
            var res = await http.PostAsJsonAsync("/api/register", new { name, password = "secret123" });
            Assert.AreEqual(ok, res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
            if (ok)
            {
                var login = await http.PostAsJsonAsync("/api/login", new { name = name.Trim(), password = "secret123" });
                Assert.IsTrue(login.IsSuccessStatusCode, "가입한 아이디로 로그인");
            }
        }

        [Test]
        public async Task 풀어쓴_한글로_보내도_같은_아이디로_친다()
        {
            var http = _factory.CreateClient();
            string composed = "토끼";
            string decomposed = composed.Normalize(System.Text.NormalizationForm.FormD); // 맥·아이폰 방식
            Assert.AreNotEqual(composed, decomposed);
            Assert.IsTrue((await http.PostAsJsonAsync("/api/register", new { name = decomposed, password = "secret123" })).IsSuccessStatusCode);
            Assert.IsTrue((await http.PostAsJsonAsync("/api/login", new { name = composed, password = "secret123" })).IsSuccessStatusCode);
            Assert.IsFalse((await http.PostAsJsonAsync("/api/register", new { name = composed, password = "secret123" })).IsSuccessStatusCode, "같은 아이디로 또 가입 불가");
        }

        [Test]
        public async Task 영문_아이디는_대소문자를_구분하지_않는다()
        {
            var http = _factory.CreateClient();
            Assert.IsTrue((await http.PostAsJsonAsync("/api/register", new { name = "Rabbit", password = "secret123" })).IsSuccessStatusCode);
            Assert.IsTrue((await http.PostAsJsonAsync("/api/login", new { name = "rabbit", password = "secret123" })).IsSuccessStatusCode);
            Assert.IsFalse((await http.PostAsJsonAsync("/api/register", new { name = "RABBIT", password = "secret123" })).IsSuccessStatusCode);
        }

        [Test]
        public async Task 로그인_실패는_없는_아이디와_틀린_비밀번호를_구분해_알려준다()
        {
            var http = _factory.CreateClient();
            var none = await http.PostAsJsonAsync("/api/login", new { name = "p로딩", password = "secret123" });
            StringAssert.Contains("회원가입", (await none.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

            await http.PostAsJsonAsync("/api/register", new { name = "p로딩", password = "secret123" });
            var wrong = await http.PostAsJsonAsync("/api/login", new { name = "p로딩", password = "wrong-pass" });
            StringAssert.Contains("비밀번호", (await wrong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
            Assert.IsTrue((await http.PostAsJsonAsync("/api/login", new { name = "p로딩", password = "secret123" })).IsSuccessStatusCode);
        }

        [Test]
        public async Task 잘못된_비밀번호와_토큰은_거절된다()
        {
            var http = _factory.CreateClient();
            await http.PostAsJsonAsync("/api/register", new { name = "철수", password = "secret123" });
            var bad = await http.PostAsJsonAsync("/api/login", new { name = "철수", password = "wrong-pass" });
            Assert.IsFalse(bad.IsSuccessStatusCode);
            var dup = await http.PostAsJsonAsync("/api/register", new { name = "철수", password = "secret123" });
            Assert.IsFalse(dup.IsSuccessStatusCode, "같은 이름으로 또 가입할 수 없다");

            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token");
            Assert.AreEqual(System.Net.HttpStatusCode.Unauthorized, (await http.GetAsync("/api/me")).StatusCode);
        }

        [Test]
        public async Task 환전은_처음_500칩_위로_번_칩만()
        {
            var http = _factory.CreateClient();
            var res = await http.PostAsJsonAsync("/api/register", new { name = "영희", password = "secret123" });
            var token = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var ex = await http.PostAsJsonAsync("/api/exchange", new { amount = 100 });
            Assert.IsFalse(ex.IsSuccessStatusCode, "500칩뿐이면 바꿀 수 없다");
        }
    }
}
