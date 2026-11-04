// Saves a table as a CSV file. The byte order mark makes spreadsheet programs read the Japanese as UTF-8.
export function downloadCsv(filename, content) {
    try {
        const blob = new Blob(['﻿' + content], { type: 'text/csv;charset=utf-8' });
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
