// Dragging Gantt bars: the right end changes the work days (half day steps), the bar itself moves the earliest start
// (whole days). The bar follows the pointer; when it is let go .NET gets the new calendar positions and plans again.
export function attach(root, dotnet) {
    if (!root || root.__gantt) {
        return;
    }
    let drag = null;
    let suppressClick = false;

    const dayWidth = () => parseFloat(root.getAttribute('data-day')) || 22;
    const from = () => parseFloat(root.getAttribute('data-from')) || 0;

    const onPointerDown = (e) => {
        if (e.button !== 0) {
            return;
        }
        const bar = e.target.closest && e.target.closest('.gantt-bar[data-movable="true"]');
        if (!bar || !root.contains(bar)) {
            return;
        }
        const resize = !!e.target.closest('.gantt-handle');
        drag = {
            bar,
            id: bar.getAttribute('data-bar'),
            mode: resize ? 'resize' : 'move',
            startX: e.clientX,
            left: parseFloat(bar.style.left) || 0,
            width: parseFloat(bar.style.width) || 0,
            moved: false
        };
        bar.setPointerCapture(e.pointerId);
        e.preventDefault();
    };

    const onPointerMove = (e) => {
        if (!drag) {
            return;
        }
        const dx = e.clientX - drag.startX;
        if (!drag.moved && Math.abs(dx) < 4) {
            return;
        }
        drag.moved = true;
        const day = dayWidth();
        drag.bar.classList.add('dragging');
        if (drag.mode === 'resize') {
            const half = day / 2;
            const width = Math.max(half, Math.round((drag.width + dx) / half) * half);
            drag.bar.style.width = width + 'px';
        } else {
            const left = drag.left + Math.round(dx / day) * day;
            drag.bar.style.left = left + 'px';
        }
    };

    const onPointerUp = (e) => {
        if (!drag) {
            return;
        }
        const current = drag;
        drag = null;
        current.bar.classList.remove('dragging');
        if (current.bar.hasPointerCapture && current.bar.hasPointerCapture(e.pointerId)) {
            current.bar.releasePointerCapture(e.pointerId);
        }
        if (!current.moved) {
            return;
        }
        suppressClick = true;
        const day = dayWidth();
        const left = parseFloat(current.bar.style.left) || 0;
        const width = parseFloat(current.bar.style.width) || 0;
        const start = left / day + from();
        const end = (left + width) / day + from();
        if (Math.abs(left - current.left) < 0.5 && Math.abs(width - current.width) < 0.5) {
            return;
        }
        dotnet.invokeMethodAsync('BarDragged', current.id, current.mode, start, end);
    };

    // The click that ends a drag should not open the ticket
    const onClick = (e) => {
        if (suppressClick) {
            suppressClick = false;
            e.stopPropagation();
            e.preventDefault();
        }
    };

    root.addEventListener('pointerdown', onPointerDown);
    root.addEventListener('pointermove', onPointerMove);
    root.addEventListener('pointerup', onPointerUp);
    root.addEventListener('pointercancel', onPointerUp);
    root.addEventListener('click', onClick, true);
    root.__gantt = { onPointerDown, onPointerMove, onPointerUp, onClick };
}

export function detach(root) {
    const state = root && root.__gantt;
    if (!state) {
        return;
    }
    root.removeEventListener('pointerdown', state.onPointerDown);
    root.removeEventListener('pointermove', state.onPointerMove);
    root.removeEventListener('pointerup', state.onPointerUp);
    root.removeEventListener('pointercancel', state.onPointerUp);
    root.removeEventListener('click', state.onClick, true);
    delete root.__gantt;
}
