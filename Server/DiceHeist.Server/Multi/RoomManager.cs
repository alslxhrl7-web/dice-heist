using System.Security.Cryptography;
using DiceHeist.Core;
using DiceHeist.Server.Data;

namespace DiceHeist.Server.Multi
{
    /// <summary>서버가 화면 쪽에 알릴 일. SignalR 구현은 HubNotifier, 테스트에서는 가짜를 쓴다.</summary>
    public interface IRoomNotifier
    {
        void Room(Room room, DateTimeOffset now);
        void Lobby(object[] rooms);
        void Profile(long userId, bool inRoom);
        void Left(long userId, string reason);
    }

    /// <summary>
    /// 방 목록과 누가 어느 방에 있는지. 모든 조작을 잠금 하나 안에서 처리한다(최대 30명 규모라 충분하다).
    /// 머니가 움직이는 일(앉기, 일어나기, stack 저장)은 여기서 Db를 부른다.
    /// </summary>
    public sealed class RoomManager
    {
        readonly object _gate = new object();
        readonly Dictionary<string, Room> _rooms = new Dictionary<string, Room>();
        readonly Db _db;
        readonly MultiTimings _timings;
        readonly Func<DateTimeOffset> _now;
        readonly IRandom _rng;
        IRoomNotifier _notify;
        int _nextId;
        string _lobbySignature = "";

        public RoomManager(Db db, MultiTimings timings, Func<DateTimeOffset> now = null, IRandom rng = null)
        {
            _db = db;
            _timings = timings;
            _now = now ?? (() => DateTimeOffset.UtcNow);
            _rng = rng ?? new SeededRandom(BitConverter.ToInt64(RandomNumberGenerator.GetBytes(8)));
        }

        public void SetNotifier(IRoomNotifier notifier) => _notify = notifier;

        public object[] Lobby()
        {
            lock (_gate) return LobbyUnlocked();
        }

        object[] LobbyUnlocked() => _rooms.Values.OrderBy(r => r.Id).Select(r => r.Summary()).ToArray();

        /// <summary>소개 페이지에 보여 줄 숫자: 열린 방 수와 앉아 있는 사람 수.</summary>
        public (int rooms, int players) Counts()
        {
            lock (_gate) return (_rooms.Count, _rooms.Values.Sum(r => r.Seats.Count));
        }

        /// <summary>앉아 있는 동안 캐릭터를 바꾸면 자리 얼굴도 바로 바꾼다.</summary>
        public void CharacterChanged(long userId, string character)
        {
            lock (_gate)
            {
                var room = RoomOf(userId);
                if (room == null) return;
                room.Find(userId).Character = character;
                Publish(room);
            }
        }

        /// <summary>감정 표현(Room.Emotes의 번호)을 보낸다.</summary>
        public string Emote(long userId, int kind) => Act(userId, (room, seat, now) => room.Emote(seat, kind, now));

        public bool IsInRoom(long userId)
        {
            lock (_gate) return RoomOf(userId) != null;
        }

        Room RoomOf(long userId) => _rooms.Values.FirstOrDefault(r => r.Find(userId) != null);

        // ---------- 방 ----------

        public string Create(long userId, string roomName, string connectionId)
        {
            lock (_gate)
            {
                roomName = (roomName ?? "").Trim();
                if (roomName.Length == 0) roomName = "다이스 테이블";
                if (roomName.Length > 20) roomName = roomName.Substring(0, 20);
                var room = new Room((++_nextId).ToString(), roomName, _timings, _rng);
                _rooms[room.Id] = room;
                var err = SitUnlocked(userId, room, connectionId);
                if (err != null) _rooms.Remove(room.Id);
                Publish(room);
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

        string SitUnlocked(long userId, Room room, string connectionId)
        {
            if (RoomOf(userId) != null) return "이미 다른 방에 앉아 있습니다.";
            if (room.Seats.Count >= Room.MaxSeats) return "방이 꽉 찼습니다.";
            var user = _db.GetUser(userId);
            if (user == null) return "계정을 찾을 수 없습니다.";
            if (_db.HasStack(userId)) return "다른 테이블(라이어 다이스)에 앉아 있습니다. 그 테이블에서 먼저 일어나 주세요.";
            if (!_db.BuyIn(userId, room.Id, Room.BuyIn)) return $"사이버머니가 {Room.BuyIn:N0} 이상 있어야 앉을 수 있습니다.";
            room.Sit(new RoomSeat
            {
                UserId = userId, Name = user.Name, Character = user.Character, Stack = Room.BuyIn, SavedStack = Room.BuyIn, ConnectionId = connectionId,
            });
            _notify?.Profile(userId, true);
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
                else _notify?.Left(userId, "이번 판을 폴드하고 방에서 나왔습니다. 판이 끝나면 칩이 돌아옵니다.");
                Publish(room);
                return null;
            }
        }

        // ---------- 판 조작 ----------

        public string Reroll(long userId, bool[] held) => Act(userId, (room, seat, now) => room.Reroll(seat, held, now));
        public string Stand(long userId) => Act(userId, (room, seat, now) => room.Stand(seat, now));
        public string Bet(long userId, string action, int raiseTo = 0) => Act(userId, (room, seat, now) => room.Bet(seat, action, now, raiseTo));
        public string SitOut(long userId) => Act(userId, (room, seat, now) => room.SitOut(seat));
        public string Back(long userId) => Act(userId, (room, seat, now) => room.Back(seat));

        string Act(long userId, Func<Room, RoomSeat, DateTimeOffset, string> action)
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

        // ---------- 연결 ----------

        /// <summary>다시 접속하면 앉아 있던 자리를 이어받는다.</summary>
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

        /// <summary>연결이 끊기면, 판 밖이면 바로 일어나고 판 중이면 자리는 남겨 시간 초과로 진행한다.</summary>
        public void Disconnected(long userId, string connectionId)
        {
            lock (_gate)
            {
                var room = RoomOf(userId);
                if (room == null) return;
                var seat = room.Find(userId);
                if (seat.ConnectionId != connectionId) return; // 이미 새 연결로 넘어감
                seat.ConnectionId = null;
                if (!room.InHand(seat) && room.Leave(seat, _now())) CashOut(seat, null);
                Publish(room);
            }
        }

        // ---------- 시간 ----------

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
                        CashOut(seat, seat.Stack < room.Ante ? "칩이 모자라 방에서 나왔습니다." : "방에서 나왔습니다.");
                    if (before != room.Phase + room.Deadline || removed.Count > 0) Publish(room);
                }
            }
        }

        // ---------- 도우미 ----------

        void CashOut(RoomSeat seat, string reason)
        {
            _db.UpdateStack(seat.UserId, seat.Stack);
            _db.CashOut(seat.UserId);
            if (reason != null) _notify?.Left(seat.UserId, reason);
            _notify?.Profile(seat.UserId, false);
        }

        /// <summary>방 상태를 앉은 사람들에게 보내고, stack 변화를 저장하고, 빈 방은 지우고, 목록이 바뀌었으면 로비에 알린다.</summary>
        void Publish(Room room)
        {
            _db.RecordRounds(room.TakeRecords());
            foreach (var s in room.Seats)
            {
                if (s.Stack == s.SavedStack) continue;
                _db.UpdateStack(s.UserId, s.Stack);
                s.SavedStack = s.Stack;
            }
            if (room.Seats.Count == 0) _rooms.Remove(room.Id);
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
