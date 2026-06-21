namespace BlazorMarkdownEditor;

/// <summary>
/// Options controlling the built-in <see cref="Markdown"/> renderer.
/// </summary>
public sealed class MarkdownOptions
{
    /// <summary>
    /// When false (default) any raw HTML in the markdown source is escaped,
    /// which prevents stored-XSS through the rendered preview. Set true only
    /// when the markdown source is fully trusted.
    /// </summary>
    public bool AllowRawHtml { get; init; }

    /// <summary>
    /// URL schemes permitted on links. Anything else (e.g. <c>javascript:</c>)
    /// is dropped to avoid script injection. Scheme-less (relative) URLs,
    /// fragments and root-relative paths are always allowed.
    /// </summary>
    public IReadOnlyList<string> AllowedUrlSchemes { get; init; } =
        ["http", "https", "mailto", "tel"];

    /// <summary>Default, safe options (raw HTML escaped).</summary>
    public static MarkdownOptions Default { get; } = new();
}
