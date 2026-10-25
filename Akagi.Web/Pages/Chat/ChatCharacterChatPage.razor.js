export function scrollToBottom(el, force = false) {
    if (!el) return;
    try {
        const threshold = 120;
        const isNearBottom = el.scrollHeight - el.scrollTop - el.clientHeight <= threshold;
        if (force || isNearBottom) {
            el.scrollTo({ top: el.scrollHeight, behavior: 'smooth' });
        }
    } catch {
        // no-op
    }
}

export function downloadTextFile(filename, content) {
    try {
        const blob = new Blob([content], { type: 'text/markdown;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    } catch {
        // no-op
    }
}
