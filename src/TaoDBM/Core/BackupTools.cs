using System.Diagnostics;

namespace TaoDBM.Core;

/// <summary>外部工具执行规格：命令行 + 环境变量（密码走环境变量，不进命令行）。</summary>
public sealed class ToolSpec
{
    public string FileName { get; set; } = "";
    public string Arguments { get; set; } = "";
    /// <summary>密码等经环境变量传入，不出现在命令行与展示文本里。</summary>
    public Dictionary<string, string> Env { get; set; } = new();
    /// <summary>还原时把该文件内容写到 stdin（mysql / psql）。</summary>
    public string? InputFile { get; set; }
    /// <summary>展示用命令行（不含密码）。</summary>
    public string Describe => FileName + " " + Arguments;
}

/// <summary>备份 / 还原能力矩阵与命令构造（纯逻辑，可测试）。</summary>
public static class BackupTools
{
    /// <summary>各引擎备份还原支持情况：MySQL/PG 用官方 CLI，SQL Server 用 T-SQL，SQLite 文件复制，其余暂不支持。</summary>
    public static bool Supported(DbEngine e) => e is DbEngine.MySQL or DbEngine.PostgreSQL
        or DbEngine.SQLite or DbEngine.SqlServer;

    public static string UnsupportedReason(DbEngine e) => e switch
    {
        DbEngine.Oracle => "Oracle 备份需服务器端 data pump 工具，暂未支持。",
        DbEngine.MongoDB => "MongoDB 备份需 mongodump，暂未支持。",
        DbEngine.Redis => "Redis 备份依赖服务器端 RDB 落盘，暂未支持。",
        _ => "",
    };

    /// <summary>命令行参数加引号（含空格或为空时）。</summary>
    public static string QuoteArg(string v) => v.Contains(' ') ? $"\"{v}\"" : v;

    // ---------- MySQL ----------

    public static ToolSpec MysqlDump(string host, int port, string user, string password, string db, string file)
    {
        return new ToolSpec
        {
            FileName = "mysqldump",
            Arguments = $"--host={QuoteArg(host)} --port={port} --user={QuoteArg(user)}" +
                $" --result-file={QuoteArg(file)} --routines --events {QuoteArg(db)}",
            Env = password.Length > 0 ? new() { ["MYSQL_PWD"] = password } : new(),
        };
    }

    public static ToolSpec MysqlRestore(string host, int port, string user, string password, string db, string file)
    {
        return new ToolSpec
        {
            FileName = "mysql",
            Arguments = $"--host={QuoteArg(host)} --port={port} --user={QuoteArg(user)} {QuoteArg(db)}",
            Env = password.Length > 0 ? new() { ["MYSQL_PWD"] = password } : new(),
            InputFile = file,
        };
    }

    // ---------- PostgreSQL ----------

    public static ToolSpec PgDump(string host, int port, string user, string password, string db, string file)
    {
        return new ToolSpec
        {
            FileName = "pg_dump",
            Arguments = $"--host={QuoteArg(host)} --port={port} --username={QuoteArg(user)}" +
                $" --file={QuoteArg(file)} --no-owner {QuoteArg(db)}",
            Env = password.Length > 0 ? new() { ["PGPASSWORD"] = password } : new(),
        };
    }

    public static ToolSpec PgRestore(string host, int port, string user, string password, string db, string file)
    {
        return new ToolSpec
        {
            FileName = "psql",
            Arguments = $"--host={QuoteArg(host)} --port={port} --username={QuoteArg(user)} --dbname={QuoteArg(db)} --file={QuoteArg(file)}",
            Env = password.Length > 0 ? new() { ["PGPASSWORD"] = password } : new(),
        };
    }

    // ---------- SQL Server（T-SQL，经 ADO 连接执行，路径为服务器侧路径） ----------

    public static string SqlServerBackupSql(string db, string file) =>
        $"BACKUP DATABASE {QuoteBracket(db)} TO DISK = '{file.Replace("'", "''")}' WITH FORMAT, INIT";

    public static string SqlServerRestoreSql(string db, string file) =>
        $"RESTORE DATABASE {QuoteBracket(db)} FROM DISK = '{file.Replace("'", "''")}' WITH REPLACE";

    private static string QuoteBracket(string s) => "[" + s.Replace("]", "]]") + "]";

    // ---------- SQLite（文件复制，地址为连接里填的库文件路径） ----------

    public static void SqliteBackup(string dbFile, string file)
    {
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file) ?? ".");
        File.Copy(dbFile, file, overwrite: true);
    }

    public static void SqliteRestore(string dbFile, string file)
    {
        if (!File.Exists(file)) throw new FileNotFoundException("还原文件不存在：" + file);
        File.Copy(file, dbFile, overwrite: true);
    }
}
