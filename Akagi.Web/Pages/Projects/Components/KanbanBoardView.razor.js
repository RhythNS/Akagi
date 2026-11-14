// Firefox only starts dragging when the drag carries data; Enter in a new ticket's title adds it instead of a line break.
export function attach(root) {
    if (!root || root.__kanban) {
        return;
    }
    const onDragStart = (e) => {
        const card = e.target.closest && e.target.closest('[data-ticket]');
        if (card && e.dataTransfer) {
            e.dataTransfer.setData('text/plain', card.getAttribute('data-ticket'));
            e.dataTransfer.effectAllowed = 'move';
        }
    };
    const onKeyDown = (e) => {
        if (e.key === 'Enter' && !e.shiftKey && !e.isComposing && e.target.matches && e.target.matches('textarea[data-new-title]')) {
            e.preventDefault();
        }
    };
    root.addEventListener('dragstart', onDragStart);
    root.addEventListener('keydown', onKeyDown);
    root.__kanban = { onDragStart, onKeyDown };
}
