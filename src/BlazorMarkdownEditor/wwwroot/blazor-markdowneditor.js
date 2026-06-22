// BlazorMarkdownEditor - JS interop module.
// Deliberately minimal: it only does what C#/Blazor cannot do directly, namely
// read & write the <textarea> selection, intercept keys synchronously so we
// can preventDefault(), and maintain an undo/redo history. All markdown
// transformations happen in C#.
//
// Why a custom history? The textarea is uncontrolled (JS owns its value to
// preserve the caret). Toolbar commands and external updates assign
// `textarea.value` directly, which wipes the browser's native undo stack and
// would otherwise make Ctrl+Z behave erratically. Owning the history here keeps
// undo/redo consistent across typing, toolbar commands and keyboard shortcuts.

const LIST_LINE = /^(\s*)([-*+] (\[[ xX]\] )?|\d+[.)] )/;

// Maximum number of states kept per direction.
const HISTORY_LIMIT = 200;
// Consecutive keystrokes within this window are coalesced into one undo step.
const TYPING_PAUSE_MS = 100;

function currentLine(textarea) {
    const value = textarea.value;
    const pos = textarea.selectionStart;
    const start = value.lastIndexOf("\n", pos - 1) + 1;
    let end = value.indexOf("\n", pos);
    if (end < 0) end = value.length;
    return value.slice(start, end);
}

// ---- history ----------------------------------------------------------------

function snapshot(textarea) {
    return {
        text: textarea.value,
        selStart: textarea.selectionStart,
        selEnd: textarea.selectionEnd
    };
}

function notifyHistory(textarea) {
    const state = textarea._mdEditor;
    if (!state) return;
    const canUndo = state.undo.length > 0;
    const canRedo = state.redo.length > 0;
    if (canUndo === state.canUndo && canRedo === state.canRedo) return;
    state.canUndo = canUndo;
    state.canRedo = canRedo;
    state.dotNetRef.invokeMethodAsync("OnHistoryChanged", canUndo, canRedo);
}

function pushUndo(textarea, snap) {
    const state = textarea._mdEditor;
    state.undo.push(snap);
    if (state.undo.length > HISTORY_LIMIT) state.undo.shift();
}

function endTypingGroup(textarea) {
    const state = textarea._mdEditor;
    state.typingActive = false;
    if (state.typingTimer) {
        clearTimeout(state.typingTimer);
        state.typingTimer = null;
    }
}

// Records the pre-change state before a programmatic edit (a toolbar command).
function commitForChange(textarea, preSnap) {
    const state = textarea._mdEditor;
    endTypingGroup(textarea);
    pushUndo(textarea, preSnap);
    state.redo = [];
}

// Captures undo history for free-form typing, coalescing rapid keystrokes into
// a single step. The first keystroke of a burst records the state that existed
// before it; subsequent keystrokes only refresh the baseline.
function recordTyping(textarea) {
    const state = textarea._mdEditor;
    if (!state.typingActive) {
        pushUndo(textarea, state.baseline);
        state.redo = [];
        state.typingActive = true;
        notifyHistory(textarea);
    }
    if (state.typingTimer) clearTimeout(state.typingTimer);
    state.typingTimer = setTimeout(() => {
        state.typingActive = false;
        state.typingTimer = null;
    }, TYPING_PAUSE_MS);
    state.baseline = snapshot(textarea);
}

// Writes a snapshot back to the textarea without feeding the change into the
// history (suppress = true), while still letting Blazor's binding update.
function applySnapshot(textarea, snap) {
    const state = textarea._mdEditor;
    state.suppress = true;
    textarea.value = snap.text;
    textarea.dispatchEvent(new Event("input", { bubbles: true }));
    state.suppress = false;
    textarea.focus();
    const max = snap.text.length;
    textarea.setSelectionRange(Math.min(snap.selStart, max), Math.min(snap.selEnd, max));
    saveSelection(textarea);
    state.baseline = snapshot(textarea);
}

function undo(textarea) {
    const state = textarea?._mdEditor;
    if (!state || textarea.readOnly || !state.undo.length) return;
    endTypingGroup(textarea);
    state.redo.push(state.baseline);
    if (state.redo.length > HISTORY_LIMIT) state.redo.shift();
    applySnapshot(textarea, state.undo.pop());
    notifyHistory(textarea);
}

function redo(textarea) {
    const state = textarea?._mdEditor;
    if (!state || textarea.readOnly || !state.redo.length) return;
    endTypingGroup(textarea);
    pushUndo(textarea, state.baseline);
    applySnapshot(textarea, state.redo.pop());
    notifyHistory(textarea);
}

// ---- command application -----------------------------------------------------

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

    // Record the state before the command so it can be undone as one step.
    commitForChange(textarea, { text: value, selStart: start, selEnd: end });
    applyResult(textarea, result);
}

function applyResult(textarea, result) {
    const state = textarea._mdEditor;
    state.suppress = true;
    textarea.value = result.text;
    // Let Blazor's two-way binding pick up the new value.
    textarea.dispatchEvent(new Event("input", { bubbles: true }));
    state.suppress = false;
    textarea.focus();
    textarea.setSelectionRange(result.selectionStart, result.selectionEnd);
    saveSelection(textarea);
    state.baseline = snapshot(textarea);
    notifyHistory(textarea);
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
        // Undo / redo. Ctrl/Cmd+Z, Ctrl/Cmd+Shift+Z and Ctrl/Cmd+Y.
        if (key === "z" && !e.shiftKey) { e.preventDefault(); undo(textarea); return; }
        if ((key === "z" && e.shiftKey) || (key === "y" && !e.shiftKey)) { e.preventDefault(); redo(textarea); return; }
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
        // Undo / redo history.
        undo: [],
        redo: [],
        baseline: snapshot(textarea),
        typingActive: false,
        typingTimer: null,
        suppress: false,
        canUndo: false,
        canRedo: false,
        onInput: () => {
            // Programmatic edits (commands, undo/redo, external sets) manage
            // their own history, so only free-form typing is recorded here.
            if (!state.suppress) recordTyping(textarea);
        },
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
    textarea.addEventListener("input", state.onInput);
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

export function undoCommand(textarea) {
    undo(textarea);
}

export function redoCommand(textarea) {
    redo(textarea);
}

export function focus(textarea) {
    textarea?.focus();
}

// Push an externally-changed value into the (uncontrolled) textarea without
// firing the input event, so we don't loop back into Blazor.
export function setText(textarea, value) {
    if (!textarea) return;
    if (textarea.value !== value) textarea.value = value;
    const state = textarea._mdEditor;
    if (state) {
        // External assignment becomes the new baseline; in-flight typing groups
        // are closed so the next keystroke starts a fresh undo step.
        endTypingGroup(textarea);
        state.baseline = snapshot(textarea);
    }
}

export function getValue(textarea) {
    return textarea ? textarea.value : "";
}

export function dispose(textarea) {
    const state = textarea?._mdEditor;
    if (state) {
        if (state.typingTimer) clearTimeout(state.typingTimer);
        textarea.removeEventListener("keydown", state.onKeyDown);
        textarea.removeEventListener("input", state.onInput);
        document.removeEventListener("selectionchange", state.onSelectionChange);
        state.root?.removeEventListener("mousedown", state.onToolbarMouseDown);
        delete textarea._mdEditor;
    }
}
