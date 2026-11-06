using System.IO;
using System.Text.Json;

namespace TaoDBM.Core;

/// <summary>主题选择持久化：三象 ink/gray/night（旧 ant 读取时映射 gray）；损坏、缺失或未知值一律回落水墨默认。</summary>
public static class ThemeStore
{
    private static string _file = "";
    private static string File => _file.Length > 0 ? _file : TaoPaths.FilePath("theme.json");   // Tao 系列存储默认 D 盘

    /// <summary>测试或自定义存储位置。</summary>
    public static void UseFile(string path) => _file = path;

    /// <summary>读取主题名；任何异常都安全回落 "ink"。</summary>
    public static string Load()
    {
        try
        {
            if (!System.IO.File.Exists(File)) return "ink";
            var v = JsonSerializer.Deserialize<ThemeFile>(System.IO.File.ReadAllText(File));
            return ThemePalette.Normalize(v?.Theme);
        }
        catch { return "ink"; }
    }

    public static void Save(string name)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File)!);
        System.IO.File.WriteAllText(File, JsonSerializer.Serialize(new ThemeFile { Theme = ThemePalette.Normalize(name) }));
    }

    private sealed class ThemeFile { public string Theme { get; set; } = "ink"; }
}
