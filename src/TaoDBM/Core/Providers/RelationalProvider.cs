using System.Data;
using System.Data.Common;

namespace TaoDBM.Core;

/// <summary>数据库提供方统一接口，关系型与非关系型共用。</summary>
public interface IDbProvider : IDisposable
{
    ConnectionProfile Profile { get; }
    string EngineName { get; }
    bool IsNoSql { get; }

    Task TestConnection();
    Task<List<string>> GetDatabases();
    Task<List<string>> GetChildren(string db);
    Task<List<ColumnSpec>> GetColumns(string db, string child);
    Task<List<QueryResult>> Execute(string db, string script);
    Task<QueryResult> Browse(string db, string child, int offset, int limit);
    bool SupportsBrowse { get; }
    string BrowseLabel { get; }
    /// <summary>标识符引用（支持 schema.table 逐段引用）。</summary>
    string Quote(string ident);
}

public static class ProviderFactory
{
    public static IDbProvider Create(ConnectionProfile p) => p.Engine switch
    {
        DbEngine.MySQL => new MySqlProvider(p),
        DbEngine.PostgreSQL => new PostgreSqlProvider(p),
        DbEngine.SqlServer => new SqlServerProvider(p),
        DbEngine.SQLite => new SqliteProvider(p),
        DbEngine.Oracle => new OracleProvider(p),
        DbEngine.MongoDB => new MongoProvider(p),
        DbEngine.Redis => new RedisProvider(p),
        _ => throw new NotSupportedException("不支持的数据库类型"),
    };
}

public static class SqlSplitter
{
    /// <summary>按分号拆分脚本，跳过引号、反引号、-- 注释与块注释。</summary>
    public static List<string> Split(string script)
    {
        var parts = new List<string>();
        if (string.IsNullOrWhiteSpace(script)) return parts;
        var sb = new System.Text.StringBuilder();
        char quote = '\0';
        int n = script.Length;
        for (int i = 0; i < n; i++)
        {
            char c = script[i];
            if (quote != '\0')
            {
                sb.Append(c);
                if (c == quote)
                {
                    if (i + 1 < n && script[i + 1] == quote) { sb.Append(script[++i]); }
                    else quote = '\0';
                }
                continue;
            }
            if (c is '\'' or '"' or '`')
            {
                quote = c; sb.Append(c); continue;
            }
            if (c == '-' && i + 1 < n && script[i + 1] == '-')
            {
                while (i < n && script[i] != '\n') sb.Append(script[i++]);
                sb.Append('\n'); continue;
            }
            if (c == '/' && i + 1 < n && script[i + 1] == '*')
            {
                int end = script.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) { sb.Append(script[i..]); break; }
                sb.Append(script[i..(end + 2)]); i = end + 1; continue;
            }
            if (c == ';')
            {
                var s = sb.ToString().Trim();
                if (s.Length > 0) parts.Add(s);
                sb.Clear();
                continue;
            }
            sb.Append(c);
        }
        var tail = sb.ToString().Trim();
        if (tail.Length > 0) parts.Add(tail);
        return parts;
    }
}

/// <summary>关系型数据库 ADO 基类。</summary>
public abstract class RelationalProvider : IDbProvider
{
    public ConnectionProfile Profile { get; }
    public string EngineName => EngineInfo.Name(Profile.Engine);
    public bool IsNoSql => false;
    public abstract bool SupportsBrowse { get; }
    public virtual string BrowseLabel => "表";

    protected RelationalProvider(ConnectionProfile p) => Profile = p;

    protected abstract DbConnection Open(string? db);
    protected abstract char OpenQuote { get; }
    protected abstract char CloseQuote { get; }
    protected virtual string Escape(string s) => s.Replace("'", "''");

    /// <summary>引用标识符；支持 schema.table 形式（逐段引用）。</summary>
    public string Quote(string ident)
    {
        var parts = ident.Split('.');
        return string.Join(".", parts.Select(s =>
            OpenQuote + s.Replace(OpenQuote.ToString(), new string(OpenQuote, 2)) + CloseQuote));
    }

    /// <summary>解析实际连接端点（启用 SSH 隧道时返回本地转发端口）。</summary>
    protected (string Host, int Port) Endpoint() =>
        SshTunnel.Resolve(Profile, Profile.Host, Profile.Port > 0 ? Profile.Port : EngineInfo.DefaultPort(Profile.Engine));

    public Task TestConnection() => Task.Run(async () =>
    {
        await using var c = Open(null);
        await c.OpenAsync();
    });


    public Task<List<string>> GetDatabases() => Task.Run(async () =>
    {
        var list = new List<string>();
        await using var c = Open(null);
        await c.OpenAsync();
        foreach (var r in await ReadAsync(c, ListDatabasesSql()))
            list.Add(Convert.ToString(r[0]) ?? "");
        return list.Where(s => s.Length > 0).ToList();
    });


    protected abstract string ListDatabasesSql();

    public Task<List<string>> GetChildren(string db) => Task.Run(async () =>
    {
        var list = new List<string>();
        await using var c = Open(db);
        await c.OpenAsync();
        foreach (var r in await ReadAsync(c, ListChildrenSql(db)))
            list.Add(Convert.ToString(r[0]) ?? "");
        return list.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
    });


    protected abstract string ListChildrenSql(string db);

    public virtual Task<List<ColumnSpec>> GetColumns(string db, string table) => Task.Run(async () =>
    {
        var list = new List<ColumnSpec>();
        await using var c = Open(db);
        await c.OpenAsync();
        foreach (var r in await ReadAsync(c, ListColumnsSql(db, table)))
        {
            list.Add(new ColumnSpec
            {
                Name = Convert.ToString(r.GetValue(0)) ?? "",
                DataType = r.FieldCount > 1 ? Convert.ToString(r.GetValue(1)) ?? "" : "",
                Nullable = r.FieldCount > 2 ? Convert.ToString(r.GetValue(2)) != "NO" : true,
                IsKey = r.FieldCount > 3 ? Convert.ToString(r.GetValue(3)) == "PRI" : false,
            });
        }
        return list;
    });


    public virtual void Dispose() { }

    protected abstract string ListColumnsSql(string db, string table);

    public Task<List<QueryResult>> Execute(string db, string script) => Task.Run(async () =>
    {
        var results = new List<QueryResult>();
        var statements = SqlSplitter.Split(script);
        if (statements.Count == 0) return results;
        await using var c = Open(db);
        await c.OpenAsync();
        foreach (var sql in statements)
        {
            var qr = new QueryResult { Title = sql.Length > 60 ? sql[..60] + "…" : sql };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await using var cmd = c.CreateCommand();
                cmd.CommandText = sql;
                var reader = await cmd.ExecuteReaderAsync();
                try
                {
                    if (reader.HasRows || reader.FieldCount > 0)
                    {
                        for (int i = 0; i < reader.FieldCount; i++) qr.Columns.Add(reader.GetName(i));
                        while (await reader.ReadAsync())
                        {
                            if (qr.Rows.Count >= 5000) { qr.Message += "（结果超过 5000 行，已截断显示）"; break; }
                            var row = new object[reader.FieldCount];
                            reader.GetValues(row);
                            qr.Rows.Add(row);
                        }
                    }
                    // 读完后取受影响行数
                    try { qr.Affected = reader.RecordsAffected; } catch { }
                }
                finally { await reader.DisposeAsync(); }
                // 无结果集的语句（DDL/DML）补充受影响行数
                if (!reader_HadRows(qr) && qr.Affected < 0) qr.Affected = 0;
            }
            catch (Exception ex)
            {
                qr.Message = "错误：" + ex.Message;
            }
            sw.Stop();
            qr.Elapsed = sw.Elapsed;
            results.Add(qr);
        }
        return results;
    });


    private static bool reader_HadRows(QueryResult qr) => qr.Columns.Count > 0;

    public Task<QueryResult> Browse(string db, string table, int offset, int limit) => Task.Run(async () =>
    {
        var qr = new QueryResult { Title = table };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await using var c = Open(db);
        await c.OpenAsync();

        await using (var cnt = c.CreateCommand())
        {
            cnt.CommandText = $"SELECT COUNT(*) FROM {Quote(table)}";
            qr.TotalRows = Convert.ToInt64(await cnt.ExecuteScalarAsync());
        }

        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Quote(table)} {PagingClause(offset, limit)}";
        var reader = await cmd.ExecuteReaderAsync();
        try
        {
            for (int i = 0; i < reader.FieldCount; i++) qr.Columns.Add(reader.GetName(i));
            while (await reader.ReadAsync())
            {
                var row = new object[reader.FieldCount];
                reader.GetValues(row);
                qr.Rows.Add(row);
            }
        }
        finally { await reader.DisposeAsync(); }
        sw.Stop();
        qr.Elapsed = sw.Elapsed;
        return qr;
    });


    protected abstract string PagingClause(int offset, int limit);

    protected async Task<List<DbDataReader>> ReadAsync(DbConnection c, string sql)
    {
        var all = new List<DbDataReader>();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new object[reader.FieldCount];
            reader.GetValues(row);
            all.Add(new ArrayReader(row));
        }
        await reader.DisposeAsync();
        return all;
    }

    /// <summary>把已读出的行包成轻量 reader，便于统一取值。</summary>
    protected sealed class ArrayReader : DbDataReader
    {
        private readonly object?[] _row;
        public ArrayReader(object?[] row) => _row = row;
        public override int FieldCount => _row.Length;
        public override object GetValue(int ordinal) => _row[ordinal] ?? DBNull.Value;
        public override int GetValues(object[] values)
        {
            int n = Math.Min(values.Length, _row.Length);
            for (int i = 0; i < n; i++) values[i] = _row[i] ?? DBNull.Value;
            return n;
        }
        // 其余成员不使用
        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => throw new NotSupportedException();
        public override int Depth => 0;
        public override bool HasRows => _row.Length > 0;
        public override bool IsClosed => false;
        public override int RecordsAffected => -1;
        public override bool NextResult() => false;
        public override bool Read() => false;
        public override string GetName(int i) => throw new NotSupportedException();
        public override string GetDataTypeName(int i) => throw new NotSupportedException();
        public override System.Collections.IEnumerator GetEnumerator() => _row.GetEnumerator();
        public override Type GetFieldType(int i) => GetValue(i).GetType();
        public override int GetOrdinal(string name) => throw new NotSupportedException();
        public override bool GetBoolean(int i) => (bool)GetValue(i);
        public override byte GetByte(int i) => (byte)GetValue(i);
        public override long GetBytes(int i, long buf, byte[]? o, int o2, int o3) => throw new NotSupportedException();
        public override char GetChar(int i) => (char)GetValue(i);
        public override long GetChars(int i, long b, char[]? o, int o2, int o3) => throw new NotSupportedException();
        public override string GetString(int i) => Convert.ToString(GetValue(i)) ?? "";
        public override DateTime GetDateTime(int i) => (DateTime)GetValue(i);
        public override decimal GetDecimal(int i) => (decimal)GetValue(i);
        public override double GetDouble(int i) => Convert.ToDouble(GetValue(i));
        public override System.Data.DataTable GetSchemaTable() => throw new NotSupportedException();
        public override float GetFloat(int i) => Convert.ToSingle(GetValue(i));
        public override short GetInt16(int i) => Convert.ToInt16(GetValue(i));
        public override int GetInt32(int i) => Convert.ToInt32(GetValue(i));
        public override long GetInt64(int i) => Convert.ToInt64(GetValue(i));
        public override Guid GetGuid(int i) => (Guid)GetValue(i);
        public override bool IsDBNull(int i) => GetValue(i) is DBNull;
        public override void Close() { }
    }

    /// <summary>转义 LIKE 模式外的普通字符串字面量。</summary>
    protected static string Lit(string s) => "'" + s.Replace("'", "''") + "'";
}
