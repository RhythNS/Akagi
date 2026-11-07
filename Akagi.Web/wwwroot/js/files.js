// Saves bytes streamed from the server as a file the browser downloads.
export async function downloadStream(fileName, streamRef) {
    const buffer = await streamRef.arrayBuffer();
    const blob = new Blob([buffer], { type: 'application/octet-stream' });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}

// Lets an <audio> element play mp3 bytes streamed from the server.
export async function setAudio(element, streamRef) {
    const buffer = await streamRef.arrayBuffer();
    const url = URL.createObjectURL(new Blob([buffer], { type: 'audio/mpeg' }));
    if (element.dataset.blobUrl) {
        URL.revokeObjectURL(element.dataset.blobUrl);
    }
    element.dataset.blobUrl = url;
    element.src = url;
}
