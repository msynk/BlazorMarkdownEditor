namespace BlazorMarkdownEditor;

/// <summary>
/// Describes a single button (or separator) in the editor toolbar.
/// The toolbar is fully data-driven, so consumers can reorder, remove,
/// or add items by supplying their own list to <c>MarkdownEditor.Toolbar</c>.
/// </summary>
public sealed class MarkdownToolbarItem
{
    /// <summary>Stable identifier, handy for tests and custom styling.</summary>
    public string Name { get; init; } = "";

    /// <summary>Tooltip / accessible label shown to the user.</summary>
    public string Title { get; init; } = "";

    /// <summary>Raw inline SVG markup rendered inside the button.</summary>
    public string Icon { get; init; } = "";

    /// <summary>How the item behaves when activated.</summary>
    public ToolbarItemType Type { get; init; } = ToolbarItemType.Command;

    /// <summary>The text command to run when <see cref="Type"/> is <see cref="ToolbarItemType.Command"/>.</summary>
    public MarkdownCommand? Command { get; init; }

    /// <summary>Optional human readable shortcut hint, e.g. "Ctrl+B".</summary>
    public string? Shortcut { get; init; }

    /// <summary>Callback used when <see cref="Type"/> is <see cref="ToolbarItemType.Custom"/>.</summary>
    public Func<MarkdownEditor, Task>? OnClick { get; init; }

    /// <summary>Convenience factory for a toolbar separator.</summary>
    public static MarkdownToolbarItem Separator { get; } = new() { Type = ToolbarItemType.Separator, Name = "separator" };
}
