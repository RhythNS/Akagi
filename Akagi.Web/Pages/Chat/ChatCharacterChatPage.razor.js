// Keeps the chat at its newest message while the user is reading there. Whether they are is decided by how they
// scroll, not by measuring at the moment a message arrives: scrolling up leaves the bottom, scrolling back down to it
// (or sending a message) returns there. While at the bottom, every growth of the thread (a new bubble, a streamed
// chunk, markdown or an image laying out late) jumps straight down. Jumps are instant; a smooth scroll still under way
// when the next message arrives used to look like "scrolled up" and the chat stopped following.
const threshold = 120;
const followers = new WeakMap();

function atBottom(el) {
    return el.scrollHeight - el.scrollTop - el.clientHeight <= threshold;
}

function jump(el, state) {
    el.scrollTop = el.scrollHeight;
    state.lastTop = el.scrollTop;
}

export function followBottom(el) {
    if (!el || followers.has(el)) return;
    try {
        const state = { pinned: true, lastTop: el.scrollTop };
        el.addEventListener('scroll', () => {
            const top = el.scrollTop;
            if (atBottom(el)) {
                state.pinned = true;
            } else if (top < state.lastTop - 1) {
                // Only moving up leaves the bottom; content growing under a pinned view does not
                state.pinned = false;
            }
            state.lastTop = top;
        }, { passive: true });
        const observer = new ResizeObserver(() => {
            if (state.pinned) jump(el, state);
        });
        observer.observe(el);
        for (const child of el.children) observer.observe(child);
        followers.set(el, state);
        jump(el, state);
    } catch {
        // no-op
    }
}

export function scrollToBottom(el, force = false) {
    if (!el) return;
    try {
        const state = followers.get(el);
        if (!state) {
            if (force || atBottom(el)) el.scrollTop = el.scrollHeight;
            return;
        }
        if (force) state.pinned = true;
        if (state.pinned) jump(el, state);
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

const recentCommandsKey = 'akagi.recentCommands';

export function loadRecentCommands() {
    try {
        const stored = JSON.parse(localStorage.getItem(recentCommandsKey) ?? '[]');
        return Array.isArray(stored) ? stored.filter(x => typeof x === 'string') : [];
    } catch {
        return [];
    }
}

export function saveRecentCommands(names) {
    try {
        localStorage.setItem(recentCommandsKey, JSON.stringify(names));
    } catch {
        // no-op
    }
}
