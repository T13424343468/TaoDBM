using Renci.SshNet;

namespace TaoDBM.Core;

/// <summary>
/// SSH 隧道：按连接配置把到数据库的 TCP 连接经 SSH 主机转发到本地随机端口。
/// 同一连接配置复用已建立的隧道，进程退出时随连接关闭。
/// </summary>
public static class SshTunnel
{
    private static readonly object _gate = new();
    private static readonly Dictionary<string, (SshClient Client, ForwardedPortLocal Port)> _active = new();

    /// <summary>未配置隧道时原样返回；配置了则返回 127.0.0.1:本地端口。</summary>
    public static (string Host, int Port) Resolve(ConnectionProfile p, string host, int port)
    {
        if (string.IsNullOrWhiteSpace(p.SshHost) || EngineInfo.IsFileBased(p.Engine))
            return (host, port);

        int realPort = port > 0 ? port : EngineInfo.DefaultPort(p.Engine);
        lock (_gate)
        {
            if (_active.TryGetValue(p.Id, out var t) && t.Client.IsConnected && t.Port.IsStarted)
                return ("127.0.0.1", (int)t.Port.BoundPort);

            // 旧隧道失效则清理重建
            if (_active.TryGetValue(p.Id, out var old))
            {
                try { old.Port.Stop(); old.Client.Disconnect(); old.Client.Dispose(); } catch { }
                _active.Remove(p.Id);
            }

            var client = new SshClient(p.SshHost, p.SshPort > 0 ? p.SshPort : 22, p.SshUser, p.SshPassword);
            client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(15);
            client.Connect();
            if (!client.IsConnected)
                throw new InvalidOperationException($"SSH 连接 {p.SshHost}:{(p.SshPort > 0 ? p.SshPort : 22)} 失败，请检查主机、账号与密码。");

            var fwd = new ForwardedPortLocal("127.0.0.1", 0u, host, (uint)realPort);
            client.AddForwardedPort(fwd);
            fwd.Start();
            _active[p.Id] = (client, fwd);
            return ("127.0.0.1", (int)fwd.BoundPort);
        }
    }

    /// <summary>关闭指定连接的隧道（连接删除或关闭时调用）。</summary>
    public static void StopProfile(string id)
    {
        lock (_gate)
        {
            if (!_active.TryGetValue(id, out var t)) return;
            try { t.Port.Stop(); t.Client.Disconnect(); t.Client.Dispose(); } catch { }
            _active.Remove(id);
        }
    }

    /// <summary>关闭全部隧道（连接删除或程序退出时调用）。</summary>
    public static void StopAll()
    {
        lock (_gate)
        {
            foreach (var (_, (c, f)) in _active)
            {
                try { f.Stop(); c.Disconnect(); c.Dispose(); } catch { }
            }
            _active.Clear();
        }
    }
}
