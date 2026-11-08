// Enter answers unless the IME is still converting; holding Shift shows furigana for a moment.
// Listens on the page's root, so the answer box can come and go between questions.
export function attach(root, dotnet) {
    if (!root || root.__conjugation) {
        return;
    }
    const onKey = (e) => {
        const input = e.target;
        if (!input || !input.matches || !input.matches('input[data-answer]')) {
            return;
        }
        if (e.key === 'Enter' && !e.isComposing && e.keyCode !== 229) {
            e.preventDefault();
            dotnet.invokeMethodAsync('SubmitFromKeyboard', input.value);
        }
    };
    const peek = (e) => {
        if (e.key === 'Shift') {
            root.classList.toggle('furigana-peek', e.type === 'keydown');
        }
    };
    const clear = () => root.classList.remove('furigana-peek');
    root.addEventListener('keydown', onKey);
    window.addEventListener('keydown', peek);
    window.addEventListener('keyup', peek);
    window.addEventListener('blur', clear);
    root.__conjugation = { onKey, peek, clear };
}

export function detach(root) {
    const state = root && root.__conjugation;
    if (!state) {
        return;
    }
    root.removeEventListener('keydown', state.onKey);
    window.removeEventListener('keydown', state.peek);
    window.removeEventListener('keyup', state.peek);
    window.removeEventListener('blur', state.clear);
    delete root.__conjugation;
}

export function focus(input) {
    try {
        input && input.focus();
    } catch {
        // The input can be gone while the page changes
    }
}
