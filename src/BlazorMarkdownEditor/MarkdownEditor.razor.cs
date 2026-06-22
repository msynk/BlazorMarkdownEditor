using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlazorMarkdownEditor;

/// <summary>
/// A native Blazor markdown editor with a customizable toolbar, keyboard
/// shortcuts, smart list handling and a live GitHub-flavored preview. It has
/// no external dependencies: markdown is rendered by the built-in
/// <see cref="Markdown"/> converter, and a small JS-interop module handles
/// textarea selection control.
/// </summary>
public partial class MarkdownEditor : ComponentBase, IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private ElementReference _textArea;
    private ElementReference _root;
    private IJSObjectReference? _module;
    private DotNetObjectReference<MarkdownEditor>? _selfRef;
    private MarkdownOptions _markdownOptions = MarkdownOptions.Default;
    private bool _allowRawHtmlCache = false;
    private bool _optionsBuilt;

    private string _value = "";
    private MarkupString _renderedHtml;
    private bool _fullscreen;
    private bool _showHelp;
    private bool _canUndo;
    private bool _canRedo;
    private bool _pendingValuePush;
    private CancellationTokenSource? _debounceCts;

    #region Parameters

    /// <summary>The markdown text. Supports two-way binding via <c>@bind-Value</c>.</summary>
    [Parameter] public string? Value { get; set; }

    /// <summary>Raised when the markdown text changes.</summary>
    [Parameter] public EventCallback<string> ValueChanged { get; set; }

    /// <summary>Raised with the rendered HTML whenever the preview is regenerated.</summary>
    [Parameter] public EventCallback<string> HtmlChanged { get; set; }

    /// <summary>Placeholder shown when the editor is empty.</summary>
    [Parameter] public string Placeholder { get; set; } = "Write some markdown\u2026";

    /// <summary>Which panes are visible. Supports two-way binding via <c>@bind-Mode</c>.</summary>
    [Parameter] public EditorMode Mode { get; set; } = EditorMode.Split;

    /// <summary>Raised when the display mode changes.</summary>
    [Parameter] public EventCallback<EditorMode> ModeChanged { get; set; }

    /// <summary>Toolbar layout. Defaults to <see cref="MarkdownToolbar.Default"/> when null.</summary>
    [Parameter] public IReadOnlyList<MarkdownToolbarItem>? Toolbar { get; set; }

    /// <summary>Whether the toolbar is shown.</summary>
    [Parameter] public bool ShowToolbar { get; set; } = true;

    /// <summary>Whether the word/character status bar is shown.</summary>
    [Parameter] public bool ShowStatusBar { get; set; } = true;

    /// <summary>Makes the editor read-only.</summary>
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>Enables native browser spell checking in the textarea.</summary>
    [Parameter] public bool SpellCheck { get; set; } = true;

    /// <summary>Editor height (any CSS length). Ignored in fullscreen.</summary>
    [Parameter] public string Height { get; set; } = "320px";

    /// <summary>String inserted per indent level (default: two spaces).</summary>
    [Parameter] public string IndentUnit { get; set; } = "  ";

    /// <summary>
    /// When false (default) raw HTML embedded in the markdown is escaped, which
    /// prevents stored-XSS through the rendered preview. Set true only when the
    /// markdown source is fully trusted.
    /// </summary>
    [Parameter] public bool AllowRawHtml { get; set; }

    /// <summary>Optional custom options for the built-in markdown renderer. Overrides <see cref="AllowRawHtml"/>.</summary>
    [Parameter] public MarkdownOptions? Options { get; set; }

    /// <summary>Debounce window (ms) before the preview re-renders while typing.</summary>
    [Parameter] public int DebounceMilliseconds { get; set; } = 150;

    /// <summary>Extra CSS class(es) applied to the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional attributes splatted onto the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    #endregion

    private IReadOnlyList<MarkdownToolbarItem> ActiveToolbar => Toolbar ?? MarkdownToolbar.Default;

    /// <summary>Determines whether a toolbar button should be rendered disabled.</summary>
    private bool IsToolbarItemDisabled(MarkdownToolbarItem item) => item.Type switch
    {
        ToolbarItemType.Command => ReadOnly,
        ToolbarItemType.Undo => ReadOnly || !_canUndo,
        ToolbarItemType.Redo => ReadOnly || !_canRedo,
        _ => false
    };
    private int WordCount =>
        string.IsNullOrWhiteSpace(_value)
            ? 0
            : _value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    protected override void OnParametersSet()
    {
        if (!_optionsBuilt || Options is not null || _allowRawHtmlCache != AllowRawHtml)
            BuildOptions();

        // Pick up externally-assigned values (initial load or parent reset).
        string incoming = Value ?? "";
        if (incoming != _value)
        {
            _value = incoming;
            RenderNow();
            // The textarea is uncontrolled (JS owns its value to preserve the
            // caret), so external changes must be pushed in after render.
            _pendingValuePush = true;
        }
    }

    private void BuildOptions()
    {
        _allowRawHtmlCache = AllowRawHtml;
        _markdownOptions = Options ?? (AllowRawHtml ? new MarkdownOptions { AllowRawHtml = true } : MarkdownOptions.Default);
        _optionsBuilt = true;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _module = await JS.InvokeAsync<IJSObjectReference>(
                "import", "./_content/BlazorMarkdownEditor/markdownEditor.js");
            _selfRef = DotNetObjectReference.Create(this);
            await _module.InvokeVoidAsync("init", _textArea, _root, _selfRef);
        }

        if (_pendingValuePush && _module is not null)
        {
            _pendingValuePush = false;
            await _module.InvokeVoidAsync("setText", _textArea, _value);
        }
    }

    private async Task OnInput(ChangeEventArgs e)
    {
        _value = e.Value?.ToString() ?? "";
        await ValueChanged.InvokeAsync(_value);
        await ScheduleRenderAsync();
    }

    private async Task ScheduleRenderAsync()
    {
        if (DebounceMilliseconds <= 0)
        {
            RenderNow();
            await InvokeAsync(StateHasChanged);
            await NotifyHtmlAsync();
            return;
        }

        _debounceCts?.Cancel();
        var cts = _debounceCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(DebounceMilliseconds, cts.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        RenderNow();
        await InvokeAsync(StateHasChanged);
        await NotifyHtmlAsync();
    }

    private void RenderNow() =>
        _renderedHtml = (MarkupString)Markdown.ToHtml(_value ?? "", _markdownOptions);

    private Task NotifyHtmlAsync() =>
        HtmlChanged.HasDelegate ? HtmlChanged.InvokeAsync(_renderedHtml.Value) : Task.CompletedTask;

    /// <summary>
    /// Invoked from JavaScript to run a command against the current selection.
    /// Returns the transformed text and the selection to restore; JS writes it
    /// back to the textarea and fires the input event so binding stays in sync.
    /// </summary>
    [JSInvokable]
    public CommandResult ApplyCommandAsync(string command, int start, int end, string value)
    {
        if (ReadOnly || !Enum.TryParse<MarkdownCommand>(command, out var cmd))
            return new CommandResult(false, value, start, end);

        EditResult r = MarkdownCommands.Apply(cmd, value, start, end, IndentUnit);
        return new CommandResult(r.Handled, r.Text, r.SelectionStart, r.SelectionEnd);
    }

    private async Task OnToolbarItemClick(MarkdownToolbarItem item)
    {
        switch (item.Type)
        {
            case ToolbarItemType.Command when item.Command is { } cmd && _module is not null:
                await _module.InvokeVoidAsync("invoke", _textArea, cmd.ToString());
                break;
            case ToolbarItemType.Undo:
                await UndoAsync();
                break;
            case ToolbarItemType.Redo:
                await RedoAsync();
                break;
            case ToolbarItemType.TogglePreview:
                await CycleModeAsync();
                break;
            case ToolbarItemType.ToggleFullscreen:
                _fullscreen = !_fullscreen;
                break;
            case ToolbarItemType.Help:
                _showHelp = !_showHelp;
                break;
            case ToolbarItemType.Custom when item.OnClick is not null:
                await item.OnClick(this);
                break;
        }
    }

    private async Task CycleModeAsync()
    {
        var next = Mode switch
        {
            EditorMode.Edit => EditorMode.Split,
            EditorMode.Split => EditorMode.Preview,
            _ => EditorMode.Edit
        };
        Mode = next;
        await ModeChanged.InvokeAsync(next);
    }

    /// <summary>Programmatically runs a command (as if a toolbar button was pressed).</summary>
    public async Task RunCommandAsync(MarkdownCommand command)
    {
        if (_module is not null)
            await _module.InvokeVoidAsync("invoke", _textArea, command.ToString());
    }

    /// <summary>Reverts the editor to the previous state in the undo history.</summary>
    public async Task UndoAsync()
    {
        if (!ReadOnly && _module is not null)
            await _module.InvokeVoidAsync("undoCommand", _textArea);
    }

    /// <summary>Re-applies the most recently undone change.</summary>
    public async Task RedoAsync()
    {
        if (!ReadOnly && _module is not null)
            await _module.InvokeVoidAsync("redoCommand", _textArea);
    }

    /// <summary>True when there is at least one change that can be undone.</summary>
    public bool CanUndo => _canUndo;

    /// <summary>True when there is at least one undone change that can be redone.</summary>
    public bool CanRedo => _canRedo;

    /// <summary>
    /// Invoked from JavaScript whenever the undo/redo history changes, so the
    /// toolbar buttons can reflect the current availability.
    /// </summary>
    [JSInvokable]
    public void OnHistoryChanged(bool canUndo, bool canRedo)
    {
        if (canUndo == _canUndo && canRedo == _canRedo)
            return;
        _canUndo = canUndo;
        _canRedo = canRedo;
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>Moves keyboard focus into the editor textarea.</summary>
    public async ValueTask FocusAsync()
    {
        if (_module is not null)
            await _module.InvokeVoidAsync("focus", _textArea);
    }

    private string RootClass =>
        string.Join(' ', new[]
        {
            "bme",
            _fullscreen ? "bme--fullscreen" : null,
            ReadOnly ? "bme--readonly" : null,
            Class
        }.Where(s => !string.IsNullOrEmpty(s)));

    public async ValueTask DisposeAsync()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        try
        {
            if (_module is not null)
            {
                await _module.InvokeVoidAsync("dispose", _textArea);
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException) { /* circuit already gone */ }
        catch (OperationCanceledException) { }
        _selfRef?.Dispose();
    }

    /// <summary>DTO returned to JS after a command runs.</summary>
    public readonly record struct CommandResult(bool Handled, string Text, int SelectionStart, int SelectionEnd);
}
