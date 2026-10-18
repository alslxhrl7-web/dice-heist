using System.Security.Cryptography;
using DiceHeist.Core;
using DiceHeist.Server.Data;

namespace DiceHeist.Server.Liar
{
    public interface ILiarNotifier
    {
        void Room(LiarRoom room, DateTimeOffset now);
        void Lobby(object[] rooms);
        void Profile(long userId);
        void Left(long userId, string reason);
    }

    /// <summary>
    /// 라이어 다이스 방 목록과 누가 어느 방에 있는지(RoomManager와 같은 방식). 모든 조작을 잠금 하나 안에서 처리한다.
    /// 머니는 Db의 stacks(앉을 때 가져간 머니)로 오가고, 서버가 꺼졌다 켜지면 남은 stack을 돌려준다.
    /// </summary>
    public sealed class LiarManager
    {
        readonly object _gate = new object();
        readonly Dictionary<string, LiarRoom> _rooms = new Dictionary<string, LiarRoom>();
        readonly Db _db;
        readonly LiarTimings _timings;
        readonly Func<DateTimeOffset> _now;
        readonly IRandom _rng;
        ILiarNotifier _notify;
        int _nextId;
        string _lobbySignature = "";

        public LiarManager(Db db, LiarTimings timings, Func<DateTimeOffset> now = null, IRandom rng = null)
        {
            _db = db;
            _timings = timings;
            _now = now ?? (() => DateTimeOffset.UtcNow);
            _rng = rng ?? new SeededRandom(BitConverter.ToInt64(RandomNumberGenerator.GetBytes(8)));
        }

        public void SetNotifier(ILiarNotifier notifier) => _notify = notifier;

        public object[] Lobby()
        {
            lock (_gate) return LobbyUnlocked();
        }

        object[] LobbyUnlocked() => _rooms.Values.OrderBy(r => r.Id).Select(r => r.Summary()).ToArray();

        public bool IsInRoom(long userId)
        {
            lock (_gate) return RoomOf(userId) != null;
        }

        LiarRoom RoomOf(long userId) => _rooms.Values.FirstOrDefault(r => r.Find(userId) != null);

        /// <summary>앉아 있는 동안 꾸미기를 바꾸면 자리에도 바로 보인다.</summary>
        public void HatChanged(long userId, string hat)
        {
            lock (_gate)
            {
                var room = RoomOf(userId);
                if (room == null) return;
                room.Find(userId).Hat = hat ?? "";
                Publish(room);
            }
        }

        public string Create(long userId, string roomName, string connectionId)
        {
            lock (_gate)
            {
                roomName = (roomName ?? "").Trim();
                if (roomName.Length == 0) roomName = "함정 의자 테이블";
                if (roomName.Length > 20) roomName = roomName.Substring(0, 20);
                var room = new LiarRoom("L" + (++_nextId), roomName, _timings, _rng);
                _rooms[room.Id] = room;
                var err = SitUnlocked(userId, room, connectionId);
                if (err != null) _rooms.Remove(room.Id);
                else Publish(room);
                return err;
            }
        }

        /// <summary>
        /// 빠른 시작: 사람이 기다리고 있는 방(빈자리 있음) → 게임 중이라도 빈자리 있는 방(다음 게임부터) → 새 방 순으로 앉는다.
        /// 봇이 차지한 자리는 사람에게 비켜 준다. 혼자여도 봇과 바로 시작한다.
        /// </summary>
        public string QuickStart(long userId, string connectionId)
        {
            lock (_gate)
            {
                if (RoomOf(userId) != null) return "이미 방에 앉아 있습니다.";
                var pick = _rooms.Values
                    .Where(r => r.HumanCount < LiarRoom.MaxSeats && (r.Seats.Count < LiarRoom.MaxSeats || r.Seats.Any(s => s.IsBot && !r.InGame(s))))
                    .OrderBy(r => r.Phase == "waiting" || r.Phase == "countdown" ? 0 : 1)
                    .ThenByDescending(r => r.HumanCount)
                    .FirstOrDefault();
                if (pick == null)
                {
                    pick = new LiarRoom("L" + (++_nextId), "빠른 시작 테이블", _timings, _rng);
                    _rooms[pick.Id] = pick;
                }
                var err = SitUnlocked(userId, pick, connectionId);
                if (err != null && pick.HumanCount == 0) _rooms.Remove(pick.Id);
                else Publish(pick);
                return err;
            }
        }

        public string Join(long userId, string roomId, string connectionId)
        {
            lock (_gate)
            {
                if (roomId == null || !_rooms.TryGetValue(roomId, out var room)) return "방이 없습니다.";
                var err = SitUnlocked(userId, room, connectionId);
                Publish(room);
                return err;
            }
        }

        string SitUnlocked(long userId, LiarRoom room, string connectionId)
        {
            if (RoomOf(userId) != null) return "이미 다른 방에 앉아 있습니다.";
            if (!room.MakeRoomForHuman()) return "방이 꽉 찼습니다.";
            var user = _db.GetUser(userId);
            if (user == null) return "계정을 찾을 수 없습니다.";
            // 예전 멀티(주사위 포커) 테이블에 앉아 있으면 머니가 거기 있다(stacks는 한 사람에 한 줄)
            if (_db.HasStack(userId)) return "다른 테이블에 앉아 있습니다. 그 테이블에서 먼저 일어나 주세요.";
            if (!_db.BuyIn(userId, room.Id, LiarRoom.BuyIn)) return $"사이버머니가 {LiarRoom.BuyIn:N0} 이상 있어야 앉을 수 있습니다.";
            room.Sit(new LiarSeat
            {
                UserId = userId, Name = user.Name, Character = user.Character, Hat = _db.EquippedItem(userId) ?? "",
                Stack = LiarRoom.BuyIn, SavedStack = LiarRoom.BuyIn, ConnectionId = connectionId,
            });
            _notify?.Profile(userId);
            return null;
        }

        public string Leave(long userId)
        {
            lock (_gate)
            {
                var room = RoomOf(userId);
                if (room == null) return "방에 있지 않습니다.";
                var seat = room.Find(userId);
                if (room.Leave(seat, _now())) CashOut(seat, "방에서 나왔습니다.");
                else _notify?.Left(userId, "게임 중에 나와 탈락으로 쳤습니다. 게임이 끝나면 남은 머니가 돌아옵니다.");
                Publish(room);
                return null;
            }
        }

        public string Emote(long userId, int kind) => Act(userId, (r, s, now) => r.Emote(s, kind, now));
        public string Bid(long userId, int quantity, int face) => Act(userId, (r, s, now) => r.Bid(s, quantity, face, now));
        public string Challenge(long userId) => Act(userId, (r, s, now) => r.Challenge(s, now));
        public string Pull(long userId) => Act(userId, (r, s, now) => r.Pull(s, now));
        public string SideBet(long userId, long target, int amount) => Act(userId, (r, s, now) => r.SideBet(s, target, amount));
        public string Double(long userId) => Act(userId, (r, s, now) => r.Double(s, now));
        public string Cash(long userId) => Act(userId, (r, s, now) => r.Cash(s, now));

        string Act(long userId, Func<LiarRoom, LiarSeat, DateTimeOffset, string> action)
        {
            lock (_gate)
            {
                var room = RoomOf(userId);
                if (room == null) return "방에 있지 않습니다.";
                var seat = room.Find(userId);
                if (seat.Leaving) return "나가는 중입니다.";
                var err = action(room, seat, _now());
                if (err == null) Publish(room);
                return err;
            }
        }

        public void Connected(long userId, string connectionId)
        {
            lock (_gate)
            {
                var room = RoomOf(userId);
                if (room == null) return;
                var seat = room.Find(userId);
                if (seat.Leaving) return;
                seat.ConnectionId = connectionId;
                Publish(room);
            }
        }

        public void Disconnected(long userId, string connectionId)
        {
            lock (_gate)
            {
                var room = RoomOf(userId);
                if (room == null) return;
                var seat = room.Find(userId);
                if (seat.ConnectionId != connectionId) return;
                seat.ConnectionId = null;
                // 게임 밖이면 바로 일어난다. 게임 중이면 자리는 남겨 시간 초과로 진행한다(돌아오면 이어서)
                if (!room.InGame(seat) && seat.SideAmount == 0 && room.Leave(seat, _now())) CashOut(seat, null);
                Publish(room);
            }
        }

        public void Tick()
        {
            lock (_gate)
            {
                var now = _now();
                foreach (var room in _rooms.Values.ToList())
                {
                    string before = room.Phase + room.Deadline;
                    var removed = room.Tick(now);
                    foreach (var seat in removed)
                        CashOut(seat, seat.Stack < LiarRoom.Ante ? "머니가 모자라 방에서 나왔습니다." : "방에서 나왔습니다.");
                    if (before != room.Phase + room.Deadline || removed.Count > 0) Publish(room);
                }
            }
        }

        /// <summary>테스트용: 방을 직접 본다.</summary>
        public LiarRoom RoomFor(long userId)
        {
            lock (_gate) return RoomOf(userId);
        }

        void CashOut(LiarSeat seat, string reason)
        {
            _db.UpdateStack(seat.UserId, seat.Stack);
            _db.CashOut(seat.UserId);
            if (reason != null) _notify?.Left(seat.UserId, reason);
            _notify?.Profile(seat.UserId);
        }

        void Publish(LiarRoom room)
        {
            _db.RecordRounds(room.TakeRecords());
            foreach (var s in room.Seats)
            {
                if (s.IsBot || s.Stack == s.SavedStack) continue;
                _db.UpdateStack(s.UserId, s.Stack);
                s.SavedStack = s.Stack;
            }
            // 사람이 모두 떠나면 봇만 남은 방은 닫는다
            if (room.HumanCount == 0) _rooms.Remove(room.Id);
            else _notify?.Room(room, _now());

            var lobby = LobbyUnlocked();
            var sig = string.Join("|", lobby.Select(o => o.ToString()));
            if (sig != _lobbySignature)
            {
                _lobbySignature = sig;
                _notify?.Lobby(lobby);
            }
        }
    }
}
