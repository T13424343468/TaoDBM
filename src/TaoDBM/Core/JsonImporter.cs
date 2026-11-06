using System.Text;
using System.Text.Json;

namespace TaoDBM.Core;

/// <summary>JSON 导入：JSON 数组对象或行式 JSON（JSONL）解析，生成批量 INSERT。</summary>
public static class JsonImporter
{
    /// <summary>解析 JSON 文本：整文件是对象数组，或每行一个对象（JSONL）。非对象行跳过。</summary>
    public static (List<string> Headers, List<string?[]> Rows) Parse(string json)
    {
        var headers = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dicts = new List<Dictionary<string, string?>>();
        foreach (var el in Elements(json))
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in el.EnumerateObject())
            {
                dict[p.Name] = Cell(p.Value);
                if (seen.Add(p.Name)) headers.Add(p.Name);
            }
            dicts.Add(dict);
        }
        // 行按最终表头取值（后续对象新出现的键在早行补 null）
        var rows = dicts.Select(d => headers.Select(h => d.TryGetValue(h, out var v) ? v : null).ToArray()).ToList();
        return (headers, rows);
    }

    /// <summary>把 JSON 值归一为 SQL 侧字符串：null→null、数字原样、布尔 TRUE/FALSE、
    /// 字符串原样（不再转义，交由 BuildInserts 统一处理）、嵌套结构序列化为紧凑 JSON 文本。</summary>
    public static string? Cell(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => "TRUE",
        JsonValueKind.False => "FALSE",
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.String => v.GetString(),
        _ => v.GetRawText(),
    };

    /// <summary>生成批量 INSERT（复用 CSV 导入的类型识别与转义）。</summary>
    public static List<string> BuildInserts(IDbProvider prov, string db, string table,
        List<ColumnSpec> cols, List<string> headers, List<string?[]> rows, int batch = 100)
        => CsvImporter.BuildInserts(prov, db, table, cols, headers, rows, batch);

    /// <summary>兼容对象数组、单对象与 JSONL 三种形态；三者都不匹配时抛出 JsonException。</summary>
    private static IEnumerable<JsonElement> Elements(string json)
    {
        var trimmed = json.TrimStart();
        if (trimmed.Length > 0 && trimmed[0] == '[')
        {
            JsonDocument? arr = null;
            try { arr = JsonDocument.Parse(json); }
            catch (Exception ex) when (ex is JsonException or ArgumentException)
            {
                // 非法 JSON 或非法 UTF-16（如未配对代理对）一律按不可导入处理
            }
            if (arr != null)
            {
                using (arr)
                    foreach (var e in arr.RootElement.EnumerateArray()) yield return e;
            }
            yield break;
        }
        if (trimmed.Length > 0 && trimmed[0] == '{')
        {
            JsonDocument? single = null;
            try { single = JsonDocument.Parse(json); }
            catch (Exception ex) when (ex is JsonException or ArgumentException) { /* 多对象拼接的 JSONL，走逐行解析 */ }
            if (single != null)
            {
                using var doc = single;
                yield return doc.RootElement; // 单对象
                yield break;
            }
        }
        foreach (var line in json.Split('\n'))
        {
            var t = line.Trim();
            if (t.Length == 0 || !t.StartsWith('{')) continue;
            JsonDocument? lineDoc;
            try { lineDoc = JsonDocument.Parse(t); }
            catch (Exception ex) when (ex is JsonException or ArgumentException) { continue; }   // 畸形行跳过，不让坏行拖垮整批导入
            using (lineDoc)
            {
                if (lineDoc.RootElement.ValueKind == JsonValueKind.Object) yield return lineDoc.RootElement;
            }
        }
    }
}
