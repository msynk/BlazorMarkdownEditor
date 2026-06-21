namespace BlazorMarkdownEditor;

/// <summary>Describes how a <see cref="MarkdownToolbarItem"/> behaves when clicked.</summary>
public enum ToolbarItemType
{
    /// <summary>Runs the associated <see cref="MarkdownCommand"/> against the text.</summary>
    Command,

    /// <summary>A non-interactive vertical divider in the toolbar.</summary>
    Separator,

    /// <summary>Cycles the editor display mode (edit / split / preview).</summary>
    TogglePreview,

    /// <summary>Toggles fullscreen mode for the editor.</summary>
    ToggleFullscreen,

    /// <summary>Toggles the keyboard-shortcut help panel.</summary>
    Help,

    /// <summary>Invokes a user-supplied callback.</summary>
    Custom
}
