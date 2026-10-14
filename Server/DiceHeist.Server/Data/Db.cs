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
CREATE TABLE IF NOT EXISTS ledger (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  user_id INTEGER NOT NULL,
  kind TEXT NOT NULL,
  chips INTEGER NOT NULL DEFAULT 0,
  money INTEGER NOT NULL DEFAULT 0,
  at TEXT NOT NULL
);");
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

        static void Ledger(SqliteConnection c, SqliteTransaction tx, long userId, string kind, long chips, long money) =>
            Exec(c, tx, "INSERT INTO ledger(user_id, kind, chips, money, at) VALUES ($u, $k, $c, $m, $t)",
                ("$u", userId), ("$k", kind), ("$c", chips), ("$m", money), ("$t", Now()));

        // ---------- 계정 ----------

        /// <summary>새 계정. 이름이 이미 있으면 null.</summary>
        public long? CreateUser(string name, string password)
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = Passwords.Hash(password, salt);
            using var c = Open();
            try
            {
                Exec(c, null, "INSERT INTO users(name, pw_hash, pw_salt, chips, created_at) VALUES ($n, $h, $s, $c, $t)",
                    ("$n", name), ("$h", hash), ("$s", salt), ("$c", TableConfig.Bronze.StartChips), ("$t", Now()));
            }
            catch (SqliteException e) when (e.SqliteErrorCode == 19) // UNIQUE
            {
                return null;
            }
            return (long)Scalar(c, null, "SELECT last_insert_rowid()");
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
            cmd.CommandText = "SELECT id, name, chips, money, bronze_cleared FROM users WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            return new UserRow
            {
                Id = r.GetInt64(0), Name = r.GetString(1), Chips = r.GetInt32(2), Money = r.GetInt64(3), BronzeCleared = r.GetInt32(4) == 1,
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

        /// <summary>검증용: 모든 사용자의 머니 + 테이블 stack 합계.</summary>
        public long TotalMoney()
        {
            using var c = Open();
            return Convert.ToInt64(Scalar(c, null, "SELECT (SELECT COALESCE(SUM(money),0) FROM users) + (SELECT COALESCE(SUM(stack),0) FROM stacks)"));
        }
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
