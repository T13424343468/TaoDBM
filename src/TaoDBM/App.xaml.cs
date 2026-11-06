using Microsoft.UI.Xaml;
using System.IO;

namespace TaoDBM;

public partial class App : Application
{
    public static Window? Shell;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            CrashLog.Write($"UnhandledException: {e.Message}\n{e.Exception}");
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            // Tao 系列存储默认 D 盘：首次运行把旧 C 盘数据迁过去（已有同名文件不覆盖）
            Core.TaoPaths.MigrateLegacyFiles();
            // 水墨为默认主题：安装资源画刷并应用已存选择（Tao 系列默认水墨，可切回清爽蓝）
            UI.Ant.ApplyTheme(Core.ThemeStore.Load());
            Shell = new MainWindow();
            // 进程残留专项：最后一个窗口关闭后，框架消息循环/COM 可能把自包含进程吊住，
            // 显式退出并加延迟强杀兜底，确保任务管理器里不留残影。
            Shell.Closed += (_, _) =>
            {
                Current.Exit();
                Task.Run(async () =>
                {
                    try { await Task.Delay(1000); Environment.Exit(0); }
                    catch { }
                });
            };
            Shell.Activate();
        }
        catch (Exception ex)
        {
            CrashLog.Write($"OnLaunched: {ex.Message}\n{ex}");
            throw;
        }
    }
}

/// <summary>崩溃日志：未处理异常与启动失败追加写入 TaoPaths.Root\crash.log（默认 D:\TaoDBMData\TaoDBM\crash.log，D 盘不可用回退 %LOCALAPPDATA%\TaoDBM）。</summary>
public static class CrashLog
{
    private static readonly object Gate = new();

    public static void Write(string text)
    {
        try
        {
            var dir = Core.TaoPaths.Root;   // Tao 系列存储默认 D 盘
            Directory.CreateDirectory(dir);
            lock (Gate)
            {
                File.AppendAllText(Path.Combine(dir, "crash.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志写不进去也不能让程序再崩一次
        }
    }
}
