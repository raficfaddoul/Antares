// =============================================================
// downloads.js — Saving a file the page built: a Save As dialog where the browser has one, a plain download otherwise.
//
// Globals, loaded on every page by _Host.cshtml:
// - downloadTextFile(name, content): the journal CSV (Journal.razor) and the plan's DXF (SVGComponent);
// - downloadDxfWithImage(dxfName, content, imageUrl, imageName): the plan's DXF with its background picture;
// - fileTypeForName(name): the Save As file type for a name.
// Moved out of ImageTaging.js on 2026-09-17, unchanged.
// =============================================================

// File-type hint for the Save As dialog, derived from the suggested name so callers stay 2-arg.
window.fileTypeForName = function (name) {
    const ext = (name || '').toLowerCase().split('.').pop();
    if (ext === 'csv') return { description: 'CSV File', accept: { 'text/csv': ['.csv'] } };
    if (ext === 'txt') return { description: 'Text File', accept: { 'text/plain': ['.txt'] } };
    return { description: 'DXF Drawing File', accept: { 'application/dxf': ['.dxf'] } };
};

window.downloadTextFile = async function (suggestedName, content) {
    // Use native Save As dialog if supported (Chrome, Edge)
    if (window.showSaveFilePicker) {
        try {
            const handle = await window.showSaveFilePicker({
                suggestedName: suggestedName,
                types: [window.fileTypeForName(suggestedName)]
            });
            const writable = await handle.createWritable();
            await writable.write(content);
            await writable.close();
            return;
        } catch (e) {
            // User cancelled the dialog — do nothing
            if (e.name === 'AbortError') return;
            // Fall through to legacy download on any other error
        }
    }

    // Fallback for Firefox / Safari — auto-download with suggested name
    const blob = new Blob([content], { type: 'text/plain' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = suggestedName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
};




// Save a DXF together with the raster it references. DXF cannot embed image data — the IMAGE
// entity only stores a path — so the picture has to sit beside the .dxf or CAD shows an empty
// frame. A directory picker is used so both files land in the SAME folder; where that API is
// unavailable both are downloaded normally (which puts them in the download folder together).
window.downloadDxfWithImage = async function (fileName, content, imageUrl, imageName) {
    const dxfBlob = new Blob([content], { type: 'application/dxf' });

    function plainDownload(blob, name) {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url; a.download = name;
        document.body.appendChild(a); a.click(); document.body.removeChild(a);
        setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
    }

    // Two Save As dialogs rather than a directory picker: showDirectoryPicker refuses a lot of
    // ordinary locations ("this folder contains system files"), while Save As is far less
    // restricted. Both handles are requested BEFORE writing, so the browser still considers the
    // click active, and the second dialog is told to start in the first file's folder — so the
    // image defaults to landing right next to the .dxf.
    if (imageUrl && window.showSaveFilePicker) {
        let dxfHandle = null, imgHandle = null;
        try {
            dxfHandle = await window.showSaveFilePicker({
                suggestedName: fileName,
                types: [{ description: 'DXF Drawing File', accept: { 'application/dxf': ['.dxf'] } }]
            });
        } catch (e) {
            if (e && e.name === 'AbortError') return;   // cancelled — export nothing
        }

        if (dxfHandle) {
            try {
                imgHandle = await window.showSaveFilePicker({
                    suggestedName: imageName,
                    startIn: dxfHandle,                 // same folder as the .dxf
                    types: [{ description: 'Image JPEG', accept: { 'image/jpeg': ['.jpg', '.jpeg'] } }]
                });
            } catch (e) {
                imgHandle = null;                       // skipped — fall back to a download below
            }

            const w = await dxfHandle.createWritable();
            await w.write(dxfBlob);
            await w.close();

            try {
                const resp = await fetch(imageUrl);
                if (resp.ok) {
                    const blob = await resp.blob();
                    if (imgHandle) {
                        const iw = await imgHandle.createWritable();
                        await iw.write(blob);
                        await iw.close();
                    } else {
                        plainDownload(blob, imageName);
                    }
                } else {
                    console.warn('Background image not fetched:', resp.status);
                }
            } catch (e) {
                console.warn('Background image could not be copied:', e);
            }
            return;
        }
    }

    plainDownload(dxfBlob, fileName);
    if (imageUrl) {
        try {
            const resp = await fetch(imageUrl);
            if (resp.ok) plainDownload(await resp.blob(), imageName);
        } catch (e) {
            console.warn('Background image could not be downloaded:', e);
        }
    }
};
