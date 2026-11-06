using System.IO;

namespace TaoDBM.Core;

/// <summary>
/// 轻量文本日志（2026-11-03 用户定调：诊断信息走日志不弹窗）。
/// 目录固定在数据根 logs\taodbm.log；写失败绝对不抛——日志永远不能成为新的故障源。
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();

    /// <summary>日志目录：数据根下 logs（默认 D:\TaoDBMData\TaoDBM\logs，D 盘不可用时回落本地数据目录）。</summary>
    public static string Dir { get; } = Path.Combine(TaoPaths.Root, "logs");

    /// <summary>日志文件完整路径。</summary>
    public static string File { get; } = Path.Combine(Dir, "taodbm.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                System.IO.File.AppendAllText(File,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch { /* 日志写失败静默：诊断绝不干扰业务 */ }
    }
}
