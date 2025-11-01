export function scrollToBottom(el) {
    if (!el) return;
    try {
        el.scrollTop = el.scrollHeight;
    } catch {
        // no-op
    }
}
