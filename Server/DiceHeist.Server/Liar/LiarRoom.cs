using DiceHeist.Core;

namespace DiceHeist.Server.Liar
{
    /// <summary>라이어 다이스 방의 시간 제한. 테스트는 짧게 바꿔 쓴다.</summary>
    public sealed class LiarTimings
    {
        public TimeSpan Countdown = TimeSpan.FromSeconds(4);
        /// <summary>부르기·거짓말 한 차례.</summary>
        public TimeSpan Turn = TimeSpan.FromSeconds(25);
        /// <summary>컵을 열고 세어 보여 주는 시간.</summary>
        public TimeSpan Reveal = TimeSpan.FromSeconds(4);
        /// <summary>진 사람이 레버를 당길 때까지(넘기면 자동으로 당긴다).</summary>
        public TimeSpan Lever = TimeSpan.FromSeconds(12);
        /// <summary>레버 결과(떨어졌는지)를 보여 주는 시간.</summary>
        public TimeSpan Fall = TimeSpan.FromSeconds(3.5);
        /// <summary>이긴 사람이 더블 오어 낫싱을 고르는 시간(넘기면 챙긴다).</summary>
        public TimeSpan Double = TimeSpan.FromSeconds(12);
        public TimeSpan DoubleRoll = TimeSpan.FromSeconds(3);
        public TimeSpan Result = TimeSpan.FromSeconds(7);
        /// <summary>사람이 있을 때 봇이 이 인원까지 자리를 채운다(0이면 봇 없음). 설정 Liar:BotFillTo</summary>
        public int BotFillTo = LiarRoom.FillTo;
    }

    public sealed class LiarSeat
    {
        public long UserId;
        public string Name;
        public string Character;
        /// <summary>쓰고 있는 꾸미기(가챠 아이템 id). 없으면 빈 문자열.</summary>
        public string Hat = "";
        /// <summary>이 테이블에 올려 둔 머니.</summary>
        public int Stack;
        public int SavedStack;
        public string ConnectionId;
        public bool Leaving;
        /// <summary>이번 게임 LiarGame에서의 자리 번호. 안 끼었으면 -1.</summary>
        public int GameIndex = -1;
        /// <summary>관전 베팅: 건 자리(이 방 Seats 기준 사용자 id), 금액, 배당(건 때의 생존자 수).</summary>
        public long SideTarget;
        public int SideAmount, SideOdds;
        /// <summary>빈자리를 채우는 봇(화면에 "봇"으로 보인다). 머니는 카지노 돈이라 DB에 남지 않고, 사용자 id는 음수.</summary>
        public bool IsBot;
        /// <summary>봇은 늘 연결된 것으로 친다.</summary>
        public bool Connected => IsBot || ConnectionId != null;
        public int EmoteKind = -1, EmoteSeq;
        public DateTimeOffset EmoteAt;
    }

    /// <summary>
    /// 함정 의자 라이어 다이스 방(최대 6명). 규칙은 Core의 LiarGame이 하고, 여기서는 참가비·판돈·시간 제한·관전 베팅·더블 오어 낫싱과
    /// 누가 무엇을 볼 수 있는지(남의 주사위는 컵을 열 때만)를 다룬다. 스레드 안전하지 않다: LiarManager가 잠금 안에서만 부른다.
    /// 단계: waiting → countdown → bid(차례마다) → reveal → lever → fall → (게임 안 끝났으면 bid…) → double ⇄ doubleRoll → result → waiting
    /// </summary>
    public sealed class LiarRoom
    {
        public const int MaxSeats = 6;
        /// <summary>앉을 때 머니에서 테이블로 가져가는 금액.</summary>
        public const int BuyIn = 50_000;
        /// <summary>게임마다 내는 참가비. 모두의 참가비가 판돈이 되고, 마지막 생존자가 가져간다.</summary>
        public const int Ante = 5_000;
        /// <summary>관전 베팅에 걸 수 있는 금액.</summary>
        public static readonly int[] SideBetAmounts = { 1_000, 5_000, 10_000 };
        /// <summary>더블 오어 낫싱: 금고 주사위 4~6이면 두 배, 1~3이면 0. 연달아 최대 몇 번.</summary>
        public const int MaxDoubles = 3, DoubleWinFrom = 4;
        /// <summary>사람이 한 명이라도 앉아 있으면, 게임을 시작할 때 이 인원이 될 때까지 봇이 자리를 채운다(2026-10-19 빠른 시작). 사람 자리 2개는 늘 남긴다.</summary>
        public const int FillTo = 4;
        /// <summary>봇이 생각하는 시간(차례·레버). 사람처럼 잠깐 뜸을 들인다.</summary>
        public static readonly TimeSpan BotThink = TimeSpan.FromSeconds(1.8), BotPull = TimeSpan.FromSeconds(1.6);
        static readonly string[] BotAnimals = { "raccoon", "cat", "fox", "bear", "rabbit" };

        public static readonly string[] Emotes = { "👍", "😱", "ㅋㅋ", "🤥", "🙏", "거짓말!" };
        public static readonly TimeSpan EmoteShown = TimeSpan.FromSeconds(3), EmoteGap = TimeSpan.FromSeconds(2);

        public readonly string Id;
        public readonly string Name;
        public readonly List<LiarSeat> Seats = new List<LiarSeat>();
        public string Phase { get; private set; } = "waiting";
        public DateTimeOffset? Deadline { get; private set; }
        /// <summary>몇 번째 게임(생존자가 정해질 때까지가 한 게임).</summary>
        public int GameNo { get; private set; }

        readonly LiarTimings _t;
        readonly IRandom _rng;
        readonly List<string> _log = new List<string>();
        LiarGame _game;
        List<LiarSeat> _inGame = new List<LiarSeat>();
        int _pot;
        int _starter;
        LiarPull _lastPull;
        /// <summary>더블 오어 낫싱에 걸려 있는 금액(이긴 사람이 챙기기 전).</summary>
        int _vault, _doubles, _lastVaultRoll;
        LiarSeat _winner;
        string _banner;
        readonly List<Data.RoundRecord> _records = new List<Data.RoundRecord>();

        public LiarRoom(string id, string name, LiarTimings timings, IRandom rng)
        {
            Id = id;
            Name = name;
            _t = timings;
            _rng = rng;
        }

        public LiarSeat Find(long userId) => Seats.FirstOrDefault(s => s.UserId == userId);
        public LiarGame Game => _game;
        public int Pot => _pot;
        public int Vault => _vault;

        public List<Data.RoundRecord> TakeRecords()
        {
            var list = _records.Where(r => r.UserId > 0).ToList(); // 봇(음수 id)은 기록하지 않는다
            _records.Clear();
            return list;
        }

        /// <summary>승부가 아직 나지 않은 게임에 끼어 있는지(부르기·컵 열기·레버 중). 이때 나가면 탈락으로 친다.</summary>
        public bool InGame(LiarSeat s) => s.GameIndex >= 0 && _game != null && Playing;

        bool Playing => Phase == "bid" || Phase == "reveal" || Phase == "lever" || Phase == "fall";
        LiarPlayer P(LiarSeat s) => s.GameIndex >= 0 && _game != null ? _game.Players[s.GameIndex] : null;
        LiarSeat SeatOf(int index) => index >= 0 && index < _inGame.Count ? _inGame[index] : null;

        void Log(string line)
        {
            _log.Insert(0, line);
            if (_log.Count > 10) _log.RemoveAt(_log.Count - 1);
        }

        // ---------- 앉고 일어나기 ----------

        public void Sit(LiarSeat seat)
        {
            Seats.Add(seat);
            Log($"{seat.Name} 님이 앉았다");
        }

        public string Emote(LiarSeat seat, int kind, DateTimeOffset now)
        {
            if (kind < 0 || kind >= Emotes.Length) return "없는 감정 표현입니다.";
            if (seat.EmoteSeq > 0 && now - seat.EmoteAt < EmoteGap) return "잠시 뒤에 다시 보내 주세요.";
            seat.EmoteKind = kind;
            seat.EmoteAt = now;
            seat.EmoteSeq++;
            return null;
        }

        /// <summary>나가기. 게임 밖이면 바로 일어나고 true. 게임 중이면 탈락으로 치고(참가비는 잃는다) 게임이 끝나면 일어난다.</summary>
        public bool Leave(LiarSeat seat, DateTimeOffset now)
        {
            // 아직 살아서 게임 중이거나, 이긴 사람이 금고를 고르는 중이거나, 정산 전 관전 베팅이 있으면 바로 못 일어난다
            bool busy = (InGame(seat) && P(seat).Alive) || (seat == _winner && (Phase == "double" || Phase == "doubleRoll")) || (seat.SideAmount > 0 && Playing);
            if (!busy)
            {
                Seats.Remove(seat);
                seat.GameIndex = -1;
                Log($"{seat.Name} 님이 일어났다");
                return true;
            }
            seat.Leaving = true;
            if (InGame(seat) && P(seat).Alive)
            {
                _game.Forfeit(seat.GameIndex);
                Log($"{seat.Name} 님이 나가서 탈락");
                AfterForfeit(now);
            }
            else if (seat == _winner && (Phase == "double" || Phase == "doubleRoll")) Collect(now);
            return false;
        }

        // ---------- 진행 ----------

        public List<LiarSeat> Tick(DateTimeOffset now)
        {
            var removed = new List<LiarSeat>();
            switch (Phase)
            {
                case "waiting":
                case "countdown":
                {
                    ManageBots();
                    var ready = Ready();
                    if (ready.Count < 2) { Phase = "waiting"; Deadline = null; }
                    else if (Phase == "waiting") { Phase = "countdown"; Deadline = now + _t.Countdown; }
                    else if (now >= Deadline) StartGame(now, ready);
                    break;
                }
                case "bid":
                    if (SeatOf(_game.Turn).IsBot && now >= Deadline - _t.Turn + BotThink && now < Deadline) BotTurn(now);
                    else if (now >= Deadline)
                    {
                        // 시간 초과: 거짓말을 외칠 수 있으면 외치고, 아니면 가장 작게 올려 부른다
                        var s = SeatOf(_game.Turn);
                        Log($"{s.Name} · 시간 초과");
                        if (_game.CanChallenge) DoChallenge(now);
                        else DoRaise(_game.MinimumRaise(), now);
                    }
                    break;
                case "reveal":
                    if (now >= Deadline) { Phase = "lever"; Deadline = now + _t.Lever; }
                    break;
                case "lever":
                    if (now >= Deadline || (SeatOf(_game.PendingPull)?.IsBot == true && now >= Deadline - _t.Lever + BotPull)) DoPull(now);
                    break;
                case "fall":
                    if (now >= Deadline) NextRoundOrWin(now);
                    break;
                case "double":
                    if (_winner.IsBot && now >= Deadline - _t.Double + BotPull)
                    {
                        // 봇도 가끔 금고 주사위를 굴린다(사람들이 봇의 운도 구경하게)
                        if (_doubles == 0 && _rng.NextDouble() < 0.35) Double(_winner, now);
                        else Collect(now);
                    }
                    else if (now >= Deadline) Collect(now);
                    break;
                case "doubleRoll":
                    if (now >= Deadline)
                    {
                        if (_vault > 0 && _doubles < MaxDoubles && !_winner.Leaving && _winner.Connected) { Phase = "double"; Deadline = now + _t.Double; }
                        else Collect(now);
                    }
                    break;
                case "result":
                    if (now >= Deadline) removed = EndGame();
                    break;
            }
            return removed;
        }

        List<LiarSeat> Ready() => Seats.Where(s => s.Connected && !s.Leaving && s.Stack >= Ante).ToList();

        // ---------- 봇 ----------

        public int HumanCount => Seats.Count(s => !s.IsBot);
        int _botNo;

        /// <summary>
        /// 게임 사이에 봇 자리를 맞춘다: 사람이 없으면 봇을 모두 내보내고, 사람이 있으면 FillTo명이 될 때까지 채우고 넘치면 줄인다.
        /// 머니가 바닥난 봇은 카지노가 다시 채워 준다.
        /// </summary>
        void ManageBots()
        {
            if (HumanCount == 0)
            {
                Seats.RemoveAll(s => s.IsBot);
                return;
            }
            int fill = Math.Min(_t.BotFillTo, MaxSeats - 2); // 사람 자리 2개는 늘 남긴다
            while (Seats.Count > fill && Seats.Any(s => s.IsBot))
                Seats.Remove(Seats.Last(s => s.IsBot));
            while (Seats.Count < fill)
            {
                _botNo++;
                string animal = BotAnimals[(_botNo - 1) % BotAnimals.Length];
                Seats.Add(new LiarSeat
                {
                    UserId = -_botNo, IsBot = true, Name = $"봇 {Characters.NameOf(animal)}{_botNo}", Character = animal,
                    Stack = BuyIn, SavedStack = BuyIn,
                });
            }
            foreach (var b in Seats.Where(s => s.IsBot && s.Stack < Ante)) b.Stack = BuyIn;
        }

        /// <summary>사람이 앉으려는데 자리가 꽉 찼으면, 게임에 끼지 않은 봇 하나를 내보내 자리를 만든다.</summary>
        public bool MakeRoomForHuman()
        {
            if (Seats.Count < MaxSeats) return true;
            var bot = Seats.LastOrDefault(s => s.IsBot && !InGame(s) && s != _winner);
            if (bot == null) return false;
            Seats.Remove(bot);
            return true;
        }

        void BotTurn(DateTimeOffset now)
        {
            var (challenge, bid) = LiarBot.Decide(_game, _game.Turn, _rng);
            if (!challenge && _game.CheckBid(bid) != null) challenge = _game.CanChallenge;
            if (challenge) DoChallenge(now);
            else DoRaise(bid, now);
        }

        void StartGame(DateTimeOffset now, List<LiarSeat> ready)
        {
            int start = _starter++ % ready.Count;
            _inGame = ready.Skip(start).Concat(ready.Take(start)).ToList();
            foreach (var s in Seats) { s.GameIndex = -1; s.SideAmount = 0; s.SideOdds = 0; s.SideTarget = 0; }
            for (int i = 0; i < _inGame.Count; i++)
            {
                _inGame[i].GameIndex = i;
                _inGame[i].Stack -= Ante;
            }
            _pot = Ante * _inGame.Count;
            _game = new LiarGame(_inGame.Select(s => s.Name).ToList(), _rng);
            _game.StartRound();
            _lastPull = null;
            _winner = null;
            _vault = _doubles = _lastVaultRoll = 0;
            _banner = null;
            GameNo++;
            Log($"{GameNo}번째 게임 · 참가비 {Ante:N0}씩 · 판돈 {_pot:N0} · {_inGame[0].Name}부터");
            Phase = "bid";
            Deadline = now + _t.Turn;
        }

        void DoRaise(LiarBid bid, DateTimeOffset now)
        {
            var s = SeatOf(_game.Turn);
            _game.Raise(bid);
            Log($"{s.Name} · \"{bid.Face} 눈이 {bid.Quantity}개 이상!\"");
            Deadline = now + _t.Turn;
        }

        void DoChallenge(DateTimeOffset now)
        {
            var r = _game.Challenge();
            var c = SeatOf(r.Challenger);
            var b = SeatOf(r.Bidder);
            Log($"{c.Name} · \"거짓말!\" → 열어 보니 {r.Bid.Face} 눈(1 포함) {r.Count}개 · {(r.BidTrue ? $"{b.Name} 말이 맞았다" : $"{b.Name} 거짓말 들통")}");
            Phase = "reveal";
            Deadline = now + _t.Reveal;
        }

        void DoPull(DateTimeOffset now)
        {
            var pull = _game.Pull();
            _lastPull = pull;
            var s = SeatOf(pull.Player);
            Log(pull.Fell
                ? $"{s.Name} · 레버 {pull.PullNo}번째 · 철컥… 풍덩! 탈락 ({pull.Chance:P0} 확률이었다)"
                : $"{s.Name} · 레버 {pull.PullNo}번째 · 철컥… 살았다! (떨어질 확률 {pull.Chance:P0})");
            if (pull.Fell) _records.Add(new Data.RoundRecord(s.UserId, false, 0, -1));
            Phase = "fall";
            Deadline = now + _t.Fall;
        }

        void AfterForfeit(DateTimeOffset now)
        {
            var s = _inGame.FirstOrDefault(x => x.Leaving && x.GameIndex >= 0 && !_game.Players[x.GameIndex].Alive);
            if (s != null) _records.Add(new Data.RoundRecord(s.UserId, false, 0, -1));
            if (_game.Over) { Win(now); return; }
            // 레버 차례였던 사람이 나갔으면 바로 다음 판
            if ((Phase == "reveal" || Phase == "lever") && _game.PendingPull < 0) NextRoundOrWin(now);
        }

        void NextRoundOrWin(DateTimeOffset now)
        {
            if (_game.Over) { Win(now); return; }
            _game.StartRound();
            Log($"새 판 · 모두 다시 굴림 · {SeatOf(_game.Turn).Name}부터");
            Phase = "bid";
            Deadline = now + _t.Turn;
        }

        void Win(DateTimeOffset now)
        {
            _winner = SeatOf(_game.Winner.Index);
            _vault = _pot;
            _pot = 0;
            Log($"{_winner.Name} 님이 끝까지 살아남았다! 판돈 {_vault:N0}");
            PaySideBets();
            if (_winner.Leaving || !_winner.Connected) { Collect(now); return; }
            Phase = "double";
            Deadline = now + _t.Double;
        }

        /// <summary>관전 베팅 정산: 이긴 사람에게 건 사람은 건 금액 × 배당(건 때의 생존자 수)을 받는다. 판돈과 상관없는 카지노 돈이다.</summary>
        void PaySideBets()
        {
            foreach (var s in Seats)
            {
                if (s.SideAmount <= 0) continue;
                if (s.SideTarget == _winner.UserId)
                {
                    int pay = s.SideAmount * s.SideOdds;
                    s.Stack += pay;
                    Log($"{s.Name} · 관전 베팅 적중! +{pay:N0} ({s.SideOdds}배)");
                }
                else Log($"{s.Name} · 관전 베팅 실패 −{s.SideAmount:N0}");
            }
        }

        void Collect(DateTimeOffset now)
        {
            if (_winner == null) return;
            _winner.Stack += _vault;
            _records.Add(new Data.RoundRecord(_winner.UserId, true, _vault, -1));
            _banner = _vault > 0
                ? $"{_winner.Name} 승리 · +{_vault:N0}" + (_doubles > 0 ? $" (더블 {_doubles}번 성공)" : "")
                : $"{_winner.Name} · 더블 오어 낫싱 실패 · 판돈을 모두 잃었다";
            Log(_vault > 0 ? $"{_winner.Name} · {_vault:N0} 챙김" : $"{_winner.Name} · 금고 주사위에 다 날렸다");
            Phase = "result";
            Deadline = now + _t.Result;
        }

        List<LiarSeat> EndGame()
        {
            // 봇은 여기서 일어나지 않는다(머니가 바닥나면 ManageBots가 채운다)
            var removed = Seats.Where(s => !s.IsBot && (s.Leaving || !s.Connected || s.Stack < Ante)).ToList();
            foreach (var s in removed)
            {
                Seats.Remove(s);
                Log(s.Stack < Ante && !s.Leaving && s.Connected ? $"{s.Name} 님이 머니가 모자라 일어났다" : $"{s.Name} 님이 일어났다");
            }
            foreach (var s in Seats.Concat(removed)) { s.GameIndex = -1; s.SideAmount = 0; s.SideOdds = 0; s.SideTarget = 0; }
            _game = null;
            _inGame = new List<LiarSeat>();
            _winner = null;
            _vault = 0;
            Phase = "waiting";
            Deadline = null;
            return removed;
        }

        // ---------- 사람의 조작(실패하면 이유) ----------

        bool MyTurn(LiarSeat seat) => Phase == "bid" && InGame(seat) && _game.Turn == seat.GameIndex;

        public string Bid(LiarSeat seat, int quantity, int face, DateTimeOffset now)
        {
            if (!MyTurn(seat)) return "지금은 내 차례가 아닙니다.";
            var bid = new LiarBid(quantity, face);
            var err = _game.CheckBid(bid);
            if (err != null) return err;
            DoRaise(bid, now);
            return null;
        }

        public string Challenge(LiarSeat seat, DateTimeOffset now)
        {
            if (!MyTurn(seat)) return "지금은 내 차례가 아닙니다.";
            if (!_game.CanChallenge) return "아직 아무도 부르지 않았습니다.";
            DoChallenge(now);
            return null;
        }

        public string Pull(LiarSeat seat, DateTimeOffset now)
        {
            if ((Phase != "lever" && Phase != "reveal") || !InGame(seat) || _game.PendingPull != seat.GameIndex) return "지금은 레버를 당길 차례가 아닙니다.";
            DoPull(now);
            return null;
        }

        /// <summary>관전 베팅: 게임에서 떨어졌거나 이번 게임에 안 낀 사람이, 살아 있는 사람 중 누가 이길지 건다. 게임마다 한 번.</summary>
        public string SideBet(LiarSeat seat, long targetUserId, int amount)
        {
            if (_game == null || _game.Over || !(Phase == "bid" || Phase == "reveal" || Phase == "lever" || Phase == "fall")) return "지금은 관전 베팅을 할 수 없습니다.";
            if (InGame(seat) && P(seat).Alive) return "아직 살아 있으면 관전 베팅을 할 수 없습니다.";
            if (seat.SideAmount > 0) return "이번 게임에는 이미 걸었습니다.";
            if (Array.IndexOf(SideBetAmounts, amount) < 0) return "걸 수 있는 금액이 아닙니다.";
            if (seat.Stack < amount) return "테이블 머니가 모자랍니다.";
            var target = _inGame.FirstOrDefault(s => s.UserId == targetUserId);
            if (target == null || !P(target).Alive) return "살아 있는 사람에게만 걸 수 있습니다.";
            seat.Stack -= amount;
            seat.SideAmount = amount;
            seat.SideTarget = targetUserId;
            seat.SideOdds = _game.AliveCount();
            Log($"{seat.Name} · 관전 베팅: {target.Name}에게 {amount:N0} ({seat.SideOdds}배)");
            return null;
        }

        /// <summary>더블 오어 낫싱: 금고 주사위를 굴려 4~6이면 금고가 두 배, 1~3이면 0.</summary>
        public string Double(LiarSeat seat, DateTimeOffset now)
        {
            if (Phase != "double" || seat != _winner) return "지금은 금고 주사위를 굴릴 수 없습니다.";
            int roll = _rng.D6();
            _lastVaultRoll = roll;
            if (roll >= DoubleWinFrom)
            {
                _vault *= 2;
                _doubles++;
                Log($"{seat.Name} · 금고 주사위 {roll}! 두 배 → {_vault:N0}");
            }
            else
            {
                Log($"{seat.Name} · 금고 주사위 {roll}… 금고가 비었다");
                _vault = 0;
            }
            Phase = "doubleRoll";
            Deadline = now + _t.DoubleRoll;
            return null;
        }

        public string Cash(LiarSeat seat, DateTimeOffset now)
        {
            if (Phase != "double" || seat != _winner) return "지금은 챙길 수 없습니다.";
            Collect(now);
            return null;
        }

        // ---------- 화면에 보낼 상태 ----------

        public object Summary() => new
        {
            id = Id, name = Name, players = HumanCount, bots = Seats.Count - HumanCount, maxPlayers = MaxSeats, ante = Ante, buyIn = BuyIn,
            phase = Phase == "waiting" || Phase == "countdown" ? "waiting" : "playing",
        };

        long PhaseMs() => (long)(Phase switch
        {
            "countdown" => _t.Countdown, "bid" => _t.Turn, "reveal" => _t.Reveal, "lever" => _t.Lever, "fall" => _t.Fall,
            "double" => _t.Double, "doubleRoll" => _t.DoubleRoll, "result" => _t.Result, _ => TimeSpan.Zero,
        }).TotalMilliseconds;

        /// <summary>viewer 한 사람이 볼 수 있는 상태. 남의 주사위는 컵을 연 뒤(reveal·lever·fall)에만 보낸다.</summary>
        public object StateFor(LiarSeat viewer, DateTimeOffset now)
        {
            var g = _game;
            bool open = g != null && g.LastReveal != null && (Phase == "reveal" || Phase == "lever" || Phase == "fall");
            var mine = P(viewer);
            bool myTurn = MyTurn(viewer);
            var reveal = open ? g.LastReveal : null;
            return new
            {
                room = new { id = Id, name = Name, ante = Ante, buyIn = BuyIn, emotes = Emotes, sideBets = SideBetAmounts, maxDoubles = MaxDoubles, doubleWinFrom = DoubleWinFrom },
                phase = Phase,
                deadline = Deadline?.ToUnixTimeMilliseconds(),
                phaseMs = PhaseMs(),
                serverNow = now.ToUnixTimeMilliseconds(),
                gameNo = GameNo,
                round = g?.RoundNo ?? 0,
                pot = _pot,
                diceInPlay = g?.DiceInPlay ?? 0,
                bid = g == null || g.Bid.IsNone ? null : new { quantity = g.Bid.Quantity, face = g.Bid.Face, by = Seats.IndexOf(SeatOf(g.Bidder)) },
                reveal = reveal == null ? null : new
                {
                    quantity = reveal.Bid.Quantity, face = reveal.Bid.Face, count = reveal.Count, bidTrue = reveal.BidTrue,
                    bidder = Seats.IndexOf(SeatOf(reveal.Bidder)), challenger = Seats.IndexOf(SeatOf(reveal.Challenger)), loser = Seats.IndexOf(SeatOf(reveal.Loser)),
                },
                // 레버를 당길 사람과 마지막 레버 결과
                lever = g != null && g.PendingPull >= 0 ? Seats.IndexOf(SeatOf(g.PendingPull)) : -1,
                lastPull = _lastPull == null || Phase != "fall" ? null : new
                {
                    seat = Seats.IndexOf(SeatOf(_lastPull.Player)), pullNo = _lastPull.PullNo, chance = _lastPull.Chance, fell = _lastPull.Fell,
                },
                winner = _winner == null ? -1 : Seats.IndexOf(_winner),
                vault = _vault,
                doubles = _doubles,
                vaultRoll = Phase == "doubleRoll" ? _lastVaultRoll : 0,
                mySeat = Seats.IndexOf(viewer),
                banner = Phase == "result" ? _banner : null,
                log = _log.ToArray(),
                seats = Seats.Select(s =>
                {
                    var p = P(s);
                    bool showDice = p != null && p.Alive && (s == viewer || open);
                    return new
                    {
                        userId = s.UserId,
                        isBot = s.IsBot,
                        name = s.Name,
                        character = s.Character,
                        hat = s.Hat,
                        stack = s.Stack,
                        connected = s.Connected,
                        leaving = s.Leaving,
                        inGame = p != null,
                        alive = p?.Alive ?? false,
                        pulls = p?.Pulls ?? 0,
                        // 다음에 레버를 당기면 떨어질 확률(모두에게 보인다: 긴장의 원천)
                        fallChance = p != null && p.Alive ? p.NextFallChance : 0,
                        turn = p != null && Phase == "bid" && g.Turn == p.Index,
                        dice = showDice ? p.Dice.ToArray() : null,
                        side = s.SideAmount > 0 ? new { target = Seats.FindIndex(x => x.UserId == s.SideTarget), amount = s.SideAmount, odds = s.SideOdds } : null,
                        emote = s.EmoteSeq > 0 && now - s.EmoteAt < EmoteShown
                            ? new { seq = s.EmoteSeq, text = Emotes[s.EmoteKind], ago = (long)(now - s.EmoteAt).TotalMilliseconds }
                            : null,
                    };
                }).ToArray(),
                me = new
                {
                    myTurn,
                    canChallenge = myTurn && g.CanChallenge,
                    minBid = myTurn ? new { quantity = g.MinimumRaise().Quantity, face = g.MinimumRaise().Face } : null,
                    canPull = g != null && (Phase == "lever" || Phase == "reveal") && mine != null && g.PendingPull == mine.Index,
                    canSideBet = g != null && !g.Over && (Phase == "bid" || Phase == "reveal" || Phase == "lever" || Phase == "fall")
                        && viewer.SideAmount == 0 && (mine == null || !mine.Alive),
                    canDouble = Phase == "double" && viewer == _winner,
                },
            };
        }
    }
}
