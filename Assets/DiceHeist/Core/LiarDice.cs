using System;
using System.Collections.Generic;

namespace DiceHeist.Core
{
    /// <summary>라이어 다이스 한 자리.</summary>
    public sealed class LiarPlayer
    {
        public readonly int Index;
        public readonly string Name;
        public readonly int[] Dice = new int[LiarGame.DicePerPlayer];
        /// <summary>함정 의자에서 떨어지지 않았는지.</summary>
        public bool Alive = true;
        /// <summary>레버를 당긴 횟수.</summary>
        public int Pulls;
        /// <summary>함정이 열리는 차례(1~6, 비밀). Pulls가 이 값이 되는 순간 떨어진다.</summary>
        internal int TrapAt;

        public LiarPlayer(int index, string name)
        {
            Index = index;
            Name = name;
        }

        /// <summary>다음에 레버를 당기면 떨어질 확률(남은 칸 중 하나가 함정).</summary>
        public double NextFallChance => 1.0 / (LiarGame.Chambers - Pulls);
    }

    /// <summary>"테이블 전체에 face 눈이 quantity개 이상"이라는 부르기. 1은 어떤 눈으로도 센다(만능).</summary>
    public readonly struct LiarBid
    {
        public readonly int Quantity;
        public readonly int Face;

        public LiarBid(int quantity, int face)
        {
            Quantity = quantity;
            Face = face;
        }

        public bool IsNone => Quantity == 0;

        /// <summary>이 부르기가 prev보다 높은지: 개수가 더 많거나, 개수가 같고 눈이 더 크다.</summary>
        public bool Beats(LiarBid prev) => prev.IsNone || Quantity > prev.Quantity || (Quantity == prev.Quantity && Face > prev.Face);

        public override string ToString() => IsNone ? "-" : $"{Face} × {Quantity}개";
    }

    /// <summary>
    /// 라이어 다이스 봇의 판단(빈자리를 채우는 AI, 화면에 "봇"으로 표시된다). 남의 주사위는 보지 않는다:
    /// 자기 주사위의 그 눈(1 포함) 개수 + 남의 주사위 수 × 1/3(그 눈이거나 1일 확률)을 기대 개수로 본다.
    /// 앞 부르기가 기대보다 확실히 많으면 "거짓말!", 아니면 가장 그럴듯한 눈으로 한 단계 올리고, 가끔은 허세로 엉뚱한 눈을 부른다.
    /// </summary>
    public static class LiarBot
    {
        /// <summary>앞 부르기가 기대 개수를 이만큼 넘으면 거짓말을 외친다.</summary>
        public const double DoubtMargin = 1.0;
        /// <summary>허세: 이 확률로 기대가 낮은 눈을 부른다.</summary>
        public const double BluffChance = 0.18;

        public static double Expected(LiarGame g, int me, int face)
        {
            int mine = 0;
            foreach (int d in g.Players[me].Dice) if (d == face || d == 1) mine++;
            int others = g.DiceInPlay - LiarGame.DicePerPlayer;
            return mine + others / 3.0;
        }

        /// <summary>차례인 봇의 선택: 거짓말이면 challenge = true, 아니면 부를 것.</summary>
        public static (bool challenge, LiarBid bid) Decide(LiarGame g, int me, IRandom rng)
        {
            var cur = g.Bid;
            if (g.CanChallenge && cur.Quantity > Expected(g, me, cur.Face) + DoubtMargin && rng.NextDouble() < 0.9)
                return (true, default);

            // 눈마다 앞 부르기를 넘는 가장 작은 개수와 그 그럴듯함(기대 - 개수)을 본다
            LiarBid best = default;
            double bestScore = double.MinValue;
            bool bluff = rng.NextDouble() < BluffChance;
            for (int f = LiarGame.MinFace; f <= LiarGame.MaxFace; f++)
            {
                int q = cur.IsNone ? 1 : (f > cur.Face ? cur.Quantity : cur.Quantity + 1);
                // 첫 부르기는 기대의 70%쯤에서 시작한다(너무 낮으면 판이 늘어진다)
                if (cur.IsNone) q = Math.Max(1, (int)Math.Floor(Expected(g, me, f) * 0.7));
                if (q > g.DiceInPlay) continue;
                double score = Expected(g, me, f) - q + (bluff ? rng.NextDouble() * 3 : rng.NextDouble() * 0.3);
                if (score > bestScore) { bestScore = score; best = new LiarBid(q, f); }
            }
            // 어떤 부르기도 그럴듯하지 않고 거짓말을 외칠 수 있으면 외친다
            if (g.CanChallenge && (best.IsNone || bestScore < -1.5)) return (true, default);
            if (best.IsNone || g.CheckBid(best) != null) best = g.MinimumRaise();
            return (false, best);
        }
    }

    /// <summary>"거짓말!"로 컵을 연 결과.</summary>
    public sealed class LiarReveal
    {
        public LiarBid Bid;
        public int Bidder;
        public int Challenger;
        /// <summary>테이블 전체에서 부른 눈(1 포함)의 실제 개수.</summary>
        public int Count;
        /// <summary>부르기가 맞았는지(맞으면 "거짓말"을 외친 사람이 진다).</summary>
        public bool BidTrue;
        public int Loser;
    }

    /// <summary>레버를 당긴 결과.</summary>
    public sealed class LiarPull
    {
        public int Player;
        /// <summary>몇 번째 당김인지(1부터).</summary>
        public int PullNo;
        /// <summary>당기기 전의 떨어질 확률.</summary>
        public double Chance;
        public bool Fell;
    }

    /// <summary>
    /// 함정 의자 라이어 다이스의 규칙(순수 규칙. 시간·돈·화면은 모른다). 2~6명.
    /// 판(라운드)마다 모두 주사위 5개를 컵 안에 굴린다(자기 것만 본다). 차례대로 "테이블 전체에 X 눈이 N개 이상"을 더 높게 부르거나,
    /// 바로 앞 부르기가 거짓이라고 "거짓말!"을 외친다. 컵을 열어 1(만능)을 포함해 세고, 틀린 쪽이 함정 레버를 당긴다.
    /// 레버는 사람마다 6칸 중 한 칸이 함정(시작할 때 정해지고 비밀): 당길 때마다 떨어질 확률이 1/6 → 1/5 → … → 1로 오른다.
    /// 떨어지면 탈락. 마지막까지 남은 한 사람이 이긴다. 주사위는 공정하다(눈마다 1/6).
    /// 호출 순서: new → StartRound → (Bid | Challenge) 반복 → Challenge가 끝나면 Pull → Over가 아니면 StartRound …
    /// </summary>
    public sealed class LiarGame
    {
        public const int DicePerPlayer = 5;
        /// <summary>레버의 칸 수. 그중 한 칸이 함정.</summary>
        public const int Chambers = 6;
        public const int MinFace = 2, MaxFace = 6;

        readonly IRandom _rng;
        readonly List<LiarPlayer> _players = new List<LiarPlayer>();

        public IReadOnlyList<LiarPlayer> Players => _players;
        public int RoundNo { get; private set; }
        /// <summary>지금 부를 차례인 사람.</summary>
        public int Turn { get; private set; }
        /// <summary>지금까지 가장 높은 부르기와 부른 사람(-1이면 아직 없음).</summary>
        public LiarBid Bid { get; private set; }
        public int Bidder { get; private set; } = -1;
        /// <summary>컵을 열어 진 사람이 레버를 당겨야 하는지. 당기기 전에는 다음 판으로 갈 수 없다.</summary>
        public int PendingPull { get; private set; } = -1;
        public LiarReveal LastReveal { get; private set; }

        public LiarGame(IReadOnlyList<string> names, IRandom rng, int firstTurn = 0)
        {
            if (names == null || names.Count < 2) throw new ArgumentException("2명 이상이어야 합니다.", nameof(names));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            for (int i = 0; i < names.Count; i++)
                _players.Add(new LiarPlayer(i, names[i]) { TrapAt = 1 + rng.Next(Chambers) });
            Turn = ((firstTurn % names.Count) + names.Count) % names.Count;
        }

        public int AliveCount()
        {
            int n = 0;
            foreach (var p in _players) if (p.Alive) n++;
            return n;
        }

        public bool Over => AliveCount() <= 1;
        public LiarPlayer Winner => Over ? _players.Find(p => p.Alive) : null;
        /// <summary>테이블에 있는 주사위 수(살아 있는 사람 × 5). 부를 수 있는 가장 큰 개수.</summary>
        public int DiceInPlay => AliveCount() * DicePerPlayer;

        /// <summary>새 판: 살아 있는 사람이 모두 다시 굴린다. 부르기는 지운다.</summary>
        public void StartRound()
        {
            if (Over) throw new InvalidOperationException("게임이 끝났습니다.");
            if (PendingPull >= 0) throw new InvalidOperationException("레버를 먼저 당겨야 합니다.");
            RoundNo++;
            foreach (var p in _players)
            {
                if (!p.Alive) continue;
                for (int i = 0; i < DicePerPlayer; i++) p.Dice[i] = _rng.D6();
            }
            Bid = default;
            Bidder = -1;
            LastReveal = null;
            if (!_players[Turn].Alive) Turn = NextAlive(Turn);
        }

        /// <summary>지금 차례인 사람이 부를 수 있는지. 실패하면 이유.</summary>
        public string CheckBid(LiarBid bid)
        {
            if (PendingPull >= 0) return "레버를 먼저 당겨야 합니다.";
            if (bid.Face < MinFace || bid.Face > MaxFace) return "2~6 눈만 부를 수 있습니다(1은 만능).";
            if (bid.Quantity < 1) return "1개 이상 불러야 합니다.";
            if (bid.Quantity > DiceInPlay) return $"테이블에 주사위가 {DiceInPlay}개뿐입니다.";
            if (!bid.Beats(Bid)) return "앞 사람보다 높게 불러야 합니다(개수를 늘리거나, 같은 개수에 더 큰 눈).";
            return null;
        }

        /// <summary>지금 차례인 사람이 부른다. 다음 사람에게 차례가 간다.</summary>
        public void Raise(LiarBid bid)
        {
            var err = CheckBid(bid);
            if (err != null) throw new InvalidOperationException(err);
            Bid = bid;
            Bidder = Turn;
            Turn = NextAlive(Turn);
        }

        /// <summary>앞 부르기보다 한 단계 높은 가장 작은 부르기(시간 초과나 AI용).</summary>
        public LiarBid MinimumRaise()
        {
            if (Bid.IsNone) return new LiarBid(1, MinFace);
            if (Bid.Face < MaxFace) return new LiarBid(Bid.Quantity, Bid.Face + 1);
            return new LiarBid(Bid.Quantity + 1, MinFace);
        }

        public bool CanChallenge => PendingPull < 0 && !Bid.IsNone && Bidder >= 0 && Bidder != Turn;

        /// <summary>지금 차례인 사람이 바로 앞 부르기에 "거짓말!"을 외친다. 컵을 열고 진 사람을 정한다.</summary>
        public LiarReveal Challenge()
        {
            if (!CanChallenge) throw new InvalidOperationException("지금은 거짓말을 외칠 수 없습니다.");
            int count = CountFace(Bid.Face);
            bool truth = count >= Bid.Quantity;
            var r = new LiarReveal
            {
                Bid = Bid, Bidder = Bidder, Challenger = Turn, Count = count, BidTrue = truth,
                Loser = truth ? Turn : Bidder,
            };
            LastReveal = r;
            PendingPull = r.Loser;
            return r;
        }

        /// <summary>테이블 전체(살아 있는 사람)에서 face 눈 + 1(만능)의 개수.</summary>
        public int CountFace(int face)
        {
            int n = 0;
            foreach (var p in _players)
            {
                if (!p.Alive) continue;
                foreach (int d in p.Dice) if (d == face || d == 1) n++;
            }
            return n;
        }

        /// <summary>진 사람이 레버를 당긴다. 떨어지면 탈락. 다음 판은 진 사람(살아 있으면)부터 부른다.</summary>
        public LiarPull Pull()
        {
            if (PendingPull < 0) throw new InvalidOperationException("레버를 당길 사람이 없습니다.");
            var p = _players[PendingPull];
            var res = new LiarPull { Player = p.Index, Chance = p.NextFallChance };
            p.Pulls++;
            res.PullNo = p.Pulls;
            res.Fell = p.Pulls >= p.TrapAt;
            if (res.Fell) p.Alive = false;
            PendingPull = -1;
            Turn = p.Alive ? p.Index : NextAlive(p.Index);
            return res;
        }

        /// <summary>사람이 판 중에 나가거나 끊겨 빠질 때: 바로 탈락으로 친다(그 사람이 당길 레버는 없다).</summary>
        public void Forfeit(int index)
        {
            var p = _players[index];
            if (!p.Alive) return;
            p.Alive = false;
            if (PendingPull == index) PendingPull = -1;
            if (Bidder == index)
            {
                // 그 사람의 부르기는 그대로 두되, 다음 사람이 거짓말을 외칠 상대가 없으니 부르기를 지운다
                Bid = default;
                Bidder = -1;
            }
            if (Turn == index && !Over) Turn = NextAlive(index);
        }

        int NextAlive(int from)
        {
            for (int k = 1; k <= _players.Count; k++)
            {
                int i = (from + k) % _players.Count;
                if (_players[i].Alive) return i;
            }
            return from;
        }
    }
}
