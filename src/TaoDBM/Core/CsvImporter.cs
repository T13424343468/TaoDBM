using System.Text;

namespace TaoDBM.Core;

/// <summary>CSV 解析与导入语句生成（RFC 4180：逗号、引号转义、跨行值）。</summary>
public static class CsvImporter
{
    /// <summary>把 CSV 文本解析为表头 + 行。stripBom 时去掉首字符 BOM。</summary>
    public static (List<string> Headers, List<string?[]> Rows) Parse(string csv, bool stripBom = true)
    {
        if (stripBom && csv.Length > 0 && csv[0] == '\uFEFF') csv = csv[1..];
        var headers = new List<string>();
        var rows = new List<string?[]>();
        var cur = new StringBuilder();
        var rowBuf = new List<string?>();
        bool inQuotes = false, fieldQuoted = false;
        int n = csv.Length;

        void EndField()
        {
            rowBuf.Add(fieldQuoted || cur.ToString().Length > 0 ? cur.ToString() : null);
            cur.Clear();
            fieldQuoted = false;
        }
        void EndRow()
        {
            EndField();
            if (rowBuf.Count == 1 && rowBuf[0] is null && headers.Count == 0 && rows.Count == 0)
                return; // 跳过空行
            if (headers.Count == 0)
            {
                headers.AddRange(rowBuf.Select(c => c ?? ""));
            }
            else
            {
                // 列数对齐表头，不足补 null，超出截断
                var r = new string?[headers.Count];
                for (int i = 0; i < headers.Count; i++) r[i] = i < rowBuf.Count ? rowBuf[i] : null;
                rows.Add(r);
            }
            rowBuf.Clear();
        }

        for (int i = 0; i < n; i++)
        {
            char c = csv[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < n && csv[i + 1] == '"') { cur.Append('"'); i++; }
                    else inQuotes = false;
                }
                else cur.Append(c);
            }
            else if (c == '"')
            {
                inQuotes = true;
                fieldQuoted = true;
            }
            else if (c == ',') EndField();
            else if (c == '\n') EndRow();
            else if (c == '\r') { if (i + 1 < n && csv[i + 1] == '\n') i++; EndRow(); }
            else cur.Append(c);
        }
        if (cur.Length > 0 || rowBuf.Count > 0 || inQuotes) EndRow();
        return (headers, rows);
    }

    /// <summary>按各列是否为数字类型决定是否加引号，生成批量 INSERT。</summary>
    public static List<string> BuildInserts(IDbProvider prov, string db, string table,
        List<ColumnSpec> cols, List<string> headers, List<string?[]> rows, int batch = 100)
    {
        var numeric = new HashSet<string>(
            cols.Where(c => IsNumeric(c.DataType)).Select(c => c.Name),
            StringComparer.OrdinalIgnoreCase);
        var statements = new List<string>();
        var names = headers.Select(prov.Quote).ToList();

        for (int start = 0; start < rows.Count; start += batch)
        {
            var chunk = rows.Skip(start).Take(batch);
            var sb = new StringBuilder();
            foreach (var r in chunk)
            {
                var vals = new string[r.Length];
                for (int i = 0; i < r.Length; i++)
                {
                    string? v = i < headers.Count && i < r.Length ? r[i] : null;
                    vals[i] = v is null
                        ? "NULL"
                        : numeric.Contains(headers[i]) && (double.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, out _) || v.Length == 0)
                            ? (v.Length == 0 ? "NULL" : v)
                            : "'" + v.Replace("'", "''") + "'";
                }
                sb.Append($"INSERT INTO {prov.Quote(table)} ({string.Join(", ", names)}) VALUES ({string.Join(", ", vals)});\n");
            }
            statements.Add(sb.ToString());
        }
        return statements;
    }

    public static bool IsNumeric(string dataType)
    {
        var t = dataType.ToLowerInvariant();
        return t.Contains("int") || t.Contains("decimal") || t.Contains("numeric")
            || t.Contains("float") || t.Contains("double") || t.Contains("real")
            || t.Contains("number") || t == "bit" || t.Contains("money");
    }
}
