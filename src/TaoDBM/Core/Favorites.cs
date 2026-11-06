using System.Text.Json;

namespace TaoDBM.Core;

/// <summary>一条查询收藏。</summary>
public class FavoriteItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Engine { get; set; } = "";
    public string Database { get; set; } = "";
    public string Sql { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.Now;
}

/// <summary>查询收藏存取：%APPDATA%\TaoDBM\favorites.json；路径可注入便于测试。</summary>
public static class Favorites
{
    private static string _file = "";
    private static string File => _file.Length > 0 ? _file : TaoPaths.FilePath("favorites.json");   // Tao 系列存储默认 D 盘

    /// <summary>测试或自定义存储位置。</summary>
    public static void UseFile(string path) => _file = path;

    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static List<FavoriteItem> Load()
    {
        try
        {
            if (!System.IO.File.Exists(File)) return new List<FavoriteItem>();
            return JsonSerializer.Deserialize<List<FavoriteItem>>(System.IO.File.ReadAllText(File)) ?? new();
        }
        catch { return new List<FavoriteItem>(); }
    }

    public static void Save(List<FavoriteItem> items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File)!);
        System.IO.File.WriteAllText(File, JsonSerializer.Serialize(items, Opt));
    }

    /// <summary>新增收藏；同名覆盖（保留原 Id 与创建时间）。</summary>
    public static List<FavoriteItem> Add(List<FavoriteItem> items, string name, string engine, string db, string sql)
    {
        var old = items.FirstOrDefault(i => i.Name == name);
        if (old is null) items.Add(new FavoriteItem { Name = name, Engine = engine, Database = db, Sql = sql });
        else { old.Engine = engine; old.Database = db; old.Sql = sql; }
        return items;
    }

    /// <summary>按 Id 删除，返回是否删到。</summary>
    public static bool Remove(List<FavoriteItem> items, string id)
    {
        var n = items.RemoveAll(i => i.Id == id);
        return n > 0;
    }
}
