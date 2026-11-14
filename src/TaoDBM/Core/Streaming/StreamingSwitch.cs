namespace TaoDBM.Core.Streaming;

/// <summary>
/// 流式通道开关（方案书风险条款）：默认开（streaming），设 DBM_STREAMING=off 一键回旧 Execute 整包路径。
/// 环境变量读取，不落配置文件——回退不需要改用户数据。
/// </summary>
public static class StreamingSwitch
{
    private static readonly bool _enabled =
        !string.Equals(Environment.GetEnvironmentVariable("DBM_STREAMING"), "off", StringComparison.OrdinalIgnoreCase);

    /// <summary>true=走流式通道；false=回旧整包 Execute。</summary>
    public static bool Enabled => _enabled;
}
