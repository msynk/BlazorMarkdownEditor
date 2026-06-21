namespace BlazorMarkdownEditor;

/// <summary>
/// The set of built-in editing commands the toolbar and keyboard shortcuts can invoke.
/// These are pure text transformations executed in C# so the markdown logic stays in one place.
/// </summary>
public enum MarkdownCommand
{
    Bold,
    Italic,
    Strikethrough,
    InlineCode,
    Heading1,
    Heading2,
    Heading3,
    Quote,
    CodeBlock,
    Link,
    Image,
    UnorderedList,
    OrderedList,
    TaskList,
    Table,
    HorizontalRule,

    /// <summary>Increase indentation of the selected lines (Tab).</summary>
    Indent,

    /// <summary>Decrease indentation of the selected lines (Shift+Tab).</summary>
    Outdent,

    /// <summary>Smart newline that continues lists/quotes (Enter).</summary>
    NewLine
}
