using System.Text;

namespace BlazorMarkdownEditor;

/// <summary>
/// A small, dependency-free Markdown to HTML converter supporting the common
/// CommonMark blocks plus the GitHub-Flavored-Markdown extensions the editor
/// produces: ATX headings, thematic breaks, blockquotes, fenced &amp; indented
/// code, ordered/unordered/task lists (nested), tables, paragraphs with hard
/// line breaks, and inline emphasis/strong/strikethrough/code/links/images and
/// autolinks.
/// </summary>
/// <remarks>
/// This is intentionally pragmatic rather than 100% CommonMark-conformant: it
/// targets the output of <see cref="BlazorMarkdownEditor"/> and typical hand-written
/// markdown. HTML is escaped by default for safety (see <see cref="BlazorMarkdownEditorOptions.AllowRawHtml"/>).
/// </remarks>
public static class BlazorMarkdownEditorMarkdown
{
    /// <summary>Converts a markdown string to an HTML fragment.</summary>
    public static string ToHtml(string? markdown, BlazorMarkdownEditorOptions? options = null)
    {
        options ??= BlazorMarkdownEditorOptions.Default;
        markdown ??= "";
        string normalized = markdown.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\t", "    ");
        var lines = normalized.Split('\n');
        var parser = new BlazorMarkdownEditorParser(options);
        var sb = new StringBuilder();
        parser.RenderBlocks(lines, 0, lines.Length, sb);
        return sb.ToString();
    }
}
