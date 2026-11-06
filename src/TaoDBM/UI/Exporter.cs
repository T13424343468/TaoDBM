using System.Text;
using TaoDBM.Core;

namespace TaoDBM.UI;

/// <summary>CSV / INSERT 导出。</summary>
public static class Exporter
{
    public static string Csv(QueryResult r)
    {
        var sb = new StringBuilder();
        sb.Append('\uFEFF'); // UTF-8 BOM，Excel 直接打开不乱码
        sb.AppendLine(string.Join(",", r.Columns.Select(EscapeCsv)));
        foreach (var row in r.Rows)
        {
            var cells = new string[row.Length];
            for (int i = 0; i < row.Length; i++) cells[i] = EscapeCsv(Format(row[i]));
            sb.AppendLine(string.Join(",", cells));
        }
        return sb.ToString();
    }

    private static string EscapeCsv(string? s)
    {
        s ??= "";
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    public static string Format(object? v) => v switch
    {
        null => "",
        byte[] b => Convert.ToHexString(b),
        DBNull => "",
        System.DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
        _ => v.ToString() ?? "",
    };

    /// <summary>导出为 INSERT 语句（需要表名）。</summary>
    public static string InsertSql(QueryResult r, string table)
    {
        var sb = new StringBuilder();
        foreach (var row in r.Rows)
        {
            var vals = new string[row.Length];
            for (int i = 0; i < row.Length; i++) vals[i] = SqlValue(row[i]);
            sb.AppendLine($"INSERT INTO {table} ({string.Join(", ", r.Columns)}) VALUES ({string.Join(", ", vals)});");
        }
        return sb.ToString();
    }

    private static string SqlValue(object? v) => v switch
    {
        null or DBNull => "NULL",
        byte[] b => "X'" + Convert.ToHexString(b) + "'",
        bool b => b ? "TRUE" : "FALSE",
        System.DateTime dt => "'" + dt.ToString("yyyy-MM-dd HH:mm:ss") + "'",
        sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal => v.ToString() ?? "NULL",
        _ => "'" + v.ToString()?.Replace("'", "''") + "'",
    };

    /// <summary>把界面输入转成 SQL 字面量：空 → NULL，数字列不加引号，其余转义加引号。</summary>
    public static string Literal(ColumnSpec col, string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "NULL";
        if (CsvImporter.IsNumeric(col.DataType) && double.TryParse(input, System.Globalization.CultureInfo.InvariantCulture, out _))
            return input.Trim();
        return "'" + input.Replace("'", "''") + "'";
    }
}
