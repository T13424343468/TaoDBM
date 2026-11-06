using System.Globalization;
using System.Text;

namespace TaoDBM.Core;

/// <summary>单列统计摘要。</summary>
public class ReportColumnStat
{
    public string Name { get; set; } = "";
    public int NullCount { get; set; }
    public int DistinctCount { get; set; }
    /// <summary>数值列才有：最小 / 最大 / 平均；其余为 null。</summary>
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Avg { get; set; }
    /// <summary>非数值列：出现最多的前 3 个值（值 → 次数）。</summary>
    public List<(string Value, int Count)> Top { get; set; } = new();
}

/// <summary>查询结果 → 报表统计与 HTML 导出（纯逻辑，可测试）。</summary>
public static class ReportBuilder
{
    public static (int Rows, List<ReportColumnStat> Columns) Build(QueryResult qr)
    {
        var stats = new List<ReportColumnStat>();
        foreach (var (col, idx) in qr.Columns.Select((c, i) => (c, i)))
        {
            var stat = new ReportColumnStat { Name = col };
            var vals = qr.Rows.Select(r => idx < r.Length ? r[idx] : null).ToList();
            stat.NullCount = vals.Count(v => v is null or DBNull);
            stat.DistinctCount = vals.Where(v => v is not null and not DBNull)
                .Select(v => v?.ToString() ?? "").Distinct(EqualityComparer<string?>.Default).Count();

            var numbers = new List<double>();
            var freq = new Dictionary<string, int>();
            foreach (var v in vals)
            {
                if (v is null or DBNull) continue;
                if (v is byte[] b) { string hx = "0x" + Convert.ToHexString(b); freq[hx] = freq.TryGetValue(hx, out int n0) ? n0 + 1 : 1; continue; }
                if (v is bool bl) { numbers.Add(bl ? 1 : 0); continue; }
                if (v is sbyte or byte or short or ushort or int or uint or long or ulong
                    or float or double or decimal or System.DateTime)
                {
                    if (v is System.DateTime dt) freq[dt.ToString("yyyy-MM-dd HH:mm:ss")] = 0;
                    else numbers.Add(Convert.ToDouble(v, CultureInfo.InvariantCulture));
                    continue;
                }
                string s = v.ToString() ?? "";
                freq[s] = freq.TryGetValue(s, out int n) ? n + 1 : 1;
            }
            if (numbers.Count > 0)
            {
                stat.Min = numbers.Min();
                stat.Max = numbers.Max();
                stat.Avg = numbers.Average();
            }
            stat.Top = freq.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Take(3).Select(kv => (kv.Key, kv.Value)).ToList();
            stats.Add(stat);
        }
        return (qr.Rows.Count, stats);
    }

    /// <summary>导出为独立 HTML 报表（Ant 风格静态页，可直接浏览器打开）。</summary>
    public static string ToHtml(QueryResult qr, string title)
    {
        var (rows, cols) = Build(qr);
        var pal = ThemePalette.Get(ThemeStore.Load());
        string accent = pal["AntPrimary"], bodyBg = pal["AntBg"], headBg = pal["AntHeadBg"];
        var sb = new StringBuilder();
        sb.Append($@"<!DOCTYPE html><html><head><meta charset=""utf-8""><title>{Esc(title)}</title><style>
body{{font-family:'Segoe UI','Microsoft YaHei',sans-serif;color:#333;margin:24px;background:{bodyBg}}}
h1{{font-size:20px;color:{accent}}} .card{{background:#fff;border-radius:8px;padding:16px;margin-bottom:16px;box-shadow:0 1px 2px rgba(0,0,0,.06)}}
table{{border-collapse:collapse;width:100%;font-size:13px;background:#fff}}
th{{background:{headBg};padding:8px;text-align:left;border-bottom:1px solid #E5E7EB}}
td{{padding:8px;border-bottom:1px solid #E5E7EB}} tr:nth-child(even){{background:#FAFAFA}}
.k{{color:{accent};font-weight:600}} .num{{text-align:right;font-variant-numeric:tabular-nums}}</style></head><body>
<h1>{Esc(title)}</h1><div class=""card""><b>行数：</b>{rows}　<b>列数：</b>{qr.Columns.Count}　<b>生成时间：</b>{DateTime.Now:yyyy-MM-dd HH:mm:ss}</div>
<div class=""card""><table><tr><th>列名</th><th>空值</th><th>去重值</th><th>最小</th><th>最大</th><th>平均</th><th>Top 值</th></tr>");
        foreach (var c in cols)
        {
            string min = c.Min?.ToString("0.###", CultureInfo.InvariantCulture) ?? "-",
                   max = c.Max?.ToString("0.###", CultureInfo.InvariantCulture) ?? "-",
                   avg = c.Avg?.ToString("0.###", CultureInfo.InvariantCulture) ?? "-";
            string top = c.Top.Count == 0 ? "-" : string.Join("，", c.Top.Select(t => $"{Esc(t.Value)}（{t.Count}）"));
            sb.Append($"<tr><td class=\"k\">{Esc(c.Name)}</td><td class=\"num\">{c.NullCount}</td><td class=\"num\">{c.DistinctCount}</td><td class=\"num\">{min}</td><td class=\"num\">{max}</td><td class=\"num\">{avg}</td><td>{top}</td></tr>");
        }
        sb.Append("</table></div><p style=\"color:#999;font-size:12px\">TaoDBM 报表 · 静态快照</p></body></html>");
        return sb.ToString();
    }

    private static string Esc(string? s) => (s ?? "")
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace("\"", "&quot;");
}
