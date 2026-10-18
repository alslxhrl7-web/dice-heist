using System.Security.Cryptography;
using DiceHeist.Core;
using Microsoft.Data.Sqlite;

namespace DiceHeist.Server.Data
{
    public sealed class UserRow
    {
        public long Id;
        public string Name;
        public int Chips;
        public long Money;
        public bool BronzeCleared;
        /// <summary>고른 동물(Characters.Ids). 겉모습만 바뀐다.</summary>
        public string Character;
        /// <summary>마지막으로 구제 머니를 받은 때. 받은 적 없으면 null.</summary>
        public DateTimeOffset? LastRescue;
        /// <summary>멀티 기록. BestRank는 HandRank 값, 쇼다운에서 족보를 보인 적 없으면 -1.</summary>
        public int Rounds, Wins, BestRank = -1;
        public long BiggestPot;
    }

    /// <summary>
    /// 계정·칩·사이버머니·테이블에 올려 둔 머니(stacks)를 SQLite 파일 하나에 둔다.
    /// 돈이 움직이는 일은 전부 트랜잭션 하나로 처리하고, 장부(ledger)에 남긴다.
    /// </summary>
    public sealed class Db
    {
        readonly string _cs;

        public Db(string path)
        {
            _cs = new SqliteConnectionStringBuilder { DataSource = path, Pooling = true }.ToString();
            using var c = Open();
            Exec(c, null, @"
PRAGMA journal_mode = WAL;
CREATE TABLE IF NOT EXISTS users (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL UNIQUE COLLATE NOCASE,
  pw_hash BLOB NOT NULL,
  pw_salt BLOB NOT NULL,
  chips INTEGER NOT NULL,
  money INTEGER NOT NULL DEFAULT 0,
  bronze_cleared INTEGER NOT NULL DEFAULT 0,
  created_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS sessions (
  token TEXT PRIMARY KEY,
  user_id INTEGER NOT NULL REFERENCES users(id),
  expires_at TEXT NOT NULL
);
-- 멀티 테이블에 앉으며 가져간 머니. 서버가 꺼져도 잃지 않게 따로 적어 두고, 켜질 때 돌려준다
CREATE TABLE IF NOT EXISTS stacks (
  user_id INTEGER PRIMARY KEY REFERENCES users(id),
  room_id TEXT NOT NULL,
  stack INTEGER NOT NULL
);
-- 멀티 기록(사람마다 한 줄): 낀 판 수, 이긴 판 수(나눈 판 포함), 쇼다운에서 보인 가장 높은 족보(-1은 아직 없음), 이겨서 가져온 가장 큰 금액
CREATE TABLE IF NOT EXISTS multi_stats (
  user_id INTEGER PRIMARY KEY REFERENCES users(id),
  rounds INTEGER NOT NULL DEFAULT 0,
  wins INTEGER NOT NULL DEFAULT 0,
  best_rank INTEGER NOT NULL DEFAULT -1,
  biggest_pot INTEGER NOT NULL DEFAULT 0
);
-- 가챠로 얻은 꾸미기(사람마다 아이템마다 한 줄). equipped는 지금 쓰고 있는 것(사람마다 많아야 하나)
CREATE TABLE IF NOT EXISTS inventory (
  user_id INTEGER NOT NULL REFERENCES users(id),
  item_id TEXT NOT NULL,
  count INTEGER NOT NULL DEFAULT 0,
  equipped INTEGER NOT NULL DEFAULT 0,
  PRIMARY KEY (user_id, item_id)
);
CREATE TABLE IF NOT EXISTS ledger (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  user_id INTEGER NOT NULL,
  kind TEXT NOT NULL,
  chips INTEGER NOT NULL DEFAULT 0,
  money INTEGER NOT NULL DEFAULT 0,
  at TEXT NOT NULL
);");
            // 시작 머니를 아직 못 받은 계정(이 기능 전에 가입한 사람)에게 한 번 준다
            // 캐릭터 칸(2026-10-16에 생김): 예전 DB에는 없으므로 더하고, 이미 있는 계정은 번호대로 나눠 준다
            if (Scalar(c, null, "SELECT 1 FROM pragma_table_info('users') WHERE name = 'character'") == null)
            {
                Exec(c, null, "ALTER TABLE users ADD COLUMN character TEXT NOT NULL DEFAULT ''");
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT id FROM users";
                var all = new List<long>();
                using (var r = cmd.ExecuteReader()) while (r.Read()) all.Add(r.GetInt64(0));
                foreach (var id in all)
                    Exec(c, null, "UPDATE users SET character = $ch WHERE id = $u", ("$ch", Characters.DefaultFor(id)), ("$u", id));
            }

            using var tx = c.BeginTransaction();
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT id FROM users WHERE id NOT IN (SELECT user_id FROM ledger WHERE kind = 'starter')";
                var ids = new List<long>();
                using (var r = cmd.ExecuteReader()) while (r.Read()) ids.Add(r.GetInt64(0));
                foreach (var id in ids) GiveStarter(c, tx, id);
            }
            tx.Commit();
        }

        /// <summary>가입하면 받는 사이버머니. 멀티 바이인 두 번어치라, 한 번 져도 다시 앉을 수 있다.</summary>
        public const long StarterMoney = 100_000;

        static void GiveStarter(SqliteConnection c, SqliteTransaction tx, long userId)
        {
            Exec(c, tx, "UPDATE users SET money = money + $m WHERE id = $u", ("$m", StarterMoney), ("$u", userId));
            Ledger(c, tx, userId, "starter", 0, StarterMoney);
        }

        SqliteConnection Open()
        {
            var c = new SqliteConnection(_cs);
            c.Open();
            return c;
        }

        static int Exec(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object)[] args)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
            return cmd.ExecuteNonQuery();
        }

        static object Scalar(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object)[] args)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
            return cmd.ExecuteScalar();
        }

        static string Now() => DateTimeOffset.UtcNow.ToString("O");

        static void Ledger(SqliteConnection c, SqliteTransaction tx, long userId, string kind, long chips, long money, DateTimeOffset? at = null) =>
            Exec(c, tx, "INSERT INTO ledger(user_id, kind, chips, money, at) VALUES ($u, $k, $c, $m, $t)",
                ("$u", userId), ("$k", kind), ("$c", chips), ("$m", money), ("$t", at?.ToUniversalTime().ToString("O") ?? Now()));

        // ---------- 계정 ----------

        /// <summary>새 계정(시작 머니 포함). 이름이 이미 있으면 null.</summary>
        public long? CreateUser(string name, string password)
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = Passwords.Hash(password, salt);
            using var c = Open();
            using var tx = c.BeginTransaction();
            try
            {
                Exec(c, tx, "INSERT INTO users(name, pw_hash, pw_salt, chips, created_at) VALUES ($n, $h, $s, $c, $t)",
                    ("$n", name), ("$h", hash), ("$s", salt), ("$c", TableConfig.Bronze.StartChips), ("$t", Now()));
            }
            catch (SqliteException e) when (e.SqliteErrorCode == 19) // UNIQUE
            {
                return null;
            }
            long id = (long)Scalar(c, tx, "SELECT last_insert_rowid()");
            GiveStarter(c, tx, id);
            Exec(c, tx, "UPDATE users SET character = $ch WHERE id = $u", ("$ch", Characters.DefaultFor(id)), ("$u", id));
            tx.Commit();
            return id;
        }

        public bool NameExists(string name)
        {
            using var c = Open();
            return Scalar(c, null, "SELECT 1 FROM users WHERE name = $n", ("$n", name)) != null;
        }

        /// <summary>이름과 비밀번호가 맞으면 사용자 번호.</summary>
        public long? VerifyUser(string name, string password)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT id, pw_hash, pw_salt FROM users WHERE name = $n";
            cmd.Parameters.AddWithValue("$n", name);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            var hash = (byte[])r["pw_hash"];
            var salt = (byte[])r["pw_salt"];
            return Passwords.Verify(password, salt, hash) ? r.GetInt64(0) : null;
        }

        public UserRow GetUser(long id)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT id, name, chips, money, bronze_cleared, " +
                "(SELECT MAX(at) FROM ledger WHERE user_id = users.id AND kind = 'rescue'), " +
                "COALESCE(m.rounds, 0), COALESCE(m.wins, 0), COALESCE(m.best_rank, -1), COALESCE(m.biggest_pot, 0), character " +
                "FROM users LEFT JOIN multi_stats m ON m.user_id = users.id WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            return new UserRow
            {
                Id = r.GetInt64(0), Name = r.GetString(1), Chips = r.GetInt32(2), Money = r.GetInt64(3), BronzeCleared = r.GetInt32(4) == 1,
                LastRescue = r.IsDBNull(5) ? null : DateTimeOffset.Parse(r.GetString(5), System.Globalization.CultureInfo.InvariantCulture),
                Rounds = r.GetInt32(6), Wins = r.GetInt32(7), BestRank = r.GetInt32(8), BiggestPot = r.GetInt64(9),
                Character = Characters.IsValid(r.GetString(10)) ? r.GetString(10) : Characters.DefaultFor(r.GetInt64(0)),
            };
        }

        public string CreateSession(long userId)
        {
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            using var c = Open();
            Exec(c, null, "INSERT INTO sessions(token, user_id, expires_at) VALUES ($t, $u, $e)",
                ("$t", token), ("$u", userId), ("$e", DateTimeOffset.UtcNow.AddDays(30).ToString("O")));
            return token;
        }

        public long? UserIdByToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            using var c = Open();
            var v = Scalar(c, null, "SELECT user_id FROM sessions WHERE token = $t AND expires_at > $now", ("$t", token), ("$now", Now()));
            return v == null ? null : (long)v;
        }

        /// <summary>비밀번호를 바꾸고, keepToken(지금 쓰는 기기)만 남기고 다른 로그인은 모두 끊는다.</summary>
        public void ChangePassword(long userId, string password, string keepToken)
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = Passwords.Hash(password, salt);
            using var c = Open();
            using var tx = c.BeginTransaction();
            Exec(c, tx, "UPDATE users SET pw_hash = $h, pw_salt = $s WHERE id = $u", ("$h", hash), ("$s", salt), ("$u", userId));
            Exec(c, tx, "DELETE FROM sessions WHERE user_id = $u AND token <> $t", ("$u", userId), ("$t", keepToken));
            tx.Commit();
        }

        /// <summary>기한이 지난 로그인 토큰을 지운다. 지운 수.</summary>
        public int DeleteExpiredSessions(DateTimeOffset now)
        {
            using var c = Open();
            return Exec(c, null, "DELETE FROM sessions WHERE expires_at <= $now", ("$now", now.ToUniversalTime().ToString("O")));
        }

        public void DeleteSession(string token)
        {
            using var c = Open();
            Exec(c, null, "DELETE FROM sessions WHERE token = $t", ("$t", token));
        }

        // ---------- 돈 ----------

        /// <summary>칩을 사이버머니로(ChipExchange 규칙). 실패하면 이유를 돌려준다.</summary>
        public string Exchange(long userId, int amount)
        {
            using var c = Open();
            using var tx = c.BeginTransaction();
            int chips = Convert.ToInt32(Scalar(c, tx, "SELECT chips FROM users WHERE id = $u", ("$u", userId)));
            if (amount <= 0 || amount % ChipExchange.Unit != 0) return $"{ChipExchange.Unit}칩 단위로만 바꿀 수 있습니다.";
            if (amount > ChipExchange.Exchangeable(chips)) return $"처음 받은 {ChipExchange.Floor}칩 위로 번 칩만 바꿀 수 있습니다.";
            var (left, money) = ChipExchange.Exchange(chips, amount);
            Exec(c, tx, "UPDATE users SET chips = $c, money = money + $m WHERE id = $u", ("$c", left), ("$m", money), ("$u", userId));
            Ledger(c, tx, userId, "exchange", -amount, money);
            tx.Commit();
            return null;
        }

        /// <summary>싱글 칩을 저장한다. clearsBronze면 멀티를 연다(한 번 열리면 닫히지 않는다). 지금 열려 있는지 돌려준다.</summary>
        public bool SaveSingleChips(long userId, int chips, bool clearsBronze)
        {
            using var c = Open();
            Exec(c, null, "UPDATE users SET chips = $c, bronze_cleared = MAX(bronze_cleared, $b) WHERE id = $u",
                ("$c", chips), ("$b", clearsBronze ? 1 : 0), ("$u", userId));
            return Convert.ToInt32(Scalar(c, null, "SELECT bronze_cleared FROM users WHERE id = $u", ("$u", userId))) == 1;
        }

        /// <summary>
        /// 구제 머니: 머니가 target보다 적고 테이블에 앉아 있지 않으면 하루(한국 시간) 한 번 target까지 채운다.
        /// 브라우저로만 하는 사람은 칩을 벌 곳이 없어, 다 잃으면 다시 앉을 수 없었다(싱글의 구제 칩과 같은 역할). 실패하면 이유를 돌려준다.
        /// </summary>
        public string ClaimRescue(long userId, long target, DateTimeOffset now)
        {
            var u = GetUser(userId);
            using var c = Open();
            using var tx = c.BeginTransaction();
            if (Scalar(c, tx, "SELECT 1 FROM stacks WHERE user_id = $u", ("$u", userId)) != null) return "테이블에서 일어난 뒤에 받을 수 있습니다.";
            if (u.Money >= target) return $"사이버머니가 {target:N0}보다 적을 때만 받을 수 있습니다.";
            if (!Rescue.Available(u.Money, u.LastRescue, target, now)) return "오늘은 이미 받았습니다. 내일(자정 지나서) 다시 받을 수 있습니다.";
            long give = target - u.Money;
            // 같은 요청이 겹쳐도 한 번만 채워지게 머니가 그대로일 때만 바꾼다
            if (Exec(c, tx, "UPDATE users SET money = $t WHERE id = $u AND money = $m", ("$t", target), ("$u", userId), ("$m", u.Money)) == 0)
                return "잠시 뒤에 다시 시도해 주세요.";
            Ledger(c, tx, userId, "rescue", 0, give, now);
            tx.Commit();
            return null;
        }

        /// <summary>개발용: 테스트 머니를 주고 브론즈를 지난 것으로 친다.</summary>
        public void DevGrant(long userId, long money)
        {
            using var c = Open();
            using var tx = c.BeginTransaction();
            Exec(c, tx, "UPDATE users SET money = money + $m, bronze_cleared = 1 WHERE id = $u", ("$m", money), ("$u", userId));
            Ledger(c, tx, userId, "dev-grant", 0, money);
            tx.Commit();
        }

        /// <summary>머니에서 amount를 빼 테이블 stack으로 옮긴다. 머니가 모자라면 false.</summary>
        public bool BuyIn(long userId, string roomId, int amount)
        {
            using var c = Open();
            using var tx = c.BeginTransaction();
            if (Exec(c, tx, "UPDATE users SET money = money - $a WHERE id = $u AND money >= $a", ("$a", amount), ("$u", userId)) == 0) return false;
            Exec(c, tx, "INSERT INTO stacks(user_id, room_id, stack) VALUES ($u, $r, $a)", ("$u", userId), ("$r", roomId), ("$a", amount));
            Ledger(c, tx, userId, "buy-in", 0, -amount);
            tx.Commit();
            return true;
        }

        public void UpdateStack(long userId, int stack)
        {
            using var c = Open();
            Exec(c, null, "UPDATE stacks SET stack = $s WHERE user_id = $u", ("$s", stack), ("$u", userId));
        }

        /// <summary>테이블 stack을 머니로 돌려준다.</summary>
        public void CashOut(long userId)
        {
            using var c = Open();
            using var tx = c.BeginTransaction();
            var v = Scalar(c, tx, "SELECT stack FROM stacks WHERE user_id = $u", ("$u", userId));
            if (v == null) return;
            long stack = Convert.ToInt64(v);
            Exec(c, tx, "UPDATE users SET money = money + $s WHERE id = $u", ("$s", stack), ("$u", userId));
            Exec(c, tx, "DELETE FROM stacks WHERE user_id = $u", ("$u", userId));
            Ledger(c, tx, userId, "cash-out", 0, stack);
            tx.Commit();
        }

        /// <summary>서버가 켜질 때: 지난번에 테이블에 남아 있던 stack을 모두 돌려준다.</summary>
        public int RefundAllStacks()
        {
            using var c = Open();
            var ids = new List<long>();
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT user_id FROM stacks";
                using var r = cmd.ExecuteReader();
                while (r.Read()) ids.Add(r.GetInt64(0));
            }
            foreach (var id in ids) CashOut(id);
            return ids.Count;
        }

        /// <summary>실행 중에도 일관된 사본을 만든다(SQLite VACUUM INTO).</summary>
        public void BackupTo(string path)
        {
            using var c = Open();
            Exec(c, null, "VACUUM INTO $p", ("$p", path));
        }

        /// <summary>순위표: 머니 + 테이블에 가져간 stack이 많은 순. 같으면 먼저 가입한 사람이 위.</summary>
        public List<(string name, long money, string character)> Ranking(int count)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT u.name, u.money + COALESCE(s.stack, 0) AS total, u.character FROM users u LEFT JOIN stacks s ON s.user_id = u.id " +
                              "ORDER BY total DESC, u.id LIMIT $n";
            cmd.Parameters.AddWithValue("$n", count);
            using var r = cmd.ExecuteReader();
            var list = new List<(string, long, string)>();
            while (r.Read()) list.Add((r.GetString(0), r.GetInt64(1), r.GetString(2)));
            return list;
        }

        /// <summary>캐릭터를 바꾼다. 없는 id면 false.</summary>
        public bool SetCharacter(long userId, string character)
        {
            if (!Characters.IsValid(character)) return false;
            using var c = Open();
            Exec(c, null, "UPDATE users SET character = $ch WHERE id = $u", ("$ch", character), ("$u", userId));
            return true;
        }

        /// <summary>끝난 멀티 판의 결과를 사람마다 기록에 더한다.</summary>
        public void RecordRounds(IReadOnlyList<RoundRecord> records)
        {
            if (records.Count == 0) return;
            using var c = Open();
            using var tx = c.BeginTransaction();
            foreach (var r in records)
                Exec(c, tx, "INSERT INTO multi_stats(user_id, rounds, wins, best_rank, biggest_pot) VALUES ($u, 1, $w, $r, $p) " +
                             "ON CONFLICT(user_id) DO UPDATE SET rounds = rounds + 1, wins = wins + excluded.wins, " +
                             "best_rank = MAX(best_rank, excluded.best_rank), biggest_pot = MAX(biggest_pot, excluded.biggest_pot)",
                    ("$u", r.UserId), ("$w", r.Won ? 1 : 0), ("$r", r.Rank), ("$p", r.Won ? r.Amount : 0));
            tx.Commit();
        }

        /// <summary>테이블(멀티·라이어 다이스 어느 쪽이든)에 머니를 가져가 앉아 있는지.</summary>
        public bool HasStack(long userId)
        {
            using var c = Open();
            return Scalar(c, null, "SELECT 1 FROM stacks WHERE user_id = $u", ("$u", userId)) != null;
        }

        // ---------- 가챠(꾸미기) ----------

        /// <summary>
        /// 가챠: 머니에서 cost를 빼고 items를 인벤토리에 더한다(한 트랜잭션). 머니가 모자라거나 테이블에 앉아 있으면 이유를 돌려준다.
        /// 앉아 있을 때 막는 이유: 머니가 테이블 stack으로 나가 있어, 뽑기로 머니가 줄면 일어날 때 계산이 헷갈린다.
        /// </summary>
        public string GachaPull(long userId, long cost, IReadOnlyList<string> items)
        {
            using var c = Open();
            using var tx = c.BeginTransaction();
            if (Scalar(c, tx, "SELECT 1 FROM stacks WHERE user_id = $u", ("$u", userId)) != null) return "테이블에서 일어난 뒤에 뽑을 수 있습니다.";
            if (Exec(c, tx, "UPDATE users SET money = money - $c WHERE id = $u AND money >= $c", ("$c", cost), ("$u", userId)) == 0)
                return $"사이버머니가 {cost:N0} 이상 있어야 뽑을 수 있습니다.";
            foreach (var item in items)
                Exec(c, tx, "INSERT INTO inventory(user_id, item_id, count) VALUES ($u, $i, 1) ON CONFLICT(user_id, item_id) DO UPDATE SET count = count + 1",
                    ("$u", userId), ("$i", item));
            Ledger(c, tx, userId, "gacha", 0, -cost);
            tx.Commit();
            return null;
        }

        /// <summary>가진 꾸미기와 개수, 쓰고 있는지.</summary>
        public List<(string item, int count, bool equipped)> Inventory(long userId)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT item_id, count, equipped FROM inventory WHERE user_id = $u AND count > 0 ORDER BY item_id";
            cmd.Parameters.AddWithValue("$u", userId);
            using var r = cmd.ExecuteReader();
            var list = new List<(string, int, bool)>();
            while (r.Read()) list.Add((r.GetString(0), r.GetInt32(1), r.GetInt32(2) == 1));
            return list;
        }

        /// <summary>꾸미기를 쓴다(가진 것만). item이 null이면 벗는다. 성공하면 true.</summary>
        public bool Equip(long userId, string item)
        {
            using var c = Open();
            using var tx = c.BeginTransaction();
            if (item != null && Scalar(c, tx, "SELECT 1 FROM inventory WHERE user_id = $u AND item_id = $i AND count > 0", ("$u", userId), ("$i", item)) == null) return false;
            Exec(c, tx, "UPDATE inventory SET equipped = 0 WHERE user_id = $u", ("$u", userId));
            if (item != null) Exec(c, tx, "UPDATE inventory SET equipped = 1 WHERE user_id = $u AND item_id = $i", ("$u", userId), ("$i", item));
            tx.Commit();
            return true;
        }

        public string EquippedItem(long userId)
        {
            using var c = Open();
            return Scalar(c, null, "SELECT item_id FROM inventory WHERE user_id = $u AND equipped = 1", ("$u", userId)) as string;
        }

        /// <summary>검증용: 모든 사용자의 머니 + 테이블 stack 합계.</summary>
        public long TotalMoney()
        {
            using var c = Open();
            return Convert.ToInt64(Scalar(c, null, "SELECT (SELECT COALESCE(SUM(money),0) FROM users) + (SELECT COALESCE(SUM(stack),0) FROM stacks)"));
        }
    }

    /// <summary>멀티 한 판에서 한 사람의 결과. Amount는 이겨서 가져온 금액(나눈 판은 몫), Rank는 쇼다운에서 보인 족보(안 보였으면 -1).</summary>
    public sealed record RoundRecord(long UserId, bool Won, int Amount, int Rank);

    /// <summary>구제 머니를 받을 수 있는지. 하루의 경계는 한국 시간 자정.</summary>
    public static class Rescue
    {
        static readonly TimeSpan Kst = TimeSpan.FromHours(9);

        public static DateOnly Day(DateTimeOffset t) => DateOnly.FromDateTime(t.ToOffset(Kst).DateTime);

        public static bool Available(long money, DateTimeOffset? last, long target, DateTimeOffset now) =>
            money < target && (last == null || Day(last.Value) != Day(now));
    }

    public static class Passwords
    {
        const int Iterations = 100_000;

        public static byte[] Hash(string password, byte[] salt) =>
            Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);

        public static bool Verify(string password, byte[] salt, byte[] hash) =>
            CryptographicOperations.FixedTimeEquals(Hash(password, salt), hash);
    }
}
