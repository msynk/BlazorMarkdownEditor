using System.Text;

namespace BlazorMarkdownEditor;

/// <summary>
/// Internal block + inline Markdown parser used by <see cref="BlazorMarkdownEditorMarkdown"/>.
/// Split into two files: this one holds block-level parsing, the inline parser
/// lives in <c>BlazorMarkdownEditorParser.Inline.cs</c>.
/// </summary>
internal sealed partial class BlazorMarkdownEditorParser(BlazorMarkdownEditorOptions options)
{
    private readonly BlazorMarkdownEditorOptions _opt = options;

    // ---- block parsing ------------------------------------------------------

    public void RenderBlocks(string[] lines, int start, int end, StringBuilder sb, bool tight = false)
    {
        int i = start;
        while (i < end)
        {
            string line = lines[i];
            if (IsBlank(line)) { i++; continue; }

            // Fenced code block
            if (TryFenceInfo(line, out char fence, out int fenceIndent, out string info))
            {
                i++;
                var code = new StringBuilder();
                while (i < end && !IsClosingFence(lines[i], fence))
                {
                    code.Append(StripIndent(lines[i], fenceIndent)).Append('\n');
                    i++;
                }
                if (i < end) i++; // consume closing fence
                EmitCodeBlock(code.ToString(), info, sb);
                continue;
            }

            int indent = IndentOf(line);

            // Indented code block (4+ spaces) at a block boundary
            if (indent >= 4)
            {
                var code = new StringBuilder();
                while (i < end && (IsBlank(lines[i]) || IndentOf(lines[i]) >= 4))
                {
                    code.Append(IsBlank(lines[i]) ? "" : StripIndent(lines[i], 4)).Append('\n');
                    i++;
                }
                EmitCodeBlock(TrimTrailingBlank(code.ToString()), "", sb);
                continue;
            }

            string trimmed = line.TrimStart();

            // ATX heading
            if (TryAtxHeading(trimmed, out int level, out string headingText))
            {
                sb.Append("<h").Append(level).Append('>')
                  .Append(RenderInline(headingText))
                  .Append("</h").Append(level).Append(">\n");
                i++;
                continue;
            }

            // Thematic break
            if (IsThematicBreak(trimmed))
            {
                sb.Append("<hr />\n");
                i++;
                continue;
            }

            // Blockquote
            if (trimmed.StartsWith('>'))
            {
                var inner = new List<string>();
                while (i < end && !IsBlank(lines[i]) && lines[i].TrimStart().StartsWith('>'))
                {
                    string q = lines[i].TrimStart()[1..];
                    if (q.StartsWith(' ')) q = q[1..];
                    inner.Add(q);
                    i++;
                }
                sb.Append("<blockquote>\n");
                RenderBlocks(inner.ToArray(), 0, inner.Count, sb);
                sb.Append("</blockquote>\n");
                continue;
            }

            // GFM table (header row followed by a delimiter row)
            if (i + 1 < end && line.Contains('|') && IsTableDelimiter(lines[i + 1]))
            {
                i = RenderTable(lines, i, end, sb);
                continue;
            }

            // Lists
            if (TryParseItemMarker(line, out _, out _, out _, out _))
            {
                i = RenderList(lines, i, end, sb);
                continue;
            }

            // Paragraph
            var para = new List<string> { line };
            i++;
            while (i < end && !IsBlank(lines[i]) && !IsParagraphInterrupter(lines, i, end))
            {
                para.Add(lines[i]);
                i++;
            }
            EmitParagraph(para, sb, tight);
        }
    }

    private bool IsParagraphInterrupter(string[] lines, int i, int end)
    {
        string t = lines[i].TrimStart();
        if (TryAtxHeading(t, out _, out _)) return true;
        if (TryFenceInfo(lines[i], out _, out _, out _)) return true;
        if (IsThematicBreak(t)) return true;
        if (t.StartsWith('>')) return true;
        if (i + 1 < end && lines[i].Contains('|') && IsTableDelimiter(lines[i + 1])) return true;
        if (TryParseItemMarker(lines[i], out _, out string content, out bool ordered, out string num))
        {
            // Only non-empty items interrupt; ordered lists must start at 1.
            if (content.Trim().Length == 0) return false;
            return !ordered || num == "1";
        }
        return false;
    }

    private void EmitParagraph(List<string> lines, StringBuilder sb, bool tight)
    {
        var buf = new StringBuilder();
        for (int k = 0; k < lines.Count; k++)
        {
            string raw = lines[k];
            string te = raw.TrimEnd();
            bool last = k == lines.Count - 1;
            bool hard = false;
            string content;
            if (!last && te.EndsWith('\\'))
            {
                content = te[..^1];
                hard = true;
            }
            else if (!last && raw.EndsWith("  "))
            {
                content = te;
                hard = true;
            }
            else
            {
                content = raw;
            }
            buf.Append(content.Trim());
            if (!last) buf.Append(hard ? '\r' : '\n');
        }

        string html = RenderInline(buf.ToString());
        if (tight)
            sb.Append(html).Append('\n');
        else
            sb.Append("<p>").Append(html).Append("</p>\n");
    }

    private void EmitCodeBlock(string code, string info, StringBuilder sb)
    {
        string lang = info.Trim();
        int sp = lang.IndexOf(' ');
        if (sp >= 0) lang = lang[..sp];

        sb.Append("<pre><code");
        if (lang.Length > 0)
            sb.Append(" class=\"language-").Append(EscapeAttr(lang)).Append('"');
        sb.Append('>');
        sb.Append(EscapeHtml(code));
        sb.Append("</code></pre>\n");
    }

    // ---- tables -------------------------------------------------------------

    private int RenderTable(string[] lines, int i, int end, StringBuilder sb)
    {
        var header = SplitRow(lines[i]);
        var aligns = ParseAligns(lines[i + 1]);
        i += 2;

        sb.Append("<table>\n<thead>\n<tr>\n");
        for (int c = 0; c < header.Count; c++)
            sb.Append("<th").Append(AlignAttr(aligns, c)).Append('>')
              .Append(RenderInline(header[c].Trim())).Append("</th>\n");
        sb.Append("</tr>\n</thead>\n<tbody>\n");

        while (i < end && !IsBlank(lines[i]) && lines[i].Contains('|'))
        {
            var cells = SplitRow(lines[i]);
            sb.Append("<tr>\n");
            for (int c = 0; c < header.Count; c++)
            {
                string cell = c < cells.Count ? cells[c].Trim() : "";
                sb.Append("<td").Append(AlignAttr(aligns, c)).Append('>')
                  .Append(RenderInline(cell)).Append("</td>\n");
            }
            sb.Append("</tr>\n");
            i++;
        }
        sb.Append("</tbody>\n</table>\n");
        return i;
    }

    private static string AlignAttr(List<string?> aligns, int c) =>
        c < aligns.Count && aligns[c] is { } a ? $" style=\"text-align:{a}\"" : "";

    private static List<string> SplitRow(string line)
    {
        string s = line.Trim();
        if (s.StartsWith('|')) s = s[1..];
        if (s.EndsWith('|') && !s.EndsWith("\\|")) s = s[..^1];

        var cells = new List<string>();
        var cur = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length && s[i + 1] == '|') { cur.Append('|'); i++; }
            else if (s[i] == '|') { cells.Add(cur.ToString()); cur.Clear(); }
            else cur.Append(s[i]);
        }
        cells.Add(cur.ToString());
        return cells;
    }

    private static List<string?> ParseAligns(string delimLine)
    {
        var result = new List<string?>();
        foreach (string raw in SplitRow(delimLine))
        {
            string cell = raw.Trim();
            bool left = cell.StartsWith(':');
            bool right = cell.EndsWith(':');
            result.Add(left && right ? "center" : right ? "right" : left ? "left" : null);
        }
        return result;
    }

    private static bool IsTableDelimiter(string line)
    {
        string s = line.Trim();
        if (s.Length == 0 || (!s.Contains('-'))) return false;
        var cells = SplitRow(s);
        if (cells.Count == 0) return false;
        foreach (string raw in cells)
        {
            string cell = raw.Trim();
            if (cell.Length == 0) return false;
            int dash = 0;
            for (int k = 0; k < cell.Length; k++)
            {
                char ch = cell[k];
                if (ch == '-') dash++;
                else if (ch == ':') { if (k != 0 && k != cell.Length - 1) return false; }
                else return false;
            }
            if (dash == 0) return false;
        }
        return true;
    }

    // ---- lists --------------------------------------------------------------

    private int RenderList(string[] lines, int i, int end, StringBuilder sb)
    {
        int baseIndent = IndentOf(lines[i]);
        TryParseItemMarker(lines[i], out _, out _, out bool ordered, out string startNum);

        var items = new List<List<string>>();
        bool loose = false;

        while (i < end)
        {
            if (IsBlank(lines[i]))
            {
                int k = i;
                while (k < end && IsBlank(lines[k])) k++;
                bool continues = k < end && IndentOf(lines[k]) >= baseIndent &&
                    (IndentOf(lines[k]) > baseIndent ||
                     (TryParseItemMarker(lines[k], out _, out _, out bool ord, out _) && ord == ordered));
                if (continues) { loose = true; i = k; continue; }
                break;
            }

            if (IndentOf(lines[i]) != baseIndent) break;
            if (!TryParseItemMarker(lines[i], out int contentCol, out string first, out bool ord2, out _) || ord2 != ordered)
                break;

            var itemLines = new List<string> { first };
            i++;
            while (i < end)
            {
                if (IsBlank(lines[i]))
                {
                    int k = i;
                    while (k < end && IsBlank(lines[k])) k++;
                    if (k < end && IndentOf(lines[k]) >= contentCol)
                    {
                        loose = true; itemLines.Add(""); i++; continue;
                    }
                    break;
                }
                if (IndentOf(lines[i]) >= contentCol)
                {
                    itemLines.Add(StripIndent(lines[i], contentCol));
                    i++; continue;
                }
                // A marker back at the base indent starts the next item.
                if (IndentOf(lines[i]) == baseIndent && TryParseItemMarker(lines[i], out _, out _, out _, out _))
                    break;
                // Lazy paragraph continuation.
                itemLines.Add(lines[i].TrimStart());
                i++;
            }
            items.Add(itemLines);
        }

        string tag = ordered ? "ol" : "ul";
        sb.Append('<').Append(tag);
        if (ordered && startNum != "1" && int.TryParse(startNum, out _))
            sb.Append(" start=\"").Append(startNum).Append('"');
        sb.Append(">\n");
        foreach (var item in items)
            RenderListItem(item, loose, sb);
        sb.Append("</").Append(tag).Append(">\n");
        return i;
    }

    private void RenderListItem(List<string> itemLines, bool loose, StringBuilder sb)
    {
        bool isTask = false, isChecked = false;
        if (itemLines.Count > 0)
        {
            string f = itemLines[0];
            if (f == "[ ]" || f.StartsWith("[ ] ")) { isTask = true; isChecked = false; itemLines[0] = f.Length > 3 ? f[4..] : ""; }
            else if (f.StartsWith("[x] ") || f.StartsWith("[X] ")) { isTask = true; isChecked = true; itemLines[0] = f[4..]; }
        }

        sb.Append("<li");
        if (isTask) sb.Append(" class=\"task-list-item\"");
        sb.Append('>');

        if (isTask)
            sb.Append("<input type=\"checkbox\" disabled").Append(isChecked ? " checked" : "").Append(" /> ");

        var content = new StringBuilder();
        RenderBlocks(itemLines.ToArray(), 0, itemLines.Count, content, tight: !loose);
        sb.Append(content.ToString().Trim('\n'));

        sb.Append("</li>\n");
    }

    // ---- block helpers ------------------------------------------------------

    private static bool IsBlank(string line) => line.Trim().Length == 0;

    private static int IndentOf(string line)
    {
        int n = 0;
        while (n < line.Length && line[n] == ' ') n++;
        return n;
    }

    private static string StripIndent(string line, int n)
    {
        int k = 0;
        while (k < n && k < line.Length && line[k] == ' ') k++;
        return line[k..];
    }

    private static string TrimTrailingBlank(string s) => s.TrimEnd('\n') + "\n";

    private static bool TryFenceInfo(string line, out char fence, out int indent, out string info)
    {
        fence = '\0'; info = ""; indent = IndentOf(line);
        if (indent > 3) return false;
        string s = line[indent..];
        if (s.Length < 3) return false;
        char c = s[0];
        if (c != '`' && c != '~') return false;
        int run = 0;
        while (run < s.Length && s[run] == c) run++;
        if (run < 3) return false;
        info = s[run..].Trim();
        if (c == '`' && info.Contains('`')) return false; // info string can't contain backticks
        fence = c;
        return true;
    }

    private static bool IsClosingFence(string line, char fence)
    {
        string s = line.Trim();
        if (s.Length < 3) return false;
        foreach (char ch in s)
            if (ch != fence) return false;
        return true;
    }

    private static bool TryAtxHeading(string trimmed, out int level, out string text)
    {
        level = 0; text = "";
        int h = 0;
        while (h < trimmed.Length && trimmed[h] == '#') h++;
        if (h < 1 || h > 6) return false;
        if (h < trimmed.Length && trimmed[h] != ' ') return false;
        level = h;
        string rest = trimmed[h..].Trim();
        // strip optional closing hashes
        rest = rest.TrimEnd();
        int e = rest.Length;
        while (e > 0 && rest[e - 1] == '#') e--;
        if (e < rest.Length && (e == 0 || rest[e - 1] == ' ')) rest = rest[..e].TrimEnd();
        text = rest;
        return true;
    }

    private static bool IsThematicBreak(string trimmed)
    {
        string s = trimmed.Replace(" ", "");
        if (s.Length < 3) return false;
        char c = s[0];
        if (c != '-' && c != '*' && c != '_') return false;
        foreach (char ch in s)
            if (ch != c) return false;
        return true;
    }

    private static bool TryParseItemMarker(string line, out int contentCol, out string content, out bool ordered, out string number)
    {
        contentCol = 0; content = ""; ordered = false; number = "";
        int ind = IndentOf(line);
        if (ind > 3) return false; // would be indented code at top level
        int p = ind;
        if (p >= line.Length) return false;

        char c = line[p];
        if (c is '-' or '+' or '*')
        {
            if (p + 1 < line.Length && line[p + 1] != ' ') return false;
            p++;
        }
        else if (char.IsDigit(c))
        {
            int s = p;
            while (p < line.Length && char.IsDigit(line[p]) && p - s < 9) p++;
            if (p >= line.Length || (line[p] != '.' && line[p] != ')')) return false;
            number = line[s..p];
            p++;
            ordered = true;
            if (p < line.Length && line[p] != ' ') return false;
        }
        else return false;

        int spStart = p;
        while (p < line.Length && line[p] == ' ' && p - spStart < 4) p++;
        if (p == spStart && p < line.Length) return false; // marker must be followed by space or EOL

        contentCol = p;
        content = p < line.Length ? line[p..] : "";
        return true;
    }
}
