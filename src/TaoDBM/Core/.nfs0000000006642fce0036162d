namespace TaoDBM.Core;

/// <summary>
/// 统一安全包装（tao-code-standard 五.5 家法，2026-11-14 收敛）：
/// 裸 catch { } 一律不允许逐处手写零散补丁，业务路径统一走 Safe.Run（落 AppLog 可诊断），
/// 清理路径（Dispose / 断开 / 退出强杀）统一走 Safe.Cleanup——允许吞但必须 AppLog.Debug 落痕。
/// </summary>
public static class Safe
{
    /// <summary>业务动作包装：失败落 AppLog（含 tag 与异常信息），不让异常外溢。</summary>
    public static void Run(Action action, string tag)
    {
        try { action(); }
        catch (Exception ex) { AppLog.Write($"[{tag}] {ex.Message}"); }
    }

    /// <summary>带返回值包装：失败落 AppLog 并返回 fallback，调用方按回落语义继续。</summary>
    public static T Run<T>(Func<T> action, string tag, T fallback)
    {
        try { return action(); }
        catch (Exception ex) { AppLog.Write($"[{tag}] {ex.Message}"); return fallback; }
    }

    /// <summary>异步业务动作包装：失败落 AppLog，不让异常外溢（UI 事件处理器等 async void 场景）。</summary>
    public static async Task RunAsync(Func<Task> action, string tag)
    {
        try { await action(); }
        catch (Exception ex) { AppLog.Write($"[{tag}] {ex.Message}"); }
    }

    /// <summary>
    /// 清理动作包装（Dispose / Disconnect / 退出强杀）：失败只落 DEBUG 级痕迹，
    /// 正常退出不刷诊断噪音，但出问题时留痕可查（五.5：允许吞但须落痕）。
    /// </summary>
    public static void Cleanup(Action action, string tag)
    {
        try { action(); }
        catch (Exception ex) { AppLog.Debug($"[{tag}] 清理失败: {ex.Message}"); }
    }

    /// <summary>异步清理动作包装：失败只落 DEBUG 级痕迹，不外溢。</summary>
    public static async Task CleanupAsync(Func<Task> action, string tag)
    {
        try { await action(); }
        catch (Exception ex) { AppLog.Debug($"[{tag}] 清理失败: {ex.Message}"); }
    }
}
