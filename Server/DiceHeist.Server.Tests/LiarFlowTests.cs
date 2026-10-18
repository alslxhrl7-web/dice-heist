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
    /// <summary>실제 서버(메모리 안)에서 라이어 다이스 한 게임을 허브로 끝까지 두고, 가챠 API를 불러 본다.</summary>
    public class LiarFlowTests
    {
        WebApplicationFactory<Program> _factory;
        string _dbPath;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"dh-liar-{Guid.NewGuid():N}.db");
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            {
                b.UseSetting("Db:Path", _dbPath);
                b.UseSetting("Db:BackupDir", _dbPath + "-backups");
                b.UseSetting("Liar:CountdownSeconds", "0.2");
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
            public HttpClient Http;
            public HubConnection Hub;
            public JsonElement? State;
            public readonly SemaphoreSlim Changed = new SemaphoreSlim(0);
        }

        async Task<Player> SignUp(string name)
        {
            var http = _factory.CreateClient();
            var res = await http.PostAsJsonAsync("/api/register", new { name, password = "secret123" });
            var token = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var p = new Player { Token = token, Http = http };
            var server = _factory.Server;
            p.Hub = new HubConnectionBuilder()
                .WithUrl(new Uri(server.BaseAddress, "liarhub"), o =>
                {
                    o.AccessTokenProvider = () => Task.FromResult(token);
                    o.HttpMessageHandlerFactory = _ => server.CreateHandler();
                    o.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                })
                .Build();
            p.Hub.On<JsonElement>("state", s => { p.State = s; p.Changed.Release(); });
            await p.Hub.StartAsync();
            return p;
        }

        static async Task<JsonElement> WaitFor(Player p, Func<JsonElement, bool> ok, string what)
        {
            var until = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < until)
            {
                if (p.State is JsonElement s && ok(s)) return s;
                await p.Changed.WaitAsync(TimeSpan.FromMilliseconds(200));
            }
            Assert.Fail("기다리다 시간 초과: " + what);
            return default;
        }

        static string Phase(JsonElement s) => s.GetProperty("phase").GetString();

        [Test, Timeout(90000)]
        public async Task 두_사람이_라이어_다이스를_끝까지_두고_머니는_새지_않는다()
        {
            var a = await SignUp("가나");
            var b = await SignUp("다라");
            var db = _factory.Services.GetRequiredService<Db>();
            long total = db.TotalMoney();

            Assert.IsNull(await a.Hub.InvokeAsync<string>("CreateRoom", "함정 방"));
            var roomId = (await WaitFor(a, s => true, "방 상태")).GetProperty("room").GetProperty("id").GetString();
            Assert.IsNull(await b.Hub.InvokeAsync<string>("JoinRoom", roomId));
            Assert.AreEqual(total, db.TotalMoney(), "바이인은 머니를 테이블로 옮길 뿐");

            var players = new[] { a, b };
            for (int guard = 0; guard < 200; guard++)
            {
                var s = await WaitFor(a, x => Phase(x) != "waiting" && Phase(x) != "countdown", "게임 시작");
                string ph = Phase(s);
                if (ph == "double" || ph == "result") break;
                foreach (var p in players)
                {
                    var st = p.State.Value;
                    var me = st.GetProperty("me");
                    if (me.GetProperty("myTurn").GetBoolean())
                    {
                        string err = me.GetProperty("canChallenge").GetBoolean()
                            ? await p.Hub.InvokeAsync<string>("Challenge")
                            : await p.Hub.InvokeAsync<string>("Bid", me.GetProperty("minBid").GetProperty("quantity").GetInt32(), me.GetProperty("minBid").GetProperty("face").GetInt32());
                        Assert.IsNull(err);
                    }
                    else if (me.GetProperty("canPull").GetBoolean()) Assert.IsNull(await p.Hub.InvokeAsync<string>("Pull"));
                }
                await Task.Delay(100);
                if (Phase(a.State.Value) == "fall") await WaitFor(a, x => Phase(x) != "fall", "레버 결과 뒤");
            }
            var end = await WaitFor(a, x => Phase(x) == "double", "더블 오어 낫싱");
            int winner = end.GetProperty("winner").GetInt32();
            var w = winner == end.GetProperty("mySeat").GetInt32() ? a : b;
            Assert.IsNull(await w.Hub.InvokeAsync<string>("Cash"));
            await WaitFor(a, x => Phase(x) == "result", "결과");
            Assert.AreEqual(total, db.TotalMoney(), "참가비는 판돈으로 옮겨질 뿐");

            Assert.IsNull(await a.Hub.InvokeAsync<string>("LeaveRoom"));
            Assert.IsNull(await b.Hub.InvokeAsync<string>("LeaveRoom"));
            Assert.AreEqual(total, db.TotalMoney());
            Assert.IsFalse(db.HasStack(1));
        }

        [Test]
        public async Task 가챠는_확률을_보여_주고_머니로_뽑아_쓸_수_있다()
        {
            var p = await SignUp("뽑기왕");
            var cfg = await p.Http.GetFromJsonAsync<JsonElement>("/api/gacha");
            var items = cfg.GetProperty("items").EnumerateArray().ToList();
            Assert.Greater(items.Count, 0);
            Assert.AreEqual(1.0, items.Sum(i => i.GetProperty("chance").GetDouble()), 1e-9, "확률 합은 1");
            long cost = cfg.GetProperty("cost").GetInt64();

            var res = await p.Http.PostAsJsonAsync("/api/gacha/pull", new { count = 1 });
            Assert.IsTrue(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
            var pulled = await res.Content.ReadFromJsonAsync<JsonElement>();
            var got = pulled.GetProperty("results")[0].GetProperty("id").GetString();
            Assert.AreEqual(Data.Db.StarterMoney - cost, pulled.GetProperty("inventory").GetProperty("money").GetInt64());

            res = await p.Http.PostAsJsonAsync("/api/gacha/equip", new { item = got });
            Assert.IsTrue(res.IsSuccessStatusCode);
            Assert.AreEqual(got, (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("equipped").GetString());

            res = await p.Http.PostAsJsonAsync("/api/gacha/equip", new { item = "없는것" });
            Assert.IsFalse(res.IsSuccessStatusCode);

            // 테이블에 앉아 있으면 못 뽑는다(머니가 테이블에 나가 있다)
            Assert.IsNull(await p.Hub.InvokeAsync<string>("CreateRoom", "방"));
            res = await p.Http.PostAsJsonAsync("/api/gacha/pull", new { count = 1 });
            Assert.IsFalse(res.IsSuccessStatusCode);
            var st = await WaitFor(p, s => true, "방 상태");
            Assert.AreEqual(got, st.GetProperty("seats")[0].GetProperty("hat").GetString(), "쓴 꾸미기가 자리에 보인다");
        }

        [Test]
        public async Task 머니가_모자라면_가챠를_못_뽑는다()
        {
            var p = await SignUp("가난");
            for (int i = 0; i < 2; i++) await p.Http.PostAsJsonAsync("/api/gacha/pull", new { count = 10 }); // 90,000 → 10,000 남음
            var res = await p.Http.PostAsJsonAsync("/api/gacha/pull", new { count = 10 });
            Assert.IsFalse(res.IsSuccessStatusCode);
            StringAssert.Contains("사이버머니", (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        }
    }
}
