# BlazorMarkdownEditor

A **native Blazor** markdown editor component with a customizable toolbar, live
GitHub-Flavored-Markdown preview, keyboard shortcuts and smart list handling.

The design goal is a full-featured editor with **zero external dependencies**.
Markdown is converted to HTML by a built-in C# renderer (`BlazorMarkdownEditorMarkdown.ToHtml`), and
everything else (toolbar, commands, list logic, two-way binding, styling) is
native Blazor and C#, plus a small JavaScript interop module that does only what
the browser requires: read and write the `<textarea>` selection.

The only package reference is `Microsoft.AspNetCore.Components.Web`, which is
part of the Blazor framework itself (not a third-party library).

## Why these choices

- **Built-in markdown renderer.** A small, dependency-free block + inline parser
  (`BlazorMarkdownEditorMarkdown` / `BlazorMarkdownEditorParser`) supports the CommonMark blocks plus the GFM
  features the editor produces: headings, thematic breaks, blockquotes, fenced
  &amp; indented code, ordered/unordered/task lists (nested), tables with
  alignment, emphasis/strong/strikethrough, inline code, links, images and
  autolinks. It is pragmatic rather than 100% CommonMark-conformant.
- **Markdown logic in C#.** All editing commands (bold, lists, indentation,
  smart Enter, …) are pure C# functions in `BlazorMarkdownEditorCommands`, so they are
  testable without a browser and there is a single source of truth.
- **Thin JS layer.** A `<textarea>` caret/selection cannot be controlled from
  C#, so a small ES module handles selection get/set and intercepts keys
  (Tab, Enter, Ctrl+B/I/K) synchronously to call `preventDefault()`.
- **Safe by default.** Raw HTML in the markdown source is escaped by default,
  and link URLs with unsafe schemes (e.g. `javascript:`) are dropped, preventing
  stored-XSS through the rendered preview. Set `AllowRawHtml="true"` only for
  fully trusted content.

## Features

- Toolbar: bold, italic, strikethrough, headings (H1–H3), blockquote, bullet /
  numbered / task lists, link, image, inline code, fenced code block, table,
  horizontal rule — all data-driven and reorderable.
- Live preview with **Edit / Split / Preview** modes.
- Full **undo / redo** history with toolbar buttons and `Ctrl/Cmd+Z`,
  `Ctrl/Cmd+Y` / `Ctrl/Cmd+Shift+Z` shortcuts. Rapid keystrokes are coalesced
  into a single step and toolbar commands are undoable as one step.
- Keyboard shortcuts: `Ctrl/Cmd+B`, `Ctrl/Cmd+I`, `Ctrl/Cmd+K`,
  `Ctrl/Cmd+Shift+S`.
- Smart `Tab` / `Shift+Tab` indentation and automatic list continuation on
  `Enter` (including numbered lists and task lists).
- Two-way binding for both the text (`@bind-Value`) and the display mode
  (`@bind-Mode`).
- Fullscreen mode, word/character status bar, keyboard-shortcut help panel.
- Scoped CSS with automatic light/dark theming via `prefers-color-scheme`.

## Usage

```razor
@using BlazorMarkdownEditor

<BlazorMarkdownEditor @bind-Value="markdown" Height="460px" />

@code {
    private string markdown = "# Hello\n\nStart **writing**…";
}
```

### Common parameters

| Parameter | Type | Default | Description |
| --------- | ---- | ------- | ----------- |
| `Value` | `string?` | `null` | Markdown text (`@bind-Value`). |
| `Mode` | `BlazorMarkdownEditorMode` | `Split` | `Edit` / `Split` / `Preview` (`@bind-Mode`). |
| `Toolbar` | `IReadOnlyList<BlazorMarkdownEditorToolbarItem>?` | default | Custom toolbar layout. |
| `ShowToolbar` / `ShowStatusBar` | `bool` | `true` | Toggle chrome. |
| `ReadOnly` | `bool` | `false` | Disable editing. |
| `AllowRawHtml` | `bool` | `false` | Allow raw HTML in the preview (unsafe). |
| `Options` | `BlazorMarkdownEditorOptions?` | `null` | Supply custom renderer options. |
| `Height` | `string` | `"320px"` | Editor height (any CSS length). |
| `DebounceMilliseconds` | `int` | `150` | Preview re-render debounce. |

The component also exposes `RunCommandAsync(BlazorMarkdownEditorCommand)`, `UndoAsync()`,
`RedoAsync()` (plus `CanUndo` / `CanRedo`) and `FocusAsync()` for programmatic
control, and an `HtmlChanged` callback with the rendered HTML.

## Project layout

```
src/BlazorMarkdownEditor      → the reusable Razor Class Library (the component)
src/BlazorMarkdownEditor.Demo → a Blazor WebAssembly showcase app
```

## Running the demo

```bash
dotnet run --project src/BlazorMarkdownEditor.Demo
```

## Notes & limitations

- Toolbar/shortcut edits replace the textarea value, which bypasses the
  browser's native undo stack, so the component maintains its own undo/redo
  history (see *Features*) that covers both typing and commands.
- The textarea is intentionally *uncontrolled* (JavaScript owns its value) to
  preserve the caret position; this is the standard approach for Blazor text
  editors. In prerendered/SSR scenarios the initial text is applied once the
  component becomes interactive.
