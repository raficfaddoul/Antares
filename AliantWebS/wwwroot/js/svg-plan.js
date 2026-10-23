// =============================================================
// svg-plan.js — The CAD plan's interaction, in the browser (ES module, imported by SVGComponent).
//
// The server draws the plan from the model (PlanScene) and never sees the mouse. This module owns the view
// and every gesture, and tells the server only what means something to the page:
//   OnPlanLineDrawn(start, end)       a line was drawn (Draw mode); each end says what it snapped to
//   OnPlanElementClicked(kind, key)   a palonnier, a mother, a load, or nothing was clicked (Select mode)
//   OnPlanSensorClicked(key)          a sensor was clicked (when sensors are clickable)
//   OnPlanPointPlaced(x, y)           a point was clicked to place a sensor (PlaceSensor mode), SVG units
//
// What it reads from the markup, all rendered by the server:
//   svg.plan-svg      data-mode (view|draw|select|place), data-snap-nodes / -mothers / -palonniers,
//                     data-sensors-clickable, data-extent "x y w h", data-study "a b c d e f" (SVG → the
//                     studies' metres), data-snap-distance (SVG units)
//   .plan-root        gets --plan-mark = 1 / (px per SVG unit): markers keep their size on the screen
//   .plan-element     data-kind (palonnier|mother), data-key; its .plan-line holds the ends
//   [data-node]       a charpente point to snap to: data-x, data-y
//   [data-charge-key] a load symbol;  [data-sensor-key] a sensor marker;  [data-tip-title] a tooltip
//   .plan-overlay     left empty by the server: the rubber band is drawn here
//   .plan-readout     left empty by the server: the mouse position is written here
//   [data-plan-action] fit | full-page | leave-full-page | export-jpg | close-dialog;  in the export dialog,
//                     [data-plan-file-name] the file name box and [data-plan-layer] the layer checkboxes, read as they
//                     are on the screen
//
// What it writes, and the component's CSS relies on: data-hover on the .plan-element a click would take, data-panning on
// the svg while panning, --plan-mark on .plan-root, data-full-page on the host while the plan fills the window; the
// viewBox itself, which the server renders once and never again.
//
// The rules (decided with Rafic on 2026-09-16, moved here from the server on 2026-09-17):
// - Middle-button drag and Shift+drag pan on every page. A plain left drag draws in Draw mode and pans in
//   every other mode. One finger does what the left button does; a pinch is left to the browser.
// - A press that moves at most 4 px along each axis is a click: the plan does not move and no line is drawn.
// - A press let go off the plan (over the page beside it, the toolbar, outside the window) does nothing: a line is
//   dropped - that is how a line is abandoned - and a click neither places nor selects.
// - The wheel zooms about the point under the mouse; down zooms in, as it always has.
// - Drawing snaps each end to a charpente point, then a mother end, then a palonnier end (each step from the
//   last), within 1 m, as each toggle allows. Lines of 500 mm or less are dropped by the server.
// - Hover and selection take the palonnier or mother within 8 px of the mouse, the palonnier on a tie: what
//   lights up is what a click takes.
// - Full page (2026-10-23): a toolbar button lays the plan over the whole window, under the page's dialogs,
//   notifications and alarm banners; the other button or Esc puts it back. The view is kept: the plan only grows.
// =============================================================

const SVG_NS = 'http://www.w3.org/2000/svg';
const PICK_PX = 8;
const SLOP_PX = 4;
const ZOOM_STEP = 0.1;
const LEFT = 0, MIDDLE = 1;

/** Attaches the interaction to one plan. host: the component's root element; svg: its svg; dotnet: the component. */
export function create(host, svg, dotnet) {
    const plan = new Plan(host, svg, dotnet);
    return { dispose: () => plan.dispose() };
}

class Plan {
    constructor(host, svg, dotnet) {
        this.host = host;
        this.svg = svg;
        this.dotnet = dotnet;
        this.root = svg.querySelector('.plan-root');
        this.overlay = svg.querySelector('.plan-overlay');
        this.readout = host.querySelector('.plan-readout');
        this.gesture = null;
        this.hovered = null;
        this.tip = null;
        this.tipShown = '';
        this.band = null;

        this.listeners = [];
        this.on(svg, 'pointerdown', e => this.onPointerDown(e));
        this.on(svg, 'pointermove', e => this.onPointerMove(e));
        this.on(svg, 'pointerup', e => this.onPointerUp(e));
        this.on(svg, 'pointercancel', e => this.onPointerCancel(e));
        this.on(svg, 'lostpointercapture', e => this.onPointerCancel(e));
        this.on(svg, 'pointerleave', () => { if (!this.gesture) { this.setHover(null); this.hideTip(); } });
        // The middle button pans: no browser autoscroll. Only mousedown cancels it, and only the middle button is
        // cancelled, so a left press still takes the focus away from a field being edited beside the plan.
        this.on(svg, 'mousedown', e => { if (e.button === MIDDLE) e.preventDefault(); });
        this.on(svg, 'wheel', e => this.onWheel(e), { passive: false });
        this.on(host, 'click', e => this.onHostClick(e));
        this.on(document, 'keydown', e => this.onKeyDown(e));

        // The view as the server left it was fitted to the Point0 of the first render; Point0 may have arrived since.
        this.extent = parseNumbers(svg.dataset.extent, 4);
        if (this.extent) this.setView(...this.extent);

        // Point0 moves the whole drawing on the screen: move the view with it, so the same place stays in view.
        this.extentWatch = new MutationObserver(() => {
            const next = parseNumbers(this.svg.dataset.extent, 4);
            if (!next) return;
            if (this.extent) {
                const v = this.view();
                this.setView(v.x + next[0] - this.extent[0], v.y + next[1] - this.extent[1], v.w, v.h);
            }
            this.extent = next;
        });
        this.extentWatch.observe(svg, { attributes: true, attributeFilter: ['data-extent'] });

        this.resizeWatch = new ResizeObserver(() => this.updateMark());
        this.resizeWatch.observe(svg);

        this.updateMark();
        this.writeReadout(0, 0);
    }

    on(target, type, handler, options) {
        target.addEventListener(type, handler, options);
        this.listeners.push(() => target.removeEventListener(type, handler, options));
    }

    dispose() {
        this.listeners.forEach(off => off());
        this.listeners = [];
        this.extentWatch.disconnect();
        this.resizeWatch.disconnect();
        this.endGesture();
        this.setHover(null);
        if (this.tip) this.tip.remove();
        this.tip = null;
        this.dotnet = null;
    }

    // ── The view ──────────────────────────────────────────────────────────────────────────────

    mode() { return this.svg.dataset.mode || 'view'; }
    flag(name) { return this.svg.dataset[name] === 'true'; }

    view() {
        const v = this.svg.viewBox.baseVal;
        return { x: v.x, y: v.y, w: v.width, h: v.height };
    }

    setView(x, y, w, h) {
        if (![x, y, w, h].every(Number.isFinite) || w <= 0 || h <= 0) return;
        this.svg.setAttribute('viewBox', `${x} ${y} ${w} ${h}`);
        this.updateMark();
    }

    /** CSS pixels per SVG unit: the viewBox fits the element at one scale both ways (xMidYMid meet). */
    pxPerUnit() {
        const r = this.svg.getBoundingClientRect();
        const v = this.view();
        if (!r.width || !r.height || !v.w || !v.h) return 1;
        return Math.min(r.width / v.w, r.height / v.h);
    }

    updateMark() {
        if (this.root) this.root.style.setProperty('--plan-mark', String(1 / this.pxPerUnit()));
    }

    /** A point of the page (client pixels) in SVG user units, or null while the plan is not laid out. */
    toSvg(clientX, clientY) {
        const m = this.svg.getScreenCTM();
        if (!m) return null;
        const p = new DOMPoint(clientX, clientY).matrixTransform(m.inverse());
        return { x: p.x, y: p.y };
    }

    /** The plan itself is what is under this client point: not the toolbar, the dialog, the page beside it, nothing. */
    overPlan(clientX, clientY) {
        const el = document.elementFromPoint(clientX, clientY);
        return !!el && this.svg.contains(el);
    }

    panBy(dxPx, dyPx) {
        const px = this.pxPerUnit();
        const v = this.view();
        this.setView(v.x - dxPx / px, v.y - dyPx / px, v.w, v.h);
    }

    fit() {
        if (this.extent) this.setView(...this.extent);
    }

    /** The plan over the whole window, or back in its place. The viewBox stays: the same view, larger or smaller. */
    setFullPage(on) {
        this.hideTip();   // it points at where the mouse's target was
        this.host.toggleAttribute('data-full-page', on);
    }

    onWheel(e) {
        e.preventDefault();
        this.hideTip();
        if (!e.deltaY) return;   // a sideways swipe on a touchpad zooms neither way
        const at = this.toSvg(e.clientX, e.clientY);
        if (!at) return;
        const zoom = e.deltaY > 0 ? 1 - ZOOM_STEP : 1 + ZOOM_STEP;
        const v = this.view();
        this.setView(at.x - (at.x - v.x) * zoom, at.y - (at.y - v.y) * zoom, v.w * zoom, v.h * zoom);
    }

    // ── Gestures ──────────────────────────────────────────────────────────────────────────────

    onPointerDown(e) {
        if (e.button !== LEFT && e.button !== MIDDLE) return;   // the right button keeps its menu
        if (this.gesture) return;                               // a second finger during a gesture
        this.hideTip();
        this.setHover(null);

        const pans = e.button === MIDDLE || e.shiftKey || this.mode() !== 'draw';
        const start = { x: e.clientX, y: e.clientY };
        const g = { kind: pans ? 'pan' : 'draw', pointerId: e.pointerId, button: e.button, start, last: start, moved: false, target: e.target };

        if (!pans) {
            const at = this.toSvg(e.clientX, e.clientY);
            if (!at) return;
            g.startRaw = at;
            g.startSnap = this.snap(at);
            this.showBand(g.startSnap.point, g.startSnap.point);
        } else {
            this.svg.setAttribute('data-panning', '');
        }

        this.gesture = g;
        try { this.svg.setPointerCapture(e.pointerId); } catch { /* a synthetic pointer: no capture */ }
    }

    onPointerMove(e) {
        const at = this.toSvg(e.clientX, e.clientY);
        if (at) this.writeReadout(at.x, at.y);

        const g = this.gesture;
        if (!g) {
            this.updateHover(at);
            this.updateTip(e);
            return;
        }
        if (e.pointerId !== g.pointerId) return;
        const client = { x: e.clientX, y: e.clientY };

        if (g.kind === 'draw') {
            if ((e.buttons & 1) === 0) {
                if (e.buttons === 0) {
                    // No button held and no pointerup came: let go where this plan never heard of it. The line is
                    // dropped, as a line let go off the plan is.
                    this.endGesture();
                    return;
                }
                // The left button let go while another stays down: the release came here, and the line ends.
                this.finishDraw(e);
                if (e.buttons & 4) {
                    // A chord with the middle button: it goes on panning until it is let go too.
                    this.gesture = { kind: 'pan', pointerId: e.pointerId, button: MIDDLE, start: client, last: client, moved: true, target: null };
                    this.svg.setAttribute('data-panning', '');
                    try { this.svg.setPointerCapture(e.pointerId); } catch { /* a synthetic pointer: no capture */ }
                }
                return;
            }
            // The middle button held during a line pans the plan under it.
            if (e.buttons & 4) {
                if (g.chordLast) this.panBy(client.x - g.chordLast.x, client.y - g.chordLast.y);
                g.chordLast = client;
                this.svg.setAttribute('data-panning', '');
            } else {
                g.chordLast = null;
                this.svg.removeAttribute('data-panning');
            }
            const now = this.toSvg(e.clientX, e.clientY);
            if (now) this.moveBand(g.startSnap.point, this.snap(now).point);
            return;
        }

        // A pan whose button is no longer held, e.g. let go where the capture was lost: over.
        if ((e.buttons & (g.button === MIDDLE ? 4 : 1)) === 0) { this.endGesture(); return; }
        if (!g.moved && Math.abs(client.x - g.start.x) <= SLOP_PX && Math.abs(client.y - g.start.y) <= SLOP_PX) return;
        g.moved = true;
        this.panBy(client.x - g.last.x, client.y - g.last.y);
        g.last = client;
    }

    onPointerUp(e) {
        const g = this.gesture;
        if (!g || e.pointerId !== g.pointerId) return;

        if (g.kind === 'draw') {
            this.finishDraw(e);
            return;
        }

        // A press let go off the plan (on the toolbar, beside the plan) is no click: it neither places nor selects.
        const click = !g.moved && e.button === g.button && this.overPlan(e.clientX, e.clientY);
        this.endGesture();
        if (click && g.button === LEFT) this.click(g.target, e.clientX, e.clientY);
    }

    onPointerCancel(e) {
        const g = this.gesture;
        if (!g || e.pointerId !== g.pointerId) return;
        // pointerup releases the capture too, and comes first: by then the gesture is over.
        this.endGesture();
    }

    endGesture() {
        const g = this.gesture;
        this.gesture = null;
        this.svg.removeAttribute('data-panning');
        this.hideBand();
        if (g) {
            try { this.svg.releasePointerCapture(g.pointerId); } catch { /* not captured */ }
        }
    }

    finishDraw(e) {
        const g = this.gesture;
        const end = this.toSvg(e.clientX, e.clientY);
        this.endGesture();
        if (!g || !end) return;

        // Let go off the plan - over the page beside it, the toolbar, or outside the window: the line is dropped. That is
        // how a line is abandoned, as it always was; the pointer capture would otherwise deliver the release here.
        if (!this.overPlan(e.clientX, e.clientY)) return;

        // Within the click slop of the press, on the plan as it is now, along each axis: a click, not a line - even
        // where snapping would take the two ends apart, to both ends of a short palonnier.
        const px = this.pxPerUnit();
        const isClick = Math.max(Math.abs(end.x - g.startRaw.x), Math.abs(end.y - g.startRaw.y)) * px <= SLOP_PX + 1e-6;
        if (isClick) {
            this.click(g.target, e.clientX, e.clientY);
            return;
        }
        const endSnap = this.snap(end);
        this.invoke('OnPlanLineDrawn', drawEnd(g.startSnap), drawEnd(endSnap));
    }

    /** A click (a press that did not move), where it was pressed and let go. */
    click(target, clientX, clientY) {
        const sensor = target && target.closest ? target.closest('[data-sensor-key]') : null;
        if (sensor && this.svg.contains(sensor) && this.flag('sensorsClickable')) {
            this.invoke('OnPlanSensorClicked', sensor.dataset.sensorKey);
            return;
        }

        const mode = this.mode();
        if (mode === 'select') {
            const charge = target && target.closest ? target.closest('[data-charge-key]') : null;
            if (charge && this.svg.contains(charge)) {
                this.invoke('OnPlanElementClicked', 'charge', charge.dataset.chargeKey);
                return;
            }
            const at = this.toSvg(clientX, clientY);
            const hit = at ? this.pick(at) : null;
            this.invoke('OnPlanElementClicked', hit ? hit.dataset.kind : 'none', hit ? hit.dataset.key : null);
        } else if (mode === 'place') {
            const at = this.toSvg(clientX, clientY);
            if (at) this.invoke('OnPlanPointPlaced', at.x, at.y);
        }
    }

    invoke(method, ...args) {
        if (!this.dotnet) return;
        this.dotnet.invokeMethodAsync(method, ...args).catch(err => console.error(`Plan: ${method} failed`, err));
    }

    // ── Picking and snapping ──────────────────────────────────────────────────────────────────

    /** The palonnier or mother within PICK_PX of the point (SVG units): the nearer, the palonnier on a tie. */
    pick(at) {
        const tolerance = PICK_PX / this.pxPerUnit();
        let palonnier = null, palonnierDistance = Infinity;
        let mother = null, motherDistance = Infinity;
        for (const element of this.svg.querySelectorAll('.plan-element')) {
            const line = element.querySelector('.plan-line');
            if (!line) continue;
            const [x1, y1, x2, y2] = lineEnds(line);
            const d = segmentDistance(at.x, at.y, x1, y1, x2, y2);
            if (d > tolerance) continue;
            if (element.dataset.kind === 'palonnier') {
                if (d < palonnierDistance) { palonnierDistance = d; palonnier = element; }
            } else if (d < motherDistance) {
                motherDistance = d;
                mother = element;
            }
        }
        return palonnier && palonnierDistance <= motherDistance ? palonnier : mother;
    }

    /** A point snapped as the toggles allow: charpente point, then mother end, then palonnier end, each from the last. */
    snap(p) {
        const distance = Number(this.svg.dataset.snapDistance) || 0;
        let point = { x: p.x, y: p.y };
        let target = null;

        if (this.flag('snapNodes')) {
            let best = null, min = Infinity;
            for (const node of this.svg.querySelectorAll('[data-node]')) {
                const x = Number(node.dataset.x), y = Number(node.dataset.y);
                const d = Math.hypot(x - point.x, y - point.y);
                if (d < min && d <= distance) { min = d; best = { x, y, key: node.dataset.node }; }
            }
            if (best) { point = { x: best.x, y: best.y }; target = { kind: 'node', key: best.key, end: 0 }; }
        }

        for (const [flag, kind] of [['snapMothers', 'mother'], ['snapPalonniers', 'palonnier']]) {
            if (!this.flag(flag)) continue;
            let best = null, min = Infinity;
            for (const element of this.svg.querySelectorAll(`.plan-element[data-kind="${kind}"]`)) {
                const line = element.querySelector('.plan-line');
                if (!line) continue;
                const [x1, y1, x2, y2] = lineEnds(line);
                const ends = [[x1, y1, 1], [x2, y2, 2]];
                for (const [x, y, end] of ends) {
                    const d = Math.hypot(x - point.x, y - point.y);
                    if (d < min && d <= distance) { min = d; best = { x, y, key: element.dataset.key, end }; }
                }
            }
            if (best) { point = { x: best.x, y: best.y }; target = { kind, key: best.key, end: best.end }; }
        }

        return { point, target };
    }

    // ── What the mouse shows ──────────────────────────────────────────────────────────────────

    updateHover(at) {
        if (!at || this.mode() === 'place') { this.setHover(null); return; }
        this.setHover(this.pick(at));
    }

    setHover(element) {
        if (this.hovered === element) return;
        if (this.hovered) this.hovered.removeAttribute('data-hover');
        this.hovered = element;
        if (element) element.setAttribute('data-hover', '');
    }

    showBand(a, b) {
        if (!this.overlay) return;
        if (!this.band) {
            this.band = document.createElementNS(SVG_NS, 'line');
            this.band.setAttribute('class', 'plan-band');
            this.band.setAttribute('stroke', 'black');
            this.band.setAttribute('stroke-width', '2');
            this.band.setAttribute('vector-effect', 'non-scaling-stroke');
            this.band.setAttribute('pointer-events', 'none');
        }
        this.overlay.appendChild(this.band);
        this.moveBand(a, b);
    }

    moveBand(a, b) {
        if (!this.band) return;
        this.band.setAttribute('x1', a.x);
        this.band.setAttribute('y1', a.y);
        this.band.setAttribute('x2', b.x);
        this.band.setAttribute('y2', b.y);
    }

    hideBand() {
        if (this.band) this.band.remove();
    }

    /** The mouse's position in the studies' axes, metres from Point0: "X: 12,35 m   Y: -4,00 m". */
    writeReadout(x, y) {
        if (!this.readout) return;
        const m = parseNumbers(this.svg.dataset.study, 6);
        if (!m) return;
        const X = m[0] * x + m[2] * y + m[4];
        const Y = m[1] * x + m[3] * y + m[5];
        this.readout.textContent = `X: ${metres(X)} m   Y: ${metres(Y)} m`;
    }

    // ── Tooltips ──────────────────────────────────────────────────────────────────────────────
    // An element carrying data-tip-title shows a tooltip while the mouse rests on it: the title in bold beside a dot
    // of data-tip-color, then each line of data-tip. Built from text nodes, never parsed as markup.

    updateTip(e) {
        const el = e.target && e.target.closest ? e.target.closest('[data-tip-title]') : null;
        if (!el || !this.svg.contains(el) || e.buttons !== 0) { this.hideTip(); return; }

        if (!this.tip) {
            this.tip = document.createElement('div');
            this.tip.className = 'plan-tooltip';
            this.tip.setAttribute('role', 'tooltip');
            this.tip.style.cssText = 'position:fixed; z-index:2000; pointer-events:none; padding:6px 9px; border-radius:6px;'
                + ' background:rgba(33,37,41,0.94); color:#fff; box-shadow:0 2px 10px rgba(0,0,0,0.25); white-space:nowrap;'
                + ' font:12px/1.45 system-ui,"Segoe UI",Roboto,Arial,sans-serif;';
            document.body.appendChild(this.tip);
        }

        const title = el.getAttribute('data-tip-title') || '';
        const body = el.getAttribute('data-tip') || '';
        const color = el.getAttribute('data-tip-color') || '';
        const key = title + '\n\n' + body + '\n\n' + color;
        if (key !== this.tipShown) {
            fillTip(this.tip, title, body, color);
            this.tipShown = key;
        }
        this.tip.style.display = 'block';

        // Below right of the mouse, or on the other side of it where the window ends.
        const gap = 14, w = this.tip.offsetWidth, h = this.tip.offsetHeight;
        let x = e.clientX + gap, y = e.clientY + gap;
        if (x + w > window.innerWidth - 4) x = Math.max(4, e.clientX - gap - w);
        if (y + h > window.innerHeight - 4) y = Math.max(4, e.clientY - gap - h);
        this.tip.style.left = x + 'px';
        this.tip.style.top = y + 'px';
    }

    hideTip() {
        if (this.tip) this.tip.style.display = 'none';
    }

    // ── Toolbar and dialog ────────────────────────────────────────────────────────────────────

    onHostClick(e) {
        const button = e.target && e.target.closest ? e.target.closest('[data-plan-action]') : null;
        if (!button || !this.host.contains(button)) return;
        const action = button.dataset.planAction;
        if (action === 'fit') this.fit();
        else if (action === 'full-page') this.setFullPage(true);
        else if (action === 'leave-full-page') this.setFullPage(false);
        // Run inside the click itself: the Save As picker needs the click's user activation, which a round trip to the
        // server would expire.
        else if (action === 'export-jpg') {
            // The name as typed: the box only reaches the server when it loses the focus, which may be this very click.
            const box = this.host.querySelector('[data-plan-file-name]');
            this.exportJpg((box && box.value.trim()) || 'plan');
        }
    }

    // Esc leaves full page, unless something else already took the key.
    onKeyDown(e) {
        if (e.key === 'Escape' && !e.defaultPrevented && this.host.hasAttribute('data-full-page')) this.setFullPage(false);
    }

    /**
     * The plan as it is on the screen - the current pan and zoom - as a JPEG, without the layers unticked in the dialog,
     * the editing aids, the hover highlight or a line being drawn. External images are inlined first: an SVG drawn into a
     * canvas does not fetch them.
     */
    async exportJpg(baseName) {
        try {
            const rect = this.svg.getBoundingClientRect();
            if (!rect.width || !rect.height) return;

            const d = new Date();
            const p2 = n => String(n).padStart(2, '0');
            const stamp = `${d.getFullYear()}-${p2(d.getMonth() + 1)}-${p2(d.getDate())}_${p2(d.getHours())}-${p2(d.getMinutes())}`;
            const fileName = String(baseName || 'plan').replace(/[\\/:*?"<>|]/g, '_') + '_' + stamp + '.jpg';

            // Ask where to save FIRST, while the click's user activation is still valid.
            let handle = null;
            if (window.showSaveFilePicker) {
                try {
                    handle = await window.showSaveFilePicker({
                        suggestedName: fileName,
                        types: [{ description: 'Image JPEG', accept: { 'image/jpeg': ['.jpg', '.jpeg'] } }],
                    });
                } catch (err) {
                    if (err && err.name === 'AbortError') return;   // cancelled
                    handle = null;
                }
            }

            const clone = this.svg.cloneNode(true);
            clone.setAttribute('width', rect.width);
            clone.setAttribute('height', rect.height);
            clone.setAttribute('xmlns', SVG_NS);
            clone.setAttribute('xmlns:xlink', 'http://www.w3.org/1999/xlink');

            let keepBackground = true;
            this.host.querySelectorAll('[data-plan-layer]').forEach(box => {
                const layer = box.dataset.planLayer;
                if (layer === 'dxf-layer-background') keepBackground = box.checked;
                if (!box.checked) clone.querySelectorAll('.' + layer).forEach(g => g.remove());
            });
            clone.querySelectorAll('.plan-editing-only').forEach(g => g.remove());
            clone.querySelectorAll('.plan-overlay > *').forEach(n => n.remove());
            clone.querySelectorAll('[data-hover]').forEach(n => n.removeAttribute('data-hover'));

            for (const img of Array.from(clone.querySelectorAll('image'))) {
                const href = img.getAttribute('href') || img.getAttribute('xlink:href');
                if (!keepBackground || !href) { img.remove(); continue; }
                if (href.startsWith('data:')) continue;
                try {
                    // Resolved against the page, as the live image is, so a base path keeps working.
                    const blob = await (await fetch(new URL(href, document.baseURI))).blob();
                    const dataUrl = await new Promise((res, rej) => {
                        const reader = new FileReader();
                        reader.onload = () => res(reader.result);
                        reader.onerror = rej;
                        reader.readAsDataURL(blob);
                    });
                    img.setAttribute('href', dataUrl);
                    img.removeAttribute('xlink:href');
                } catch {
                    img.remove();
                }
            }

            const xml = new XMLSerializer().serializeToString(clone);
            const svgUrl = URL.createObjectURL(new Blob([xml], { type: 'image/svg+xml;charset=utf-8' }));
            const bitmap = await new Promise((res, rej) => {
                const im = new Image();
                im.onload = () => res(im);
                im.onerror = rej;
                im.src = svgUrl;
            });

            const scale = 2;
            const out = document.createElement('canvas');
            out.width = Math.round(rect.width * scale);
            out.height = Math.round(rect.height * scale);
            const ctx = out.getContext('2d');
            ctx.scale(scale, scale);
            ctx.fillStyle = '#ffffff';   // JPEG has no alpha
            ctx.fillRect(0, 0, rect.width, rect.height);
            ctx.drawImage(bitmap, 0, 0, rect.width, rect.height);
            URL.revokeObjectURL(svgUrl);

            const blob = await new Promise(res => out.toBlob(res, 'image/jpeg', 0.92));
            if (!blob) return;
            let saved = false;
            if (handle) {
                try {
                    const w = await handle.createWritable();
                    await w.write(blob);
                    await w.close();
                    saved = true;
                } catch (err) {
                    console.error('Plan: saving the JPG failed, downloading it instead', err);
                }
            }
            if (!saved) {
                const url = URL.createObjectURL(blob);
                const a = document.createElement('a');
                a.href = url;
                a.download = fileName;
                document.body.appendChild(a);
                a.click();
                document.body.removeChild(a);
                setTimeout(() => URL.revokeObjectURL(url), 1000);
            }
            if (window.journalExport) window.journalExport('export.jpg.plan', fileName, '');
            const close = this.host.querySelector('[data-plan-action="close-dialog"]');
            if (close) close.click();
        } catch (err) {
            console.error('Plan: the JPG export failed', err);
        }
    }
}

// ── Helpers ───────────────────────────────────────────────────────────────────────────────────

function parseNumbers(text, count) {
    if (!text) return null;
    const values = text.trim().split(/[\s,]+/).map(Number);
    return values.length === count && values.every(Number.isFinite) ? values : null;
}

function lineEnds(line) {
    return [line.x1.baseVal.value, line.y1.baseVal.value, line.x2.baseVal.value, line.y2.baseVal.value];
}

/** Distance from (px, py) to the segment (x1, y1)-(x2, y2), clamped to its ends. */
function segmentDistance(px, py, x1, y1, x2, y2) {
    const c = x2 - x1, d = y2 - y1;
    const lengthSq = c * c + d * d;
    const t = lengthSq !== 0 ? ((px - x1) * c + (py - y1) * d) / lengthSq : -1;
    const [x, y] = t < 0 ? [x1, y1] : t > 1 ? [x2, y2] : [x1 + t * c, y1 + t * d];
    return Math.hypot(px - x, py - y);
}

function drawEnd(snapped) {
    const t = snapped.target;
    return { x: snapped.point.x, y: snapped.point.y, snapKind: t ? t.kind : null, snapKey: t ? t.key : null, snapEnd: t ? t.end : 0 };
}

/** Metres with two decimals and a decimal comma, as the pages print them; a zero never shows a minus. */
function metres(value) {
    const text = value.toFixed(2).replace('.', ',');
    return text === '-0,00' ? '0,00' : text;
}

function fillTip(tip, title, body, color) {
    tip.textContent = '';
    const head = document.createElement('div');
    head.style.cssText = 'display:flex; align-items:center; gap:6px; font-weight:600;';
    if (color) {
        const dot = document.createElement('span');
        dot.style.cssText = 'flex:none; width:10px; height:10px; border-radius:50%; border:1px solid #fff;';
        dot.style.background = color;
        head.appendChild(dot);
    }
    head.appendChild(document.createTextNode(title));
    tip.appendChild(head);
    body.split('\n').forEach(line => {
        if (!line) return;
        const row = document.createElement('div');
        row.textContent = line;
        tip.appendChild(row);
    });
}
