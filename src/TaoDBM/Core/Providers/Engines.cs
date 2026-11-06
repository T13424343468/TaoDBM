using System.Data.Common;

namespace TaoDBM.Core;

public class MySqlProvider : RelationalProvider
{
    public MySqlProvider(ConnectionProfile p) : base(p) { }

    protected override char OpenQuote => '`';
    protected override char CloseQuote => '`';
    public override bool SupportsBrowse => true;

    private MySqlConnector.MySqlConnection Build(string? db)
    {
        var (host, port) = Endpoint();
        var sb = new MySqlConnector.MySqlConnectionStringBuilder
        {
            Server = host,
            Port = (uint)(port > 0 ? port : EngineInfo.DefaultPort(DbEngine.MySQL)),
            UserID = Profile.User,
            Password = Profile.Password,
            AllowUserVariables = true,
            ConnectionTimeout = 10,
        };
        if (!string.IsNullOrEmpty(db)) sb.Database = db;
        foreach (var kv in Profile.Parameters.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = kv.IndexOf('=');
            if (i > 0) sb[kv[..i].Trim()] = kv[(i + 1)..].Trim();
        }
        return new MySqlConnector.MySqlConnection(sb.ConnectionString);
    }

    protected override DbConnection Open(string? db) => Build(db);

    protected override string ListDatabasesSql() =>
        "SELECT SCHEMA_NAME FROM information_schema.SCHEMATA ORDER BY SCHEMA_NAME";

    protected override string ListChildrenSql(string db) => "SHOW TABLES";

    protected override string ListColumnsSql(string db, string table) =>
        "SELECT COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, COLUMN_KEY FROM information_schema.COLUMNS" +
        $" WHERE TABLE_SCHEMA = {Lit(db)} AND TABLE_NAME = {Lit(table)} ORDER BY ORDINAL_POSITION";

    protected override string PagingClause(int offset, int limit) => $"LIMIT {limit} OFFSET {offset}";
}

public class PostgreSqlProvider : RelationalProvider
{
    public PostgreSqlProvider(ConnectionProfile p) : base(p) { }

    protected override char OpenQuote => '"';
    protected override char CloseQuote => '"';
    public override bool SupportsBrowse => true;

    private Npgsql.NpgsqlConnection Build(string? db)
    {
        var (host, port) = Endpoint();
        var sb = new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port > 0 ? port : EngineInfo.DefaultPort(DbEngine.PostgreSQL),
            Username = Profile.User,
            Password = Profile.Password,
            Timeout = 10,
        };
        sb.Database = string.IsNullOrEmpty(db) ? (string.IsNullOrEmpty(Profile.Database) ? "postgres" : Profile.Database) : db;
        foreach (var kv in Profile.Parameters.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = kv.IndexOf('=');
            if (i > 0) sb[kv[..i].Trim()] = kv[(i + 1)..].Trim();
        }
        return new Npgsql.NpgsqlConnection(sb.ConnectionString);
    }

    protected override DbConnection Open(string? db) => Build(db);

    protected override string ListDatabasesSql() =>
        "SELECT datname FROM pg_database WHERE datallowconn AND NOT datistemplate ORDER BY datname";

    protected override string ListChildrenSql(string db) =>
        "SELECT table_schema || '.' || table_name FROM information_schema.tables" +
        " WHERE table_schema NOT IN ('pg_catalog','information_schema')" +
        " AND table_schema !~ '^pg_' AND table_type = 'BASE TABLE'";

    protected override string ListColumnsSql(string db, string table)
    {
        var dot = table.IndexOf('.');
        string schema = dot > 0 ? table[..dot] : "public";
        string name = dot > 0 ? table[(dot + 1)..] : table;
        return "SELECT c.column_name, c.data_type, c.is_nullable, CASE WHEN tc.constraint_type = 'PRIMARY KEY' THEN 'PRI' ELSE '' END" +
            " FROM information_schema.columns c" +
            " LEFT JOIN information_schema.key_column_usage k ON k.table_schema = c.table_schema AND k.table_name = c.table_name AND k.column_name = c.column_name" +
            " LEFT JOIN information_schema.table_constraints tc ON tc.constraint_name = k.constraint_name AND tc.constraint_type = 'PRIMARY KEY'" +
            $" WHERE c.table_schema = {Lit(schema)} AND c.table_name = {Lit(name)} ORDER BY c.ordinal_position";
    }

    protected override string PagingClause(int offset, int limit) => $"LIMIT {limit} OFFSET {offset}";
}

public class SqlServerProvider : RelationalProvider
{
    public SqlServerProvider(ConnectionProfile p) : base(p) { }

    protected override char OpenQuote => '[';
    protected override char CloseQuote => ']';
    public override bool SupportsBrowse => true;

    private Microsoft.Data.SqlClient.SqlConnection Build(string? db)
    {
        var (host, port) = Endpoint();
        var sb = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
        {
            DataSource = port > 0 ? $"{host},{port}" : host,
            UserID = Profile.User,
            Password = Profile.Password,
            TrustServerCertificate = true,
            ConnectTimeout = 10,
        };
        if (!string.IsNullOrEmpty(db)) sb.InitialCatalog = db;
        foreach (var kv in Profile.Parameters.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = kv.IndexOf('=');
            if (i > 0) sb[kv[..i].Trim()] = kv[(i + 1)..].Trim();
        }
        return new Microsoft.Data.SqlClient.SqlConnection(sb.ConnectionString);
    }

    protected override DbConnection Open(string? db) => Build(db);

    protected override string ListDatabasesSql() => "SELECT name FROM sys.databases ORDER BY name";

    // 多 schema：与 PostgreSQL 一致返回 schema.table（含 dbo），树与 Quote 逐段引用均支持点号
    protected override string ListChildrenSql(string db) =>
        "SELECT s.name + '.' + t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id ORDER BY 1";

    protected override string ListColumnsSql(string db, string table) =>
        "SELECT c.name, t.name + CASE WHEN t.name IN ('nvarchar','varchar','nchar','char') THEN '(' + CAST(c.max_length AS varchar) + ')' ELSE '' END," +
        " c.is_nullable, CASE WHEN ic.column_id IS NOT NULL THEN 'PRI' ELSE '' END" +
        " FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id" +
        " LEFT JOIN sys.index_columns ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id" +
        " LEFT JOIN sys.indexes ix ON ix.object_id = ic.object_id AND ix.index_id = ic.index_id AND ix.is_primary_key = 1" +
        $" WHERE c.object_id = OBJECT_ID(N'{table.Replace("'", "''")}') ORDER BY c.column_id";

    protected override string PagingClause(int offset, int limit) =>
        $"ORDER BY (SELECT NULL) OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY";
}

public class SqliteProvider : RelationalProvider
{
    public SqliteProvider(ConnectionProfile p) : base(p) { }

    protected override char OpenQuote => '"';
    protected override char CloseQuote => '"';
    public override bool SupportsBrowse => true;

    private Microsoft.Data.Sqlite.SqliteConnection Build()
    {
        var path = Profile.Database;
        if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("请先在连接设置中指定 SQLite 数据库文件路径。");
        return new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate,
        }.ConnectionString);
    }

    protected override DbConnection Open(string? db) => Build();

    protected override string ListDatabasesSql() => "SELECT 'main'";

    protected override string ListChildrenSql(string db) =>
        "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";

    protected override string ListColumnsSql(string db, string table) =>
        $"PRAGMA table_info({Quote(table)})";

    public override Task<List<ColumnSpec>> GetColumns(string db, string table) => Task.Run(async () =>
    {
        var list = new List<ColumnSpec>();
        await using var c = Open(null);
        await c.OpenAsync();
        foreach (var r in await ReadAsync(c, ListColumnsSql(null!, table)))
        {
            list.Add(new ColumnSpec
            {
                Name = Convert.ToString(r.GetValue(1)) ?? "",
                DataType = Convert.ToString(r.GetValue(2)) ?? "",
                Nullable = Convert.ToInt64(r.GetValue(3)) == 0,
                IsKey = Convert.ToInt64(r.GetValue(5)) > 0,
            });
        }
        return list;
    });


    protected override string PagingClause(int offset, int limit) => $"LIMIT {limit} OFFSET {offset}";
}

public class OracleProvider : RelationalProvider
{
    public OracleProvider(ConnectionProfile p) : base(p) { }

    protected override char OpenQuote => '"';
    protected override char CloseQuote => '"';
    public override bool SupportsBrowse => true;

    private Oracle.ManagedDataAccess.Client.OracleConnection Build(string? db)
    {
        var (host, port) = Endpoint();
        var sb = new Oracle.ManagedDataAccess.Client.OracleConnectionStringBuilder
        {
            DataSource = $"{host}:{(port > 0 ? port : EngineInfo.DefaultPort(DbEngine.Oracle))}/{(string.IsNullOrEmpty(Profile.Parameters) ? "orcl" : Profile.Parameters)}",
            UserID = Profile.User,
            Password = Profile.Password,
            ConnectionTimeout = 10,
        };
        return new Oracle.ManagedDataAccess.Client.OracleConnection(sb.ConnectionString);
    }

    protected override DbConnection Open(string? db) => Build(db);

    protected override string ListDatabasesSql() =>
        "SELECT username FROM all_users ORDER BY username";

    protected override string ListChildrenSql(string db) =>
        "SELECT table_name FROM user_tables ORDER BY table_name";

    protected override string ListColumnsSql(string db, string table) =>
        "SELECT column_name, data_type || CASE WHEN data_type IN ('VARCHAR2','NVARCHAR2','CHAR') THEN '(' || data_length || ')' ELSE '' END," +
        " CASE WHEN nullable = 'Y' THEN 'YES' ELSE 'NO' END, ''" +
        " FROM user_tab_columns WHERE table_name = UPPER('" + table.Replace("'", "''") + "') ORDER BY column_id";

    protected override string PagingClause(int offset, int limit) =>
        $"OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY";
}
