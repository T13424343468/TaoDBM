using System.Text;

namespace TaoDBM.Core;

/// <summary>结构同步 + 数据传输（跨库 ETL）的纯逻辑部分。</summary>
public static class DataSync
{
    /// <summary>按主表结构生成目标侧补齐语句：只加缺失列，不删列；
    /// alignTypes=true 时同时对齐共有列类型（有覆盖风险，默认关）。</summary>
    public static (List<string> Statements, List<string> Skipped) SyncColumns(
        string table, List<ColumnSpec> sourceCols, List<string> targetColNames,
        DbEngine targetEngine, Func<string, string> quote, bool alignTypes)
    {
        var design = TableDesign.FromSpecs(table, sourceCols);
        // 只让缺失列进入 ADD 流程
        var missing = design.Columns
            .Where(c => !targetColNames.Contains(c.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        var addOnly = new TableDesign { TableName = table, Columns = missing };
        var (stmts, skipped) = TableDdl.BuildAlter(addOnly, Enumerable.Empty<string>(), targetEngine, quote,
            modifyExisting: false, dropMissing: false);
        if (alignTypes)
        {
            var shared = design.Columns
                .Where(c => targetColNames.Contains(c.Name, StringComparer.OrdinalIgnoreCase)).ToList();
            var sharedDesign = new TableDesign { TableName = table, Columns = shared };
            var (mods, sk2) = TableDdl.BuildAlter(sharedDesign, targetColNames, targetEngine, quote,
                modifyExisting: true, dropMissing: false);
            stmts.AddRange(mods);
            skipped.AddRange(sk2);
        }
        return (stmts, skipped);
    }

    /// <summary>目标表不存在时，按源结构 + 目标方言生成 CREATE TABLE。</summary>
    public static string CreateForTarget(string table, List<ColumnSpec> sourceCols,
        DbEngine targetEngine, Func<string, string> quote)
        => TableDdl.BuildCreate(TableDesign.FromSpecs(table, sourceCols), targetEngine, quote);

    /// <summary>把查询结果转成目标侧批量 INSERT，每批 batch 行一个语句元素（列名经 quote 引用）。</summary>
    public static List<string> InsertBatches(QueryResult r, string quotedTable,
        Func<string, string> quote, int batch = 200)
    {
        var result = new List<string>();
        if (r.Rows.Count == 0) return result;
        var names = r.Columns.Select(quote).ToList();
        var sb = new StringBuilder();
        int inBatch = 0;
        foreach (var row in r.Rows)
        {
            var vals = new string[row.Length];
            for (int i = 0; i < row.Length; i++)
                vals[i] = SqlValue(row[i]);
            sb.Append($"INSERT INTO {quotedTable} ({string.Join(", ", names)}) VALUES ({string.Join(", ", vals)});\n");
            if (++inBatch >= batch)
            {
                result.Add(sb.ToString());
                sb.Clear();
                inBatch = 0;
            }
        }
        if (inBatch > 0) result.Add(sb.ToString());
        return result;
    }

    /// <summary>SQL 字面量：NULL / 二进制 X'' / 布尔 / 日期 / 数字原样 / 字符串转义。</summary>
    public static string SqlValue(object? v) => v switch
    {
        null or DBNull => "NULL",
        byte[] b => "X'" + Convert.ToHexString(b) + "'",
        bool b => b ? "TRUE" : "FALSE",
        System.DateTime dt => "'" + dt.ToString("yyyy-MM-dd HH:mm:ss") + "'",
        sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal => v.ToString() ?? "NULL",
        _ => "'" + v.ToString()?.Replace("'", "''") + "'",
    };
}
