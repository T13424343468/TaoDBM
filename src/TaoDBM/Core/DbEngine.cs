namespace TaoDBM.Core;

/// <summary>支持的数据库引擎。</summary>
public enum DbEngine
{
    MySQL,
    PostgreSQL,
    SqlServer,
    SQLite,
    Oracle,
    MongoDB,
    Redis,
}

public static class EngineInfo
{
    public static string Name(DbEngine e) => e switch
    {
        DbEngine.MySQL => "MySQL",
        DbEngine.PostgreSQL => "PostgreSQL",
        DbEngine.SqlServer => "SQL Server",
        DbEngine.SQLite => "SQLite",
        DbEngine.Oracle => "Oracle",
        DbEngine.MongoDB => "MongoDB",
        DbEngine.Redis => "Redis",
        _ => e.ToString(),
    };

    public static int DefaultPort(DbEngine e) => e switch
    {
        DbEngine.MySQL => 3306,
        DbEngine.PostgreSQL => 5432,
        DbEngine.SqlServer => 1433,
        DbEngine.Oracle => 1521,
        DbEngine.Redis => 6379,
        DbEngine.MongoDB => 27017,
        _ => 0,
    };

    public static bool IsFileBased(DbEngine e) => e == DbEngine.SQLite;

    public static bool IsRelational(DbEngine e) =>
        e is not (DbEngine.MongoDB or DbEngine.Redis);

    public static IEnumerable<DbEngine> All =>
        new[] { DbEngine.MySQL, DbEngine.PostgreSQL, DbEngine.SqlServer, DbEngine.SQLite,
                DbEngine.Oracle, DbEngine.MongoDB, DbEngine.Redis };
}
