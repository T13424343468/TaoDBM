using System.Text.Json;

namespace TaoDBM.Core;

/// <summary>ER 图：一张实体表的框（名称、列、画布位置）。</summary>
public class ErdTable
{
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public List<ErdColumn> Columns { get; set; } = new();
}

public class ErdColumn
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool IsKey { get; set; }
}

/// <summary>表间关系（推断或手加）：from 表.fromCol → to 表主键。</summary>
public class ErdEdge
{
    public string FromTable { get; set; } = "";
    public string FromCol { get; set; } = "";
    public string ToTable { get; set; } = "";
}

/// <summary>整张图；JSON 持久化可往返。</summary>
public class ErdModel
{
    public List<ErdTable> Tables { get; set; } = new();
    public List<ErdEdge> Edges { get; set; } = new();

    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>由表结构建框；x/y 由布局器分配。</summary>
    public static ErdTable FromSpecs(string table, List<ColumnSpec> cols) => new()
    {
        Name = table,
        Columns = cols.Select(c => new ErdColumn { Name = c.Name, Type = c.DataType, IsKey = c.IsKey }).ToList(),
    };

    /// <summary>网格布局：每行 4 个，间距 300x260。</summary>
    public static void AutoLayout(ErdModel m)
    {
        for (int i = 0; i < m.Tables.Count; i++)
        {
            m.Tables[i].X = (i % 4) * 300 + 40;
            m.Tables[i].Y = (i / 4) * 260 + 40;
        }
    }

    /// <summary>按命名约定推断关系：外键列 user_id / userId → 表 user(s) 的主键 id。</summary>
    public static List<ErdEdge> InferEdges(ErdModel m)
    {
        var edges = new List<ErdEdge>();
        var byName = m.Tables.Select(t => (t.Name, t)).ToDictionary(p => p.Name, p => p.t, StringComparer.OrdinalIgnoreCase);
        foreach (var t in m.Tables)
        {
            foreach (var c in t.Columns)
            {
                if (c.IsKey) continue;
                var stem = c.Name.EndsWith("_id", StringComparison.OrdinalIgnoreCase) ? c.Name[..^3]
                    : c.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) ? c.Name[..^2] : null;
                if (string.IsNullOrEmpty(stem)) continue;
                // 目标表：单数 / 复数两种形式
                var target = byName.ContainsKey(stem) ? stem
                    : byName.ContainsKey(stem + "s") ? stem + "s"
                    : null;
                if (target is null) continue;
                var targetTable = byName[target];
                // 目标表需有 id 主键列
                if (!targetTable.Columns.Any(p => p.IsKey && p.Name.Equals("id", StringComparison.OrdinalIgnoreCase))) continue;
                if (target.Equals(t.Name, StringComparison.OrdinalIgnoreCase)) continue; // 自引用跳过（避免自环线）
                if (edges.Any(e => e.FromTable == t.Name && e.FromCol == c.Name)) continue;
                edges.Add(new ErdEdge { FromTable = t.Name, FromCol = c.Name, ToTable = targetTable.Name });
            }
        }
        return edges;
    }

    public string ToJson() => JsonSerializer.Serialize(this, Opt);

    public static ErdModel? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<ErdModel>(json); }
        catch { return null; }
    }
}
