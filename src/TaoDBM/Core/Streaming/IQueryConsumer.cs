namespace TaoDBM.Core.Streaming;

/// <summary>流式消费契约：网格、导出、同步三个消费面统一从 StreamingQuery 取数。</summary>
public interface IQueryConsumer
{
    ValueTask ConsumeAsync(StreamingQuery q, CancellationToken ct);
}
