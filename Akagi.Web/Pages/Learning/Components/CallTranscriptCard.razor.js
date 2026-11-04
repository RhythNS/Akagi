// Brings the transcript card into view and, when a line is given, scrolls the transcript to it.
export function reveal(card, lineIndex) {
    if (!card) return;
    try {
        const line = lineIndex >= 0 ? card.querySelector(`[data-line="${lineIndex}"]`) : null;
        if (line) {
            line.scrollIntoView({ behavior: 'smooth', block: 'center' });
        } else {
            card.scrollIntoView({ behavior: 'smooth', block: 'start' });
        }
    } catch {
        // no-op
    }
}
