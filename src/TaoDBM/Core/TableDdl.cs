namespace TaoDBM.Core;

/// <summary>表结构设计器里的一列定义。</summary>
public class TableColumnDesign
{
    public string Name { get; set; } = "";
    /// <summary>原始 SQL 类型，如 INT、VARCHAR(50)、TIMESTAMP。</summary>
    public string SqlType { get; set; } = "";
    public bool Nullable { get; set; } = true;
    public bool IsPrimaryKey { get; set; }
    public bool AutoIncrement { get; set; }
    /// <summary>默认值表达式，空表示不设置。</summary>
    public string DefaultValue { get; set; } = "";

    public bool SameNameAs(string? other) => Name.Equals(other, StringComparison.OrdinalIgnoreCase);
}

/// <summary>一张表的列定义集合（新建或改表）。</summary>
public class TableDesign
{
    public string TableName { get; set; } = "";
    public List<TableColumnDesign> Columns { get; set; } = new();

    public static TableDesign FromSpecs(string table, IEnumerable<ColumnSpec> specs) => new()
    {
        TableName = table,
        Columns = specs.Select(s => new TableColumnDesign
        {
            Name = s.Name,
            SqlType = s.DataType,
            Nullable = s.Nullable,
            IsPrimaryKey = s.IsKey,
        }).ToList(),
    };
}

/// <summary>按引擎方言生成建表 / 改表 DDL（纯逻辑，可测试）。</summary>
public static class TableDdl
{
    public static string DefaultType(DbEngine e) => e switch
    {
        DbEngine.Oracle => "VARCHAR2(100)",
        _ => "VARCHAR(100)",
    };

    /// <summary>生成 CREATE TABLE 语句；autoIncrement 列按方言落到对应位置。</summary>
    public static string BuildCreate(TableDesign t, DbEngine engine, Func<string, string> quote)
    {
        var pks = t.Columns.Where(c => c.IsPrimaryKey).Select(c => quote(c.Name)).ToList();
        bool tableLevelPk = pks.Count > 1;
        var lines = new List<string>();
        foreach (var c in t.Columns)
            lines.Add("  " + ColumnDef(c, engine, quote, tableLevelPk));
        if (tableLevelPk)
            lines.Add("  PRIMARY KEY (" + string.Join(", ", pks) + ")");
        return $"CREATE TABLE {quote(t.TableName)} (\n{string.Join(",\n", lines)}\n)";
    }

    /// <summary>生成 ALTER 语句：新增列 / 修改列 / 删除列（与 existingNames 对比）。
    /// SQLite 不支持修改列类型，记入 skipped。modifyExisting=false 只加列不改列；
    /// dropMissing=false 不删除目标侧多余列（结构同步默认不开删）。</summary>
    public static (List<string> Statements, List<string> Skipped) BuildAlter(
        TableDesign t, IEnumerable<string> existingNames, DbEngine engine, Func<string, string> quote,
        bool modifyExisting = true, bool dropMissing = true)
    {
        var stmts = new List<string>();
        var skipped = new List<string>();
        var table = quote(t.TableName);
        var existing = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keep = t.Columns.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 新增列
        foreach (var c in t.Columns.Where(c => c.Name.Length > 0 && !existing.Contains(c.Name)))
        {
            if (c.AutoIncrement && engine is DbEngine.SqlServer or DbEngine.Oracle)
            {
                skipped.Add($"{engine}: 无法对已有表新增 IDENTITY/自增列（{c.Name}），请手动处理。");
                c.AutoIncrement = false;
            }
            var def = ColumnDef(c, engine, quote, tableLevelPk: false);
            stmts.Add(engine == DbEngine.Oracle
                ? $"ALTER TABLE {table} ADD ({def})"
                : $"ALTER TABLE {table} ADD {def}");
        }

        // 修改已有列（自增属性一律不可改，跳过）
        if (modifyExisting)
        foreach (var c in t.Columns.Where(c => c.Name.Length > 0 && existing.Contains(c.Name)))
        {
            if (engine == DbEngine.SQLite)
            {
                skipped.Add("SQLite: 不支持修改列类型/属性，仅支持加列（SQLite 官方限制）。");
                continue;
            }
            string type = TypeOr(c, engine);
            string nullable = !c.Nullable ? " NOT NULL" : "";
            stmts.Add(engine switch
            {
                DbEngine.MySQL => $"ALTER TABLE {table} MODIFY COLUMN {ColumnDef(c, engine, quote, tableLevelPk: false, withIdentity: false)}",
                DbEngine.SqlServer => $"ALTER TABLE {table} ALTER COLUMN {quote(c.Name)} {type}{nullable}",
                DbEngine.Oracle => $"ALTER TABLE {table} MODIFY ({quote(c.Name)} {type}{nullable})",
                _ => "", // PostgreSQL 在下方单独拼（可能拆两条）
            });
            if (engine == DbEngine.PostgreSQL)
            {
                stmts.Add($"ALTER TABLE {table} ALTER COLUMN {quote(c.Name)} TYPE {type}");
                stmts.Add(c.Nullable
                    ? $"ALTER TABLE {table} ALTER COLUMN {quote(c.Name)} DROP NOT NULL"
                    : $"ALTER TABLE {table} ALTER COLUMN {quote(c.Name)} SET NOT NULL");
            }
        }

        // 删除列
        if (dropMissing)
            foreach (var name in existingNames.Where(n => n.Length > 0 && !keep.Contains(n)))
                stmts.Add($"ALTER TABLE {table} DROP COLUMN {quote(name)}");

        return (stmts.Where(s => s.Length > 0).ToList(), skipped);
    }

    private static string TypeOr(TableColumnDesign c, DbEngine e) =>
        string.IsNullOrWhiteSpace(c.SqlType) ? DefaultType(e) : c.SqlType.Trim();

    /// <summary>单列定义片段。withIdentity=false 时忽略自增（改表场景）。</summary>
    private static string ColumnDef(TableColumnDesign c, DbEngine e, Func<string, string> quote, bool tableLevelPk, bool withIdentity = true)
    {
        string name = quote(c.Name);
        string type = TypeOr(c, e);
        bool inc = withIdentity && c.AutoIncrement;
        var parts = new List<string>();

        if (inc && e == DbEngine.SQLite)
        {
            // SQLite 自增只支持 INTEGER PRIMARY KEY AUTOINCREMENT
            return $"{name} INTEGER PRIMARY KEY AUTOINCREMENT";
        }
        parts.Add(name);
        parts.Add(type);
        if (inc)
        {
            switch (e)
            {
                case DbEngine.MySQL: parts.Add("AUTO_INCREMENT"); break;
                case DbEngine.PostgreSQL: parts.Add("GENERATED BY DEFAULT AS IDENTITY"); break;
                case DbEngine.SqlServer: parts.Add("IDENTITY(1,1)"); break;
                case DbEngine.Oracle: parts.Add("GENERATED ALWAYS AS IDENTITY"); break;
            }
            // 自增列隐式非空；单列主键仍需内联 PRIMARY KEY
            if (c.IsPrimaryKey && !tableLevelPk) parts.Add("PRIMARY KEY");
        }
        else
        {
            if (c.IsPrimaryKey && !tableLevelPk) parts.Add("PRIMARY KEY");
            else if (!c.Nullable) parts.Add("NOT NULL");
        }
        if (c.DefaultValue.Trim().Length > 0) parts.Add("DEFAULT " + FormatDefault(c.DefaultValue.Trim()));
        return string.Join(" ", parts);
    }

    /// <summary>建表语句包装为语句列表（统一执行入口用）。</summary>
    public static List<string> AsStatements(string sql) => new() { sql.TrimEnd(';') };

    /// <summary>默认值：NULL / 数字 / 常见时间表达式原样，其余加单引号。</summary>
    public static string FormatDefault(string v)
    {
        if (v.Equals("NULL", StringComparison.OrdinalIgnoreCase)) return "NULL";
        if (decimal.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, out _)) return v;
        if (System.Text.RegularExpressions.Regex.IsMatch(v,
                @"^(CURRENT_\w+|NOW\(\)|GETDATE\(\)|SYSDATE|SYSTIMESTAMP|RAND\(\)|UUID\(\))$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return v;
        return "'" + v.Replace("'", "''") + "'";
    }
}
