const _fileHandles = new Map();
let _handleCounter = 0;

// Called FIRST in the click handler, while the click's transient activation is still
// valid (a Blazor Server round-trip would otherwise lose the user gesture). Shows the
// native Save-As dialog so the user picks the folder + filename.
// Returns: >0 = handle id (location chosen), 0 = API unsupported (caller falls back to
// a normal download), -1 = user cancelled.
window.openSaveDialog = async (fileName) => {
    if (!window.showSaveFilePicker) return 0; // Firefox/Safari or non-secure context
    try {
        const handle = await window.showSaveFilePicker({
            suggestedName: fileName,
            types: [{ description: 'SQL Server Backup', accept: { 'application/octet-stream': ['.bak'] } }]
        });
        const id = ++_handleCounter;
        _fileHandles.set(id, handle);
        return id;
    } catch (e) {
        return -1; // user cancelled the dialog
    }
};

// Streams the server response straight into the previously chosen file handle, chunk by
// chunk — the whole .bak is never held in browser memory.
window.streamToHandle = async (handleId, url) => {
    const handle = _fileHandles.get(handleId);
    _fileHandles.delete(handleId);
    if (!handle) throw new Error('Save location was lost. Please try again.');

    const response = await fetch(url, { credentials: 'same-origin' });
    if (!response.ok) throw new Error('Download failed: HTTP ' + response.status);

    const writable = await handle.createWritable();
    await response.body.pipeTo(writable); // aborts + discards the partial file on error
};

// Fallback for browsers without the File System Access API: a plain browser download
// into the default folder. The server still streams the file (Content-Disposition:
// attachment), so nothing is buffered server-side.
window.triggerFileDownload = (url) => {
    const link = document.createElement('a');
    link.href = url;
    link.download = 'AliantWeb.bak'; // header filename takes precedence; fallback hint
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
};

// Copies text to the clipboard. Uses the async Clipboard API on secure contexts
// (HTTPS/localhost) and falls back to a hidden textarea + execCommand on plain HTTP.
window.copyTextToClipboard = async (text) => {
    try {
        if (navigator.clipboard && window.isSecureContext) {
            await navigator.clipboard.writeText(text);
            return true;
        }
    } catch (e) { /* fall through to the legacy path */ }
    try {
        const ta = document.createElement('textarea');
        ta.value = text;
        ta.style.position = 'fixed';
        ta.style.top = '-1000px';
        ta.style.opacity = '0';
        document.body.appendChild(ta);
        ta.focus();
        ta.select();
        const ok = document.execCommand('copy');
        document.body.removeChild(ta);
        return ok;
    } catch (e) {
        return false;
    }
};
