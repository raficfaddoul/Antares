window.downloadCsvFile = (fileName, base64Data) => {
    const bytes = Uint8Array.from(atob(base64Data), c => c.charCodeAt(0));
    const blob = new Blob([bytes], { type: "text/csv;charset=utf-8;" });

    const link = document.createElement("a");
    link.href = URL.createObjectURL(blob);
    link.download = fileName;
    link.click();
    URL.revokeObjectURL(link.href);
};

// Chunked CSV download functions
let csvChunks = [];
let csvFileName = '';

window.initChunkedDownload = function (fileName) {
    csvChunks = [];
    csvFileName = fileName;
};

window.appendChunk = function (chunk) {
    csvChunks.push(chunk);
};

window.finalizeChunkedDownload = function () {
    const blob = new Blob(csvChunks, { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = csvFileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
    csvChunks = [];
    csvFileName = '';
};