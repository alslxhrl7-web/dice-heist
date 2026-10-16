namespace DiceHeist.Server
{
    /// <summary>
    /// 비밀번호를 계속 틀리면 그 아이디의 로그인을 잠시 막고(무차별 대입 방지), 가입은 서버 전체에서 시간당 횟수를 제한한다.
    /// 서버는 Tailscale Funnel 뒤에 있어 접속 주소가 모두 localhost로 보이므로, 주소가 아니라 아이디·서버 전체를 기준으로 센다.
    /// 메모리에만 두므로 서버를 다시 켜면 풀린다.
    /// </summary>
    public sealed class LoginGuard
    {
        readonly object _gate = new object();
        readonly Dictionary<string, List<DateTimeOffset>> _failures = new Dictionary<string, List<DateTimeOffset>>(StringComparer.OrdinalIgnoreCase);
        readonly Queue<DateTimeOffset> _registrations = new Queue<DateTimeOffset>();
        readonly Func<DateTimeOffset> _now;

        /// <summary>Window 안에 MaxFailures번 틀리면 잠근다. 그 실패들이 Window 밖으로 나가면 풀린다.</summary>
        public int MaxFailures { get; init; } = 5;
        public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(15);
        /// <summary>서버 전체에서 한 시간에 받는 가입 수.</summary>
        public int RegistrationsPerHour { get; init; } = 20;

        public LoginGuard(Func<DateTimeOffset> now = null) => _now = now ?? (() => DateTimeOffset.UtcNow);

        /// <summary>이 아이디가 잠겨 있으면 풀릴 때까지 남은 시간, 아니면 null.</summary>
        public TimeSpan? LockedFor(string name)
        {
            lock (_gate)
            {
                var list = Recent(name);
                if (list == null || list.Count < MaxFailures) return null;
                // 잠긴 동안의 시도는 세지 않으므로, 최근 MaxFailures번 중 가장 오래된 실패가 창 밖으로 나가면 풀린다
                return list[list.Count - MaxFailures] + Window - _now();
            }
        }

        public void Failed(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            lock (_gate)
            {
                var list = Recent(name);
                if (list == null) _failures[name] = list = new List<DateTimeOffset>();
                list.Add(_now());
            }
        }

        public void Succeeded(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            lock (_gate) _failures.Remove(name);
        }

        /// <summary>가입을 하나 받아도 되면 세고 true. 한도를 넘었으면 false.</summary>
        public bool TryRegister()
        {
            lock (_gate)
            {
                var now = _now();
                while (_registrations.Count > 0 && _registrations.Peek() <= now - TimeSpan.FromHours(1)) _registrations.Dequeue();
                if (_registrations.Count >= RegistrationsPerHour) return false;
                _registrations.Enqueue(now);
                return true;
            }
        }

        /// <summary>창 밖으로 나간 실패는 지우고 남은 목록. 하나도 없으면 null(항목도 지운다).</summary>
        List<DateTimeOffset> Recent(string name)
        {
            if (string.IsNullOrEmpty(name) || !_failures.TryGetValue(name, out var list)) return null;
            var since = _now() - Window;
            list.RemoveAll(t => t <= since);
            if (list.Count > 0) return list;
            _failures.Remove(name);
            return null;
        }

        public static string Minutes(TimeSpan t) => $"{Math.Max(1, (int)Math.Ceiling(t.TotalMinutes))}분";
    }
}
