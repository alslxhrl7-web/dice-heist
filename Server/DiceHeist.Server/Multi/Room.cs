using DiceHeist.Core;

namespace DiceHeist.Server.Multi
{
    public sealed class MultiTimings
    {
        public TimeSpan Countdown = TimeSpan.FromSeconds(3);
        public TimeSpan Hold = TimeSpan.FromSeconds(30);
        public TimeSpan Bet = TimeSpan.FromSeconds(20);
        public TimeSpan Result = TimeSpan.FromSeconds(6);
    }

    public sealed class RoomSeat
    {
        public long UserId;
        public string Name;
        /// <summary>이 테이블에 올려 둔 머니. 판 중에는 DiceGame의 칩과 같다.</summary>
        public int Stack;
        public int SavedStack;
        /// <summary>지금 연결. 끊기면 null(판 중이면 자리는 남고 시간 초과로 진행된다).</summary>
        public string ConnectionId;
        /// <summary>판 중에 나가기를 눌렀다. 이번 판이 끝나면 일어난다.</summary>
        public bool Leaving;
        /// <summary>이번 판 DiceGame에서의 자리 번호. 판에 안 끼었으면 -1.</summary>
        public int GameIndex = -1;
        /// <summary>자리 비움: 앉아 있지만 판에 끼지 않는다(앤티도 안 낸다). 스스로 쉬거나, 판에 껴서 아무것도 안 하고 넘긴 판이 쌓이면 켜진다.</summary>
        public bool Away;
        /// <summary>판에 껴서 아무 조작 없이 시간 초과로만 넘긴 판이 연달아 몇 번인지.</summary>
        public int IdleRounds;

        public bool Connected => ConnectionId != null;

        /// <summary>마지막으로 보낸 감정 표현(Room.Emotes의 번호)과 때. Seq는 보낼 때마다 늘어 화면이 새것인지 가린다.</summary>
        public int EmoteKind = -1, EmoteSeq;
        public DateTimeOffset EmoteAt;
    }

    /// <summary>
    /// 멀티 방 하나. 규칙 판단은 전부 Core(DiceGame)가 하고, 여기서는 순서·시간 제한·누가 무엇을 볼 수 있는지만 다룬다.
    /// 판마다 앉아 있는 사람으로 DiceGame을 새로 만든다(선은 판마다 한 자리씩 돈다).
    /// 스레드 안전하지 않다: RoomManager가 잠금 안에서만 부른다.
    /// </summary>
    public sealed class Room
    {
        /// <summary>방 하나에 앉을 수 있는 최대 인원. 2명이 모이면 시작하고, 판 중에 들어온 사람은 다음 판부터 낀다.</summary>
        public const int MaxSeats = 6;
        /// <summary>앉을 때 머니에서 테이블로 가져가는 금액(앤티 50번분).</summary>
        public const int BuyIn = 50_000;
        /// <summary>
        /// 이만큼 연달아 아무 조작 없이 판을 넘기면 자리 비움으로 돌린다. 탭을 열어 둔 채 자리를 뜨면
        /// 굴리기는 자동 확정·베팅은 자동 폴드되면서 판마다 앤티만 나가 바이인을 다 잃었다.
        /// </summary>
        public const int AwayAfterIdleRounds = 2;

        /// <summary>방에서 보낼 수 있는 짧은 감정 표현. 자유 채팅은 두지 않는다(욕설·도배 관리가 필요 없게).</summary>
        public static readonly string[] Emotes = { "👍", "😱", "ㅋㅋ", "😎", "🙏", "빨리요!" };
        /// <summary>감정 표현이 자리 위에 떠 있는 시간과, 한 사람이 다시 보낼 수 있을 때까지의 간격.</summary>
        public static readonly TimeSpan EmoteShown = TimeSpan.FromSeconds(3), EmoteGap = TimeSpan.FromSeconds(2);

        public static TableConfig Config => new TableConfig
        {
            Name = "멀티", Ante = 1000, RaiseMultiplier = 3, MaxRerolls = 2, ChipCap = int.MaxValue,
        };

        public readonly string Id;
        public readonly string Name;
        public readonly List<RoomSeat> Seats = new List<RoomSeat>();

        /// <summary>waiting / countdown / hold / betting / result</summary>
        public string Phase { get; private set; } = "waiting";
        public DateTimeOffset? Deadline { get; private set; }
        public int RoundNo { get; private set; }

        readonly TableConfig _cfg = Config;
        readonly MultiTimings _t;
        readonly IRandom _rng;
        readonly List<string> _log = new List<string>();
        DiceGame _game;
        List<RoomSeat> _inHand = new List<RoomSeat>();
        bool[] _stood = Array.Empty<bool>();
        /// <summary>이번 판에 스스로 무엇이든 했는지(다시 굴리기·확정·베팅). 자리 비움을 가린다.</summary>
        bool[] _acted = Array.Empty<bool>();
        int _dealer;
        string _banner;
        /// <summary>끝났지만 아직 DB에 적지 않은 판 결과. RoomManager가 TakeRecords로 가져간다.</summary>
        readonly List<Data.RoundRecord> _records = new List<Data.RoundRecord>();

        public List<Data.RoundRecord> TakeRecords()
        {
            var list = _records.ToList();
            _records.Clear();
            return list;
        }

        public Room(string id, string name, MultiTimings timings, IRandom rng)
        {
            Id = id;
            Name = name;
            _t = timings;
            _rng = rng;
        }

        public int Ante => _cfg.Ante;
        public RoomSeat Find(long userId) => Seats.FirstOrDefault(s => s.UserId == userId);

        /// <summary>지금 진행 중인 판에 끼어 있는지(굴리기·베팅 중).</summary>
        public bool InHand(RoomSeat s) => s.GameIndex >= 0 && (Phase == "hold" || Phase == "betting");

        PlayerState P(RoomSeat s) => s.GameIndex >= 0 && _game != null ? _game.Players[s.GameIndex] : null;
        RoomSeat SeatOf(PlayerState p) => p == null ? null : _inHand[p.Index];

        void Log(string line)
        {
            _log.Insert(0, line);
            if (_log.Count > 8) _log.RemoveAt(_log.Count - 1);
        }

        string Times(int amount) => $"{amount / (double)_cfg.Ante:0.#}배";

        // ---------- 앉고 일어나기 ----------

        public void Sit(RoomSeat seat)
        {
            Seats.Add(seat);
            Log($"{seat.Name} 님이 앉았다");
        }

        /// <summary>감정 표현을 보낸다. 판 중이든 아니든, 앉아 있으면 누구나.</summary>
        public string Emote(RoomSeat seat, int kind, DateTimeOffset now)
        {
            if (kind < 0 || kind >= Emotes.Length) return "없는 감정 표현입니다.";
            if (seat.EmoteSeq > 0 && now - seat.EmoteAt < EmoteGap) return "잠시 뒤에 다시 보내 주세요.";
            seat.EmoteKind = kind;
            seat.EmoteAt = now;
            seat.EmoteSeq++;
            return null;
        }

        /// <summary>나가기. 판 밖이면 바로 일어나고 true(머니로 돌려줄 것). 판 중이면 이번 판을 포기하고 끝나면 일어난다.</summary>
        public bool Leave(RoomSeat seat, DateTimeOffset now)
        {
            if (!InHand(seat))
            {
                Seats.Remove(seat);
                seat.GameIndex = -1;
                Log($"{seat.Name} 님이 일어났다");
                return true;
            }
            seat.Leaving = true;
            if (Phase == "hold") _stood[seat.GameIndex] = true;
            Log($"{seat.Name} 님이 나가는 중 (이번 판은 폴드)");
            Advance(now);
            return false;
        }

        // ---------- 진행 ----------

        /// <summary>시간이 지나 넘어갈 일을 처리한다. 일어난 자리(머니로 돌려줄 것)를 돌려준다.</summary>
        public List<RoomSeat> Tick(DateTimeOffset now)
        {
            var removed = new List<RoomSeat>();
            switch (Phase)
            {
                case "waiting":
                case "countdown":
                {
                    var ready = ReadySeats();
                    if (ready.Count < 2)
                    {
                        Phase = "waiting";
                        Deadline = null;
                    }
                    else if (Phase == "waiting")
                    {
                        Phase = "countdown";
                        Deadline = now + _t.Countdown;
                    }
                    else if (now >= Deadline) StartRound(now, ready);
                    break;
                }
                case "hold":
                    if (now >= Deadline)
                    {
                        for (int i = 0; i < _stood.Length; i++) _stood[i] = true;
                        Advance(now);
                    }
                    break;
                case "betting":
                    if (now >= Deadline && !_game.Betting.IsDone)
                    {
                        var s = SeatOf(_game.Betting.Current);
                        Log($"{s.Name} · 시간 초과");
                        ApplyBet(BetAction.Fold);
                        Advance(now);
                    }
                    break;
                case "result":
                    if (now >= Deadline) removed = EndRound();
                    break;
            }
            return removed;
        }

        List<RoomSeat> ReadySeats() => Seats.Where(s => s.Connected && !s.Leaving && !s.Away && s.Stack >= _cfg.Ante).ToList();

        void StartRound(DateTimeOffset now, List<RoomSeat> ready)
        {
            // 선은 판마다 한 자리씩 돈다: 앉은 순서를 돌려서 0번 자리가 선이 되게 한다
            int start = _dealer++ % ready.Count;
            _inHand = ready.Skip(start).Concat(ready.Take(start)).ToList();
            foreach (var s in Seats) s.GameIndex = -1;
            for (int i = 0; i < _inHand.Count; i++) _inHand[i].GameIndex = i;

            var seats = _inHand.Select(s => new DiceGame.Seat(s.UserId.ToString(), s.Name, s.Stack)).ToList();
            _game = new DiceGame(_cfg, _rng, seats);
            _game.StartRound();
            SyncStacks();
            _stood = new bool[_inHand.Count];
            _acted = new bool[_inHand.Count];
            _banner = null;
            RoundNo++;
            Log($"{RoundNo}판 시작 · 앤티 (참가비) {_cfg.Ante:N0}씩 · 선 {_inHand[0].Name}");
            Phase = "hold";
            Deadline = now + _t.Hold;
        }

        /// <summary>다음 단계로 넘어갈 수 있으면 넘긴다(모두 굴리기를 끝냄 → 베팅, 베팅 끝 → 쇼다운).</summary>
        void Advance(DateTimeOffset now)
        {
            if (Phase == "hold" && _stood.All(x => x))
            {
                _game.BeginBetting();
                Phase = "betting";
                Deadline = now + _t.Bet;
            }
            if (Phase == "betting")
            {
                // 나가는 중인 사람은 차례가 오면 바로 폴드
                while (!_game.Betting.IsDone && SeatOf(_game.Betting.Current).Leaving) ApplyBet(BetAction.Fold);
                if (_game.Betting.IsDone) Showdown(now);
            }
        }

        void ApplyBet(BetAction request, int raiseTo = 0)
        {
            var p = _game.Betting.Current;
            var (act, paid) = _game.Betting.Apply(request, raiseTo > 0 ? raiseTo : _game.RaiseTotal);
            switch (act)
            {
                case BetAction.Fold: Log($"{p.Name} · 폴드 (포기)"); break;
                case BetAction.Call: Log($"{p.Name} · 콜 (따라가기) +{paid:N0} · {Times(p.Contrib)}"); break;
                case BetAction.Raise: Log($"{p.Name} · 레이즈 (올리기) +{paid:N0} · {Times(p.Contrib)}"); break;
            }
            SyncStacks();
        }

        void Showdown(DateTimeOffset now)
        {
            int pot = _game.Pot;
            var res = _game.ResolveShowdown();
            SyncStacks();
            if (res.Uncontested)
            {
                _banner = $"{res.Winners[0].Name} 승리 · 나머지가 모두 폴드 · +{pot:N0}";
                Log($"{res.Winners[0].Name} · 팟 {pot:N0} 획득 (쇼다운 없이)");
            }
            else
            {
                string hand = HandEvaluator.NameOf(res.BestHand.Rank);
                _banner = res.Winners.Count > 1
                    ? $"무승부 · {string.Join(", ", res.Winners.Select(w => w.Name))} · {hand} · {res.Share:N0}씩"
                    : $"{res.Winners[0].Name} 승리 · {hand} · +{pot:N0}";
                Log("쇼다운 · " + string.Join(" / ", res.Hands.Select(h => $"{h.player.Name} {HandEvaluator.NameOf(h.hand.Rank)}")));
            }
            foreach (var p in _game.Players)
            {
                bool won = res.Winners.Contains(p);
                int amount = !won ? 0 : res.Winners.Count > 1 ? res.Share : pot;
                int rank = res.Hands.Where(h => h.player == p).Select(h => (int)h.hand.Rank).DefaultIfEmpty(-1).First();
                _records.Add(new Data.RoundRecord(SeatOf(p).UserId, won, amount, rank));
            }
            Phase = "result";
            Deadline = now + _t.Result;
        }

        List<RoomSeat> EndRound()
        {
            foreach (var s in _inHand)
            {
                if (s.Leaving) continue;
                s.IdleRounds = _acted[s.GameIndex] ? 0 : s.IdleRounds + 1;
                if (s.IdleRounds >= AwayAfterIdleRounds && !s.Away)
                {
                    s.Away = true;
                    Log($"{s.Name} 님이 자리를 비웠다 ({AwayAfterIdleRounds}판 동안 움직임 없음 · 앤티 안 냄)");
                }
            }
            // 나가는 중이거나, 연결이 끊겼거나, 앤티를 낼 수 없는 사람은 일어난다
            var removed = Seats.Where(s => s.Leaving || !s.Connected || s.Stack < _cfg.Ante).ToList();
            foreach (var s in removed)
            {
                Seats.Remove(s);
                Log(s.Stack < _cfg.Ante && !s.Leaving && s.Connected ? $"{s.Name} 님이 칩이 모자라 일어났다" : $"{s.Name} 님이 일어났다");
            }
            foreach (var s in Seats) s.GameIndex = -1;
            foreach (var s in removed) s.GameIndex = -1;
            _game = null;
            _inHand = new List<RoomSeat>();
            Phase = "waiting";
            Deadline = null;
            return removed;
        }

        void SyncStacks()
        {
            foreach (var s in _inHand) s.Stack = _game.Players[s.GameIndex].Chips;
        }

        // ---------- 사람의 조작 (실패하면 이유를 돌려준다) ----------

        public string Reroll(RoomSeat seat, bool[] held, DateTimeOffset now)
        {
            var p = P(seat);
            if (Phase != "hold" || p == null) return "지금은 다시 굴릴 수 없습니다.";
            if (_stood[seat.GameIndex]) return "이미 확정했습니다.";
            if (held == null || held.Length != 5) return "고정할 주사위 정보가 잘못됐습니다.";
            if (held.All(h => h)) return "모두 고정하면 다시 굴릴 수 없습니다.";
            if (!_game.CanReroll(p)) return "다시 굴리기를 다 썼습니다.";
            _game.Reroll(p, held);
            _acted[seat.GameIndex] = true;
            if (!_game.CanReroll(p)) _stood[seat.GameIndex] = true;
            Advance(now);
            return null;
        }

        public string Stand(RoomSeat seat, DateTimeOffset now)
        {
            if (Phase != "hold" || P(seat) == null) return "지금은 확정할 수 없습니다.";
            _stood[seat.GameIndex] = true;
            _acted[seat.GameIndex] = true;
            Advance(now);
            return null;
        }

        /// <param name="raiseTo">레이즈일 때 이번 판 누적 금액. 0이면 기본 크기(앤티 × RaiseMultiplier). 범위 밖이면 Core가 맞춘다.</param>
        public string Bet(RoomSeat seat, string action, DateTimeOffset now, int raiseTo = 0)
        {
            var p = P(seat);
            if (Phase != "betting" || p == null || _game.Betting.Current != p) return "지금은 내 차례가 아닙니다.";
            BetAction request;
            switch (action)
            {
                case "fold": request = BetAction.Fold; break;
                case "call": request = BetAction.Call; break;
                case "raise": request = BetAction.Raise; break;
                default: return "알 수 없는 베팅입니다.";
            }
            _acted[seat.GameIndex] = true;
            ApplyBet(request, raiseTo);
            if (!_game.Betting.IsDone) Deadline = now + _t.Bet;
            Advance(now);
            return null;
        }

        /// <summary>잠깐 쉬기: 다음 판부터 끼지 않는다(지금 판은 그대로 끝까지 한다).</summary>
        public string SitOut(RoomSeat seat)
        {
            if (seat.Away) return "이미 쉬는 중입니다.";
            seat.Away = true;
            Log($"{seat.Name} 님이 잠깐 쉰다");
            return null;
        }

        /// <summary>자리 비움에서 돌아온다. 다음 판부터 다시 낀다.</summary>
        public string Back(RoomSeat seat)
        {
            if (!seat.Away) return "쉬는 중이 아닙니다.";
            seat.Away = false;
            seat.IdleRounds = 0;
            Log($"{seat.Name} 님이 돌아왔다");
            return null;
        }

        // ---------- 화면에 보낼 상태 ----------

        public object Summary() => new
        {
            id = Id,
            name = Name,
            players = Seats.Count,
            maxPlayers = MaxSeats,
            ante = _cfg.Ante,
            buyIn = BuyIn,
            phase = Phase,
        };

        /// <summary>viewer 한 사람이 볼 수 있는 상태. 남의 주사위는 쇼다운에서 공개되기 전까지 보내지 않는다.</summary>
        public object StateFor(RoomSeat viewer, DateTimeOffset now)
        {
            var mine = P(viewer);
            var bet = _game?.Betting;
            var current = Phase == "betting" && bet != null && !bet.IsDone ? bet.Current : null;
            return new
            {
                room = new { id = Id, name = Name, ante = _cfg.Ante, buyIn = BuyIn, raiseTotal = _cfg.Ante * _cfg.RaiseMultiplier, maxRerolls = _cfg.MaxRerolls, emotes = Emotes },
                phase = Phase,
                deadline = Deadline?.ToUnixTimeMilliseconds(),
                phaseMs = (long)(Phase switch { "countdown" => _t.Countdown, "hold" => _t.Hold, "betting" => _t.Bet, "result" => _t.Result, _ => TimeSpan.Zero }).TotalMilliseconds,
                serverNow = now.ToUnixTimeMilliseconds(),
                roundNo = RoundNo,
                pot = _game?.Pot ?? 0,
                mySeat = Seats.IndexOf(viewer),
                banner = Phase == "result" ? _banner : null,
                log = _log.ToArray(),
                seats = Seats.Select(s =>
                {
                    var p = P(s);
                    bool show = p != null && (s == viewer || p.Revealed);
                    return new
                    {
                        name = s.Name,
                        stack = s.Stack,
                        connected = s.Connected,
                        // 보낸 지 EmoteShown이 안 된 감정 표현. ago는 보낸 뒤 지난 밀리초(화면이 남은 시간만큼만 띄운다)
                        emote = s.EmoteSeq > 0 && now - s.EmoteAt < EmoteShown
                            ? new { seq = s.EmoteSeq, text = Emotes[s.EmoteKind], ago = (long)(now - s.EmoteAt).TotalMilliseconds }
                            : null,
                        leaving = s.Leaving,
                        away = s.Away,
                        inHand = p != null,
                        rerolls = p?.Rerolls ?? 0,
                        stood = p != null && Phase == "hold" && _stood[s.GameIndex],
                        status = p == null ? "waiting" : p.Status.ToString().ToLowerInvariant(),
                        times = p == null || p.Contrib == 0 ? null : Times(p.Contrib),
                        folded = p?.Folded ?? false,
                        winner = p?.Winner ?? false,
                        starter = p != null && _game.Starter == p.Index,
                        turn = p != null && p == current,
                        dice = show ? p.Dice.ToArray() : null,
                        hand = show ? HandEvaluator.NameOf(HandEvaluator.Evaluate(p.Dice).Rank) : null,
                        rank = show ? (int?)HandEvaluator.Evaluate(p.Dice).Rank : null,
                    };
                }).ToArray(),
                me = mine == null ? null : new
                {
                    canReroll = Phase == "hold" && !_stood[viewer.GameIndex] && _game.CanReroll(mine),
                    rerollsLeft = _cfg.MaxRerolls - mine.Rerolls,
                    stood = Phase == "hold" && _stood[viewer.GameIndex],
                    myTurn = current == mine,
                    responding = bet != null && bet.Responding,
                    raiseUsed = bet != null && bet.RaiseUsed,
                    callCost = bet != null && current == mine ? bet.CallCost(mine) : 0,
                    callTimes = bet != null && current == mine ? Times(bet.Level) : null,
                    canCall = bet != null && current == mine && bet.CanCall(mine),
                    raiseCost = bet != null && current == mine ? bet.RaiseCost(mine) : 0,
                    raiseTimes = Times(_cfg.Ante * _cfg.RaiseMultiplier),
                    canRaise = bet != null && current == mine && bet.CanRaiseAny(mine),
                    // 고를 수 있는 레이즈 크기(이번 판 누적, 앤티 단위). 올릴 수 없으면 0
                    minRaise = bet != null && current == mine && bet.CanRaiseAny(mine) ? bet.MinRaiseFor(mine) : 0,
                    maxRaise = bet != null && current == mine && bet.CanRaiseAny(mine) ? bet.MaxRaiseFor(mine) : 0,
                    contrib = mine.Contrib,
                },
            };
        }
    }
}
