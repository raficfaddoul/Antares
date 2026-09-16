var dygraphInstance1 = null; // Hold the dygraph instance
var dygraphInstance2 = null; // Hold the dygraph instance

var dygraphInstances = {};

window.dygraphInterop = {
    // Dictionary of Dygraph instances so we can handle multiple graphs
    dygraphInstances: {},

    // Initialize a Dygraph using a 2D array
    // data is expected to be an array of arrays: [[x1, y1, y2, ...], [x2, y1, y2, ...], ...]
    initializeDygraphArray: function (divId, data, labels) {
        const options = {
            labels: labels,         // e.g. ["Date", "Series A", "Series B", ...]
            showRoller: true,
            connectSeparatedPoints: true,
            legend: 'always',
            labelsSeparateLines: true,
            showRangeSelector: true,
            highlightCircleSize: 4,
            highlightSeriesOpts: {
                strokeWidth: 2,
                strokeBorderWidth: 1,
                highlightCircleSize: 5
            }
            // ... any other Dygraph options ...
        };

        // Create a new Dygraph instance and store it
        const elem = document.getElementById(divId);
        const g = new Dygraph(elem, data, options);
        this.dygraphInstances[divId] = g;
    },

    // Update the Dygraph data using a new 2D array
    updateDygraphArray: function (divId, data, labels) {
        const g = this.dygraphInstances[divId];
        if (g) {
            g.updateOptions({
                file: data,
                labels: labels
            });
        }
    }
};


function initializeDygraph(divid,xlabel, ylabel) {
    //dygraphInstance = new Dygraph(
    //    document.getElementById("graphdiv"),
    //    "Date,Temperature\n" // Initialize with an empty data set or some default data
    //);

    const options = {
        //customBars: true, sharmouta
        //drawPoints: true,
        //  valueRange: [0.0, 100],
        connectSeparatedPoints: true,
        showRoller: true,
        ylabel: ylabel,
        legend: 'always',
        labelsSeparateLines: true,
        // Render the series legend into a dedicated scrollable side panel when one exists
        // (IndicesInTimeComponent adds "<divid>_legend"); otherwise fall back to the overlay.
        labelsDiv: document.getElementById(divid + '_legend') || undefined,
        showRangeSelector: true,
        strokeWidth: 2.5,           // <-- thicker lines (default is 1.5)
        highlightCircleSize: 4,
        highlightSeriesOpts: {
            strokeWidth: 3.5,       // <-- even thicker on hover
            strokeBorderWidth: 1,
            highlightCircleSize: 5
        },
        highlightCallback: function(event, x, points, row, seriesName) {
            console.log(`Hovered on: ${seriesName}, X: ${x}, Y: ${points}`);
        }
    };


  //  if (divid == "graphdiv1") {

        headers = `${xlabel},${ylabel}\n`;
    g = new Dygraph(document.getElementById(divid), headers, options);
    dygraphInstances[divid] = g;

    // Observe the *parent* container � it is sized by the splitter layout,
    // not by Dygraph's canvas, so its width is always the correct available space.
    const chartElem = document.getElementById(divid);
    const observeTarget = chartElem.parentElement || chartElem;
    const resizeObserver = new ResizeObserver(() => {
        const instance = dygraphInstances[divid];
        if (instance) {
            instance.resize();
        }
    });
    resizeObserver.observe(observeTarget);
}

// Export a dygraph (plot + its side legend) to a JPEG download.
// Dygraph paints the series onto <canvas> but renders axis ticks, axis titles and the legend as
// positioned HTML, so a plain canvas.toDataURL() would lose all the text. This composites both:
// every canvas is drawn at its measured position, then each leaf text node is re-drawn with its
// computed font/colour. JPEG has no alpha, so the background is filled white first.
// Metadata shown in the exported image header. Pushed from Blazor whenever the plot changes.
window.setGraphExportMeta = function (meta) { window.__graphExportMeta = meta || null; };

window.exportDygraphToJpg = async function (divId, baseName) {
    try {
        const plot = document.getElementById(divId);
        if (!plot) return;

        // Capture the plot together with its legend panel when the flex wrapper is present.
        const root = plot.closest('.chart-flex-row') || plot;
        const box = root.getBoundingClientRect();
        if (!box.width || !box.height) return;

        const scale = 2;                       // render at 2x for a crisp image
        const pad = 14;
        const meta = window.__graphExportMeta || {};

        const now = new Date();
        const p2 = function (n) { return String(n).padStart(2, '0'); };
        const exportedAt = p2(now.getDate()) + '/' + p2(now.getMonth() + 1) + '/' + now.getFullYear()
                         + ' ' + p2(now.getHours()) + ':' + p2(now.getMinutes());

        // --- build the header lines -------------------------------------------------------------
        const measure = document.createElement('canvas').getContext('2d');
        const bodyFont = '13px Segoe UI, Arial, sans-serif';
        const maxTextWidth = box.width - 2 * pad;

        const wrap = function (label, text) {
            measure.font = bodyFont;
            const words = String(text).split(', ');
            const lines = [];
            let line = label;
            for (let i = 0; i < words.length; i++) {
                const candidate = (line === label ? line + words[i] : line + ', ' + words[i]);
                if (measure.measureText(candidate).width > maxTextWidth && line !== label) {
                    lines.push(line);
                    line = '    ' + words[i];
                } else {
                    line = candidate;
                }
            }
            lines.push(line);
            return lines;
        };

        const title = meta.structure ? ('Surveillance des capteurs — ' + meta.structure) : 'Surveillance des capteurs';
        const bodyLines = [];
        if (meta.start && meta.end) bodyLines.push('Période : ' + meta.start + '  →  ' + meta.end);
        bodyLines.push("Date d'export : " + exportedAt);
        if (meta.series && meta.series.length) {
            // "Sensor|channel" → "Sensor (channel)"
            const pretty = meta.series.map(function (s) {
                const i = String(s).indexOf('|');
                return i < 0 ? s : String(s).substring(0, i) + ' (' + String(s).substring(i + 1) + ')';
            });
            wrap('Capteurs : ', pretty.join(', ')).forEach(function (l) { bodyLines.push(l); });
        }

        const titleH = 22, lineH = 18;
        const headerH = pad + titleH + bodyLines.length * lineH + pad;

        const out = document.createElement('canvas');
        out.width = Math.round(box.width * scale);
        out.height = Math.round((box.height + headerH) * scale);
        const ctx = out.getContext('2d');
        ctx.scale(scale, scale);

        ctx.fillStyle = '#ffffff';
        ctx.fillRect(0, 0, box.width, box.height + headerH);

        // --- draw the header --------------------------------------------------------------------
        ctx.textAlign = 'left';
        ctx.textBaseline = 'top';
        ctx.fillStyle = '#2c3e50';
        ctx.font = '600 16px Segoe UI, Arial, sans-serif';
        ctx.fillText(title, pad, pad);

        ctx.fillStyle = '#555555';
        ctx.font = bodyFont;
        bodyLines.forEach(function (l, i) { ctx.fillText(l, pad, pad + titleH + i * lineH); });

        ctx.strokeStyle = '#dcdce4';
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.moveTo(pad, headerH - 6);
        ctx.lineTo(box.width - pad, headerH - 6);
        ctx.stroke();

        // Everything below is drawn beneath the header band.
        ctx.translate(0, headerH);

        root.querySelectorAll('canvas').forEach(function (c) {
            const r = c.getBoundingClientRect();
            if (r.width > 0 && r.height > 0) {
                try { ctx.drawImage(c, r.left - box.left, r.top - box.top, r.width, r.height); } catch (e) { }
            }
        });

        root.querySelectorAll('*').forEach(function (el) {
            if (el.children.length > 0) return;                 // leaf nodes only
            const txt = (el.textContent || '').trim();
            if (!txt) return;

            const cs = window.getComputedStyle(el);
            if (cs.display === 'none' || cs.visibility === 'hidden' || parseFloat(cs.opacity) === 0) return;

            const r = el.getBoundingClientRect();
            if (!r.width || !r.height) return;
            if (r.bottom < box.top || r.top > box.bottom) return;   // scrolled out of view

            const x = r.left - box.left;
            const y = r.top - box.top;

            ctx.save();
            ctx.fillStyle = cs.color;
            ctx.font = cs.fontStyle + ' ' + cs.fontWeight + ' ' + cs.fontSize + ' ' + cs.fontFamily;

            // Dygraph rotates the y-axis titles with a CSS transform.
            if (/ylabel|y2label/.test(el.className || '')) {
                ctx.translate(x + r.width / 2, y + r.height / 2);
                ctx.rotate(/y2label/.test(el.className) ? Math.PI / 2 : -Math.PI / 2);
                ctx.textAlign = 'center';
                ctx.textBaseline = 'middle';
                ctx.fillText(txt, 0, 0);
            } else {
                const align = cs.textAlign === 'right' ? 'right' : (cs.textAlign === 'center' ? 'center' : 'left');
                ctx.textAlign = align;
                ctx.textBaseline = 'top';
                const tx = align === 'right' ? x + r.width : (align === 'center' ? x + r.width / 2 : x);
                ctx.fillText(txt, tx, y);
            }
            ctx.restore();
        });

        const d = new Date();
        const p = function (n) { return String(n).padStart(2, '0'); };
        const stamp = d.getFullYear() + '-' + p(d.getMonth() + 1) + '-' + p(d.getDate())
                    + '_' + p(d.getHours()) + '-' + p(d.getMinutes());
        const fileName = (baseName || 'graphique').replace(/[\\/:*?"<>|]/g, '_') + '_' + stamp + '.jpg';

        // Ask WHERE to save first, while the click's user activation is still valid — the picker is
        // refused once the gesture expires. Chromium only; other browsers fall back to a plain
        // download (there "Save As" is governed by the browser's own download setting).
        let handle = null;
        if (window.showSaveFilePicker) {
            try {
                handle = await window.showSaveFilePicker({
                    suggestedName: fileName,
                    types: [{ description: 'Image JPEG', accept: { 'image/jpeg': ['.jpg', '.jpeg'] } }]
                });
            } catch (e) {
                if (e && e.name === 'AbortError') return;   // user cancelled the dialog
                handle = null;                              // not permitted → fall back below
            }
        }

        out.toBlob(async function (blob) {
            if (!blob) return;
            if (handle) {
                try {
                    const writable = await handle.createWritable();
                    await writable.write(blob);
                    await writable.close();
                    // Journaled only once the file is actually written — a cancelled dialog is not
                    // an export. The button uses a plain onclick (to keep the user activation the
                    // picker needs), so this callback is the only place the server learns about it.
                    if (window.journalExport) window.journalExport('export.jpg.plot', fileName, '');
                    return;
                } catch (e) {
                    console.error('Save failed, falling back to download:', e);
                }
            }
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = fileName;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
            if (window.journalExport) window.journalExport('export.jpg.plot', fileName, '');
        }, 'image/jpeg', 0.92);
    } catch (error) {
        console.error('Failed to export dygraph to JPG:', error);
    }
};

function parseInvariantDate(dateStr) {
    // Handle format: "yyyy-MM-dd HH:mm:ss.fff"
    // Convert to ISO format by replacing space with T
    if (dateStr && typeof dateStr === 'string') {
        // Replace space with T for ISO compatibility
        var isoStr = dateStr.replace(' ', 'T');
        // Ensure it's treated as UTC if no timezone specified
        if (!isoStr.endsWith('Z') && !isoStr.includes('+') && !isoStr.includes('-', 10)) {
            isoStr += 'Z';
        }
        return new Date(isoStr);
    }
    return new Date(dateStr);
}

function updateDygraph(divid, data, startTime) {
    try {
        const g = this.dygraphInstances[divid];
        if (g) {
            g.updateOptions({
                file: data,
            });

            if (startTime) {
                var lastRow = data.split("\n").slice(-2, -1)[0];
                var lastTimestamp = lastRow.split(",")[0];

                // Use the helper function for consistent parsing
                var endDate = parseInvariantDate(lastTimestamp);
                var startDate = parseInvariantDate(startTime);

                g.updateOptions({
                    dateWindow: [startDate.getTime(), endDate.getTime()]
                });
            }
        }
    } catch (error) {
        console.error("Failed to update dygraph:", error);
    }
}

function updateDygraphFast(divid, csvData) {
    try {
        const g = dygraphInstances[divid];
        if (g) {
            // Keep zoom functionality, just update data efficiently
            g.updateOptions({
                file: csvData
            });
        }
    } catch (error) {
        console.error("Failed to update dygraph:", error);
    }
}

function removeDygraph(divid) {
    try {
        //const g = this.dygraphInstances[divid];
        const g = document.getElementById(divid)
        if (g) {
            g.destroy(); // Clean up the Dygraph instance
            delete this.dygraphInstances[divid]; // Remove from the dictionary
        }
    } catch (error) {
        console.error("Failed to remove dygraph:", error);
    }
}

function updateTimeDygraph(divid,data) {
    
    try {
   
            if (dygraphInstance1) {
                dygraphInstance1.updateOptions({
                    'file': data
                });


                // Get the latest timestamp from the data
                var lastRow = data.split("\n").slice(-2, -1)[0]; // Get the second to last item (last is empty)
                var lastTimestamp = lastRow.split(",")[0];

                // Calculate the 30-second window
                var endDate = new Date(lastTimestamp);
                var startDate = new Date(endDate.getTime() - 10000); // 30 seconds before the last timestamp

                // Update the x-axis range to show only the 30-second window
                dygraphInstance1.updateOptions({
                    dateWindow: [startDate.getTime(), endDate.getTime()]
                });
            }
        
    } catch (error) { 
        console.error("Failed to update dygraph:", error);  
    }
}


function unzoomGraph() {
    g.updateOptions({
        dateWindow: null,
        valueRange: null
    });
}

function updateDygraphWithAxes(divid, csvData, seriesAxisMap, y1Label, y2Label) {
    try {
        const g = dygraphInstances[divid];
        if (g) {
            var options = { file: csvData };

            // Assign the axis EXPLICITLY for EVERY series (both 'y' and 'y2'). Setting only the y2
            // ones leaves a series that moved back to the left axis stuck on y2 (stale): its label
            // then shows on the left while its scale stays on the right (the deselect-first bug).
            var seriesOpts = {};
            var hasY2 = false;
            if (seriesAxisMap) {
                for (var seriesName in seriesAxisMap) {
                    var axis = (seriesAxisMap[seriesName] === 'y2') ? 'y2' : 'y';
                    seriesOpts[seriesName] = { axis: axis };
                    if (axis === 'y2') hasY2 = true;
                }
            }
            options.series = seriesOpts;

            options.ylabel = y1Label || '';
            options.y2label = hasY2 ? (y2Label || '') : '';
            // Only declare y2 when a series actually uses it. Declaring an axis with no series
            // attached makes the range selector crash (computeCombinedSeriesAndLimits_ reads the
            // axis' undefined series list), so single-axis plots must omit y2 entirely.
            options.axes = hasY2
                ? { y: { independentTicks: true }, y2: { independentTicks: true } }
                : { y: { independentTicks: true } };

            g.updateOptions(options);
        }
    } catch (error) {
        console.error("Failed to update dygraph with axes:", error);
    }
}

