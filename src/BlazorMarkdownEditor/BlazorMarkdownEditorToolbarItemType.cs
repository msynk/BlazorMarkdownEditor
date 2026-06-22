namespace BlazorMarkdownEditor;

/// <summary>Describes how a <see cref="BlazorMarkdownEditorToolbarItem"/> behaves when clicked.</summary>
public enum BlazorMarkdownEditorToolbarItemType
{
    /// <summary>Runs the associated <see cref="BlazorMarkdownEditorCommand"/> against the text.</summary>
    Command,

    /// <summary>Reverts the editor to the previous state in the undo history.</summary>
    Undo,

    /// <summary>Re-applies the most recently undone change.</summary>
    Redo,

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
