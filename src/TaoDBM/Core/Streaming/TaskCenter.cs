using System.Threading.Channels;

namespace TaoDBM.Core.Streaming;

/// <summary>任务规格：提交长活时的描述。</summary>
public sealed class TaskSpec
{
    public string Kind { get; init; } = "query";   // query / export / sync / import
    public string Title { get; init; } = "";
    public string Db { get; init; } = "";
    public string Sql { get; init; } = "";
}

/// <summary>任务句柄：取消与查询快照的唯一凭据。</summary>
public sealed class TaskHandle
{
    public string TaskId { get; init; } = "";
    public TaskSpec Spec { get; init; } = new();
    internal CancellationTokenSource Cts { get; } = new();
    public CancellationToken Token => Cts.Token;
}

/// <summary>任务快照：任务中心列表可见的一行。</summary>
public sealed record TaskSnapshot
{
    public string TaskId { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Title { get; init; } = "";
    public string State { get; init; } = "";       // running / done / failed / canceled
    public string Message { get; init; } = "";
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
}

/// <summary>
/// 统一任务中心：所有长活（大查询/导出/同步/导入）都进队列串行执行，
/// 任务状态进内存快照表（方案书 2.1）。
/// </summary>
public sealed class TaskCenter
{
    private readonly Channel<TaskHandle> _queue;
    private readonly Dictionary<string, TaskSnapshot> _states = new();
    private readonly object _lock = new();

    public TaskCenter()
    {
        _queue = Channel.CreateUnbounded<TaskHandle>();
        _ = Task.Run(LoopAsync); // 常驻消费循环：进程生命周期内存续，无页面级退订
    }

    public TaskHandle Submit(TaskSpec spec)
    {
        var h = new TaskHandle { TaskId = Guid.NewGuid().ToString("N")[..12], Spec = spec };
        lock (_lock) _states[h.TaskId] = new TaskSnapshot
        {
            TaskId = h.TaskId, Kind = spec.Kind, Title = spec.Title,
            State = "running", StartedAt = DateTimeOffset.Now,
        };
        _queue.Writer.TryWrite(h);
        return h;
    }

    public IReadOnlyList<TaskSnapshot> List()
    {
        lock (_lock) return _states.Values.OrderByDescending(s => s.StartedAt).ToList();
    }

    public bool Cancel(string taskId)
    {
        lock (_lock)
        {
            if (!_states.TryGetValue(taskId, out var s) || s.State != "running") return false;
            _states[taskId] = s with { State = "canceled", EndedAt = DateTimeOffset.Now };
        }
        return true;
    }

    internal void Complete(string taskId, string state, string message)
    {
        lock (_lock)
        {
            if (!_states.TryGetValue(taskId, out var s)) return;
            _states[taskId] = s with { State = state, Message = message, EndedAt = DateTimeOffset.Now };
        }
    }

    private async Task LoopAsync()
    {
        await foreach (var h in _queue.Reader.ReadAllAsync())
        {
            var snapshot = List().FirstOrDefault(s => s.TaskId == h.TaskId);
            // Cancel 已把任务标记 canceled 的：直接跳过执行
            if (snapshot is null || snapshot.State != "running") { h.Cts.Dispose(); continue; }
            try
            {
                await RunAsync(h);
                Complete(h.TaskId, "done", "");
            }
            catch (OperationCanceledException)
            {
                Complete(h.TaskId, "canceled", "已取消");
            }
            catch (Exception ex)
            {
                Complete(h.TaskId, "failed", "错误：" + ex.Message);
            }
            h.Cts.Dispose();
        }
    }

    /// <summary>执行体由宿主注入：把 TaskHandle 变成实际工作（查询/导出/同步/导入）。</summary>
    internal Func<TaskHandle, Task>? Runner { get; set; }
    private Task RunAsync(TaskHandle h) => Runner is null ? Task.CompletedTask : Runner(h);
}
