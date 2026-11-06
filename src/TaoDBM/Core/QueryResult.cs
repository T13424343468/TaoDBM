namespace TaoDBM.Core;

public class QueryResult
{
    public string Title { get; set; } = "结果";
    public List<string> Columns { get; set; } = new();
    public List<object?[]> Rows { get; set; } = new();
    /// <summary>-1 表示非写语句。</summary>
    public int Affected { get; set; } = -1;
    public TimeSpan Elapsed { get; set; }
    /// <summary>浏览模式下的总行数（可空）。</summary>
    public long? TotalRows { get; set; }
    public string Message { get; set; } = "";
    public bool HasRows => Rows.Count > 0;
}

public class ColumnSpec
{
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "";
    public bool Nullable { get; set; } = true;
    public bool IsKey { get; set; }

    public override string ToString() =>
        Name + (string.IsNullOrEmpty(DataType) ? "" : " · " + DataType) + (IsKey ? " · 🔑" : "");
}
