using StackExchange.Redis;

namespace TaoDBM.Core;

/// <summary>Redis：库 = db0..db15，子节点 = key（按模式扫描）。</summary>
public class RedisProvider : IDbProvider
{
    public ConnectionProfile Profile { get; }
    public string EngineName => EngineInfo.Name(Profile.Engine);
    public bool IsNoSql => true;
    public bool SupportsBrowse => true;   // v1.4.2：键值视图走 Browse（库 → 键列表；键 → 详情）
    public string BrowseLabel => "键";

    private ConnectionMultiplexer? _conn;
    private int _db = -1;

    public RedisProvider(ConnectionProfile p) => Profile = p;

    public string Quote(string ident) => ident; // Redis 无 SQL 标识符引用

    private ConnectionMultiplexer Connect()
    {
        if (_conn is not null) return _conn;
        var cfg = new ConfigurationOptions
        {
            EndPoints = { { Profile.Host, Profile.Port > 0 ? Profile.Port : 6379 } },
            ConnectTimeout = 8000,
            SyncTimeout = 8000,
            AbortOnConnectFail = false,
        };
        if (!string.IsNullOrEmpty(Profile.User)) cfg.User = Profile.User;
        if (!string.IsNullOrEmpty(Profile.Password)) cfg.Password = Profile.Password;
        _conn = ConnectionMultiplexer.Connect(cfg);
        return _conn;
    }

    private IDatabase DB(string? db)
    {
        int n = ParseDb(db);
        if (n != _db) _db = n;   // StackExchange.Redis 按 select 自动切换
        return Connect().GetDatabase(n);
    }

    private static int ParseDb(string? db)
    {
        if (!string.IsNullOrEmpty(db) && db.StartsWith("db", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(db[2..], out int n)) return n;
        return 0;
    }

    public Task TestConnection() => Task.Run(() => Connect());

    public Task<List<string>> GetDatabases() =>
        Task.FromResult(Enumerable.Range(0, 16).Select(i => $"db{i}").ToList());

    public Task<List<string>> GetChildren(string db) => Task.Run(() =>
    {
        var server = Connect().GetServers().FirstOrDefault()
                     ?? throw new InvalidOperationException("Redis 服务器不可用。");
        var keys = new List<string>();
        var iter = server.Keys(ParseDb(db), pattern: "*", pageSize: 500);
        foreach (var k in iter)
        {
            keys.Add(k.ToString());
            if (keys.Count >= 500) break;
        }
        return keys.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
    });

    public Task<List<ColumnSpec>> GetColumns(string db, string key) =>
        Task.FromResult(new List<ColumnSpec>
        {
            new ColumnSpec { Name = key, DataType = "key", IsKey = true },
            new ColumnSpec { Name = "(value)", DataType = "value" },
        });

    /// <summary>支持命令：KEYS/GET/TYPE/TTL/EXISTS/STRLEN/LLEN/DEL（可省略，按选中键执行）。</summary>
    public Task<List<QueryResult>> Execute(string db, string script) => Task.Run(async () =>
    {
        var qr = new QueryResult { Title = script.Trim() };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var parts = script.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts[0].ToUpperInvariant();
            var d = DB(db);
            string key = parts.Length > 1 ? parts[1] : CurrentKey ?? throw new InvalidOperationException("请输入键名，或在左侧树中选中键。");
            switch (cmd)
            {
                case "KEYS" or "SCAN":
                    var server = Connect().GetServers().FirstOrDefault() ?? throw new InvalidOperationException("Redis 服务器不可用。");
                    qr.Columns.Add("key");
                    int i = 0;
                    var iter = server.Keys(int.Parse(db!.Substring(2)), pattern: parts.Length > 1 ? parts[1] : "*", pageSize: 500);
                    foreach (var k in iter)
                    {
                        qr.Rows.Add(new object?[] { k.ToString() });
                        if (++i >= 500) { qr.Message = "（最多显示 500 个键）"; break; }
                    }
                    break;
                case "GET":
                {
                    var v = await d.StringGetAsync(key);
                    qr.Columns.Add("value"); qr.Rows.Add(new object?[] { v.ToString() });
                    break;
                }
                case "TYPE":
                {
                    var t = await d.KeyTypeAsync(key);
                    qr.Columns.Add("type"); qr.Rows.Add(new object?[] { t.ToString() });
                    break;
                }
                case "TTL":
                {
                    var t = await d.KeyTimeToLiveAsync(key);
                    qr.Columns.Add("ttl"); qr.Rows.Add(new object?[] { t?.ToString() ?? "-1" });
                    break;
                }
                case "EXISTS":
                {
                    var t = await d.KeyExistsAsync(key);
                    qr.Columns.Add("exists"); qr.Rows.Add(new object?[] { t ? "1" : "0" });
                    break;
                }
                case "STRLEN":
                {
                    var t = await d.StringLengthAsync(key);
                    qr.Columns.Add("length"); qr.Rows.Add(new object?[] { t.ToString() });
                    break;
                }
                case "LLEN":
                {
                    var t = await d.ListLengthAsync(key);
                    qr.Columns.Add("length"); qr.Rows.Add(new object?[] { t.ToString() });
                    break;
                }
                case "DEL":
                {
                    var n = await d.KeyDeleteAsync(key);
                    qr.Columns.Add("deleted"); qr.Rows.Add(new object?[] { n ? "1" : "0" });
                    break;
                }
                default:
                    qr.Message = $"暂不支持命令 {cmd}。可用：KEYS 模式 / GET / TYPE / TTL / EXISTS / STRLEN / LLEN / DEL 键名（也可只输入 GET 后在树中选中键）。";
                    break;
            }
        }
        catch (Exception ex)
        {
            qr.Message = "错误：" + ex.Message;
        }
        sw.Stop();
        qr.Elapsed = sw.Elapsed;
        return new List<QueryResult> { qr };
    });


    /// <summary>
    /// Redis 键值视图（v1.4.2：按库选最合理的展示方式）。
    /// child 为空 → 键列表（键名 / 类型 / TTL / 大小）；child 为键名 → 键详情（类型 / TTL / 大小 / 值）。
    /// </summary>
    public Task<QueryResult> Browse(string db, string child, int offset, int limit) => Task.Run(async () =>
    {
        var qr = new QueryResult { Title = string.IsNullOrEmpty(child) ? $"{db} · 键" : child };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var d = DB(db);
        if (string.IsNullOrEmpty(child))
        {
            var server = Connect().GetServers().FirstOrDefault(s => s.IsConnected)
                         ?? throw new InvalidOperationException("Redis 服务器不可用。");
            qr.Columns.Add("键名");
            qr.Columns.Add("类型");
            qr.Columns.Add("TTL(秒)");
            qr.Columns.Add("大小 / 长度");
            long scanned = 0;
            var keys = new List<string>();
            foreach (var k in server.Keys(ParseDb(db), pattern: "*", pageSize: 500))
            {
                scanned++;
                if (keys.Count >= limit) break;
                if (scanned > offset) keys.Add(k.ToString());
            }
            foreach (var key in keys)
            {
                var type = await d.KeyTypeAsync(key);
                var ttl = await d.KeyTimeToLiveAsync(key);
                string size = await KeySizeAsync(d, type, key);
                qr.Rows.Add(new object?[] { key, type.ToString(), (ttl?.TotalSeconds ?? -1).ToString("0"), size });
            }
            qr.TotalRows = scanned;
        }
        else
        {
            var type = await d.KeyTypeAsync(child);
            if (type == RedisType.None) throw new InvalidOperationException($"键 {child} 不存在（可能已过期或被删除）。");
            var ttl = await d.KeyTimeToLiveAsync(child);
            string size = await KeySizeAsync(d, type, child);
            string value = await ReadValueAsync(d, type, child);
            qr.Columns.Add("属性");
            qr.Columns.Add("值");
            qr.Rows.Add(new object?[] { "键名", child });
            qr.Rows.Add(new object?[] { "类型", type.ToString() });
            long ttlSec = (long)(ttl?.TotalSeconds ?? -1);
            string ttlNote = ttl is null ? "（-1=永不过期）" : ttlSec < 0 ? "（-2=键不存在）" : ttlSec <= 0 ? "（即将过期）" : "";
            qr.Rows.Add(new object?[] { "TTL(秒)", ttlSec.ToString("0") + ttlNote });
            qr.Rows.Add(new object?[] { "大小 / 长度", size });
            qr.Rows.Add(new object?[] { "值", value });
        }
        sw.Stop();
        qr.Elapsed = sw.Elapsed;
        return qr;
    });

    private static async Task<string> KeySizeAsync(IDatabase d, RedisType type, string key)
    {
        try
        {
            return type switch
            {
                RedisType.String => (await d.StringLengthAsync(key)).ToString(),
                RedisType.List => (await d.ListLengthAsync(key)).ToString(),
                RedisType.Set => (await d.SetLengthAsync(key)).ToString(),
                RedisType.SortedSet => (await d.SortedSetLengthAsync(key)).ToString(),
                RedisType.Hash => (await d.HashLengthAsync(key)).ToString(),
                RedisType.Stream => "流",
                _ => "-",
            };
        }
        catch { return "-"; }
    }

    /// <summary>读键值：String 取原文；容器类型取前若干项摘要，避免一次拉爆。</summary>
    private static async Task<string> ReadValueAsync(IDatabase d, RedisType type, string key)
    {
        const int Show = 20;
        try
        {
            switch (type)
            {
                case RedisType.String:
                    return (string?)await d.StringGetAsync(key) ?? "(空)";
                case RedisType.List:
                {
                    var len = await d.ListLengthAsync(key);
                    var items = await d.ListRangeAsync(key, 0, Show - 1);
                    return Summarize("元素", items.Select(x => x.ToString()), len, items.Length);
                }
                case RedisType.Set:
                {
                    var len = await d.SetLengthAsync(key);
                    var items = await d.SetMembersAsync(key);
                    return Summarize("成员", items.Select(x => x.ToString()), len, (int)Math.Min(len, Show));
                }
                case RedisType.SortedSet:
                {
                    var len = await d.SortedSetLengthAsync(key);
                    var items = await d.SortedSetRangeByRankWithScoresAsync(key, 0, Show - 1);
                    return Summarize("条目", items.Select(x => $"{x.Element}（分值 {x.Score:0.##}）"), len, items.Length);
                }
                case RedisType.Hash:
                {
                    var len = await d.HashLengthAsync(key);
                    var entries = await d.HashGetAllAsync(key);
                    return Summarize("字段", entries.Select(x => $"{x.Name} = {x.Value}"), len, entries.Length);
                }
                default:
                    return "（该类型暂不提供值预览，可在下方命令行输入 GET/TYPE 等命令查看。）";
            }
        }
        catch (Exception ex)
        {
            return "（读取失败：" + ex.Message + "）";
        }
    }

    private static string Summarize(string label, IEnumerable<string> items, long total, int shown)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"共 {total:N0} 个{label}");
        if (total > shown) sb.Append($"（显示前 {shown} 个）");
        sb.Append("：\n");
        foreach (var s in items.Take(shown)) sb.AppendLine(s);
        if (total > shown) sb.Append("……（完整内容可在下方命令行查看）");
        return sb.ToString();
    }

    /// <summary>删除键（键详情页按钮调用），返回是否删除成功。</summary>
    public Task<bool> ExecuteDelKey(string db, string key) => Task.Run(() => DB(db).KeyDeleteAsync(key));

    /// <summary>当前操作的键（由界面在选中键节点时设置）。</summary>
    public string? CurrentKey { get; set; }

    public void Dispose() => _conn?.Dispose();
}
