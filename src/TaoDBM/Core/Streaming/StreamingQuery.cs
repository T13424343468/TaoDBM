using System.Data.Common;

namespace TaoDBM.Core.Streaming;

/// <summary>
/// 流式查询会话：一次查询产生一个会话，DataReader 直驱逐页取数，不落中间文件。
/// 关闭会话即释放底层连接；取消 = Dispose（方案书 2.1）。
/// </summary>
public sealed class StreamingQuery : IAsyncDisposable
{
    private readonly DbConnection _conn;
    private readonly DbCommand _cmd;
    private readonly DbDataReader? _reader;
    private readonly IReadOnlyList<string> _columns;
    private readonly long? _totalEstimate;

    public string QueryId { get; }

    /// <summary>结果集列名；无结果集（DDL/DML）为空表。</summary>
    public IReadOnlyList<string> Columns => _columns;

    /// <summary>引擎能估总行数则给，不能给 null。</summary>
    public int? TotalEstimate => _totalEstimate is null ? null : (int)Math.Min(_totalEstimate.Value, int.MaxValue);

    /// <summary>无结果集语句（DDL/DML）的受影响行数；查询语句为 -1。</summary>
    public int Affected { get; }

    private StreamingQuery(DbConnection conn, DbCommand cmd, DbDataReader? reader,
        IReadOnlyList<string> columns, long? totalEstimate, int affected)
    {
        _conn = conn; _cmd = cmd; _reader = reader;
        _columns = columns; _totalEstimate = totalEstimate; Affected = affected;
        QueryId = Guid.NewGuid().ToString("N")[..12];
    }

    /// <summary>发起流式查询：打开连接、执行命令、读列名。失败抛异常由调用方（TaskCenter）落任务状态。</summary>
    public static async Task<StreamingQuery> StartAsync(DbConnection conn, string sql, CancellationToken ct)
    {
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var reader = await cmd.ExecuteReaderAsync(ct);
        try
        {
            var cols = new List<string>();
            if (reader.HasRows || reader.FieldCount > 0)
                for (int i = 0; i < reader.FieldCount; i++) cols.Add(reader.GetName(i));
            return new StreamingQuery(conn, cmd, reader, cols, null, reader.RecordsAffected);
        }
        catch
        {
            await reader.DisposeAsync();
            await cmd.DisposeAsync();
            throw;
        }
    }

    /// <summary>取下一页。页尽返回 HasMore=false；无结果集语句返回 Empty。</summary>
    public async ValueTask<QueryPage> NextPageAsync(int size, CancellationToken ct)
    {
        if (_reader is null) return QueryPage.Empty;
        var rows = new List<object?[]>(size);
        while (rows.Count < size && await _reader.ReadAsync(ct))
        {
            var row = new object[_reader.FieldCount];
            _reader.GetValues(row);
            rows.Add(row);
        }
        return new QueryPage { Rows = rows.Cast<object[]>().ToArray(), HasMore = rows.Count == size && await _reader.ReadAsync(ct) };
    }

    public async ValueTask DisposeAsync()
    {
        if (_reader is not null) await _reader.DisposeAsync();
        await _cmd.DisposeAsync();
        await _conn.DisposeAsync();
    }
}
