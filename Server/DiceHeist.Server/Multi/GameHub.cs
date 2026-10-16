using System.Collections.Concurrent;
using DiceHeist.Server.Data;
using Microsoft.AspNetCore.SignalR;

namespace DiceHeist.Server.Multi
{
    /// <summary>사용자 한 명당 지금 연결 하나. 같은 계정으로 새로 접속하면 예전 연결은 밀려난다.</summary>
    public sealed class ConnectionRegistry
    {
        readonly ConcurrentDictionary<long, string> _byUser = new ConcurrentDictionary<long, string>();

        /// <summary>새 연결을 등록하고 밀려난 예전 연결을 돌려준다.</summary>
        public string Set(long userId, string connectionId)
        {
            string old = null;
            _byUser.AddOrUpdate(userId, connectionId, (_, prev) => { old = prev; return connectionId; });
            return old;
        }

        public string Get(long userId) => _byUser.TryGetValue(userId, out var c) ? c : null;

        public void RemoveIf(long userId, string connectionId) =>
            _byUser.TryRemove(new KeyValuePair<long, string>(userId, connectionId));
    }

    public static class Profiles
    {
        public static object Of(UserRow u, bool inRoom) => new
        {
            name = u.Name,
            chips = u.Chips,
            money = u.Money,
            bronzeCleared = u.BronzeCleared,
            exchangeable = DiceHeist.Core.ChipExchange.Exchangeable(u.Chips),
            exchangeUnit = DiceHeist.Core.ChipExchange.Unit,
            exchangeRate = DiceHeist.Core.ChipExchange.Rate,
            buyIn = Room.BuyIn,
            inRoom,
            // 멀티 기록: 낀 판, 이긴 판, 쇼다운에서 보인 가장 높은 족보, 이겨서 가져온 가장 큰 금액
            record = new
            {
                rounds = u.Rounds,
                wins = u.Wins,
                bestHand = u.BestRank >= 0 ? DiceHeist.Core.HandEvaluator.NameOf((DiceHeist.Core.HandRank)u.BestRank) : null,
                biggestPot = u.BiggestPot,
            },
            // 구제 머니(하루 한 번, 바이인까지 채움)를 지금 받을 수 있는지
            canRescue = !inRoom && Rescue.Available(u.Money, u.LastRescue, Room.BuyIn, DateTimeOffset.UtcNow),
        };
    }

    public sealed class HubNotifier : IRoomNotifier
    {
        readonly IHubContext<GameHub> _hub;
        readonly Db _db;
        readonly ConnectionRegistry _connections;

        public HubNotifier(IHubContext<GameHub> hub, Db db, ConnectionRegistry connections)
        {
            _hub = hub;
            _db = db;
            _connections = connections;
        }

        public void Room(Room room, DateTimeOffset now)
        {
            foreach (var seat in room.Seats)
                if (seat.ConnectionId != null)
                    _ = _hub.Clients.Client(seat.ConnectionId).SendAsync("state", room.StateFor(seat, now));
        }

        public void Lobby(object[] rooms) => _ = _hub.Clients.Group(GameHub.LobbyGroup).SendAsync("lobby", rooms);

        public void Profile(long userId, bool inRoom)
        {
            var c = _connections.Get(userId);
            var u = _db.GetUser(userId);
            if (c != null && u != null) _ = _hub.Clients.Client(c).SendAsync("profile", Profiles.Of(u, inRoom));
        }

        public void Left(long userId, string reason)
        {
            var c = _connections.Get(userId);
            if (c != null) _ = _hub.Clients.Client(c).SendAsync("left", reason);
        }
    }

    /// <summary>
    /// 브라우저와 실시간으로 주고받는 곳. 접속할 때 로그인 토큰(access_token)을 확인한다.
    /// 메서드는 실패하면 이유 문자열을, 성공하면 null을 돌려준다.
    /// </summary>
    public sealed class GameHub : Hub
    {
        public const string LobbyGroup = "lobby";

        readonly Db _db;
        readonly RoomManager _rooms;
        readonly ConnectionRegistry _connections;

        public GameHub(Db db, RoomManager rooms, ConnectionRegistry connections)
        {
            _db = db;
            _rooms = rooms;
            _connections = connections;
        }

        long UserId => (long)Context.Items["uid"];

        public override async Task OnConnectedAsync()
        {
            // 브라우저 웹소켓은 주소의 access_token으로, 롱폴링 등은 Authorization 헤더로 토큰을 보낸다
            var req = Context.GetHttpContext()?.Request;
            var token = req?.Query["access_token"].ToString();
            var header = req?.Headers.Authorization.ToString() ?? "";
            if (string.IsNullOrEmpty(token) && header.StartsWith("Bearer ")) token = header.Substring(7);
            var uid = _db.UserIdByToken(token);
            if (uid == null)
            {
                Context.Abort();
                return;
            }
            Context.Items["uid"] = uid.Value;
            var old = _connections.Set(uid.Value, Context.ConnectionId);
            if (old != null && old != Context.ConnectionId)
                await Clients.Client(old).SendAsync("kicked", "다른 창에서 같은 계정으로 접속했습니다.");

            await Groups.AddToGroupAsync(Context.ConnectionId, LobbyGroup);
            await Clients.Caller.SendAsync("profile", Profiles.Of(_db.GetUser(uid.Value), _rooms.IsInRoom(uid.Value)));
            await Clients.Caller.SendAsync("lobby", _rooms.Lobby());
            _rooms.Connected(uid.Value, Context.ConnectionId);
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            if (Context.Items.TryGetValue("uid", out var v))
            {
                var uid = (long)v;
                _connections.RemoveIf(uid, Context.ConnectionId);
                _rooms.Disconnected(uid, Context.ConnectionId);
            }
            return Task.CompletedTask;
        }

        public string CreateRoom(string name) => _rooms.Create(UserId, name, Context.ConnectionId);
        public string JoinRoom(string roomId) => _rooms.Join(UserId, roomId, Context.ConnectionId);
        public string LeaveRoom() => _rooms.Leave(UserId);
        public string Emote(int kind) => _rooms.Emote(UserId, kind);
        public string Reroll(bool[] held) => _rooms.Reroll(UserId, held);
        public string Stand() => _rooms.Stand(UserId);
        public string Bet(string action) => _rooms.Bet(UserId, action);
        /// <summary>이번 판 누적이 total이 되게 올린다. Bet("raise")는 기본 크기. 따로 둔 이유: 허브 메서드는 인자 수가 맞아야 해서, 예전 화면이 부르는 Bet(action)을 그대로 둔다</summary>
        public string RaiseTo(int total) => _rooms.Bet(UserId, "raise", total);
        public string SitOut() => _rooms.SitOut(UserId);
        public string Back() => _rooms.Back(UserId);
    }

    /// <summary>시간 제한(굴리기 30초, 베팅 20초 등)을 넘긴 방을 진행시킨다.</summary>
    public sealed class RoomTicker : BackgroundService
    {
        readonly RoomManager _rooms;
        readonly ILogger<RoomTicker> _log;

        public RoomTicker(RoomManager rooms, ILogger<RoomTicker> log)
        {
            _rooms = rooms;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stop)
        {
            while (!stop.IsCancellationRequested)
            {
                try { _rooms.Tick(); }
                catch (Exception e) { _log.LogError(e, "방 진행 중 오류"); }
                await Task.Delay(200, stop).ContinueWith(_ => { });
            }
        }
    }
}
