using System.Data.Common;

namespace TaoDBM.Core.Streaming;

/// <summary>分流入口：按 StreamingSwitch 把查询接到流式或旧整包通道（方案书并存口径）。</summary>
public static class QueryRouter
{
    /// <summary>流式执行：跑完返回整包结果（与旧 Execute 同形），内部逐页取数，无 5000 截断。</summary>
    public static async Task<List<QueryResult>> ExecuteAsync(RelationalProvider provider, string db, string script, CancellationToken ct)
    {
        var results = new List<QueryResult>();
        var statements = SqlSplitter.Split(script);
        if (statements.Count == 0) return results;
        await using var conn = provider.OpenStream(db);
        foreach (var sql in statements)
        {
            var qr = new QueryResult { Title = sql.Length > 60 ? sql[..60] + "…" : sql };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await using var sq = await StreamingQuery.StartAsync(conn, sql, ct);
            try
            {
                qr.Columns.AddRange(sq.Columns);
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    var page = await sq.NextPageAsync(2000, ct);
                    foreach (var row in page.Rows) qr.Rows.Add(row);
                    if (!page.HasMore) break;
                }
                Safe.Run(() => qr.Affected = sq.Affected, "读取受影响行数");
                if (qr.Columns.Count == 0 && qr.Affected < 0) qr.Affected = 0;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                qr.Message = "错误：" + ex.Message;
            }
            sw.Stop();
            qr.Elapsed = sw.Elapsed;
            results.Add(qr);
        }
        return results;
    }
}
