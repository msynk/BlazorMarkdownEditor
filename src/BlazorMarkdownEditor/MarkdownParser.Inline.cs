using System.Text;

namespace BlazorMarkdownEditor;

internal sealed partial class MarkdownParser
{
    // ---- inline parsing -----------------------------------------------------

    public string RenderInline(string text)
    {
        var sb = new StringBuilder();
        int i = 0, n = text.Length;
        while (i < n)
        {
            char c = text[i];
            switch (c)
            {
                case '\\':
                    if (i + 1 < n && IsEscapable(text[i + 1])) { sb.Append(EscapeChar(text[i + 1])); i += 2; }
                    else { sb.Append('\\'); i++; }
                    continue;
                case '\r':
                    sb.Append("<br />\n"); i++; continue;
                case '\n':
                    sb.Append('\n'); i++; continue;
                case '`':
                    if (TryCodeSpan(text, i, sb, out int afterCode)) { i = afterCode; continue; }
                    sb.Append('`'); i++; continue;
                case '!':
                    if (i + 1 < n && text[i + 1] == '[' && TryLink(text, i, true, sb, out int afterImg)) { i = afterImg; continue; }
                    sb.Append('!'); i++; continue;
                case '[':
                    if (TryLink(text, i, false, sb, out int afterLink)) { i = afterLink; continue; }
                    sb.Append('['); i++; continue;
                case '<':
                    if (TryAutolink(text, i, sb, out int afterAuto)) { i = afterAuto; continue; }
                    if (_opt.AllowRawHtml && TryRawHtml(text, i, sb, out int afterRaw)) { i = afterRaw; continue; }
                    sb.Append(EscapeChar('<')); i++; continue;
                case '*':
                case '_':
                case '~':
                    if (TryEmphasis(text, i, sb, out int afterEm)) { i = afterEm; continue; }
                    sb.Append(EscapeChar(c)); i++; continue;
                default:
                    if ((c == 'h' || c == 'w') && TryBareAutolink(text, i, sb, out int afterBare)) { i = afterBare; continue; }
                    sb.Append(EscapeChar(c)); i++; continue;
            }
        }
        return sb.ToString();
    }

    private static bool TryCodeSpan(string text, int i, StringBuilder sb, out int after)
    {
        int n = text.Length;
        int run = 0;
        while (i + run < n && text[i + run] == '`') run++;
        int j = i + run;
        while (j < n)
        {
            if (text[j] == '`')
            {
                int r = 0;
                while (j + r < n && text[j + r] == '`') r++;
                if (r == run)
                {
                    string code = text.Substring(i + run, j - (i + run)).Replace('\n', ' ');
                    if (code.Length >= 2 && code[0] == ' ' && code[^1] == ' ' && code.Trim().Length > 0)
                        code = code[1..^1];
                    sb.Append("<code>").Append(EscapeHtml(code)).Append("</code>");
                    after = j + r;
                    return true;
                }
                j += r;
            }
            else j++;
        }
        after = i;
        return false;
    }

    private bool TryLink(string text, int i, bool isImage, StringBuilder sb, out int after)
    {
        after = i;
        int n = text.Length;
        int start = i + (isImage ? 1 : 0);
        if (start >= n || text[start] != '[') return false;

        int p = start + 1, depth = 1;
        while (p < n && depth > 0)
        {
            if (text[p] == '\\') { p += 2; continue; }
            if (text[p] == '[') depth++;
            else if (text[p] == ']') { depth--; if (depth == 0) break; }
            p++;
        }
        if (depth != 0 || p >= n) return false;

        string label = text.Substring(start + 1, p - (start + 1));
        int q = p + 1;
        if (q >= n || text[q] != '(') return false;
        q++;

        while (q < n && (text[q] == ' ' || text[q] == '\t')) q++;
        var url = new StringBuilder();
        if (q < n && text[q] == '<')
        {
            q++;
            while (q < n && text[q] != '>') { url.Append(text[q]); q++; }
            if (q < n) q++;
        }
        else
        {
            int paren = 0;
            while (q < n)
            {
                char ch = text[q];
                if (ch == ' ' || ch == '\t') break;
                if (ch == '(') paren++;
                if (ch == ')') { if (paren == 0) break; paren--; }
                if (ch == '\\' && q + 1 < n) { url.Append(text[q + 1]); q += 2; continue; }
                url.Append(ch);
                q++;
            }
        }

        string? title = null;
        while (q < n && (text[q] == ' ' || text[q] == '\t')) q++;
        if (q < n && (text[q] == '"' || text[q] == '\''))
        {
            char quote = text[q++];
            var t = new StringBuilder();
            while (q < n && text[q] != quote) { t.Append(text[q]); q++; }
            if (q < n) q++;
            title = t.ToString();
        }
        while (q < n && text[q] == ' ') q++;
        if (q >= n || text[q] != ')') return false;

        string href = SanitizeUrl(url.ToString(), isImage);
        if (isImage)
        {
            sb.Append("<img src=\"").Append(EscapeAttr(href)).Append("\" alt=\"").Append(EscapeAttr(label)).Append('"');
            if (title != null) sb.Append(" title=\"").Append(EscapeAttr(title)).Append('"');
            sb.Append(" />");
        }
        else
        {
            sb.Append("<a href=\"").Append(EscapeAttr(href)).Append('"');
            if (title != null) sb.Append(" title=\"").Append(EscapeAttr(title)).Append('"');
            sb.Append('>').Append(RenderInline(label)).Append("</a>");
        }
        after = q + 1;
        return true;
    }

    private bool TryAutolink(string text, int i, StringBuilder sb, out int after)
    {
        after = i;
        int n = text.Length, j = i + 1;
        var inner = new StringBuilder();
        while (j < n && text[j] != '>' && text[j] != '<' && !char.IsWhiteSpace(text[j])) { inner.Append(text[j]); j++; }
        if (j >= n || text[j] != '>') return false;
        string content = inner.ToString();

        if (IsUriAutolink(content))
        {
            string href = SanitizeUrl(content, false);
            sb.Append("<a href=\"").Append(EscapeAttr(href)).Append("\">").Append(EscapeHtml(content)).Append("</a>");
            after = j + 1;
            return true;
        }
        if (IsEmail(content))
        {
            sb.Append("<a href=\"mailto:").Append(EscapeAttr(content)).Append("\">").Append(EscapeHtml(content)).Append("</a>");
            after = j + 1;
            return true;
        }
        return false;
    }

    private bool TryBareAutolink(string text, int i, StringBuilder sb, out int after)
    {
        after = i;
        if (i > 0 && char.IsLetterOrDigit(text[i - 1])) return false;

        string rest = text[i..];
        string prefix;
        if (rest.StartsWith("https://")) prefix = "https://";
        else if (rest.StartsWith("http://")) prefix = "http://";
        else if (rest.StartsWith("www.")) prefix = "www.";
        else return false;

        int n = text.Length, j = i;
        while (j < n && !char.IsWhiteSpace(text[j]) && text[j] != '<') j++;
        string urlText = text[i..j];
        urlText = urlText.TrimEnd('.', ',', '!', '?', ';', ':', '"', '\'');
        while (urlText.EndsWith(')') && Count(urlText, '(') < Count(urlText, ')')) urlText = urlText[..^1];
        if (urlText.Length <= prefix.Length) return false;

        string href = prefix == "www." ? "http://" + urlText : urlText;
        sb.Append("<a href=\"").Append(EscapeAttr(href)).Append("\">").Append(EscapeHtml(urlText)).Append("</a>");
        after = i + urlText.Length;
        return true;
    }

    private bool TryRawHtml(string text, int i, StringBuilder sb, out int after)
    {
        int j = text.IndexOf('>', i);
        if (j < 0) { after = i; return false; }
        sb.Append(text.Substring(i, j - i + 1));
        after = j + 1;
        return true;
    }

    private bool TryEmphasis(string text, int i, StringBuilder sb, out int after)
    {
        after = i;
        int n = text.Length;
        char d = text[i];
        int run = 0;
        while (i + run < n && text[i + run] == d) run++;

        // Opener must be left-flanking: followed by a non-space character.
        if (i + run >= n || char.IsWhiteSpace(text[i + run])) return false;
        if (d == '_' && i > 0 && char.IsLetterOrDigit(text[i - 1])) return false;

        if (d == '~')
        {
            if (run < 2) return false;
            int close = FindClosing(text, i + 2, "~~", d);
            if (close < 0) return false;
            sb.Append("<del>").Append(RenderInline(text.Substring(i + 2, close - (i + 2)))).Append("</del>");
            after = close + 2;
            return true;
        }

        if (run >= 3)
        {
            int close = FindClosing(text, i + 3, new string(d, 3), d);
            if (close >= 0)
            {
                sb.Append("<strong><em>").Append(RenderInline(text.Substring(i + 3, close - (i + 3)))).Append("</em></strong>");
                after = close + 3;
                return true;
            }
        }
        if (run >= 2)
        {
            int close = FindClosing(text, i + 2, new string(d, 2), d);
            if (close >= 0)
            {
                sb.Append("<strong>").Append(RenderInline(text.Substring(i + 2, close - (i + 2)))).Append("</strong>");
                after = close + 2;
                return true;
            }
        }
        {
            int close = FindClosing(text, i + 1, d.ToString(), d);
            if (close >= 0)
            {
                sb.Append("<em>").Append(RenderInline(text.Substring(i + 1, close - (i + 1)))).Append("</em>");
                after = close + 1;
                return true;
            }
        }
        return false;
    }

    private static int FindClosing(string text, int from, string delim, char d)
    {
        int n = text.Length, len = delim.Length;
        int j = from;
        while (j <= n - len)
        {
            char ch = text[j];
            if (ch == '\\') { j += 2; continue; }
            if (ch == '`')
            {
                // skip over code spans so delimiters inside them don't match
                int run = 0;
                while (j + run < n && text[j + run] == '`') run++;
                int k = j + run; bool closed = false;
                while (k < n)
                {
                    if (text[k] == '`')
                    {
                        int r = 0;
                        while (k + r < n && text[k + r] == '`') r++;
                        if (r == run) { k += r; closed = true; break; }
                        k += r;
                    }
                    else k++;
                }
                j = closed ? k : j + run;
                continue;
            }
            if (ch == d && Matches(text, j, delim))
            {
                if (j == 0 || char.IsWhiteSpace(text[j - 1])) { j++; continue; }   // closer must be right-flanking
                if (d == '_' && j + len < n && char.IsLetterOrDigit(text[j + len])) { j++; continue; }
                return j;
            }
            j++;
        }
        return -1;
    }

    private static bool Matches(string text, int j, string delim)
    {
        if (j + delim.Length > text.Length) return false;
        for (int k = 0; k < delim.Length; k++)
            if (text[j + k] != delim[k]) return false;
        return true;
    }

    // ---- inline helpers -----------------------------------------------------

    private string SanitizeUrl(string url, bool isImage)
    {
        string u = url.Trim();
        if (u.Length == 0) return "";

        int colon = u.IndexOf(':');
        if (colon > 0)
        {
            int slash = u.IndexOf('/'), hash = u.IndexOf('#'), q = u.IndexOf('?');
            bool schemeBeforeOthers =
                (slash < 0 || colon < slash) && (hash < 0 || colon < hash) && (q < 0 || colon < q);
            if (schemeBeforeOthers)
            {
                string scheme = u[..colon].ToLowerInvariant();
                bool letters = scheme.All(ch => char.IsLetterOrDigit(ch) || ch is '+' or '.' or '-');
                if (letters)
                {
                    bool ok = _opt.AllowedUrlSchemes.Contains(scheme);
                    if (isImage && scheme == "data" && u.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                        ok = true;
                    if (!ok) return "#";
                }
            }
        }
        return u;
    }

    private static bool IsUriAutolink(string s)
    {
        int colon = s.IndexOf(':');
        if (colon <= 0) return false;
        if (!char.IsLetter(s[0])) return false;
        for (int i = 0; i < colon; i++)
            if (!(char.IsLetterOrDigit(s[i]) || s[i] is '+' or '.' or '-')) return false;
        return !s.Any(char.IsWhiteSpace);
    }

    private static bool IsEmail(string s)
    {
        int at = s.IndexOf('@');
        if (at <= 0 || at == s.Length - 1) return false;
        if (s.IndexOf('@', at + 1) >= 0) return false;
        return s[(at + 1)..].Contains('.');
    }

    private static int Count(string s, char c)
    {
        int n = 0;
        foreach (char ch in s) if (ch == c) n++;
        return n;
    }

    private static bool IsEscapable(char c) =>
        "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~".IndexOf(c) >= 0;

    private string EscapeChar(char c)
    {
        if (_opt.AllowRawHtml) return c.ToString();
        return c switch
        {
            '&' => "&amp;",
            '<' => "&lt;",
            '>' => "&gt;",
            _ => c.ToString()
        };
    }

    private static string EscapeHtml(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string EscapeAttr(string s) =>
        EscapeHtml(s).Replace("\"", "&quot;");
}
