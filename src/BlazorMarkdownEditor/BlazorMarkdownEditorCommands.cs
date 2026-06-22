using System.Text;
using System.Text.RegularExpressions;

namespace BlazorMarkdownEditor;

/// <summary>
/// Pure, side-effect-free implementations of every <see cref="BlazorMarkdownEditorCommand"/>.
/// Each method takes the current text plus the selection range and returns the
/// new text and where the selection should land. Keeping these pure makes the
/// editing behaviour fully unit-testable without a browser.
/// </summary>
public static partial class BlazorMarkdownEditorCommands
{
    /// <summary>Applies <paramref name="command"/> to <paramref name="text"/> for the given selection.</summary>
    /// <param name="command">The command to run.</param>
    /// <param name="text">Full editor text (LF line endings).</param>
    /// <param name="start">Selection start index.</param>
    /// <param name="end">Selection end index.</param>
    /// <param name="indentUnit">String inserted for one indent level (e.g. two spaces or a tab).</param>
    public static BlazorMarkdownEditorEditResult Apply(BlazorMarkdownEditorCommand command, string text, int start, int end, string indentUnit = "  ")
    {
        text ??= "";
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, 0, text.Length);
        if (end < start)
            (start, end) = (end, start);

        return command switch
        {
            BlazorMarkdownEditorCommand.Bold => ToggleWrap(text, start, end, "**", "bold text"),
            BlazorMarkdownEditorCommand.Italic => ToggleWrap(text, start, end, "*", "italic text"),
            BlazorMarkdownEditorCommand.Strikethrough => ToggleWrap(text, start, end, "~~", "strikethrough"),
            BlazorMarkdownEditorCommand.InlineCode => ToggleWrap(text, start, end, "`", "code"),
            BlazorMarkdownEditorCommand.Heading1 => Heading(text, start, end, 1),
            BlazorMarkdownEditorCommand.Heading2 => Heading(text, start, end, 2),
            BlazorMarkdownEditorCommand.Heading3 => Heading(text, start, end, 3),
            BlazorMarkdownEditorCommand.Quote => LinePrefixToggle(text, start, end, "> ", QuotePrefix()),
            BlazorMarkdownEditorCommand.UnorderedList => UnorderedList(text, start, end),
            BlazorMarkdownEditorCommand.OrderedList => OrderedList(text, start, end),
            BlazorMarkdownEditorCommand.TaskList => TaskList(text, start, end),
            BlazorMarkdownEditorCommand.CodeBlock => CodeBlock(text, start, end),
            BlazorMarkdownEditorCommand.Link => LinkOrImage(text, start, end, isImage: false),
            BlazorMarkdownEditorCommand.Image => LinkOrImage(text, start, end, isImage: true),
            BlazorMarkdownEditorCommand.Table => Table(text, start, end),
            BlazorMarkdownEditorCommand.HorizontalRule => HorizontalRule(text, start, end),
            BlazorMarkdownEditorCommand.Indent => Indent(text, start, end, indentUnit),
            BlazorMarkdownEditorCommand.Outdent => Outdent(text, start, end, indentUnit),
            BlazorMarkdownEditorCommand.NewLine => NewLine(text, start, end),
            _ => BlazorMarkdownEditorEditResult.NotHandled(text, start, end)
        };
    }

    // ---- inline wrapping (bold / italic / strikethrough / code) -------------

    private static BlazorMarkdownEditorEditResult ToggleWrap(string text, int start, int end, string marker, string placeholder)
    {
        int ml = marker.Length;
        string selected = text[start..end];

        // Already wrapped on the outside? -> unwrap.
        if (start >= ml && end + ml <= text.Length &&
            text.Substring(start - ml, ml) == marker &&
            text.Substring(end, ml) == marker)
        {
            string unwrapped = text[..(start - ml)] + selected + text[(end + ml)..];
            return new BlazorMarkdownEditorEditResult(true, unwrapped, start - ml, end - ml);
        }

        // Markers captured inside the selection? -> unwrap.
        if (selected.Length >= 2 * ml && selected.StartsWith(marker) && selected.EndsWith(marker))
        {
            string inner = selected[ml..^ml];
            return new BlazorMarkdownEditorEditResult(true, text[..start] + inner + text[end..], start, start + inner.Length);
        }

        if (start == end)
        {
            string insert = marker + placeholder + marker;
            return new BlazorMarkdownEditorEditResult(true, text[..start] + insert + text[end..], start + ml, start + ml + placeholder.Length);
        }

        string wrapped = marker + selected + marker;
        return new BlazorMarkdownEditorEditResult(true, text[..start] + wrapped + text[end..], start + ml, start + ml + selected.Length);
    }

    // ---- headings -----------------------------------------------------------

    private static BlazorMarkdownEditorEditResult Heading(string text, int start, int end, int level)
    {
        return TransformBlock(text, start, end, lines =>
        {
            string hashes = new('#', level);
            for (int i = 0; i < lines.Count; i++)
            {
                Match m = HeadingPrefix().Match(lines[i]);
                string rest = m.Success ? lines[i][m.Length..] : lines[i];
                int existing = m.Success ? m.Groups[1].Value.Length : 0;
                lines[i] = existing == level ? rest : $"{hashes} {rest}";
            }
        });
    }

    // ---- blockquote ---------------------------------------------------------

    private static BlazorMarkdownEditorEditResult LinePrefixToggle(string text, int start, int end, string prefix, Regex detect)
    {
        return TransformBlock(text, start, end, lines =>
        {
            bool allPrefixed = lines.Where(l => l.Length > 0).All(l => detect.IsMatch(l));
            for (int i = 0; i < lines.Count; i++)
            {
                if (allPrefixed)
                {
                    Match m = detect.Match(lines[i]);
                    if (m.Success) lines[i] = lines[i][m.Length..];
                }
                else
                {
                    lines[i] = prefix + lines[i];
                }
            }
        });
    }

    // ---- lists --------------------------------------------------------------

    private static BlazorMarkdownEditorEditResult UnorderedList(string text, int start, int end)
    {
        return TransformBlock(text, start, end, lines =>
        {
            bool allListed = lines.Where(l => l.Trim().Length > 0).All(l => UnorderedItem().IsMatch(l));
            for (int i = 0; i < lines.Count; i++)
            {
                if (allListed)
                {
                    Match m = UnorderedItem().Match(lines[i]);
                    if (m.Success) lines[i] = m.Groups[1].Value + lines[i][m.Length..];
                }
                else if (lines[i].Trim().Length > 0)
                {
                    lines[i] = "- " + lines[i];
                }
            }
        });
    }

    private static BlazorMarkdownEditorEditResult TaskList(string text, int start, int end)
    {
        return TransformBlock(text, start, end, lines =>
        {
            bool allTasks = lines.Where(l => l.Trim().Length > 0).All(l => TaskItem().IsMatch(l));
            for (int i = 0; i < lines.Count; i++)
            {
                if (allTasks)
                {
                    Match m = TaskItem().Match(lines[i]);
                    if (m.Success) lines[i] = m.Groups[1].Value + lines[i][m.Length..];
                }
                else if (lines[i].Trim().Length > 0)
                {
                    lines[i] = "- [ ] " + lines[i];
                }
            }
        });
    }

    private static BlazorMarkdownEditorEditResult OrderedList(string text, int start, int end)
    {
        return TransformBlock(text, start, end, lines =>
        {
            bool allOrdered = lines.Where(l => l.Trim().Length > 0).All(l => OrderedItem().IsMatch(l));
            int n = 1;
            for (int i = 0; i < lines.Count; i++)
            {
                if (allOrdered)
                {
                    Match m = OrderedItem().Match(lines[i]);
                    if (m.Success) lines[i] = lines[i][m.Length..];
                }
                else if (lines[i].Trim().Length > 0)
                {
                    lines[i] = $"{n++}. {lines[i]}";
                }
            }
        });
    }

    // ---- fenced code block --------------------------------------------------

    private static BlazorMarkdownEditorEditResult CodeBlock(string text, int start, int end)
    {
        string selected = text[start..end];
        if (start == end)
        {
            string insert = "```\n\n```";
            // caret on the empty middle line
            int caret = start + 4;
            return new BlazorMarkdownEditorEditResult(true, text[..start] + insert + text[end..], caret, caret);
        }

        string body = selected.TrimEnd('\n');
        string fenced = $"```\n{body}\n```";
        return new BlazorMarkdownEditorEditResult(true, text[..start] + fenced + text[end..], start + 4, start + 4 + body.Length);
    }

    // ---- links & images -----------------------------------------------------

    private static BlazorMarkdownEditorEditResult LinkOrImage(string text, int start, int end, bool isImage)
    {
        string bang = isImage ? "!" : "";
        string selected = text[start..end];
        if (start == end)
        {
            string label = isImage ? "alt" : "text";
            string insert = $"{bang}[{label}](url)";
            int selStart = start + bang.Length + 1;            // inside the [..]
            return new BlazorMarkdownEditorEditResult(true, text[..start] + insert + text[end..], selStart, selStart + label.Length);
        }

        // Use the selection as the label and drop the caret on the url placeholder.
        string built = $"{bang}[{selected}](url)";
        int urlStart = start + bang.Length + 1 + selected.Length + 2; // after "](".
        return new BlazorMarkdownEditorEditResult(true, text[..start] + built + text[end..], urlStart, urlStart + 3);
    }

    // ---- table --------------------------------------------------------------

    private static BlazorMarkdownEditorEditResult Table(string text, int start, int end)
    {
        string prefix = NeedsLeadingBlankLine(text, start) ? "\n" : "";
        const string template =
            "| Column 1 | Column 2 |\n" +
            "| -------- | -------- |\n" +
            "| Cell     | Cell     |\n";
        string insert = prefix + template;
        int sel = start + prefix.Length + 2; // start of "Column 1"
        return new BlazorMarkdownEditorEditResult(true, text[..start] + insert + text[end..], sel, sel + "Column 1".Length);
    }

    // ---- horizontal rule ----------------------------------------------------

    private static BlazorMarkdownEditorEditResult HorizontalRule(string text, int start, int end)
    {
        string prefix = NeedsLeadingBlankLine(text, start) ? "\n" : "";
        string insert = prefix + "---\n";
        int caret = start + insert.Length;
        return new BlazorMarkdownEditorEditResult(true, text[..start] + insert + text[end..], caret, caret);
    }

    // ---- indentation --------------------------------------------------------

    private static BlazorMarkdownEditorEditResult Indent(string text, int start, int end, string indentUnit)
    {
        if (start == end)
        {
            string ins = indentUnit;
            return new BlazorMarkdownEditorEditResult(true, text[..start] + ins + text[end..], start + ins.Length, start + ins.Length);
        }
        return TransformBlock(text, start, end, lines =>
        {
            for (int i = 0; i < lines.Count; i++)
                lines[i] = indentUnit + lines[i];
        });
    }

    private static BlazorMarkdownEditorEditResult Outdent(string text, int start, int end, string indentUnit)
    {
        return TransformBlock(text, start, end, lines =>
        {
            for (int i = 0; i < lines.Count; i++)
                lines[i] = RemoveOneIndent(lines[i], indentUnit);
        });
    }

    private static string RemoveOneIndent(string line, string indentUnit)
    {
        if (line.StartsWith('\t'))
            return line[1..];
        int spaces = 0;
        int max = indentUnit.Length == 0 ? 2 : indentUnit.Length;
        while (spaces < max && spaces < line.Length && line[spaces] == ' ')
            spaces++;
        return line[spaces..];
    }

    // ---- smart newline (list / quote continuation) --------------------------

    private static BlazorMarkdownEditorEditResult NewLine(string text, int start, int end)
    {
        int lineStart = LineStartIndex(text, start);
        int lineEnd = LineEndIndex(text, start);
        string fullLine = text[lineStart..lineEnd];

        Match task = TaskItem().Match(fullLine);
        Match unordered = UnorderedItem().Match(fullLine);
        Match ordered = OrderedItem().Match(fullLine);

        // Continue task list
        if (task.Success)
        {
            string content = fullLine[task.Length..];
            if (content.Trim().Length == 0)
                return ClearLine(text, lineStart, lineEnd);
            string insertion = "\n" + task.Groups[1].Value + "- [ ] ";
            int caret = start + insertion.Length;
            return new BlazorMarkdownEditorEditResult(true, text[..start] + insertion + text[end..], caret, caret);
        }

        // Continue unordered list
        if (unordered.Success)
        {
            string content = fullLine[unordered.Length..];
            if (content.Trim().Length == 0)
                return ClearLine(text, lineStart, lineEnd);
            string insertion = "\n" + unordered.Groups[1].Value + unordered.Groups[2].Value + " ";
            int caret = start + insertion.Length;
            return new BlazorMarkdownEditorEditResult(true, text[..start] + insertion + text[end..], caret, caret);
        }

        // Continue ordered list (increment the number)
        if (ordered.Success)
        {
            string content = fullLine[ordered.Length..];
            if (content.Trim().Length == 0)
                return ClearLine(text, lineStart, lineEnd);
            int number = int.TryParse(ordered.Groups[2].Value, out int parsed) ? parsed + 1 : 1;
            string insertion = "\n" + ordered.Groups[1].Value + number + ordered.Groups[3].Value + " ";
            int caret = start + insertion.Length;
            return new BlazorMarkdownEditorEditResult(true, text[..start] + insertion + text[end..], caret, caret);
        }

        // Default: plain newline, preserving the current line's leading whitespace.
        string indent = LeadingWhitespace().Match(fullLine).Value;
        string plain = "\n" + indent;
        int pos = start + plain.Length;
        return new BlazorMarkdownEditorEditResult(true, text[..start] + plain + text[end..], pos, pos);
    }

    private static BlazorMarkdownEditorEditResult ClearLine(string text, int lineStart, int lineEnd) =>
        new(true, text[..lineStart] + text[lineEnd..], lineStart, lineStart);

    // ---- shared helpers -----------------------------------------------------

    /// <summary>Runs <paramref name="transform"/> over every full line touched by the selection.</summary>
    private static BlazorMarkdownEditorEditResult TransformBlock(string text, int start, int end, Action<List<string>> transform)
    {
        int blockStart = LineStartIndex(text, start);
        int effEnd = end;
        if (effEnd > start && effEnd > 0 && text[effEnd - 1] == '\n')
            effEnd--;
        int blockEnd = LineEndIndex(text, effEnd);

        string block = text[blockStart..blockEnd];
        List<string> lines = [.. block.Split('\n')];
        transform(lines);
        string rebuilt = string.Join('\n', lines);

        string newText = text[..blockStart] + rebuilt + text[blockEnd..];
        return new BlazorMarkdownEditorEditResult(true, newText, blockStart, blockStart + rebuilt.Length);
    }

    private static int LineStartIndex(string text, int p)
    {
        if (p <= 0) return 0;
        int idx = text.LastIndexOf('\n', p - 1);
        return idx + 1;
    }

    private static int LineEndIndex(string text, int p)
    {
        int idx = text.IndexOf('\n', Math.Min(p, text.Length));
        return idx < 0 ? text.Length : idx;
    }

    private static bool NeedsLeadingBlankLine(string text, int pos)
    {
        if (pos == 0) return false;
        // Already preceded by a blank line (two newlines) or beginning of doc.
        if (pos >= 1 && text[pos - 1] != '\n') return true;
        if (pos >= 2 && text[pos - 1] == '\n' && text[pos - 2] != '\n') return false;
        return false;
    }

    [GeneratedRegex(@"^(#{1,6}) ")]
    private static partial Regex HeadingPrefix();

    [GeneratedRegex(@"^> ")]
    private static partial Regex QuotePrefix();

    // group 1 = leading whitespace, group 2 = bullet char
    [GeneratedRegex(@"^(\s*)([-*+]) (?!\[[ xX]\])")]
    private static partial Regex UnorderedItem();

    // group 1 = leading whitespace
    [GeneratedRegex(@"^(\s*)[-*+] \[[ xX]\] ")]
    private static partial Regex TaskItem();

    // group 1 = leading whitespace, group 2 = number, group 3 = delimiter (. or ))
    [GeneratedRegex(@"^(\s*)(\d+)([.)]) ")]
    private static partial Regex OrderedItem();

    [GeneratedRegex(@"^[ \t]*")]
    private static partial Regex LeadingWhitespace();
}
