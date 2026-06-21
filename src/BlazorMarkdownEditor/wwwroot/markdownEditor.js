// BlazorMarkdownEditor - JS interop module.
// Deliberately minimal: it only does what C#/Blazor cannot do directly, namely
// read & write the <textarea> selection and intercept keys synchronously so we
// can preventDefault(). All markdown transformations happen in C#.

const LIST_LINE = /^(\s*)([-*+] (\[[ xX]\] )?|\d+[.)] )/;

function currentLine(textarea) {
    const value = textarea.value;
    const pos = textarea.selectionStart;
    const start = value.lastIndexOf("\n", pos - 1) + 1;
    let end = value.indexOf("\n", pos);
    if (end < 0) end = value.length;
    return value.slice(start, end);
}

// Reads selection + value, asks C# to transform it, then writes the result back.
async function runCommand(textarea, command) {
    const state = textarea._mdEditor;
    if (!state || textarea.readOnly) return;

    // When a toolbar button takes focus, the textarea's live selection can be
    // lost, so fall back to the last selection captured while it was focused.
    const focused = document.activeElement === textarea;
    const start = focused ? textarea.selectionStart : state.lastSelection.start;
    const end = focused ? textarea.selectionEnd : state.lastSelection.end;
    const value = textarea.value;

    const result = await state.dotNetRef.invokeMethodAsync(
        "ApplyCommandAsync", command, start, end, value);

    if (!result || !result.handled) return;
    applyResult(textarea, result);
}

function applyResult(textarea, result) {
    textarea.value = result.text;
    // Let Blazor's two-way binding pick up the new value.
    textarea.dispatchEvent(new Event("input", { bubbles: true }));
    textarea.focus();
    textarea.setSelectionRange(result.selectionStart, result.selectionEnd);
    saveSelection(textarea);
}

function saveSelection(textarea) {
    const state = textarea._mdEditor;
    if (state) state.lastSelection = { start: textarea.selectionStart, end: textarea.selectionEnd };
}

function onKeyDown(e) {
    const textarea = e.currentTarget;
    if (e.isComposing) return;

    const mod = e.ctrlKey || e.metaKey;

    if (mod && !e.altKey) {
        const key = e.key.toLowerCase();
        if (e.shiftKey && key === "s") { e.preventDefault(); runCommand(textarea, "Strikethrough"); return; }
        if (e.shiftKey) return;
        switch (key) {
            case "b": e.preventDefault(); runCommand(textarea, "Bold"); return;
            case "i": e.preventDefault(); runCommand(textarea, "Italic"); return;
            case "k": e.preventDefault(); runCommand(textarea, "Link"); return;
        }
        return;
    }

    if (e.key === "Tab") {
        e.preventDefault();
        runCommand(textarea, e.shiftKey ? "Outdent" : "Indent");
        return;
    }

    // Only hijack Enter when continuing a list/quote, so normal typing keeps
    // the browser's native undo history intact.
    if (e.key === "Enter" && !e.shiftKey &&
        textarea.selectionStart === textarea.selectionEnd) {
        const line = currentLine(textarea);
        if (LIST_LINE.test(line) || /^\s*> /.test(line)) {
            e.preventDefault();
            runCommand(textarea, "NewLine");
        }
    }
}

// ---- exported API (called from C#) -----------------------------------------

export function init(textarea, root, dotNetRef) {
    if (!textarea) return;

    const state = {
        dotNetRef,
        onKeyDown,
        root,
        lastSelection: { start: textarea.selectionStart || 0, end: textarea.selectionEnd || 0 },
        onSelectionChange: () => {
            if (document.activeElement === textarea) saveSelection(textarea);
        },
        // Stop toolbar buttons from stealing focus from the textarea. A native
        // mousedown preventDefault reliably keeps the caret/selection in place.
        onToolbarMouseDown: (e) => {
            if (e.target.closest && e.target.closest(".bme-btn")) e.preventDefault();
        }
    };
    textarea._mdEditor = state;

    textarea.addEventListener("keydown", onKeyDown);
    // Capture the selection whenever it changes while the textarea is focused,
    // so commands always know the intended range.
    document.addEventListener("selectionchange", state.onSelectionChange);
    textarea.addEventListener("mouseup", () => saveSelection(textarea));
    textarea.addEventListener("keyup", () => saveSelection(textarea));
    root?.addEventListener("mousedown", state.onToolbarMouseDown);
}

export function invoke(textarea, command) {
    return runCommand(textarea, command);
}

export function focus(textarea) {
    textarea?.focus();
}

// Push an externally-changed value into the (uncontrolled) textarea without
// firing the input event, so we don't loop back into Blazor.
export function setText(textarea, value) {
    if (!textarea) return;
    if (textarea.value !== value) textarea.value = value;
}

export function getValue(textarea) {
    return textarea ? textarea.value : "";
}

export function dispose(textarea) {
    const state = textarea?._mdEditor;
    if (state) {
        textarea.removeEventListener("keydown", state.onKeyDown);
        document.removeEventListener("selectionchange", state.onSelectionChange);
        state.root?.removeEventListener("mousedown", state.onToolbarMouseDown);
        delete textarea._mdEditor;
    }
}
