using System.Text.Json;

namespace TaoDBM.Core;

/// <summary>一条查询历史。</summary>
public class HistoryEntry
{
    public DateTime Time { get; set; }
    public string Engine { get; set; } = "";
    public string Database { get; set; } = "";
    public string Sql { get; set; } = "";
}

/// <summary>查询历史：内存 + %APPDATA%\TaoDBM\history.json，最多 300 条。</summary>
public static class QueryHistory
{
    private const int Max = 300;
    private static List<HistoryEntry> _items = Load();
    public static IReadOnlyList<HistoryEntry> Items => _items;

    private static string _file = "";
    private static string File => _file.Length > 0 ? _file : TaoPaths.FilePath("history.json");   // Tao 系列存储默认 D 盘

    /// <summary>测试或自定义存储位置（清空内存缓存，下轮访问按新路径重载）。</summary>
    public static void UseFile(string path)
    {
        _file = path;
        _items = Load();
    }

    public static void Add(string engine, string db, string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return;
        _items.Insert(0, new HistoryEntry
        {
            Time = DateTime.Now,
            Engine = engine,
            Database = db,
            Sql = sql.Length > 8000 ? sql[..8000] + "…" : sql,
        });
        if (_items.Count > Max) _items.RemoveRange(Max, _items.Count - Max);
        Save();
    }

    public static void Clear()
    {
        _items.Clear();
        Save();
    }

    private static List<HistoryEntry> Load()
    {
        try
        {
            if (!System.IO.File.Exists(File)) return new List<HistoryEntry>();
            return JsonSerializer.Deserialize<List<HistoryEntry>>(System.IO.File.ReadAllText(File)) ?? new();
        }
        catch { return new List<HistoryEntry>(); }
    }

    private static void Save()
    {
        try
        {
            var opt = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            System.IO.File.WriteAllText(File, JsonSerializer.Serialize(_items, opt));
        }
        catch { /* 历史保存失败不影响主流程 */ }
    }
}
