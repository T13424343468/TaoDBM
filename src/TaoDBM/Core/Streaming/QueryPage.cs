namespace TaoDBM.Core.Streaming;

/// <summary>一页流式结果：本页数据与是否还有更多可取。</summary>
public sealed class QueryPage
{
    /// <summary>空页：无数据且无更多。</summary>
    public static QueryPage Empty { get; } = new();

    public object[][] Rows { get; init; } = Array.Empty<object[]>();
    public bool HasMore { get; init; }
}
