using System.Security.Cryptography;
using DiceHeist.Core;
using DiceHeist.Server.Data;

namespace DiceHeist.Server.Single
{
    /// <summary>
    /// 사용자마다 지금 앉아 있는 싱글 테이블. 판정은 Core의 SingleTable이 하고, 여기서는 칩을 DB에 저장한다.
    /// 칩은 요청마다 바로 저장하므로, 판 중에 나가거나 서버가 꺼지면 그 판에 낸 칩(앤티·베팅)은 잃는다.
    /// </summary>
    public sealed class SingleSessions
    {
        readonly object _gate = new object();
        readonly Dictionary<long, SingleTable> _tables = new Dictionary<long, SingleTable>();
        readonly Db _db;

        public SingleSessions(Db db) => _db = db;

        static SingleView Error(string message) => new SingleView { error = message };

        /// <summary>테이블에 앉는다(이미 앉아 있던 테이블은 버린다). 가진 칩으로 입장 조건을 확인한다.</summary>
        public SingleView Sit(long userId, string tableName)
        {
            lock (_gate)
            {
                var cfg = TableConfig.Tiers.FirstOrDefault(t => t.Name == tableName);
                if (cfg == null) return Error("없는 테이블입니다.");
                var user = _db.GetUser(userId);
                if (!cfg.CanEnter(user.Chips)) return Error($"{cfg.Name}은(는) {cfg.MinChips:N0}칩 이상 있어야 앉을 수 있습니다.");
                var table = new SingleTable(cfg, new SeededRandom(BitConverter.ToInt64(RandomNumberGenerator.GetBytes(8))), user.Chips);
                _tables[userId] = table;
                return Finish(userId, table, table.Look());
            }
        }

        public SingleView Leave(long userId)
        {
            lock (_gate)
            {
                _tables.Remove(userId);
                return new SingleView { phase = "idle" };
            }
        }

        public SingleView Look(long userId) => Do(userId, t => t.Look());
        public SingleView Start(long userId) => Do(userId, t => t.Start());
        public SingleView Rescue(long userId) => Do(userId, t => t.Rescue());
        public SingleView Reroll(long userId, bool[] held) => Do(userId, t => t.Reroll(held));
        public SingleView Stand(long userId) => Do(userId, t => t.Stand());
        public SingleView Bet(long userId, string action, int raiseTo = 0) => Do(userId, t => t.Bet(action, raiseTo));

        SingleView Do(long userId, Func<SingleTable, SingleView> action)
        {
            lock (_gate)
            {
                if (!_tables.TryGetValue(userId, out var table)) return Error("테이블에 앉아 있지 않습니다. 다시 테이블을 골라 주세요.");
                return Finish(userId, table, action(table));
            }
        }

        /// <summary>칩을 저장하고, 브론즈 한도에 닿았으면 멀티를 연다.</summary>
        SingleView Finish(long userId, SingleTable table, SingleView view)
        {
            bool cleared = _db.SaveSingleChips(userId, table.Chips, TableConfig.ClearsBronze(table.Chips));
            view.bronzeCleared = cleared;
            return view;
        }
    }
}
