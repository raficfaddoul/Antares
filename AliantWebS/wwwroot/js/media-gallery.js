// Media gallery - the browser half of Pages/MediaComponent.razor and Pages/MediaViewer.razor: keeping the active
// thumbnail in view, and the full-screen viewer's zoom, pan and swipe (a server round trip per pointer move would
// make both unusable).

export function reveal(strip, index) {
    const item = strip && strip.children ? strip.children[index] : null;
    if (item) item.scrollIntoView({ block: 'nearest', inline: 'center', behavior: 'smooth' });
}

export function focus(element) {
    if (element) element.focus({ preventScroll: true });
}

export function createViewer(viewport, dotnet) {
    return new Viewer(viewport, dotnet);
}

const MAX_SCALE = 8;

class Viewer {
    constructor(viewport, dotnet) {
        this.viewport = viewport;
        this.dotnet = dotnet;
        this.scale = 1;
        this.x = 0;
        this.y = 0;
        this.pointers = new Map();
        this.handlers = {
            wheel: e => this.onWheel(e),
            pointerdown: e => this.onDown(e),
            pointermove: e => this.onMove(e),
            pointerup: e => this.onUp(e),
            pointercancel: e => this.onUp(e),
            dblclick: e => this.onDoubleClick(e),
        };
        for (const [name, handler] of Object.entries(this.handlers)) {
            viewport.addEventListener(name, handler, name === 'wheel' ? { passive: false } : undefined);
        }
    }

    target() {
        return this.viewport.querySelector('[data-media="zoomable"]');
    }

    // Called by the component whenever it shows another item.
    reset() {
        this.scale = 1;
        this.x = 0;
        this.y = 0;
        this.pointers.clear();
        this.pinch = null;
        this.apply();
        return 1;
    }

    zoomBy(factor, clientX, clientY) {
        const target = this.target();
        if (!target) return this.scale;
        const rect = this.viewport.getBoundingClientRect();
        const next = Math.min(MAX_SCALE, Math.max(1, this.scale * factor));
        // Screen pixels to CSS pixels (they differ while an ancestor is scaled, e.g. the dialog opening).
        const kx = rect.width / (this.viewport.clientWidth || rect.width) || 1;
        const ky = rect.height / (this.viewport.clientHeight || rect.height) || 1;
        const px = ((clientX ?? rect.left + rect.width / 2) - rect.left - rect.width / 2) / kx;
        const py = ((clientY ?? rect.top + rect.height / 2) - rect.top - rect.height / 2) / ky;
        // Keep the point under the cursor (or the centre) where it is.
        this.x = px - (px - this.x) * (next / this.scale);
        this.y = py - (py - this.y) * (next / this.scale);
        this.scale = next;
        this.clamp();
        this.apply();
        return this.scale;
    }

    clamp() {
        const target = this.target();
        if (!target || this.scale <= 1) {
            this.x = 0;
            this.y = 0;
            return;
        }
        const maxX = Math.max(0, (target.offsetWidth * this.scale - this.viewport.clientWidth) / 2);
        const maxY = Math.max(0, (target.offsetHeight * this.scale - this.viewport.clientHeight) / 2);
        this.x = Math.min(maxX, Math.max(-maxX, this.x));
        this.y = Math.min(maxY, Math.max(-maxY, this.y));
    }

    apply() {
        const target = this.target();
        if (target) target.style.transform = `translate(${this.x}px, ${this.y}px) scale(${this.scale})`;
        this.viewport.dataset.zoomed = this.scale > 1.001 ? '1' : '0';
    }

    onWheel(e) {
        if (!this.target()) return;
        e.preventDefault();
        this.zoomBy(Math.exp(-e.deltaY * 0.0018), e.clientX, e.clientY);
        this.report();
    }

    onDoubleClick(e) {
        if (!this.target() || e.target.closest('button, a, video')) return;
        if (this.scale > 1.001) this.reset();
        else this.zoomBy(2.5, e.clientX, e.clientY);
        this.report();
    }

    onDown(e) {
        if (e.target.closest('button, a, video')) return;
        if (e.pointerType === 'mouse' && e.button !== 0) return;
        this.pointers.set(e.pointerId, { x: e.clientX, y: e.clientY, startX: e.clientX, startY: e.clientY });
        try { this.viewport.setPointerCapture(e.pointerId); } catch { /* pointer already gone */ }
        if (this.pointers.size === 2) {
            const [a, b] = [...this.pointers.values()];
            this.pinch = {
                distance: Math.hypot(a.x - b.x, a.y - b.y), scale: this.scale,
                cx: (a.x + b.x) / 2, cy: (a.y + b.y) / 2,
            };
        }
    }

    onMove(e) {
        const pointer = this.pointers.get(e.pointerId);
        if (!pointer) return;
        const dx = e.clientX - pointer.x;
        const dy = e.clientY - pointer.y;
        pointer.x = e.clientX;
        pointer.y = e.clientY;
        if (this.pointers.size === 2 && this.pinch && this.pinch.distance > 0) {
            const [a, b] = [...this.pointers.values()];
            const ratio = Math.hypot(a.x - b.x, a.y - b.y) / this.pinch.distance;
            this.zoomBy((this.pinch.scale * ratio) / this.scale, this.pinch.cx, this.pinch.cy);
            return;
        }
        if (this.pointers.size === 1 && this.scale > 1.001) {
            const rect = this.viewport.getBoundingClientRect();
            this.x += dx / (rect.width / (this.viewport.clientWidth || rect.width) || 1);
            this.y += dy / (rect.height / (this.viewport.clientHeight || rect.height) || 1);
            this.clamp();
            this.apply();
        }
    }

    onUp(e) {
        const pointer = this.pointers.get(e.pointerId);
        this.pointers.delete(e.pointerId);
        if (this.pointers.size < 2 && this.pinch) {
            this.pinch = null;
            this.report();
            return;
        }
        if (!pointer || this.scale > 1.001 || e.type === 'pointercancel') return;
        // A clear horizontal flick on an unzoomed picture moves to the next or previous item.
        const dx = e.clientX - pointer.startX;
        const dy = e.clientY - pointer.startY;
        if (Math.abs(dx) > 60 && Math.abs(dx) > Math.abs(dy) * 1.5) {
            this.dotnet.invokeMethodAsync('OnSwipe', dx < 0 ? 1 : -1).catch(() => { });
        }
    }

    report() {
        this.dotnet.invokeMethodAsync('OnZoom', this.scale).catch(() => { });
    }

    dispose() {
        for (const [name, handler] of Object.entries(this.handlers)) this.viewport.removeEventListener(name, handler);
        this.pointers.clear();
    }
}
