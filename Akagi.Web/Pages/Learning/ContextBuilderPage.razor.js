// The context builder keeps only what to export in this browser; the text is written fresh from the bot each time.
export function load(key) {
    try {
        return localStorage.getItem(key);
    } catch {
        return null;
    }
}

export function save(key, value) {
    try {
        localStorage.setItem(key, value);
    } catch {
        // Storage can be full or blocked; the page still works without it
    }
}

export function download(filename, content) {
    try {
        const blob = new Blob([content], { type: 'text/plain;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        a.remove();
        URL.revokeObjectURL(url);
    } catch {
        // no-op
    }
}

export async function copy(content) {
    try {
        await navigator.clipboard.writeText(content);
        return true;
    } catch {
        return false;
    }
}

// Firefox only starts dragging when the drag carries data.
export function enableDrag(list) {
    if (list && !list.dataset.dragReady) {
        list.dataset.dragReady = '1';
        list.addEventListener('dragstart', e => {
            try {
                e.dataTransfer.effectAllowed = 'move';
                e.dataTransfer.setData('text/plain', '');
            } catch {
                // no-op
            }
        });
    }
}
