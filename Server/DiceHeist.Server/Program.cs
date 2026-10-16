using System.Text.RegularExpressions;
using DiceHeist.Core;
using DiceHeist.Server;
using DiceHeist.Server.Data;
using DiceHeist.Server.Multi;
using DiceHeist.Server.Single;
using Microsoft.Extensions.Hosting.WindowsServices;

// 배포본(서비스·실행 파일)은 어디서 실행되든 설정·화면 파일을 실행 파일 옆에서 찾는다.
// 개발(dotnet run, Development)만 프로젝트 폴더를 쓴다
bool dev = string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() || !dev ? AppContext.BaseDirectory : null,
});
builder.Host.UseWindowsService(o => o.ServiceName = "DiceHeist");

var dbPath = builder.Configuration["Db:Path"] ?? "dice-heist.db";
if (!Path.IsPathRooted(dbPath)) dbPath = Path.Combine(builder.Environment.ContentRootPath, dbPath);
var multi = builder.Configuration.GetSection("Multi");
var timings = new MultiTimings
{
    Countdown = TimeSpan.FromSeconds(multi.GetValue("CountdownSeconds", 3.0)),
    Hold = TimeSpan.FromSeconds(multi.GetValue("HoldSeconds", 30.0)),
    Bet = TimeSpan.FromSeconds(multi.GetValue("BetSeconds", 20.0)),
    Result = TimeSpan.FromSeconds(multi.GetValue("ResultSeconds", 6.0)),
};

builder.Services.AddSingleton(new Db(dbPath));
builder.Services.AddSingleton(timings);
builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddSingleton(sp => new RoomManager(sp.GetRequiredService<Db>(), timings));
builder.Services.AddSingleton<HubNotifier>();
builder.Services.AddHostedService<RoomTicker>();
builder.Services.AddHostedService<DbBackup>();
builder.Services.AddSingleton<SingleSessions>();
builder.Services.AddSignalR();
// SingleView 같은 Core의 화면용 클래스는 필드로 되어 있어(Unity JsonUtility와 같이 쓰려고) 필드도 JSON에 넣는다
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.IncludeFields = true);

var app = builder.Build();

var db = app.Services.GetRequiredService<Db>();
var rooms = app.Services.GetRequiredService<RoomManager>();
rooms.SetNotifier(app.Services.GetRequiredService<HubNotifier>());
int refunded = db.RefundAllStacks();
if (refunded > 0) app.Logger.LogInformation("지난번 테이블에 남아 있던 {Count}명의 칩을 돌려줬습니다.", refunded);

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHub<GameHub>("/hub");

long? Auth(HttpRequest req)
{
    var h = req.Headers.Authorization.ToString();
    return h.StartsWith("Bearer ") ? db.UserIdByToken(h.Substring(7)) : null;
}

IResult Profile(long uid) => Results.Ok(Profiles.Of(db.GetUser(uid), rooms.IsInRoom(uid)));

// 아이디: 한글(완성형·자음·모음), 영문 대소문자, 숫자, _ 를 섞어 2~12자. 영문은 대소문자를 구분하지 않는다(DB의 NOCASE)
var namePattern = new Regex(@"^[0-9A-Za-z가-힣ㄱ-ㅎㅏ-ㅣ_]{2,12}$");

// 앞뒤 공백을 지우고, 맥·아이폰이 보내는 풀어쓴 한글(NFD)을 보통 한글(NFC)로 합친다.
// 그래야 같은 아이디가 기기마다 다른 이름으로 저장되지 않는다
static string CleanName(string name) => name?.Trim().Normalize(System.Text.NormalizationForm.FormC);

app.MapPost("/api/register", (Credentials c) =>
{
    var name = CleanName(c?.Name);
    if (name == null || !namePattern.IsMatch(name))
        return Results.BadRequest(new { error = "아이디는 한글·영문·숫자·_ 를 섞어 2~12자로 정해 주세요. (띄어쓰기·특수문자 불가)" });
    if (c.Password == null || c.Password.Length < 6 || c.Password.Length > 64) return Results.BadRequest(new { error = "비밀번호는 6~64자입니다." });
    var uid = db.CreateUser(name, c.Password);
    if (uid == null) return Results.BadRequest(new { error = "이미 있는 아이디입니다." });
    return Results.Ok(new { token = db.CreateSession(uid.Value), profile = Profiles.Of(db.GetUser(uid.Value), false) });
});

app.MapPost("/api/login", (Credentials c) =>
{
    var name = CleanName(c?.Name);
    var uid = name == null || c.Password == null ? null : db.VerifyUser(name, c.Password);
    // 친구끼리 쓰는 규모라, 무엇이 틀렸는지 알려 주는 편이 낫다(없는 아이디 / 틀린 비밀번호)
    if (uid == null)
        return Results.BadRequest(new
        {
            error = name != null && namePattern.IsMatch(name) && !db.NameExists(name)
                ? $"'{name}'(으)로 가입된 계정이 없습니다. 처음이면 [회원가입]을 눌러 주세요."
                : "비밀번호가 맞지 않습니다.",
        });
    return Results.Ok(new { token = db.CreateSession(uid.Value), profile = Profiles.Of(db.GetUser(uid.Value), rooms.IsInRoom(uid.Value)) });
});

app.MapPost("/api/logout", (HttpRequest req) =>
{
    var h = req.Headers.Authorization.ToString();
    if (h.StartsWith("Bearer ")) db.DeleteSession(h.Substring(7));
    return Results.Ok();
});

app.MapGet("/api/me", (HttpRequest req) => Auth(req) is long uid ? Profile(uid) : Results.Unauthorized());

// ---------- 싱글 (Unity가 부른다). 판정은 서버의 Core가 하고, 칩은 요청마다 저장한다 ----------
var single = app.Services.GetRequiredService<SingleSessions>();
IResult Single(HttpRequest req, Func<long, DiceHeist.Core.SingleView> act) =>
    Auth(req) is long uid ? Results.Ok(act(uid)) : Results.Unauthorized();

app.MapPost("/api/single/sit", (HttpRequest req, SitRequest s) => Single(req, uid => single.Sit(uid, s?.Table)));
app.MapPost("/api/single/look", (HttpRequest req) => Single(req, single.Look));
app.MapPost("/api/single/start", (HttpRequest req) => Single(req, single.Start));
app.MapPost("/api/single/rescue", (HttpRequest req) => Single(req, single.Rescue));
app.MapPost("/api/single/reroll", (HttpRequest req, RerollRequest r) => Single(req, uid => single.Reroll(uid, r?.Held)));
app.MapPost("/api/single/stand", (HttpRequest req) => Single(req, single.Stand));
app.MapPost("/api/single/bet", (HttpRequest req, BetRequest b) => Single(req, uid => single.Bet(uid, b?.Action, b?.RaiseTo ?? 0)));
app.MapPost("/api/single/leave", (HttpRequest req) => Single(req, single.Leave));

app.MapPost("/api/exchange", (HttpRequest req, ExchangeRequest e) =>
{
    if (Auth(req) is not long uid) return Results.Unauthorized();
    var err = db.Exchange(uid, e?.Amount ?? 0);
    return err != null ? Results.BadRequest(new { error = err }) : Profile(uid);
});

// 시험용: 머니를 더 받는다. 개발 모드에서는 항상 켜지고, 운영(서비스)에서는 Dev:AllowGrant가 true일 때만 켜진다. 공개 전에 끈다
bool allowGrant = app.Environment.IsDevelopment() || app.Configuration.GetValue("Dev:AllowGrant", false);
// 족보 표(높은 순): 이름과 한 번 굴렸을 때 나올 확률. 확률은 Core의 WeightedDice.RankWeights 그대로라 싱글·멀티가 같다
double weightSum = WeightedDice.RankWeights.Sum();
var hands = Enumerable.Range(0, WeightedDice.RankWeights.Length).Reverse()
    .Select(r => new { rank = r, name = HandEvaluator.NameOf((HandRank)r), chance = WeightedDice.RankWeights[r] / weightSum })
    .ToArray();
// 테이블 등급(싱글)과 멀티 규칙. 소개 페이지가 읽는다
var tiers = TableConfig.Tiers.Select(t => new { name = t.Name, minChips = t.MinChips, cap = t.ChipCap, ante = t.Ante, startChips = t.StartChips }).ToArray();
var multiRules = new { maxPlayers = Room.MaxSeats, buyIn = Room.BuyIn, ante = Room.Config.Ante, starterMoney = Db.StarterMoney, exchangeRate = ChipExchange.Rate, exchangeUnit = ChipExchange.Unit, exchangeFloor = ChipExchange.Floor };
app.MapGet("/api/config", () => new { devGrant = allowGrant, hands, tiers, multi = multiRules });

// PC 게임 내려받기. 파일은 Site:WindowsZip(기본: 실행 파일 옆 downloads/DiceHeist-Windows.zip). 없으면 소개 페이지가 버튼을 숨긴다
string windowsZip = app.Configuration["Site:WindowsZip"] ?? Path.Combine("downloads", "DiceHeist-Windows.zip");
if (!Path.IsPathRooted(windowsZip)) windowsZip = Path.Combine(app.Environment.ContentRootPath, windowsZip);
app.MapGet("/api/site", () =>
{
    var (roomCount, players) = rooms.Counts();
    var zip = new FileInfo(windowsZip);
    return new
    {
        rooms = roomCount,
        players,
        windows = zip.Exists ? new { size = zip.Length, updated = zip.LastWriteTimeUtc } : null,
    };
});
app.MapGet("/download/windows", () => File.Exists(windowsZip)
    ? Results.File(windowsZip, "application/zip", "DiceHeist-Windows.zip")
    : Results.NotFound());
if (allowGrant)
{
    app.MapPost("/api/dev/grant", (HttpRequest req) =>
    {
        if (Auth(req) is not long uid) return Results.Unauthorized();
        db.DevGrant(uid, app.Configuration.GetValue("Dev:GrantMoney", 100_000L));
        return Profile(uid);
    });
}

app.Run();

public sealed record Credentials(string Name, string Password);
public sealed record ExchangeRequest(int Amount);
public sealed record SitRequest(string Table);
public sealed record RerollRequest(bool[] Held);
/// <param name="RaiseTo">레이즈일 때 이번 판 누적 금액. 0이면 기본 크기. 허용 범위 밖이면 Core가 맞춘다</param>
public sealed record BetRequest(string Action, int RaiseTo = 0);

public partial class Program { }
