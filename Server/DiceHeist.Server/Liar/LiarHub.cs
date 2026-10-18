using System.Collections.Concurrent;
using DiceHeist.Server.Data;
using DiceHeist.Server.Multi;
using Microsoft.AspNetCore.SignalR;

namespace DiceHeist.Server.Liar
{
    /// <summary>라이어 다이스 허브의 사용자별 지금 연결(같은 계정으로 새로 접속하면 예전 연결은 밀려난다).</summary>
    public sealed class LiarConnections
    {
        readonly ConcurrentDictionary<long, string> _byUser = new ConcurrentDictionary<long, string>();

        public string Set(long userId, string connectionId)
        {
            string old = null;
            _byUser.AddOrUpdate(userId, connectionId, (_, prev) => { old = prev; return connectionId; });
            return old;
        }

        public string Get(long userId) => _byUser.TryGetValue(userId, out var c) ? c : null;
        public void RemoveIf(long userId, string connectionId) => _byUser.TryRemove(new KeyValuePair<long, string>(userId, connectionId));
    }

    public sealed class LiarNotifier : ILiarNotifier
    {
        readonly IHubContext<LiarHub> _hub;
        readonly Db _db;
        readonly LiarConnections _connections;
        readonly Func<LiarManager> _rooms;

        public LiarNotifier(IHubContext<LiarHub> hub, Db db, LiarConnections connections, IServiceProvider sp)
        {
            _hub = hub;
            _db = db;
            _connections = connections;
            _rooms = () => sp.GetRequiredService<LiarManager>();
        }

        public void Room(LiarRoom room, DateTimeOffset now)
        {
            foreach (var seat in room.Seats)
                if (seat.ConnectionId != null)
                    _ = _hub.Clients.Client(seat.ConnectionId).SendAsync("state", room.StateFor(seat, now));
        }

        public void Lobby(object[] rooms) => _ = _hub.Clients.Group(LiarHub.LobbyGroup).SendAsync("lobby", rooms);

        public void Profile(long userId)
        {
            var c = _connections.Get(userId);
            var u = _db.GetUser(userId);
            // 방 안에 있는지는 라이어 다이스 방 기준(이 화면은 라이어 다이스만 다룬다)
            if (c != null && u != null) _ = _hub.Clients.Client(c).SendAsync("profile", Profiles.Of(u, _rooms().IsInRoom(userId)));
        }

        public void Left(long userId, string reason)
        {
            var c = _connections.Get(userId);
            if (c != null) _ = _hub.Clients.Client(c).SendAsync("left", reason);
        }
    }

    /// <summary>
    /// 라이어 다이스 브라우저 화면과 실시간으로 주고받는 곳(/liarhub). 접속할 때 로그인 토큰(access_token)을 확인한다.
    /// 메서드는 실패하면 이유 문자열을, 성공하면 null을 돌려준다.
    /// </summary>
    public sealed class LiarHub : Hub
    {
        public const string LobbyGroup = "liar-lobby";

        readonly Db _db;
        readonly LiarManager _rooms;
        readonly LiarConnections _connections;

        public LiarHub(Db db, LiarManager rooms, LiarConnections connections)
        {
            _db = db;
            _rooms = rooms;
            _connections = connections;
        }

        long UserId => (long)Context.Items["uid"];

        public override async Task OnConnectedAsync()
        {
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
        /// <summary>빠른 시작: 빈자리가 있는 방에 바로 앉는다(없으면 새 방, 모자라면 봇이 채운다).</summary>
        public string QuickStart() => _rooms.QuickStart(UserId, Context.ConnectionId);
        public string LeaveRoom() => _rooms.Leave(UserId);
        public string Emote(int kind) => _rooms.Emote(UserId, kind);
        /// <summary>"face 눈이 quantity개 이상".</summary>
        public string Bid(int quantity, int face) => _rooms.Bid(UserId, quantity, face);
        /// <summary>"거짓말!"</summary>
        public string Challenge() => _rooms.Challenge(UserId);
        public string Pull() => _rooms.Pull(UserId);
        /// <summary>관전 베팅: target은 걸 사람의 사용자 id(state.seats[].userId).</summary>
        public string SideBet(long target, int amount) => _rooms.SideBet(UserId, target, amount);
        public string Double() => _rooms.Double(UserId);
        public string Cash() => _rooms.Cash(UserId);
    }

    /// <summary>시간 제한을 넘긴 라이어 다이스 방을 진행시킨다.</summary>
    public sealed class LiarTicker : BackgroundService
    {
        readonly LiarManager _rooms;
        readonly ILogger<LiarTicker> _log;

        public LiarTicker(LiarManager rooms, ILogger<LiarTicker> log)
        {
            _rooms = rooms;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stop)
        {
            while (!stop.IsCancellationRequested)
            {
                try { _rooms.Tick(); }
                catch (Exception e) { _log.LogError(e, "라이어 다이스 방 진행 중 오류"); }
                await Task.Delay(200, stop).ContinueWith(_ => { });
            }
        }
    }
}
