using System.IO;
using System.Text.Json;

namespace TaoDBM.Core;

/// <summary>关闭主窗口的行为：每次询问（默认）/ 最小化到托盘 / 直接退出。</summary>
public enum CloseBehavior { Ask, Tray, Exit }

/// <summary>应用偏好持久化（托盘 / 关闭行为等）；损坏、缺失回落默认，绝不闪退。</summary>
public static class AppPrefs
{
    private static string _file = "";
    private static string File => _file.Length > 0 ? _file : TaoPaths.FilePath("appprefs.json");   // Tao 系列存储默认 D 盘

    /// <summary>测试或自定义存储位置。</summary>
    public static void UseFile(string path) => _file = path;

    /// <summary>读取关闭行为；任何异常都安全回落 Ask。</summary>
    public static CloseBehavior Load()
    {
        try
        {
            if (!System.IO.File.Exists(File)) return CloseBehavior.Ask;
            var v = JsonSerializer.Deserialize<PrefsFile>(System.IO.File.ReadAllText(File));
            return v?.CloseBehavior switch
            {
                "tray" => CloseBehavior.Tray,
                "exit" => CloseBehavior.Exit,
                _ => CloseBehavior.Ask,
            };
        }
        catch (Exception ex)
        {
            Core.AppLog.Write($"偏好读取失败，回落每次询问: {ex.Message}");
            return CloseBehavior.Ask;
        }
    }

    public static void Save(CloseBehavior behavior)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File)!);
            System.IO.File.WriteAllText(File, JsonSerializer.Serialize(new PrefsFile
            {
                CloseBehavior = behavior switch
                {
                    CloseBehavior.Tray => "tray",
                    CloseBehavior.Exit => "exit",
                    _ => "ask",
                }
            }));
        }
        catch (Exception ex) { Core.AppLog.Write($"偏好保存失败: {ex.Message}"); }
    }

    private sealed class PrefsFile { public string CloseBehavior { get; set; } = "ask"; }
}
