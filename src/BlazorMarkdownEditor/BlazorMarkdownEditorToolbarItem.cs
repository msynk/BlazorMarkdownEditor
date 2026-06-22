namespace BlazorMarkdownEditor;

/// <summary>
/// Describes a single button (or separator) in the editor toolbar.
/// The toolbar is fully data-driven, so consumers can reorder, remove,
/// or add items by supplying their own list to <c>BlazorMarkdownEditor.Toolbar</c>.
/// </summary>
public sealed class BlazorMarkdownEditorToolbarItem
{
    /// <summary>Stable identifier, handy for tests and custom styling.</summary>
    public string Name { get; init; } = "";

    /// <summary>Tooltip / accessible label shown to the user.</summary>
    public string Title { get; init; } = "";

    /// <summary>Raw inline SVG markup rendered inside the button.</summary>
    public string Icon { get; init; } = "";

    /// <summary>How the item behaves when activated.</summary>
    public BlazorMarkdownEditorToolbarItemType Type { get; init; } = BlazorMarkdownEditorToolbarItemType.Command;

    /// <summary>The text command to run when <see cref="Type"/> is <see cref="BlazorMarkdownEditorToolbarItemType.Command"/>.</summary>
    public BlazorMarkdownEditorCommand? Command { get; init; }

    /// <summary>Optional human readable shortcut hint, e.g. "Ctrl+B".</summary>
    public string? Shortcut { get; init; }

    /// <summary>Callback used when <see cref="Type"/> is <see cref="BlazorMarkdownEditorToolbarItemType.Custom"/>.</summary>
    public Func<BlazorMarkdownEditor, Task>? OnClick { get; init; }

    /// <summary>Convenience factory for a toolbar separator.</summary>
    public static BlazorMarkdownEditorToolbarItem Separator { get; } = new() { Type = BlazorMarkdownEditorToolbarItemType.Separator, Name = "separator" };
}
