// Focuses the first value field of a command form, or its Run button when the command takes no values
export function focusFirstField(panel) {
    if (!panel) return;
    try {
        const field = panel.querySelector('input:not([type=checkbox]), textarea')
            ?? panel.querySelector('[data-command-run]:not([disabled])');
        field?.focus();
    } catch {
        // no-op
    }
}
