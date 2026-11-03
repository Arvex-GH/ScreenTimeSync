using Microsoft.Data.Sqlite;
using System.Text.Json;
using ScreenTimeSync.Models;

namespace ScreenTimeSync.Services;

public class StorageService : IDisposable
{
    private readonly string _dbPath;
    private SqliteConnection? _conn;

    public StorageService()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenTimeSync");
        Directory.CreateDirectory(appData);
        _dbPath = Path.Combine(appData, "screentime.db");
    }

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection($"Data Source={_dbPath}");
        await _conn.OpenAsync();
        var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS daily_usage (
                date TEXT PRIMARY KEY,
                total_minutes INTEGER,
                payload_json TEXT
            );
            CREATE TABLE IF NOT EXISTS app_limits (
                process_name TEXT PRIMARY KEY,
                limit_minutes INTEGER,
                is_limited INTEGER
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SaveSnapshotAsync(IEnumerable<AppUsageModel> apps, TimeSpan total)
    {
        if (_conn == null) return;
        try
        {
            var date = DateTime.Now.ToString("yyyy-MM-dd");
            var payload = JsonSerializer.Serialize(apps.Select(a => new
            {
                a.ProcessName, a.AppName, a.Category,
                Minutes = (int)a.TodayUsage.TotalMinutes,
                LimitMinutes = (int)a.DailyLimit.TotalMinutes,
                a.IsLimited
            }));
            var cmd = _conn.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO daily_usage(date, total_minutes, payload_json) VALUES($d,$t,$p)";
            cmd.Parameters.AddWithValue("$d", date);
            cmd.Parameters.AddWithValue("$t", (int)total.TotalMinutes);
            cmd.Parameters.AddWithValue("$p", payload);
            await cmd.ExecuteNonQueryAsync();

            // persist limits separately
            foreach (var a in apps.Where(x => x.IsLimited))
            {
                var c2 = _conn.CreateCommand();
                c2.CommandText = "INSERT OR REPLACE INTO app_limits(process_name, limit_minutes, is_limited) VALUES($n,$l,1)";
                c2.Parameters.AddWithValue("$n", a.ProcessName);
                c2.Parameters.AddWithValue("$l", (int)a.DailyLimit.TotalMinutes);
                await c2.ExecuteNonQueryAsync();
            }
        }
        catch {}
    }

    public async Task<(List<AppUsageModel> Apps, TimeSpan Total)> LoadTodayAsync()
    {
        if (_conn == null) return ([], TimeSpan.Zero);
        try
        {
            var date = DateTime.Now.ToString("yyyy-MM-dd");
            var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT total_minutes, payload_json FROM daily_usage WHERE date=$d";
            cmd.Parameters.AddWithValue("$d", date);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var total = TimeSpan.FromMinutes(reader.GetInt32(0));
                var json = reader.GetString(1);
                var raw = JsonSerializer.Deserialize<List<RawApp>>(json) ?? [];
                var list = raw.Select(r => new AppUsageModel
                {
                    ProcessName = r.ProcessName,
                    AppName = r.AppName,
                    Category = r.Category,
                    TodayUsage = TimeSpan.FromMinutes(r.Minutes),
                    DailyLimit = TimeSpan.FromMinutes(r.LimitMinutes),
                    IsLimited = r.IsLimited
                }).ToList();
                // load limits overlay
                await LoadLimitsIntoAsync(list);
                return (list, total);
            }
            return ([], TimeSpan.Zero);
        }
        catch { return ([], TimeSpan.Zero); }
    }

    private async Task LoadLimitsIntoAsync(List<AppUsageModel> apps)
    {
        if (_conn == null) return;
        var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT process_name, limit_minutes FROM app_limits WHERE is_limited=1";
        using var r = await cmd.ExecuteReaderAsync();
        var limits = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        while (await r.ReadAsync()) limits[r.GetString(0)] = r.GetInt32(1);
        foreach (var a in apps) if (limits.TryGetValue(a.ProcessName, out var lim)) { a.DailyLimit = TimeSpan.FromMinutes(lim); a.IsLimited = true; }
    }

    public void Dispose() => _conn?.Dispose();

    private class RawApp
    {
        public string ProcessName { get; set; } = "";
        public string AppName { get; set; } = "";
        public string Category { get; set; } = "Other";
        public int Minutes { get; set; }
        public int LimitMinutes { get; set; }
        public bool IsLimited { get; set; }
    }
}
