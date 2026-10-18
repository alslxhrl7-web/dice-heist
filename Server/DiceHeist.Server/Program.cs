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
var security = builder.Configuration.GetSection("Security");
builder.Services.AddSingleton(new LoginGuard
{
    MaxFailures = security.GetValue("LoginFailures", 5),
    Window = TimeSpan.FromMinutes(security.GetValue("LockMinutes", 15.0)),
    RegistrationsPerHour = security.GetValue("RegistrationsPerHour", 20),
});
// 라이어 다이스(함정 의자, 2026-10-18): 브라우저 /liar/, 허브 /liarhub. 시간 제한은 설정 Liar:*Seconds로 바꿀 수 있다
var liarCfg = builder.Configuration.GetSection("Liar");
var liarTimings = new DiceHeist.Server.Liar.LiarTimings();
liarTimings.Countdown = TimeSpan.FromSeconds(liarCfg.GetValue("CountdownSeconds", liarTimings.Countdown.TotalSeconds));
liarTimings.Turn = TimeSpan.FromSeconds(liarCfg.GetValue("TurnSeconds", liarTimings.Turn.TotalSeconds));
liarTimings.Lever = TimeSpan.FromSeconds(liarCfg.GetValue("LeverSeconds", liarTimings.Lever.TotalSeconds));
liarTimings.Double = TimeSpan.FromSeconds(liarCfg.GetValue("DoubleSeconds", liarTimings.Double.TotalSeconds));
builder.Services.AddSingleton(liarTimings);
builder.Services.AddSingleton<DiceHeist.Server.Liar.LiarConnections>();
builder.Services.AddSingleton(sp => new DiceHeist.Server.Liar.LiarManager(sp.GetRequiredService<Db>(), liarTimings));
builder.Services.AddSingleton<DiceHeist.Server.Liar.LiarNotifier>();
builder.Services.AddHostedService<DiceHeist.Server.Liar.LiarTicker>();
// 가챠 설정(확률·보상은 사용자가 gacha.json에서 정한다). 경로는 설정 Gacha:Path(기본: 실행 파일 옆 gacha.json)
string gachaPath = builder.Configuration["Gacha:Path"] ?? "gacha.json";
if (!Path.IsPathRooted(gachaPath)) gachaPath = Path.Combine(builder.Environment.ContentRootPath, gachaPath);
builder.Services.AddSingleton(GachaConfig.Load(gachaPath));
builder.Services.AddSignalR();
// SingleView 같은 Core의 화면용 클래스는 필드로 되어 있어(Unity JsonUtility와 같이 쓰려고) 필드도 JSON에 넣는다
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.IncludeFields = true);

var app = builder.Build();

var db = app.Services.GetRequiredService<Db>();
var guard = app.Services.GetRequiredService<LoginGuard>();
var rooms = app.Services.GetRequiredService<RoomManager>();
rooms.SetNotifier(app.Services.GetRequiredService<HubNotifier>());
int refunded = db.RefundAllStacks();
if (refunded > 0) app.Logger.LogInformation("지난번 테이블에 남아 있던 {Count}명의 칩을 돌려줬습니다.", refunded);

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHub<GameHub>("/hub");
app.MapHub<DiceHeist.Server.Liar.LiarHub>("/liarhub");
var liar = app.Services.GetRequiredService<DiceHeist.Server.Liar.LiarManager>();
liar.SetNotifier(app.Services.GetRequiredService<DiceHeist.Server.Liar.LiarNotifier>());
var gacha = app.Services.GetRequiredService<GachaConfig>();

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
    if (db.NameExists(name)) return Results.BadRequest(new { error = "이미 있는 아이디입니다." });
    // 가입마다 시작 머니를 주므로 계정을 마구 만들지 못하게 서버 전체에서 시간당 횟수를 센다
    if (!guard.TryRegister())
        return Results.Json(new { error = "지금은 가입이 몰려 잠시 막혀 있습니다. 조금 뒤에 다시 시도해 주세요." }, statusCode: StatusCodes.Status429TooManyRequests);
    var uid = db.CreateUser(name, c.Password);
    if (uid == null) return Results.BadRequest(new { error = "이미 있는 아이디입니다." });
    return Results.Ok(new { token = db.CreateSession(uid.Value), profile = Profiles.Of(db.GetUser(uid.Value), false) });
});

app.MapPost("/api/login", (Credentials c) =>
{
    var name = CleanName(c?.Name);
    // 비밀번호를 계속 틀린 아이디는 잠시 막는다. 잠긴 동안은 비밀번호를 확인하지 않는다(맞혀도 들어오지 못한다)
    if (name != null && guard.LockedFor(name) is TimeSpan wait)
        return Results.Json(new { error = $"비밀번호를 여러 번 틀려 이 아이디는 잠시 잠겼습니다. {LoginGuard.Minutes(wait)} 뒤에 다시 시도해 주세요." },
            statusCode: StatusCodes.Status429TooManyRequests);
    var uid = name == null || c.Password == null ? null : db.VerifyUser(name, c.Password);
    // 있는 아이디만 센다(없는 아이디는 지킬 것이 없고, 세면 아무 이름으로 메모리를 채울 수 있다)
    if (uid == null && name != null && db.NameExists(name)) guard.Failed(name);
    if (uid != null) guard.Succeeded(name);
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

app.MapPost("/api/password", (HttpRequest req, PasswordChange p) =>
{
    if (Auth(req) is not long uid) return Results.Unauthorized();
    var name = db.GetUser(uid).Name;
    if (guard.LockedFor(name) is TimeSpan wait)
        return Results.Json(new { error = $"비밀번호를 여러 번 틀려 잠시 잠겼습니다. {LoginGuard.Minutes(wait)} 뒤에 다시 시도해 주세요." },
            statusCode: StatusCodes.Status429TooManyRequests);
    if (p?.Current == null || db.VerifyUser(name, p.Current) != uid)
    {
        guard.Failed(name);
        return Results.BadRequest(new { error = "지금 비밀번호가 맞지 않습니다." });
    }
    if (p.Next == null || p.Next.Length < 6 || p.Next.Length > 64) return Results.BadRequest(new { error = "새 비밀번호는 6~64자입니다." });
    db.ChangePassword(uid, p.Next, req.Headers.Authorization.ToString().Substring("Bearer ".Length));
    guard.Succeeded(name);
    return Profile(uid);
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

app.MapPost("/api/rescue", (HttpRequest req) =>
{
    if (Auth(req) is not long uid) return Results.Unauthorized();
    if (rooms.IsInRoom(uid)) return Results.BadRequest(new { error = "테이블에서 일어난 뒤에 받을 수 있습니다." });
    var err = db.ClaimRescue(uid, Room.BuyIn, DateTimeOffset.UtcNow);
    return err != null ? Results.BadRequest(new { error = err }) : Profile(uid);
});

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
var characters = Characters.Ids.Select((id, i) => new { id, name = Characters.Names[i] }).ToArray();
app.MapGet("/api/config", () => new { devGrant = allowGrant, hands, tiers, multi = multiRules, characters });

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
// 순위표(로그인 없이 볼 수 있다): 사이버머니 상위 10명
app.MapGet("/api/ranking", () => db.Ranking(10).Select((r, i) => new { rank = i + 1, r.name, r.money, r.character }).ToArray());

// 캐릭터 바꾸기(겉모습만). 앉아 있으면 자리 얼굴도 바로 바뀐다
app.MapPost("/api/character", (HttpRequest req, CharacterRequest ch) =>
{
    if (Auth(req) is not long uid) return Results.Unauthorized();
    if (!db.SetCharacter(uid, ch?.Character)) return Results.BadRequest(new { error = "없는 캐릭터입니다." });
    rooms.CharacterChanged(uid, ch.Character);
    return Profile(uid);
});

// ---------- 가챠(꾸미기). 사이버머니로만 뽑고 현금은 오가지 않는다. 확률은 gacha.json의 weight 비율 그대로 보여 준다 ----------
object Inventory(long uid) => new
{
    money = db.GetUser(uid).Money,
    equipped = db.EquippedItem(uid),
    items = db.Inventory(uid).Select(x => new { id = x.item, x.count, x.equipped }).ToArray(),
};
app.MapGet("/api/gacha", () => gacha.Public());
app.MapGet("/api/gacha/me", (HttpRequest req) => Auth(req) is long uid ? Results.Ok(Inventory(uid)) : Results.Unauthorized());
app.MapPost("/api/gacha/pull", (HttpRequest req, GachaPullRequest p) =>
{
    if (Auth(req) is not long uid) return Results.Unauthorized();
    int count = p?.Count == 10 ? 10 : 1;
    long cost = count == 10 ? gacha.TenPullCost : gacha.Cost;
    var got = Enumerable.Range(0, count).Select(_ => gacha.Draw()).ToList();
    var err = db.GachaPull(uid, cost, got.Select(i => i.Id).ToList());
    if (err != null) return Results.BadRequest(new { error = err });
    return Results.Ok(new { results = got.Select(i => new { i.Id, i.Name, i.Icon, i.Rarity }).ToArray(), cost, inventory = Inventory(uid) });
});
app.MapPost("/api/gacha/equip", (HttpRequest req, GachaEquipRequest e) =>
{
    if (Auth(req) is not long uid) return Results.Unauthorized();
    var item = string.IsNullOrEmpty(e?.Item) ? null : e.Item;
    if (item != null && gacha.Find(item) == null) return Results.BadRequest(new { error = "없는 아이템입니다." });
    if (!db.Equip(uid, item)) return Results.BadRequest(new { error = "가지고 있는 아이템만 쓸 수 있습니다." });
    liar.HatChanged(uid, item);
    return Results.Ok(Inventory(uid));
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
public sealed record PasswordChange(string Current, string Next);
public sealed record CharacterRequest(string Character);
public sealed record SitRequest(string Table);
public sealed record GachaPullRequest(int Count);
public sealed record GachaEquipRequest(string Item);
public sealed record RerollRequest(bool[] Held);
/// <param name="RaiseTo">레이즈일 때 이번 판 누적 금액. 0이면 기본 크기. 허용 범위 밖이면 Core가 맞춘다</param>
public sealed record BetRequest(string Action, int RaiseTo = 0);

public partial class Program { }
