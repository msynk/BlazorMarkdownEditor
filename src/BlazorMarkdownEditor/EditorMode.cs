namespace BlazorMarkdownEditor;

/// <summary>
/// Controls which panes the <see cref="MarkdownEditor"/> displays.
/// </summary>
public enum EditorMode
{
    /// <summary>Only the markdown text area is shown.</summary>
    Edit,

    /// <summary>Editor and rendered preview are shown side by side.</summary>
    Split,

    /// <summary>Only the rendered HTML preview is shown.</summary>
    Preview
}
