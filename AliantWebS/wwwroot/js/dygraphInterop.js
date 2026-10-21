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
        // A redraw (new data, zoom, resize) clears dygraph's highlight, so the curve tooltip goes too, and the
        // point menu, whose point may no longer be drawn.
        drawCallback: function () {
            hideCurveTooltip(divid);
            hidePointMenu(divid);
        }
    };


  //  if (divid == "graphdiv1") {

        headers = `${xlabel},${ylabel}\n`;
    g = new Dygraph(document.getElementById(divid), headers, options);
    dygraphInstances[divid] = g;
    attachCurveTooltip(g, divid);

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

// Tooltip naming the curve under the mouse pointer. The pointer is on a curve when it is within
// CURVE_TOOLTIP_DISTANCE pixels of the line as drawn: the segments, not only the data points, since a
// sparse series is drawn with long segments. The curve found is also the highlighted one, so the tooltip
// and the thickened line never disagree (left alone, dygraph highlights the series owning the nearest
// data point, which near crossings or on long segments is another curve).
// Reads dygraph's internals (layout_.points, as its own findClosestPoint does, and canvas_), so it is
// tied to the dygraph version loaded in _Host.cshtml (2.2.1).
var CURVE_TOOLTIP_DISTANCE = 7;

function hideCurveTooltip(divid) {
    const tip = document.getElementById(divid + '_curve_tooltip');
    if (tip) tip.style.display = 'none';
}

function attachCurveTooltip(g, divid) {
    // In <body> with position:fixed so the splitter panes' overflow cannot clip it, and so the JPG
    // export, which captures only the chart's own elements, never draws it. Reused when the graph is
    // created again under the same id (switching tabs).
    const tipId = divid + '_curve_tooltip';
    let tip = document.getElementById(tipId);
    if (!tip) {
        tip = document.createElement('div');
        tip.id = tipId;
        tip.style.cssText = 'position:fixed; z-index:10000; pointer-events:none; display:none;'
            + ' background:rgba(33,37,41,0.92); color:#fff; padding:4px 8px; border-radius:4px;'
            + ' font:12px "Segoe UI", Arial, sans-serif; white-space:nowrap; box-shadow:0 2px 6px rgba(0,0,0,0.25);';
        document.body.appendChild(tip);
    }
    tip.style.display = 'none';
    tip.dataset.content = '';   // label and colour the content was last built for

    const elem = document.getElementById(divid);

    // Dygraph's mousemove handler selects the row and the series of the point findClosestPoint returns:
    // the row gives the hover dot and the legend's time and values, the series the thickened line.
    // Answering with the curve under the pointer, and with that curve's own nearest sample, makes dygraph
    // select exactly what the tooltip names, once per move. With each sensor at its own timestamps, the
    // row of another sensor's sample would carry no value for this curve, leaving it out of the legend.
    // Only that handler calls findClosestPoint, and only with highlightSeriesOpts set, as
    // initializeDygraph does. The curve found is handed on to the handler below: dygraph's listens on its
    // canvas, so it runs first for the same event.
    let curveOfThisMove = null;
    const findClosestPoint = g.findClosestPoint;
    g.findClosestPoint = function (domX, domY) {
        const curve = findCurveAt(g, domX, domY);
        curveOfThisMove = curve;
        return curve
            ? { row: curve.point.idx, seriesName: curve.name, point: curve.point }
            : findClosestPoint.call(this, domX, domY);
    };

    elem.addEventListener('mouseleave', function () {
        if (dygraphInstances[divid] === g) hideCurveTooltip(divid);
    });
    elem.addEventListener('mousemove', function (e) {
        const curve = curveOfThisMove;
        curveOfThisMove = null;

        // The ids are fixed ("g3", "graphdiv1"): once a newer graph has taken this id, possibly on this
        // very element, only that graph's handler may drive the shared tooltip.
        if (dygraphInstances[divid] !== g) return;

        // No curve when dygraph did not handle this move itself (the pointer is over its legend box or
        // the roller rather than its canvas), or when a button is pressed: a zoom or pan drag, where the
        // tooltip would sit in the way.
        if (!curve || e.target !== g.canvas_ || e.buttons) {
            hideCurveTooltip(divid);
            return;
        }

        // Keyed on the colour too: dygraph recolours every series when the set of series changes. And on the
        // point's note, which changes from one point of the curve to the next.
        const name = curve.name;
        const props = g.getPropertiesForSeries(name);
        const note = pointNote(g, name, curve.point.xval);
        const content = name + '\n' + (props ? props.color : '') + '\n' + (note || '');
        if (tip.dataset.content !== content) {
            fillCurveTooltip(tip, name, props, note);
            tip.dataset.content = content;
        }

        tip.style.display = 'block';
        const offset = 14;
        let left = e.clientX + offset;
        let top = e.clientY + offset;
        if (left + tip.offsetWidth > window.innerWidth - 4) left = e.clientX - offset - tip.offsetWidth;
        if (top + tip.offsetHeight > window.innerHeight - 4) top = e.clientY - offset - tip.offsetHeight;
        tip.style.left = Math.max(4, left) + 'px';
        tip.style.top = Math.max(4, top) + 'px';

        watchCurveTooltip(tip, elem);
    });
}

// A chart removed while the pointer rests on one of its curves (browser Back, a tab switched from the
// keyboard) gets no mouseleave, and the tooltip, which lives in <body>, would stay on the next page. So
// while it is shown, each frame checks that its chart is still in the document.
function watchCurveTooltip(tip, elem) {
    tip.chartElement = elem;
    if (tip.watching) return;
    tip.watching = true;

    const check = function () {
        if (tip.style.display !== 'none' && !tip.chartElement.isConnected) tip.style.display = 'none';
        if (tip.style.display === 'none') {
            tip.watching = false;
            tip.chartElement = null;
            return;
        }
        requestAnimationFrame(check);
    };
    requestAnimationFrame(check);
}

// Series labels on the surveillance plot are "Sensor|channel": the sensor is the tooltip's title and
// the channel goes under it. Any other label is shown whole. The point's note, if any, comes last.
// Built with textContent, never as HTML.
function fillCurveTooltip(tip, label, props, note) {
    tip.textContent = '';
    const sep = label.lastIndexOf('|');

    const title = document.createElement('div');
    const swatch = document.createElement('span');
    swatch.style.cssText = 'display:inline-block; width:12px; height:3px; margin-right:6px; vertical-align:middle;'
        + ' background:' + ((props && props.color) || '#fff') + ';';
    const sensor = document.createElement('strong');
    sensor.textContent = sep < 0 ? label : label.substring(0, sep);
    title.appendChild(swatch);
    title.appendChild(sensor);
    tip.appendChild(title);

    if (sep >= 0) {
        const channel = document.createElement('div');
        channel.style.cssText = 'opacity:0.8; font-size:11px; padding-left:18px;';
        channel.textContent = label.substring(sep + 1);
        tip.appendChild(channel);
    }

    if (note) {
        const noteLine = document.createElement('div');
        noteLine.style.cssText = 'font-size:11px; padding-left:18px;';
        noteLine.textContent = note;
        tip.appendChild(noteLine);
    }
}

// Right-click menu on a curve, offering to delete the point under the pointer from InfluxDB (the Surveillance plot,
// Developer role). IndicesInTimeComponent turns it on for a graph with the .NET object that takes the choice, or off
// with null. The menu only offers: the server traces the point back to its readings, checks the role and asks for
// confirmation. Away from the curves, with Shift, or with the menu already open, the right-click is left to the
// browser, whose own menu copies or saves the image.
function setPointDeleteMenu(divid, dotnetRef) {
    const g = dygraphInstances[divid];
    if (!g) throw new Error('graph ' + divid + ' does not exist');
    g.pointDeleteTarget = dotnetRef || null;
    if (!g.pointDeleteTarget) hidePointMenu(divid);
    if (g.pointDeleteMenuWired) return;
    g.pointDeleteMenuWired = true;

    const elem = document.getElementById(divid);
    elem.addEventListener('contextmenu', function (e) {
        if (dygraphInstances[divid] !== g || !g.pointDeleteTarget) return;
        if (e.shiftKey || pointMenuOpen(divid)) {
            hidePointMenu(divid);
            return;
        }
        if (e.target !== g.canvas_) return;

        const pos = g.eventToDomCoords(e);
        const curve = findCurveAt(g, pos[0], pos[1]);
        if (!curve) return;

        e.preventDefault();
        hideCurveTooltip(divid);
        showPointMenu(g, divid, curve, e.clientX, e.clientY);
    });
}

function pointMenuOpen(divid) {
    const menu = document.getElementById(divid + '_point_menu');
    return !!menu && menu.style.display !== 'none';
}

function hidePointMenu(divid) {
    const menu = document.getElementById(divid + '_point_menu');
    if (menu) menu.style.display = 'none';
}

// The x of a date axis as the CSV's first column wrote it (IndicesInTimeComponent.CsvX, "yyyy-MM-dd HH:mm:ss.fff").
// dygraph read that text as local time, so local time writes it back.
function csvDateText(ms) {
    const d = new Date(ms);
    const p = function (n, width) { return String(n).padStart(width || 2, '0'); };
    return d.getFullYear() + '-' + p(d.getMonth() + 1) + '-' + p(d.getDate()) + ' '
        + p(d.getHours()) + ':' + p(d.getMinutes()) + ':' + p(d.getSeconds()) + '.' + p(d.getMilliseconds(), 3);
}

// The menu for one curve's point, in <body> like the curve tooltip so the splitter panes cannot clip it. Its text
// is set with textContent, never as HTML.
function showPointMenu(g, divid, curve, clientX, clientY) {
    const menuId = divid + '_point_menu';
    let menu = document.getElementById(menuId);
    if (!menu) {
        menu = document.createElement('div');
        menu.id = menuId;
        menu.className = 'dygraph-point-menu';
        menu.setAttribute('role', 'menu');
        document.body.appendChild(menu);

        // Closed by a press anywhere else, Escape, a scroll or leaving the window. Capture, so that a handler
        // stopping the event further down cannot keep it open. A right press on the chart is left to its
        // contextmenu handler, which must still see the menu open to hand that right-click to the browser.
        document.addEventListener('mousedown', function (e) {
            if (menu.contains(e.target)) return;
            const chart = document.getElementById(divid);
            if (e.button === 2 && chart && chart.contains(e.target)) return;
            hidePointMenu(divid);
        }, true);
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') hidePointMenu(divid);
        }, true);
        document.addEventListener('wheel', function () { hidePointMenu(divid); }, true);
        document.addEventListener('scroll', function () { hidePointMenu(divid); }, true);
        window.addEventListener('blur', function () { hidePointMenu(divid); });
    }

    const name = curve.name;
    const sep = name.lastIndexOf('|');
    const x = csvDateText(curve.point.xval);
    const y = curve.point.yval;
    const parse = g.getOption('xValueParser');

    menu.textContent = '';
    const title = document.createElement('div');
    title.className = 'dygraph-point-menu-title';
    const label = document.createElement('strong');
    label.textContent = sep < 0 ? name : name.substring(0, sep) + ' — ' + name.substring(sep + 1);
    const at = document.createElement('div');
    at.textContent = x.substring(8, 10) + '/' + x.substring(5, 7) + '/' + x.substring(0, 4) + ' ' + x.substring(11, 19)
        + '  ·  ' + Number(y.toPrecision(6));
    title.appendChild(label);
    title.appendChild(at);
    menu.appendChild(title);

    const item = document.createElement('button');
    item.type = 'button';
    item.className = 'dygraph-point-menu-item';
    item.setAttribute('role', 'menuitem');
    item.textContent = 'Supprimer ce point de la base…';
    item.addEventListener('click', function () {
        hidePointMenu(divid);
        const target = g.pointDeleteTarget;
        // The graph replaced or turned off since the menu opened: its point is gone with it.
        if (!target || dygraphInstances[divid] !== g || !g.graphDiv.isConnected) {
            alert("Le graphique a changé depuis l'ouverture du menu : rien n'a été supprimé.");
            return;
        }
        // Only a date axis names its points this way. A point whose text still names another instant (a CSV time
        // in the hour a clock change skips) is refused by the server, which wants that text and that value.
        if (!parse || parse(x) !== curve.point.xval) {
            alert("Ce point ne peut pas être désigné sans ambiguïté : rien n'a été supprimé.");
            return;
        }
        target.invokeMethodAsync('OnPointDeleteChosen', name, x, y).catch(function (err) {
            console.error('Point delete request failed:', err);
            alert("La demande de suppression n'a pas pu être envoyée : rien n'a été supprimé.");
        });
    });
    menu.appendChild(item);

    const hint = document.createElement('div');
    hint.className = 'dygraph-point-menu-hint';
    hint.textContent = "Clic droit à nouveau (ou Maj + clic droit) : menu du navigateur, pour copier ou enregistrer l'image.";
    menu.appendChild(hint);

    menu.style.display = 'block';
    let left = clientX, top = clientY;
    if (left + menu.offsetWidth > window.innerWidth - 4) left = clientX - menu.offsetWidth;
    if (top + menu.offsetHeight > window.innerHeight - 4) top = clientY - menu.offsetHeight;
    menu.style.left = Math.max(4, left) + 'px';
    menu.style.top = Math.max(4, top) + 'px';
    item.focus({ preventScroll: true });
}

// Point notes: text shown under a point's value while the pointer is on it, in the legend and in the curve
// tooltip (the dashboard names the shackle behind each weekly peak). They come with the data from
// IndicesInTimeComponent.DisplayData as [{ series, x, text }], so the graph redraws once: x is written like the
// CSV's first column and read with the parser dygraph picked for it, so it lands on the very instant of the
// point whatever the browser's time zone. Kept on the graph object, so a graph created again under the same id
// starts with none. No notes (an empty list, or none passed) puts dygraph's own legend back.
function updateWithPointNotes(g, divid, options, notes) {
    const hasNotes = Array.isArray(notes) && notes.length > 0;
    g.pointNotes = null;   // the old data's notes are never shown against the new data
    options.legendFormatter = hasNotes ? legendWithPointNotes : null;
    g.updateOptions(options);
    if (!hasNotes) return;

    const parse = g.getOption('xValueParser');
    if (!parse) throw new Error('graph ' + divid + ' has no x values to place notes on');
    g.pointNotes = new Map(notes.map(function (n) { return [pointNoteKey(n.series, parse(n.x)), n.text]; }));
}

function pointNoteKey(series, x) {
    return series + '\n' + x;
}

function pointNote(g, series, x) {
    return g.pointNotes ? g.pointNotes.get(pointNoteKey(series, x)) : undefined;
}

// dygraph's own legend, then the note of each visible series at the selected point. dygraph calls it with the
// graph as this. A note is text: written with textContent, never as HTML.
function legendWithPointNotes(data) {
    let html = Dygraph.Plugins.Legend.defaultFormatter.call(this, data);
    if (data.x == null) return html;

    const g = this;
    data.series.forEach(function (s) {
        const note = s.isVisible ? pointNote(g, s.label, data.x) : undefined;
        if (!note) return;
        const line = document.createElement('div');
        line.className = 'dygraph-point-note';
        line.textContent = note;
        html += line.outerHTML;
    });
    return html;
}

// The visible series whose drawn line passes within CURVE_TOOLTIP_DISTANCE pixels of (x, y), the nearest
// one if several do, as { name, point }: point is that series' sample nearest along its line, a layout
// point whose idx is the row. null when no line is that close or (x, y) is more than CURVE_TOOLTIP_DISTANCE
// outside the plotting area (axes, range selector): a range's first and last points lie on the area's edges,
// and are reached from outside too. x and y are pixels from the top-left corner of dygraph's canvas, as its
// layout points and the coordinates it hands to findClosestPoint are.
function findCurveAt(g, x, y) {
    const layout = g.layout_;
    if (!layout || !layout.points) return null;

    const area = g.getArea();
    const margin = CURVE_TOOLTIP_DISTANCE;
    if (x < area.x - margin || x > area.x + area.w + margin || y < area.y - margin || y > area.y + area.h + margin) return null;

    let best = null;
    for (let s = 0; s < layout.points.length; s++) {
        const name = layout.setNames[s];
        const hit = nearestOnDrawnSeries(layout.points[s], x, y, CURVE_TOOLTIP_DISTANCE,
            g.getBooleanOption('connectSeparatedPoints', name));
        // "<=": on a tie the series drawn later, i.e. on top, wins.
        if (hit && hit.distance <= (best ? best.distance : CURVE_TOOLTIP_DISTANCE)) {
            best = { name: name, point: hit.point, distance: hit.distance };
        }
    }
    return best;
}

// Distance from (x, y) to one series' line as dygraph draws it, with the sample at the nearer end of the
// nearest segment (or the dot), as { distance, point }; null when nothing is drawn near. Only the
// segments reaching the band [x - maxDist, x + maxDist] are looked at. Mirrors
// DygraphCanvasRenderer._drawSeries: a missing value is skipped when connectSeparatedPoints is on, and
// otherwise (or for a NaN) breaks the line; a point with no drawn neighbour counts as a dot.
function nearestOnDrawnSeries(points, x, y, maxDist, connectSeparated) {
    if (!points || points.length === 0) return null;

    const skipped = function (p) { return connectSeparated && p.yval === null; };

    // Points are in x order: find the first one inside the band, then step back to the last point
    // drawn before it, so the segment entering the band counts.
    let lo = 0, hi = points.length;
    while (lo < hi) {
        const mid = (lo + hi) >> 1;
        if (points[mid].canvasx < x - maxDist) lo = mid + 1; else hi = mid;
    }
    let start = lo - 1;
    while (start >= 0 && skipped(points[start])) start--;

    let bestDistance = Infinity;
    let bestPoint = null;
    let prev = null;
    for (let i = Math.max(start, 0); i < points.length; i++) {
        const p = points[i];
        if (skipped(p)) continue;

        const beyond = p.canvasx > x + maxDist;
        if (p.canvasy === null || p.canvasy !== p.canvasy) {
            prev = null;                                  // the line is broken here
        } else {
            let d, nearer;
            if (prev) {
                const t = projectOnSegment(x, y, prev, p);
                d = Math.hypot(x - (prev.canvasx + t * (p.canvasx - prev.canvasx)),
                               y - (prev.canvasy + t * (p.canvasy - prev.canvasy)));
                nearer = t < 0.5 ? prev : p;
            } else {
                d = Math.hypot(p.canvasx - x, p.canvasy - y);
                nearer = p;
            }
            if (d < bestDistance) {
                bestDistance = d;
                bestPoint = nearer;
            }
            prev = p;
        }
        if (beyond) break;                                // the segment leaving the band is done
    }
    return bestPoint ? { distance: bestDistance, point: bestPoint } : null;
}

// Position (0 at a, 1 at b) of the point of segment ab nearest to (x, y).
function projectOnSegment(x, y, a, b) {
    const dx = b.canvasx - a.canvasx;
    const dy = b.canvasy - a.canvasy;
    const len2 = dx * dx + dy * dy;
    const t = len2 > 0 ? ((x - a.canvasx) * dx + (y - a.canvasy) * dy) / len2 : 0;
    return Math.max(0, Math.min(1, t));
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

function updateDygraphFast(divid, csvData, notes) {
    try {
        const g = dygraphInstances[divid];
        if (g) {
            // Keep zoom functionality, just update data efficiently
            updateWithPointNotes(g, divid, { file: csvData }, notes);
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

function updateDygraphWithAxes(divid, csvData, seriesAxisMap, y1Label, y2Label, notes) {
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

            updateWithPointNotes(g, divid, options, notes);
        }
    } catch (error) {
        console.error("Failed to update dygraph with axes:", error);
    }
}

