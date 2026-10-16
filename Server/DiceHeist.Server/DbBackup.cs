using DiceHeist.Server.Data;

namespace DiceHeist.Server
{
    /// <summary>
    /// 켜질 때와 하루에 한 번 DB 사본을 Db:BackupDir(기본: DB 옆 backups 폴더)에 만들고, 최근 Db:BackupKeep개(기본 7)만 남긴다.
    /// 계정과 사이버머니가 파일 하나에 있어서, 파일이 망가져도 하루 전으로 되돌릴 수 있게 한다.
    /// 백업 전에 기한이 지난 로그인 토큰도 지운다(로그인할 때마다 하나씩 쌓인다).
    /// </summary>
    public sealed class DbBackup : BackgroundService
    {
        readonly Db _db;
        readonly string _dir;
        readonly int _keep;
        readonly ILogger<DbBackup> _log;

        public DbBackup(Db db, IConfiguration config, IWebHostEnvironment env, ILogger<DbBackup> log)
        {
            _db = db;
            _log = log;
            var dbPath = config["Db:Path"] ?? "dice-heist.db";
            if (!Path.IsPathRooted(dbPath)) dbPath = Path.Combine(env.ContentRootPath, dbPath);
            _dir = config["Db:BackupDir"] ?? Path.Combine(Path.GetDirectoryName(dbPath), "backups");
            _keep = config.GetValue("Db:BackupKeep", 7);
        }

        protected override async Task ExecuteAsync(CancellationToken stop)
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    int expired = _db.DeleteExpiredSessions(DateTimeOffset.UtcNow);
                    if (expired > 0) _log.LogInformation("만료된 로그인 {Count}개를 지웠습니다.", expired);
                }
                catch (Exception e) { _log.LogError(e, "만료된 로그인 지우기 실패"); }
                try
                {
                    Directory.CreateDirectory(_dir);
                    var path = Path.Combine(_dir, $"dice-heist-{DateTime.Now:yyyyMMdd-HHmmss}.db");
                    _db.BackupTo(path);
                    foreach (var old in Directory.GetFiles(_dir, "dice-heist-*.db").OrderByDescending(f => f).Skip(_keep))
                        File.Delete(old);
                    _log.LogInformation("DB 백업: {Path}", path);
                }
                catch (Exception e) { _log.LogError(e, "DB 백업 실패"); }
                await Task.Delay(TimeSpan.FromHours(24), stop).ContinueWith(_ => { });
            }
        }
    }
}
