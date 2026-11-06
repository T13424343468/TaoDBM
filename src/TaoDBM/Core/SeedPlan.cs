// 拟真数据装配：外键 / 字段名关联推断 → 拓扑排序 → 按方言生成 INSERT 脚本。
// 关联规则：真实外键优先；无外键时按字段名关联（user_id → users.id；同名主键亦可）。
using System.Text.RegularExpressions;

namespace TaoDBM.Core;

/// <summary>一条表间关联：From 表.FromCol 值来自 To 表.ToCol 已生成值。</summary>
public class SeedEdge
{
    public string FromTable { get; set; } = "";
    public string FromCol { get; set; } = "";
    public string ToTable { get; set; } = "";
    public string ToCol { get; set; } = "";
    /// <summary>来源：fk = 真实外键，name = 字段名关联。</summary>
    public string Source { get; set; } = "name";
}

/// <summary>一个待填充的表。</summary>
public class SeedTable
{
    public string Name { get; set; } = "";
    public List<ColumnSpec> Columns { get; set; } = new();
    public int Rows { get; set; } = 50;
}

/// <summary>按方言生成插入脚本的纯逻辑装配器（可测试）。</summary>
public static class SeedPlan
{
    // ---------- 关联推断 ----------

    /// <summary>真实外键 + 字段名关联合并去重（纯函数）。</summary>
    public static List<SeedEdge> BuildRelations(List<SeedTable> tables, List<SeedEdge>? explicitFks)
    {
        var edges = new List<SeedEdge>(explicitFks ?? new());
        var byName = tables.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var t in tables)
        {
            var pkCols = t.Columns.Where(c => c.IsKey).ToList();
            foreach (var col in t.Columns.Where(c => !c.IsKey))
            {
                if (edges.Any(e => e.FromTable == t.Name && e.FromCol == col.Name)) continue;
                var stem = col.Name.EndsWith("_id", StringComparison.OrdinalIgnoreCase) ? col.Name[..^3]
                    : col.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) ? col.Name[..^2] : null;
                if (string.IsNullOrEmpty(stem)) continue;
                // 自引用跳过：避免环
                if (string.Equals(stem, t.Name, StringComparison.OrdinalIgnoreCase)) continue;
                // 目标表：stem 原名 / 复数；目标列 = 目标表任一单列主键（不限定叫 id）
                var target = byName.ContainsKey(stem) ? stem : byName.ContainsKey(stem + "s") ? stem + "s" : null;
                if (target is null) continue;
                var targetTable = byName[target];
                var pk = targetTable.Columns.FirstOrDefault(c => c.IsKey);
                if (pk is null) continue;
                edges.Add(new SeedEdge { FromTable = t.Name, FromCol = col.Name, ToTable = targetTable.Name, ToCol = pk.Name, Source = "name" });
            }
        }
        return edges;
    }

    /// <summary>拓扑排序：被引用的表先填充。无出边约束的环内表按声明顺序（值池兜底）。</summary>
    public static List<SeedTable> TopoOrder(List<SeedTable> tables, List<SeedEdge> edges)
    {
        var byName = tables.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        var indeg = tables.ToDictionary(t => t.Name, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var e in edges)
            if (byName.ContainsKey(e.FromTable) && byName.ContainsKey(e.ToTable) &&
                !string.Equals(e.FromTable, e.ToTable, StringComparison.OrdinalIgnoreCase))
                indeg[e.FromTable]++;

        var order = new List<SeedTable>();
        var pending = new List<SeedTable>(tables);
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool progress = true;
        while (pending.Count > 0 && progress)
        {
            progress = false;
            foreach (var t in pending.ToList())
            {
                if (indeg[t.Name] == 0)
                {
                    order.Add(t); pending.Remove(t); placed.Add(t.Name); progress = true;
                    // 释放：引用了 t 的表依赖减一
                    foreach (var e in edges)
                        if (byName.ContainsKey(e.FromTable) && e.ToTable.Equals(t.Name, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(e.FromTable, e.ToTable, StringComparison.OrdinalIgnoreCase) && indeg[e.FromTable] > 0)
                            indeg[e.FromTable]--;
                }
            }
        }
        // 环兜底：按剩余顺序补齐
        order.AddRange(pending);
        return order;
    }

    // ---------- 方言引号 ----------

    public static string QuoteIdent(DbEngine e, string ident) => e switch
    {
        DbEngine.MySQL => "`" + ident.Replace("`", "``") + "`",
        DbEngine.PostgreSQL or DbEngine.SQLite => "\"" + ident.Replace("\"", "\"\"") + "\"",
        DbEngine.SqlServer => "[" + ident.Replace("]", "]]") + "]",
        _ => ident,
    };

    /// <summary>SQL 字面量：字符串加引号（SQL Server 用 N''），单引号双写；数值原样。</summary>
    public static string SqlValue(DbEngine e, object? v)
    {
        if (v is null) return "NULL";
        if (v is int or long or short or byte or decimal or double or float)
            return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "0";
        if (v is bool b) return b ? "1" : "0";
        var s = v.ToString() ?? "";
        var quoted = "'" + s.Replace("'", "''") + "'";
        return e == DbEngine.SqlServer ? "N" + quoted : quoted;
    }

    /// <summary>把一行拟真值转 INSERT VALUES 元组文本（不含括号内逗号歧义处理由调用方完成）。</summary>
    public static string ValuesRow(DbEngine e, List<object?> vals) =>
        "(" + string.Join(", ", vals.Select(v => SqlValue(e, v))) + ")";

    /// <summary>生成 INSERT 脚本（每批 ≤500 行）。只生成可执行 SQL；调用方按表逐条 Execute。</summary>
    public static List<string> BuildInsertScripts(DbEngine e, string table, List<ColumnSpec> cols, List<List<object?>> rows)
    {
        var scripts = new List<string>();
        if (cols.Count == 0 || rows.Count == 0) return scripts;
        var colList = string.Join(", ", cols.Select(c => QuoteIdent(e, c.Name)));
        const int batch = 500;
        for (int start = 0; start < rows.Count; start += batch)
        {
            var chunk = rows.Skip(start).Take(batch);
            var values = string.Join(",\n", chunk.Select(r => ValuesRow(e, r)));
            scripts.Add($"INSERT INTO {QuoteIdent(e, table)} ({colList}) VALUES\n{values};");
        }
        return scripts;
    }

    // ---------- 外键发现 SQL（按方言；失败返回空，由调用方回落字段名推断） ----------

    public static string? ForeignKeySql(DbEngine e, string db, string table) => e switch
    {
        DbEngine.MySQL => $"SELECT COLUMN_NAME AS FK_COL, REFERENCED_TABLE_NAME AS REF_TABLE, REFERENCED_COLUMN_NAME AS REF_COL " +
                          $"FROM information_schema.KEY_COLUMN_USAGE " +
                          $"WHERE TABLE_SCHEMA = '{db.Replace("'", "''")}' AND TABLE_NAME = '{table.Replace("'", "''")}' " +
                          $"AND REFERENCED_TABLE_NAME IS NOT NULL",
        DbEngine.PostgreSQL => "SELECT kcu.column_name AS FK_COL, ccu.table_name AS REF_TABLE, ccu.column_name AS REF_COL " +
                               "FROM information_schema.table_constraints tc " +
                               "JOIN information_schema.key_column_usage kcu ON tc.constraint_name = kcu.constraint_name " +
                               "JOIN information_schema.constraint_column_usage ccu ON tc.constraint_name = ccu.constraint_name " +
                               $"WHERE tc.constraint_type = 'FOREIGN KEY' AND tc.table_name = '{table.Replace("'", "''")}'",
        DbEngine.SqlServer => "SELECT c.name AS FK_COL, OBJECT_NAME(f.referenced_object_id) AS REF_TABLE, rc.name AS REF_COL " +
                              "FROM sys.foreign_keys f " +
                              "JOIN sys.foreign_key_columns fc ON f.object_id = fc.constraint_object_id " +
                              "JOIN sys.columns c ON c.object_id = fc.parent_object_id AND c.column_id = fc.parent_column_id " +
                              "JOIN sys.columns rc ON rc.object_id = f.referenced_object_id AND rc.column_id = fc.referenced_column_id " +
                              $"WHERE OBJECT_NAME(f.parent_object_id) = '{table.Replace("'", "''")}'",
        DbEngine.Oracle => "SELECT cc.column_name AS FK_COL, rcc.table_name AS REF_TABLE, rcc.column_name AS REF_COL " +
                           "FROM all_constraints c JOIN all_cons_columns cc ON c.constraint_name = cc.constraint_name " +
                           "JOIN all_cons_columns rcc ON c.r_constraint_name = rcc.constraint_name " +
                           $"WHERE c.constraint_type = 'R' AND c.table_name = '{table.Replace("'", "''").ToUpperInvariant()}'",
        _ => null,   // SQLite 用 PRAGMA（调用方专用）；Mongo/Redis 无外键
    };

    /// <summary>SQLite 外键查询（需按表逐个跑）。</summary>
    public static string SqliteForeignKeySql(string table) => $"PRAGMA foreign_key_list({QuoteIdent(DbEngine.SQLite, table)})";

    /// <summary>取表前 n 行某列（各方言分页写法；拟真值池采样用）。</summary>
    public static string SelectTopSql(DbEngine e, string table, string col, int n) => e switch
    {
        DbEngine.SqlServer => $"SELECT TOP ({n}) {QuoteIdent(e, col)} FROM {QuoteIdent(e, table)}",
        DbEngine.Oracle => $"SELECT {QuoteIdent(e, col)} FROM {QuoteIdent(e, table)} WHERE ROWNUM <= {n}",
        _ => $"SELECT {QuoteIdent(e, col)} FROM {QuoteIdent(e, table)} LIMIT {n}",
    };

    // ---------- 生成计划 ----------

    /// <summary>单表生成 N 行拟真值（列序与 cols 一致）。pool 为关联值池（表→列→值列表）。</summary>
    public static List<List<object?>> GenerateRows(DbEngine e, SeedTable t, List<SeedEdge> edges,
        Dictionary<string, Dictionary<string, List<object?>>> pools, int seed, bool keepPk = true)
    {
        var rows = new List<List<object?>>();
        var myEdges = edges.Where(x => x.FromTable == t.Name).ToList();
        var rnd = new Random(seed);   // 确定性：同 seed 同值序列
        var autoInc = new Dictionary<string, long>();
        for (long rowNo = 1; rowNo <= t.Rows; rowNo++)
        {
            var vals = new List<object?>();
            foreach (var col in t.Columns)
            {
                var kind = Faker.InferSemantic(col.Name, col.DataType);
                object? v;
                if (col.IsKey && keepPk)
                {
                    v = NextPk(kind, col, autoInc, rnd, rowNo);
                }
                else
                {
                    var edge = myEdges.FirstOrDefault(x => x.FromCol == col.Name);
                    if (edge is not null && pools.TryGetValue(edge.ToTable, out var colPools) &&
                        colPools.TryGetValue(edge.ToCol, out var pool) && pool.Count > 0)
                    {
                        v = pool[rnd.Next(pool.Count)];   // 从已生成引用值采样（一对多允许重复）
                    }
                    else if (col.IsKey && kind == SemanticKind.Id && !keepPk)
                    {
                        v = null;   // 不覆盖 PK（用户选择保留原值）
                    }
                    else
                    {
                        v = Faker.Gen(kind, new ColumnCtx
                        {
                            Name = col.Name,
                            RawType = col.DataType,
                            Nullable = col.Nullable,
                            IsPrimaryKey = col.IsKey,
                            MaxLen = Faker.ParseLen(col.DataType),
                            Precision = 0,
                            Scale = Faker.ParsePrecision(col.DataType).s,
                            EnumValues = Faker.ParseEnumValues(col.DataType),
                        }, rnd, rowNo);
                    }
                }
                vals.Add(v);
            }
            rows.Add(vals);
        }
        return rows;
    }

    private static object NextPk(SemanticKind kind, ColumnSpec col, Dictionary<string, long> autoInc, Random rnd, long rowNo)
    {
        if (kind == SemanticKind.Guid || Faker.IsUuidType(col.DataType)) return Guid.NewGuid().ToString();
        var cur = autoInc.TryGetValue(col.Name, out var v) ? v : 0;
        cur++;
        autoInc[col.Name] = cur;
        return Faker.IsNumericType(col.DataType) ? (Faker.BaseType(col.DataType) == "BIGINT" ? (object)cur : (object)(int)cur) : (object)cur;
    }

    /// <summary>把已生成主键写进值池（供下游表采样）。</summary>
    public static Dictionary<string, List<object?>> PoolFrom(DbEngine e, SeedTable t, List<List<object?>> rows)
    {
        var pools = new Dictionary<string, List<object?>>();
        for (int i = 0; i < t.Columns.Count; i++)
        {
            if (!t.Columns[i].IsKey) continue;
            pools[t.Columns[i].Name] = rows.Select(r => r[i]).Where(v => v is not null).Take(100).ToList();
        }
        return pools;
    }

    /// <summary>
    /// 多表生成计划（v1.4.2）：每表行数（缺失 / 0 = 不生成）→ 剔除零行 → 按拓扑顺序排列
    /// （被引用表先写入，引用列才能采到值）。纯函数，可测。
    /// </summary>
    public static List<(SeedTable Table, int Count)> PlanFromCounts(
        IReadOnlyDictionary<string, int> counts, List<SeedTable> tables, List<SeedEdge> edges)
    {
        var plan = new List<(SeedTable Table, int Count)>();
        foreach (var t in TopoOrder(tables, edges))
            if (counts.TryGetValue(t.Name, out var n) && n > 0)
            {
                t.Rows = n;   // 生成器读 SeedTable.Rows：计划数直接落到表对象上
                plan.Add((t, n));
            }
        return plan;
    }
}
