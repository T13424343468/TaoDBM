using System.Text.Json;

namespace TaoDBM.Core;

/// <summary>一条连接配置。</summary>
public class ConnectionProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = "新建连接";
    public DbEngine Engine { get; set; } = DbEngine.MySQL;
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; }
    public string User { get; set; } = "";
    public string Password { get; set; } = "";   // 存储时为 DPAPI 密文
    public string Database { get; set; } = "";   // 默认库 / 文件路径
    public string Parameters { get; set; } = ""; // 附加连接参数 k=v;k2=v2
    public bool SavePassword { get; set; } = true;
    public string ColorTag { get; set; } = "#1677FF";
    // SSH 隧道（留空不启用）
    public string SshHost { get; set; } = "";
    public int SshPort { get; set; } = 22;
    public string SshUser { get; set; } = "";
    public string SshPassword { get; set; } = "";
}

public static class ProfileCrypto
{
    private static readonly byte[] Entropy = "TaoDBM.v1"u8.ToArray();

    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        try
        {
            var data = System.Text.Encoding.UTF8.GetBytes(plain);
            var enc = System.Security.Cryptography.ProtectedData.Protect(data, Entropy, System.Security.Cryptography.DataProtectionScope.LocalMachine);
            return "dpapi:" + Convert.ToBase64String(enc);
        }
        catch { return plain; }
    }

    public static string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        if (!stored.StartsWith("dpapi:", StringComparison.Ordinal)) return stored;
        try
        {
            var enc = Convert.FromBase64String(stored["dpapi:".Length..]);
            var dec = System.Security.Cryptography.ProtectedData.Unprotect(enc, Entropy, System.Security.Cryptography.DataProtectionScope.LocalMachine);
            return System.Text.Encoding.UTF8.GetString(dec);
        }
        catch { return ""; }
    }
}

/// <summary>连接配置存取：%APPDATA%\TaoDBM\connections.json</summary>
public static class ConnectionStore
{
    private static string File => TaoPaths.FilePath("connections.json");   // Tao 系列存储默认 D 盘

    public static List<ConnectionProfile> Load()
    {
        try
        {
            if (!System.IO.File.Exists(File)) return new List<ConnectionProfile>();
            var json = System.IO.File.ReadAllText(File);
            var list = JsonSerializer.Deserialize<List<ConnectionProfile>>(json) ?? new();
            foreach (var p in list) p.Password = ProfileCrypto.Unprotect(p.Password);
            return list;
        }
        catch { return new List<ConnectionProfile>(); }
    }

    public static void Save(List<ConnectionProfile> profiles)
    {
        var copies = profiles.Select(p =>
        {
            var c = Clone(p);
            if (c.SavePassword) c.Password = ProfileCrypto.Protect(c.Password);
            else c.Password = "";
            return c;
        }).ToList();
        var opt = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        System.IO.File.WriteAllText(File, JsonSerializer.Serialize(copies, opt));
    }

    private static ConnectionProfile Clone(ConnectionProfile p) => new()
    {
        Id = p.Id, DisplayName = p.DisplayName, Engine = p.Engine, Host = p.Host,
        Port = p.Port, User = p.User, Password = p.Password, Database = p.Database,
        Parameters = p.Parameters, SavePassword = p.SavePassword, ColorTag = p.ColorTag,
        SshHost = p.SshHost, SshPort = p.SshPort, SshUser = p.SshUser, SshPassword = p.SshPassword,
    };
}
