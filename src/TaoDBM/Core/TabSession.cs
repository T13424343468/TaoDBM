using System.Text.Json;

namespace TaoDBM.Core;

/// <summary>一个打开的标签页的会话信息（重启后恢复用）。</summary>
public class TabSessionItem
{
    /// <summary>关联连接的 Id；空表示欢迎页等无连接标签。</summary>
    public string ConnectionId { get; set; } = "";
    public string Mode { get; set; } = "Query";   // Query / Browse
    public string Db { get; set; } = "";
    public string Child { get; set; } = "";       // Browse 模式下的表 / 集合名
    public string Sql { get; set; } = "";         // Query 模式下的编辑器内容
}

/// <summary>标签会话存取：%APPDATA%\TaoDBM\tabsession.json；路径可注入便于测试。</summary>
public static class TabSession
{
    private static string _file = "";
    private static string File => _file.Length > 0 ? _file : TaoPaths.FilePath("tabsession.json");   // Tao 系列存储默认 D 盘

    /// <summary>测试或自定义存储位置。</summary>
    public static void UseFile(string path) => _file = path;

    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static List<TabSessionItem> Load()
    {
        try
        {
            if (!System.IO.File.Exists(File)) return new List<TabSessionItem>();
            return JsonSerializer.Deserialize<List<TabSessionItem>>(System.IO.File.ReadAllText(File)) ?? new();
        }
        catch { return new List<TabSessionItem>(); }
    }

    public static void Save(List<TabSessionItem> items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File)!);
        System.IO.File.WriteAllText(File, JsonSerializer.Serialize(items, Opt));
    }

    /// <summary>按当前连接集合过滤：连接已删除的标签不再恢复。</summary>
    public static List<TabSessionItem> FilterValid(List<TabSessionItem> items, IEnumerable<ConnectionProfile> profiles)
    {
        var ids = profiles.Select(p => p.Id).ToHashSet();
        return items.Where(i => i.ConnectionId.Length == 0 || ids.Contains(i.ConnectionId)).ToList();
    }
}
