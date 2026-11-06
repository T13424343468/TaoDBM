using System.Text;

namespace TaoDBM.Core;

/// <summary>tnsnames.ora 解析：提取每个网络服务名的主机、端口与服务名（纯文本解析，可测试）。</summary>
public static class TnsNames
{
    public record Entry(string Name, string Host, int Port, string Service);

    /// <summary>解析 tnsnames.ora 全文。条目形如 ALIAS = (DESCRIPTION=... (HOST=x)(PORT=n)(SERVICE_NAME=y))；
    /// 支持 # 注释与 "A, B = (...)" 多别名；解析失败的条目跳过，不做任何网络请求。</summary>
    public static List<Entry> Parse(string text)
    {
        var list = new List<Entry>();
        var sb = new StringBuilder();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
            sb.AppendLine(line);
        }
        var clean = sb.ToString();

        int i = 0;
        while (i < clean.Length)
        {
            // 找下一个 "别名 = (" 的起点
            int eq = IndexOfAssign(clean, i);
            if (eq < 0) break;
            int paren = clean.IndexOf('(', eq);
            if (paren < 0) break;
            var aliasRaw = clean[i..eq].Trim().TrimEnd('=').Trim();
            if (aliasRaw.Length == 0) { i = paren; continue; }
            int end = MatchParen(clean, paren);
            if (end < 0) break;
            var body = clean[paren..(end + 1)];
            i = end + 1;
            var host = Find(body, "HOST");
            if (host is null) continue;
            var service = Find(body, "SERVICE_NAME") ?? "";
            int.TryParse(Find(body, "PORT"), out var port);
            foreach (var a in aliasRaw.Split(','))
            {
                var name = a.Trim();
                if (name.Length > 0) list.Add(new Entry(name, host, port, service));
            }
        }
        return list;
    }

    private static int IndexOfAssign(string s, int start)
    {
        for (int i = start; i < s.Length; i++)
        {
            if (s[i] == '=') return i;
            if (s[i] == '(') i = MatchParen(s, i) is int e && e > i ? e : s.Length; // 跳过值内的括号块
        }
        return -1;
    }

    /// <summary>返回与 start 配对的 ')' 下标，未配对返回 -1。</summary>
    private static int MatchParen(string s, int start)
    {
        int depth = 0, quote = 0;
        for (int i = start; i < s.Length; i++)
        {
            var c = s[i];
            if (quote == 1) { if (c == '"') quote = 0; continue; }
            if (c == '"') { quote = 1; continue; }
            if (c == '(') depth++;
            else if (c == ')') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    /// <summary>在条目文本里找 (KEY = value)，大小写不敏感，忽略引号。</summary>
    private static string? Find(string body, string key)
    {
        int idx = 0;
        while (true)
        {
            int p = body.IndexOf('(' + key, idx, StringComparison.OrdinalIgnoreCase);
            if (p < 0) return null;
            int eq = body.IndexOf('=', p + 1);
            int close = MatchParen(body, p);
            if (eq < 0 || (close >= 0 && eq > close)) { idx = p + 1; continue; }
            int end = close < 0 ? body.Length : close;
            var v = body[(eq + 1)..end].Trim().Trim('"').Trim();
            return v.Length == 0 ? null : v;
        }
    }
}
