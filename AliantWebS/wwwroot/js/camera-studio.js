// Camera studio - the browser half of Pages/CameraComponent.razor.
//
// The component renders the dialog; this module owns everything that has to react without a round trip to the
// server: the camera stream, the shutter and the recorder, zoom and torch, the self-timer, sound, flash and
// vibration, the date stamp, the markup editor and the session's shots. A shot's bytes stay here, as a Blob, until
// the component streams them to the server (blob(id)), so nothing large ever travels as a SignalR message.
//
// Elements are looked up by their data-cam attribute when they are needed: Blazor adds and removes parts of the
// dialog (review, markup) as the user moves around, so a reference kept from create() could go stale. What this
// module sets on those elements (data-recording, data-countdown, data-mirror, data-zoomable, inline styles, the
// children of the stamp preview) is never rendered by the component, so a Blazor re-render leaves it alone.

const SHUTTER_SOUND = 'camera-shutter-click-01.mp3';
const THUMB_SIZE = 360;
const PHOTO_QUALITY = 0.92;
const THUMB_QUALITY = 0.8;
const MAX_CLIP_SECONDS = 300;
const DIGITAL_ZOOM_MAX = 4;
const COMPOSITE_MAX_SIDE = 1920;
const SETTINGS_KEY = 'antares.camera.v1';

const SIZES = {
    '2160': { width: 3840, height: 2160 },
    '1080': { width: 1920, height: 1080 },
    '720': { width: 1280, height: 720 },
};

// As MediaLibrary on the server. No HEIC: most browsers cannot show it (an iPhone's picker hands a web page JPEG).
const PHOTO_EXTENSIONS = ['jpg', 'jpeg', 'png', 'webp', 'gif', 'bmp'];
const VIDEO_EXTENSIONS = ['mp4', 'webm', 'mov', 'm4v'];

export function create(root, dotnet, options) {
    return new CameraStudio(root, dotnet, options || {});
}

// Only the known settings, each only when it has the right type: anything else (another version, a hand edit) falls
// back to the component's default instead of failing to deserialize.
export function loadSettings() {
    let saved = null;
    try { saved = JSON.parse(localStorage.getItem(SETTINGS_KEY) || 'null'); } catch { return null; }
    if (!saved || typeof saved !== 'object') return null;
    const pick = (key, type) => (typeof saved[key] === type ? saved[key] : undefined);
    return {
        mode: pick('mode', 'string'), resolution: pick('resolution', 'string'), deviceId: pick('deviceId', 'string'),
        timer: Number.isInteger(saved.timer) ? saved.timer : undefined,
        grid: pick('grid', 'boolean'), stamp: pick('stamp', 'boolean'), location: pick('location', 'boolean'),
        mic: pick('mic', 'boolean'), sound: pick('sound', 'boolean'),
    };
}

export function saveSettings(settings) {
    try { localStorage.setItem(SETTINGS_KEY, JSON.stringify(settings)); } catch { /* private window: not remembered */ }
}

// What the browser can do before anything is asked of the user: the component shows the right screen at once
// instead of waiting for a getUserMedia that cannot succeed.
export function probe() {
    return {
        secure: window.isSecureContext !== false,
        camera: !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia),
        recorder: typeof MediaRecorder !== 'undefined',
        touch: matchMedia('(pointer: coarse)').matches,
    };
}

class CameraStudio {
    constructor(root, dotnet, options) {
        this.root = root;
        this.dotnet = dotnet;
        this.settings = {
            mode: 'photo', timer: 0, grid: false, stamp: false, location: false, mic: true, sound: true,
            resolution: 'auto', label: '', culture: navigator.language,
            ...(options.settings || {}),
        };
        this.text = { camera: 'Camera', ...(options.text || {}) };
        this.view = 'camera';
        this.stream = null;
        this.generation = 0;
        this.facing = '';
        this.zoom = { native: false, min: 1, max: 1, step: 0.1, value: 1 };
        this.torch = false;
        this.mic = null;
        this.micPromise = null;
        this.recording = null;
        this.countdown = null;
        this.position = null;
        this.geoWatch = null;
        this.shots = new Map();
        this.sequence = 0;
        this.pinch = new Map();
        this.editor = null;
        this.micFailed = false;
        this.wakeLock = null;
        this.wakeLockRequest = null;
        this.disposed = false;

        this.shutterSound = new Audio(new URL(SHUTTER_SOUND, document.baseURI).href);
        this.shutterSound.preload = 'auto';

        this.handlers = {
            click: e => this.onClick(e),
            change: e => this.onChange(e),
            input: e => this.onInput(e),
            wheel: e => this.onWheel(e),
            pointerdown: e => this.onPointerDown(e),
            pointermove: e => this.onPointerMove(e),
            pointerup: e => this.onPointerUp(e),
            pointercancel: e => this.onPointerUp(e),
            mousedown: e => this.keepTextFocus(e),
        };
        for (const [name, handler] of Object.entries(this.handlers)) {
            root.addEventListener(name, handler, name === 'wheel' ? { passive: false } : undefined);
        }
        this.onKeyDown = e => this.onKey(e);
        document.addEventListener('keydown', this.onKeyDown);
        this.onVisibility = () => this.checkStream();
        document.addEventListener('visibilitychange', this.onVisibility);
        this.onWindowFocus = () => this.resumeAfterNative();
        window.addEventListener('focus', this.onWindowFocus);
        // Keyboard shortcuts work from the start, without a first click (and without focusing the Close button).
        root.focus({ preventScroll: true });

        this.resizeObserver = new ResizeObserver(() => this.layoutFrame());
        const stage = this.el('stage');
        if (stage) this.resizeObserver.observe(stage);
        this.clock = setInterval(() => this.renderStamp(), 1000);
        this.renderStamp();
        this.syncLocation();
    }

    el(role) {
        return this.root.querySelector(`[data-cam="${role}"]`);
    }

    notify(method, ...args) {
        if (this.disposed) return;
        this.dotnet.invokeMethodAsync(method, ...args).catch(() => { /* circuit gone */ });
    }

    // ---- stream ------------------------------------------------------------------------------------------------

    async start(request) {
        request = request || {};
        const generation = ++this.generation;
        this.stopStream();

        const support = probe();
        if (!support.camera) return { ok: false, error: support.secure ? 'unsupported' : 'insecure' };

        const size = this.sizeFor(request.resolution || this.settings.resolution, this.settings.mode);
        const video = { width: { ideal: size.width }, height: { ideal: size.height }, frameRate: { ideal: 30 } };
        if (request.deviceId) video.deviceId = { exact: request.deviceId };
        else video.facingMode = request.facing ? { exact: request.facing } : { ideal: 'environment' };

        let stream;
        try {
            stream = await navigator.mediaDevices.getUserMedia({ video, audio: false });
        } catch (error) {
            // A remembered camera that is gone, or a facing the device does not have: fall back to any camera.
            const retry = (error.name === 'OverconstrainedError' || error.name === 'NotFoundError') &&
                (request.deviceId || request.facing);
            if (!retry) return { ok: false, error: classify(error), detail: error.message || error.name };
            if (generation !== this.generation || this.disposed) return { ok: false, stale: true };
            delete video.deviceId;
            video.facingMode = { ideal: 'environment' };
            try {
                stream = await navigator.mediaDevices.getUserMedia({ video, audio: false });
            } catch (second) {
                return { ok: false, error: classify(second), detail: second.message || second.name };
            }
        }

        if (generation !== this.generation || this.disposed) {
            stopTracks(stream);
            return { ok: false, stale: true };
        }
        return this.attach(stream);
    }

    async attach(stream) {
        this.stream = stream;
        const track = stream.getVideoTracks()[0];
        track.addEventListener('ended', () => {
            if (this.stream !== stream || this.disposed) return;
            // A clip being recorded keeps what it has (it would otherwise go on recording a frozen or empty picture).
            if (this.recording) this.stopRecording();
            this.notify('OnStreamEnded');
        });

        const video = this.el('video');
        if (video) {
            video.muted = true;
            video.playsInline = true;
            video.srcObject = stream;
            video.onloadedmetadata = () => this.layoutFrame();
            video.onresize = () => this.layoutFrame();
            try { await video.play(); } catch { /* autoplay refused: the stream still shows once the user taps */ }
            await waitFor(() => video.videoWidth > 0, 4000);
        }

        const settings = track.getSettings ? track.getSettings() : {};
        this.facing = settings.facingMode || guessFacing(track.label);
        this.root.dataset.mirror = this.facing === 'user' ? '1' : '0';
        this.torch = false;
        this.setupZoom(track);
        this.layoutFrame();
        return { ok: true, ...(await this.describeStream()) };
    }

    async describeStream() {
        const track = this.videoTrack();
        const video = this.el('video');
        const settings = track && track.getSettings ? track.getSettings() : {};
        const capabilities = track && track.getCapabilities ? track.getCapabilities() : {};
        return {
            deviceId: settings.deviceId || '',
            label: track ? track.label : '',
            facing: this.facing || '',
            width: (video && video.videoWidth) || settings.width || 0,
            height: (video && video.videoHeight) || settings.height || 0,
            torch: capabilities.torch === true,
            zoom: this.zoom.max > this.zoom.min,
            devices: await this.listDevices(),
        };
    }

    async listDevices() {
        try {
            const all = await navigator.mediaDevices.enumerateDevices();
            return all.filter(d => d.kind === 'videoinput').map((d, i) => ({
                id: d.deviceId,
                label: d.label || `${this.text.camera} ${i + 1}`,
                facing: guessFacing(d.label),
            }));
        } catch {
            return [];
        }
    }

    // Front <-> back. By facing first (what phones understand); a device that has no such facing (a laptop with two
    // webcams) moves to its next camera instead.
    async flip() {
        const track = this.videoTrack();
        const previous = (track && track.getSettings && track.getSettings().deviceId) || '';
        const target = this.facing === 'user' ? 'environment' : 'user';
        const result = await this.start({ facing: target });
        if (result.stale || (result.ok && result.facing === target)) return result;
        if (!result.ok && result.error !== 'notfound') return result; // blocked or busy: another camera would be too
        const devices = await this.listDevices();
        if (devices.length < 2) return result.ok ? result : this.start({});
        const index = devices.findIndex(d => d.id === previous);
        return this.start({ deviceId: devices[(index + 1) % devices.length].id });
    }

    // Resolution 'auto' follows the mode: the sharpest the camera gives for photos, 1080p for video (a 4K clip is
    // several times larger to keep in memory and to upload, for little gain on a site inspection).
    // Kept as this.retuning so a recording started meanwhile waits for the new format before sizing anything.
    retune() {
        this.retuning = this.retuneNow().finally(() => { this.retuning = null; });
        return this.retuning;
    }

    async retuneNow() {
        const track = this.videoTrack();
        if (!track || track.readyState !== 'live' || this.recording) return;
        const size = this.sizeFor(this.settings.resolution, this.settings.mode);
        const video = this.el('video');
        const before = video ? `${video.videoWidth}x${video.videoHeight}` : '';
        try {
            await track.applyConstraints({ width: { ideal: size.width }, height: { ideal: size.height }, frameRate: { ideal: 30 } });
        } catch { /* keeps the current format */ }
        // The element reports the new size a few frames after the constraints are applied.
        if (video) await waitFor(() => video.videoWidth > 0 && `${video.videoWidth}x${video.videoHeight}` !== before, 1500);
        this.layoutFrame();
        if (this.stream && !this.disposed && track.readyState === 'live') {
            this.notify('OnStreamInfo', { ok: true, ...(await this.describeStream()) });
        }
    }

    sizeFor(resolution, mode) {
        if (SIZES[resolution]) return SIZES[resolution];
        return mode === 'video' ? SIZES['1080'] : SIZES['2160'];
    }

    videoTrack() {
        return this.stream ? this.stream.getVideoTracks()[0] || null : null;
    }

    stopStream() {
        if (this.recording) this.stopRecording();
        if (this.stream) stopTracks(this.stream);
        this.stream = null;
        const video = this.el('video');
        if (video) video.srcObject = null;
    }

    checkStream() {
        if (document.visibilityState !== 'visible' || !this.stream || this.disposed) return;
        const track = this.videoTrack();
        if (!track || track.readyState === 'ended') this.notify('OnStreamEnded');
    }

    // The frame is the part of the stage the picture really covers (the stage letterboxes it): overlays drawn in it
    // (grid, stamp preview) line up with what is captured, and digital zoom is clipped to it.
    layoutFrame() {
        const stage = this.el('stage');
        const frame = this.el('frame');
        const video = this.el('video');
        if (!stage || !frame) return;
        const stageWidth = stage.clientWidth;
        const stageHeight = stage.clientHeight;
        const videoWidth = (video && video.videoWidth) || 16;
        const videoHeight = (video && video.videoHeight) || 9;
        const scale = Math.min(stageWidth / videoWidth, stageHeight / videoHeight) || 0;
        const width = Math.round(videoWidth * scale);
        const height = Math.round(videoHeight * scale);
        frame.style.width = `${width}px`;
        frame.style.height = `${height}px`;
        frame.style.left = `${Math.round((stageWidth - width) / 2)}px`;
        frame.style.top = `${Math.round((stageHeight - height) / 2)}px`;
        frame.style.setProperty('--frame-min', `${Math.min(width, height)}px`);
    }

    // ---- zoom and torch --------------------------------------------------------------------------------------------

    setupZoom(track) {
        const capabilities = track && track.getCapabilities ? track.getCapabilities() : {};
        const zoom = capabilities.zoom;
        if (zoom && zoom.max > zoom.min) {
            const current = (track.getSettings && track.getSettings().zoom) || zoom.min;
            this.zoom = { native: true, min: zoom.min, max: zoom.max, step: zoom.step || 0.1, value: current };
        } else {
            this.zoom = { native: false, min: 1, max: DIGITAL_ZOOM_MAX, step: 0.1, value: 1 };
        }
        this.renderZoom();
    }

    setZoom(value) {
        const zoom = this.zoom;
        // A clip recorded straight from the camera cannot follow a digital zoom: it stays as it started.
        if (!zoom.native && this.recording && !this.recording.compositor) return zoom.value;
        const clamped = Math.min(zoom.max, Math.max(zoom.min, Number(value) || zoom.min));
        zoom.value = Math.round(clamped / zoom.step) * zoom.step;
        this.renderZoom();
        if (zoom.native) this.applyNativeZoom();
        return zoom.value;
    }

    applyNativeZoom() {
        if (this.zoomBusy) { this.zoomPending = true; return; }
        const track = this.videoTrack();
        if (!track) return;
        this.zoomBusy = true;
        const target = this.zoom.value;
        track.applyConstraints({ advanced: [{ zoom: target }] })
            .catch(() => { })
            .finally(() => {
                this.zoomBusy = false;
                if (this.zoomPending) {
                    this.zoomPending = false;
                    if (this.zoom.value !== target) this.applyNativeZoom();
                }
            });
    }

    renderZoom() {
        const zoom = this.zoom;
        this.root.dataset.zoomable = zoom.max > zoom.min ? '1' : '0';
        const input = this.el('zoom');
        if (input) {
            input.min = String(zoom.min);
            input.max = String(zoom.max);
            input.step = String(zoom.step);
            input.value = String(zoom.value);
        }
        const label = this.el('zoom-label');
        if (label) label.textContent = `${zoom.value.toFixed(1)}×`;
        const video = this.el('video');
        if (video) {
            const parts = [];
            if (!zoom.native && zoom.value > 1) parts.push(`scale(${zoom.value})`);
            if (this.facing === 'user') parts.push('scaleX(-1)');
            video.style.transform = parts.join(' ');
        }
    }

    async setTorch(on) {
        const track = this.videoTrack();
        if (!track) return false;
        try {
            await track.applyConstraints({ advanced: [{ torch: !!on }] });
            this.torch = !!on;
        } catch {
            this.torch = false;
        }
        return this.torch;
    }

    // ---- settings, view and location ---------------------------------------------------------------------------

    update(patch) {
        const previous = this.settings;
        this.settings = { ...previous, ...(patch || {}) };
        const settings = this.settings;
        const format = previous.resolution !== settings.resolution ||
            (previous.mode !== settings.mode && settings.resolution === 'auto');
        if (previous.mode !== settings.mode) this.cancelCountdown(); // a photo timer must not end in a video
        if (format && this.stream) this.retune();
        // A refused or missing microphone is asked again only when the user changes the mode or the sound setting.
        if (previous.mode !== settings.mode || previous.mic !== settings.mic) this.micFailed = false;
        if (settings.mode === 'video' && settings.mic && !this.micFailed) this.ensureMic();
        else if (!this.recording) this.releaseMic();
        this.syncLocation();
        this.renderStamp(true);
    }

    setView(view) {
        this.view = view || 'camera';
        if (this.view !== 'camera') this.cancelCountdown();
    }

    syncLocation() {
        const wanted = !!this.settings.location && !this.disposed;
        if (wanted && this.geoWatch == null && navigator.geolocation) {
            this.geoWatch = navigator.geolocation.watchPosition(
                position => {
                    this.position = {
                        lat: position.coords.latitude,
                        lon: position.coords.longitude,
                        accuracy: position.coords.accuracy,
                    };
                    this.notify('OnLocation', 'ok');
                    this.renderStamp(true);
                },
                error => this.notify('OnLocation', error.code === 1 ? 'denied' : 'unavailable'),
                { enableHighAccuracy: true, maximumAge: 15000, timeout: 30000 });
        } else if (!wanted && this.geoWatch != null) {
            navigator.geolocation.clearWatch(this.geoWatch);
            this.geoWatch = null;
            this.position = null;
        }
    }

    // ---- stamp -------------------------------------------------------------------------------------------------

    stampLines(date) {
        const lines = [formatDate(date, this.settings.culture)];
        if (this.settings.label) lines.push(this.settings.label);
        if (this.settings.location && this.position) lines.push(formatPosition(this.position));
        return lines;
    }

    renderStamp(force) {
        const box = this.el('stamp');
        if (!box) return;
        if (!this.settings.stamp) {
            if (box.childElementCount) box.replaceChildren();
            box.dataset.text = '';
            return;
        }
        const lines = this.stampLines(new Date());
        const text = lines.join('\n');
        if (!force && box.dataset.text === text) return;
        box.dataset.text = text;
        box.replaceChildren(...lines.map((line, i) => {
            const row = document.createElement('div');
            row.textContent = line;
            if (i === 0) row.className = 'cam-stamp-main';
            return row;
        }));
    }

    drawStamp(ctx, width, height, date) {
        const lines = this.stampLines(date);
        const size = Math.max(12, Math.round(Math.min(width, height) * 0.03));
        const small = Math.round(size * 0.82);
        const pad = Math.round(size * 0.6);
        const gap = Math.round(size * 0.32);
        const bar = Math.max(3, Math.round(size * 0.16));
        const font = px => `${px}px system-ui, -apple-system, "Segoe UI", Roboto, sans-serif`;
        ctx.save();
        ctx.textBaseline = 'top';
        const widths = lines.map((line, i) => {
            ctx.font = `${i === 0 ? 600 : 500} ${font(i === 0 ? size : small)}`;
            return ctx.measureText(line).width;
        });
        const boxWidth = Math.ceil(Math.max(...widths)) + pad * 2 + bar;
        const boxHeight = size + (lines.length - 1) * (small + gap) + pad * 2;
        const x = Math.round(size * 0.8);
        const y = height - boxHeight - Math.round(size * 0.8);
        ctx.fillStyle = 'rgba(8, 13, 26, 0.74)';
        roundRect(ctx, x, y, boxWidth, boxHeight, Math.round(size * 0.35));
        ctx.fill();
        ctx.fillStyle = '#00FFAD';
        roundRect(ctx, x, y, bar, boxHeight, Math.min(bar, Math.round(size * 0.35)));
        ctx.fill();
        ctx.fillStyle = '#ffffff';
        let top = y + pad;
        lines.forEach((line, i) => {
            ctx.font = `${i === 0 ? 600 : 500} ${font(i === 0 ? size : small)}`;
            ctx.globalAlpha = i === 0 ? 1 : 0.88;
            ctx.fillText(line, x + bar + pad, top);
            top += (i === 0 ? size : small) + gap;
        });
        ctx.restore();
    }

    // ---- shutter -----------------------------------------------------------------------------------------------

    shutter() {
        if (this.disposed) return;
        // A recording always stops, whatever is on screen.
        if (this.recording) { this.stopRecording(); return; }
        if (this.view !== 'camera') return;
        if (this.countdown) { this.cancelCountdown(); return; }
        if (this.settings.mode === 'video') {
            if (this.recordingStarting) return;
            this.afterTimer(() => { if (this.settings.mode === 'video' && this.view === 'camera') this.startRecording(); });
        } else {
            this.afterTimer(() => { if (this.settings.mode === 'photo' && this.view === 'camera') this.capturePhoto(); });
        }
    }

    afterTimer(action) {
        const seconds = Number(this.settings.timer) || 0;
        if (seconds <= 0) { action(); return; }
        let left = seconds;
        const box = this.el('countdown');
        this.root.dataset.countdown = '1';
        const step = () => {
            if (left <= 0) {
                this.cancelCountdown();
                action();
                return;
            }
            if (box) {
                box.textContent = String(left);
                box.animate([{ transform: 'scale(1.35)', opacity: 0.2 }, { transform: 'scale(1)', opacity: 1 }],
                    { duration: 320, easing: 'cubic-bezier(.2,.8,.2,1)' });
            }
            this.beep(left === 1 ? 1320 : 880, 70);
            left -= 1;
            this.countdown = setTimeout(step, 1000);
        };
        this.countdown = -1;
        step();
    }

    cancelCountdown() {
        if (this.countdown) clearTimeout(this.countdown);
        this.countdown = null;
        delete this.root.dataset.countdown;
        const box = this.el('countdown');
        if (box) box.textContent = '';
    }

    grabFrame(video, maxSide) {
        const sourceWidth = video.videoWidth;
        const sourceHeight = video.videoHeight;
        const factor = this.zoom.native ? 1 : this.zoom.value;
        const cropWidth = sourceWidth / factor;
        const cropHeight = sourceHeight / factor;
        const scale = maxSide ? Math.min(1, maxSide / Math.max(cropWidth, cropHeight)) : 1;
        const canvas = document.createElement('canvas');
        canvas.width = even(cropWidth * scale);
        canvas.height = even(cropHeight * scale);
        const ctx = canvas.getContext('2d');
        ctx.drawImage(video, (sourceWidth - cropWidth) / 2, (sourceHeight - cropHeight) / 2, cropWidth, cropHeight,
            0, 0, canvas.width, canvas.height);
        return canvas;
    }

    async capturePhoto() {
        const video = this.el('video');
        if (!this.stream || !video || !video.videoWidth) { this.notify('OnNotice', 'not-ready', ''); return; }
        const takenAt = Date.now();
        const position = this.settings.location ? this.position : null;
        let canvas = null, small = null;
        try {
            canvas = this.grabFrame(video);
            if (this.settings.stamp) this.drawStamp(canvas.getContext('2d'), canvas.width, canvas.height, new Date(takenAt));
            this.feedback();
            const blob = await canvasToBlob(canvas, 'image/jpeg', PHOTO_QUALITY);
            small = scaled(canvas, canvas.width, canvas.height, THUMB_SIZE);
            const thumb = await canvasToBlob(small, 'image/jpeg', THUMB_QUALITY);
            this.addShot({
                kind: 'photo', blob, thumb, width: canvas.width, height: canvas.height, duration: 0,
                mime: 'image/jpeg', ext: 'jpg', takenAt, source: 'camera', position,
            });
        } catch (error) {
            this.notify('OnNotice', 'capture-failed', error.message || String(error));
        } finally {
            // Frees the canvases' memory now: iOS caps the total, and a burst of 4K frames would reach it.
            release(canvas);
            release(small);
        }
    }

    feedback() {
        const flash = this.el('flash');
        if (flash) flash.animate([{ opacity: 0.9 }, { opacity: 0 }], { duration: 300, easing: 'ease-out' });
        if (this.settings.sound) {
            try {
                this.shutterSound.currentTime = 0;
                this.shutterSound.play().catch(() => { });
            } catch { /* no sound */ }
        }
        if (navigator.vibrate) navigator.vibrate(20);
    }

    beep(frequency, milliseconds) {
        if (!this.settings.sound) return;
        try {
            const Context = window.AudioContext || window.webkitAudioContext;
            if (!Context) return;
            this.audio = this.audio || new Context();
            const ctx = this.audio;
            const now = ctx.currentTime;
            const oscillator = ctx.createOscillator();
            const gain = ctx.createGain();
            oscillator.frequency.value = frequency;
            gain.gain.setValueAtTime(0.0001, now);
            gain.gain.exponentialRampToValueAtTime(0.18, now + 0.01);
            gain.gain.exponentialRampToValueAtTime(0.0001, now + milliseconds / 1000);
            oscillator.connect(gain).connect(ctx.destination);
            oscillator.start(now);
            oscillator.stop(now + milliseconds / 1000 + 0.02);
        } catch { /* no sound */ }
    }

    // ---- video ----------------------------------------------------------------------------------------------------

    async ensureMic() {
        if (this.mic && this.mic.getAudioTracks().some(t => t.readyState === 'live')) return this.mic;
        if (this.micFailed) return null;
        if (this.micPromise) return this.micPromise;
        this.micPromise = (async () => {
            try {
                const mic = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true } });
                const stillWanted = !this.disposed && this.settings.mic &&
                    (this.settings.mode === 'video' || this.recording || this.recordingStarting);
                if (!stillWanted) { stopTracks(mic); return null; }
                this.mic = mic;
                return mic;
            } catch (error) {
                this.micFailed = true; // said once; asked again when the mode or the sound setting changes
                this.notify('OnNotice', 'mic-unavailable', error.name || '');
                return null;
            } finally {
                this.micPromise = null;
            }
        })();
        return this.micPromise;
    }

    releaseMic() {
        if (this.mic) stopTracks(this.mic);
        this.mic = null;
    }

    async startRecording() {
        const video = this.el('video');
        if (!this.stream || !video || !video.videoWidth) { this.notify('OnNotice', 'not-ready', ''); return; }
        if (typeof MediaRecorder === 'undefined') { this.notify('OnNotice', 'record-unsupported', ''); return; }
        this.recordingStarting = true;
        this.abortStart = false;
        try {
            if (this.retuning) await this.retuning; // a mode switch may still be changing the format
            const mic = this.settings.mic ? await this.ensureMic() : null;
            // Meanwhile (the microphone prompt can stay open) the user may have switched to Photo, opened a shot,
            // closed the dialog, or lost the camera: then there is nothing to record.
            const track = this.videoTrack();
            if (this.disposed || this.abortStart || this.settings.mode !== 'video' || this.view !== 'camera' ||
                !track || track.readyState !== 'live' || !video.videoWidth) return;

            const composite = this.settings.stamp || (!this.zoom.native && this.zoom.value > 1.01);
            const compositor = composite ? this.startCompositor(video) : null;
            const tracks = compositor ? compositor.stream.getVideoTracks() : this.stream.getVideoTracks();
            const audio = mic ? mic.getAudioTracks().filter(t => t.readyState === 'live') : [];
            const mime = pickRecordingType(audio.length > 0);
            const width = compositor ? compositor.width : video.videoWidth;
            const height = compositor ? compositor.height : video.videoHeight;
            let recorder;
            try {
                recorder = new MediaRecorder(new MediaStream([...tracks, ...audio]),
                    { mimeType: mime || undefined, videoBitsPerSecond: bitrateFor(Math.min(width, height)) });
            } catch {
                try {
                    recorder = new MediaRecorder(new MediaStream([...tracks, ...audio]));
                } catch (error) {
                    if (compositor) compositor.stop();
                    this.notify('OnNotice', 'record-failed', error.message || error.name || '');
                    return;
                }
            }

            const frame = this.grabFrame(video, 1280);
            const poster = scaled(frame, 0, 0, THUMB_SIZE);
            release(frame);
            let resolveDone = () => { };
            const session = {
                recorder, compositor, chunks: [], startedAt: Date.now(), poster, width, height,
                position: this.settings.location ? this.position : null, discard: false,
                done: new Promise(resolve => { resolveDone = resolve; }),
            };
            session.resolveDone = resolveDone;
            recorder.ondataavailable = e => { if (e.data && e.data.size) session.chunks.push(e.data); };
            recorder.onerror = e => this.notify('OnNotice', 'record-failed', (e.error && e.error.message) || '');
            recorder.onstop = () => {
                // Stopped by itself (an error, its tracks gone): the interface stops too, and the clip is kept.
                if (this.recording === session) this.endRecordingUi(session, false);
                this.finishRecording(session);
            };
            recorder.start(1000);
            this.recording = session;
            this.root.dataset.recording = '1';
            this.recordTicker = setInterval(() => this.renderRecordTime(), 250);
            this.renderRecordTime();
            this.beep(660, 120);
            this.holdWakeLock();
            this.notify('OnRecording', true);
        } finally {
            this.recordingStarting = false;
        }
    }

    // Stops a recording whatever is on screen, and resolves once its clip is a shot (or null): closing the dialog
    // waits for it, so the clip is counted - and saved or discarded knowingly - instead of vanishing.
    async finishRecordingNow() {
        if (this.recordingStarting) this.abortStart = true;
        const session = this.recording;
        if (!session) return null;
        this.stopRecording();
        return session.done;
    }

    renderRecordTime() {
        const session = this.recording;
        if (!session) return;
        const seconds = Math.floor((Date.now() - session.startedAt) / 1000);
        const label = this.el('rec-time');
        if (label) label.textContent = clock(seconds);
        if (seconds >= MAX_CLIP_SECONDS) {
            this.stopRecording();
            this.notify('OnNotice', 'max-length', String(MAX_CLIP_SECONDS));
        }
    }

    stopRecording(discard) {
        const session = this.recording;
        if (!session) return;
        this.endRecordingUi(session, discard);
        try { session.recorder.stop(); } catch { this.finishRecording(session); }
    }

    // What a stop looks like, whoever stops (the user, the length cap, the recorder itself): ticker, red badge,
    // wake lock and the component's state.
    endRecordingUi(session, discard) {
        if (this.recording !== session) return;
        this.recording = null;
        session.discard = !!discard;
        if (!session.duration) session.duration = (Date.now() - session.startedAt) / 1000;
        clearInterval(this.recordTicker);
        delete this.root.dataset.recording;
        const label = this.el('rec-time');
        if (label) label.textContent = '';
        this.releaseWakeLock();
        if (!discard) this.beep(440, 120);
        this.notify('OnRecording', false);
    }

    async finishRecording(session) {
        if (session.finished) return;
        session.finished = true;
        let described = null;
        try {
            if (session.compositor) session.compositor.stop();
            if (this.settings.mode !== 'video' || !this.settings.mic) this.releaseMic();
            if (!session.duration) session.duration = (Date.now() - session.startedAt) / 1000;
            if (session.discard || this.disposed || !session.chunks.length) return;
            const type = (session.recorder.mimeType || session.chunks[0].type || 'video/webm').split(';')[0];
            let blob = new Blob(session.chunks, { type });
            if (type === 'video/webm' && session.duration > 0) blob = await fixWebmDuration(blob, session.duration * 1000);
            let thumb = null;
            try { thumb = await canvasToBlob(session.poster, 'image/jpeg', THUMB_QUALITY); } catch { /* no poster */ }
            release(session.poster);
            described = this.addShot({
                kind: 'video', blob, thumb, width: session.width, height: session.height, duration: session.duration,
                mime: type, ext: extensionFor(type), takenAt: session.startedAt, source: 'camera', position: session.position,
            });
        } finally {
            session.resolveDone(described);
        }
    }

    // Stamp and digital zoom are not in the camera's own frames, so a clip that needs them is recorded from a canvas
    // that redraws each frame (capped at 1080p: a phone cannot composite 4K in real time). The canvas keeps the clip's
    // first size; each frame is fitted into it, since the camera's frame can change size mid-clip (a phone turned).
    startCompositor(video) {
        const scale = Math.min(1, COMPOSITE_MAX_SIDE / Math.max(video.videoWidth, video.videoHeight));
        const canvas = document.createElement('canvas');
        canvas.width = even(video.videoWidth * scale);
        canvas.height = even(video.videoHeight * scale);
        const ctx = canvas.getContext('2d');
        let running = true;
        const draw = () => {
            if (!running) return;
            const sourceWidth = video.videoWidth;
            const sourceHeight = video.videoHeight;
            if (sourceWidth && sourceHeight) {
                const z = this.zoom.native ? 1 : this.zoom.value;
                const cropWidth = sourceWidth / z;
                const cropHeight = sourceHeight / z;
                const fit = Math.min(canvas.width / cropWidth, canvas.height / cropHeight);
                const width = cropWidth * fit;
                const height = cropHeight * fit;
                ctx.fillStyle = '#000';
                ctx.fillRect(0, 0, canvas.width, canvas.height);
                ctx.drawImage(video, (sourceWidth - cropWidth) / 2, (sourceHeight - cropHeight) / 2, cropWidth, cropHeight,
                    (canvas.width - width) / 2, (canvas.height - height) / 2, width, height);
                if (this.settings.stamp) this.drawStamp(ctx, canvas.width, canvas.height, new Date());
            }
            if (video.requestVideoFrameCallback) video.requestVideoFrameCallback(draw);
            else requestAnimationFrame(draw);
        };
        draw();
        const stream = canvas.captureStream(30);
        return {
            stream, width: canvas.width, height: canvas.height,
            stop() { running = false; stopTracks(stream); release(canvas); },
        };
    }

    // The request resolves later: a lock that arrives after the recording ended is released at once.
    holdWakeLock() {
        if (!navigator.wakeLock) return;
        const request = navigator.wakeLock.request('screen')
            .then(lock => {
                if (this.wakeLockRequest === request) this.wakeLock = lock;
                else lock.release().catch(() => { });
            })
            .catch(() => { });
        this.wakeLockRequest = request;
    }

    releaseWakeLock() {
        this.wakeLockRequest = null;
        if (this.wakeLock) this.wakeLock.release().catch(() => { });
        this.wakeLock = null;
    }

    // ---- shots -------------------------------------------------------------------------------------------------

    addShot(data) {
        const id = `s${++this.sequence}`;
        const shot = {
            ...data, id,
            url: URL.createObjectURL(data.blob),
            thumbUrl: data.thumb ? URL.createObjectURL(data.thumb) : '',
            edited: false,
        };
        this.shots.set(id, shot);
        if (this.disposed) { this.remove(id); return null; }
        const described = describeShot(shot);
        this.notify('OnShot', described);
        return described;
    }

    blob(id) {
        const shot = this.shots.get(id);
        if (!shot) throw new Error(`Unknown shot ${id}`);
        return shot.blob;
    }

    thumb(id) {
        const shot = this.shots.get(id);
        if (!shot || !shot.thumb) throw new Error(`No thumbnail for ${id}`);
        return shot.thumb;
    }

    remove(id) {
        const shot = this.shots.get(id);
        if (!shot) return;
        URL.revokeObjectURL(shot.url);
        if (shot.thumbUrl) URL.revokeObjectURL(shot.thumbUrl);
        this.shots.delete(id);
    }

    async importFiles(files, source) {
        for (const file of files) {
            if (this.disposed) return;
            const kind = kindOf(file);
            if (!kind) { this.notify('OnNotice', 'import-unsupported', file.name); continue; }
            let thumb = null, width = 0, height = 0, duration = 0;
            try {
                if (kind === 'photo') {
                    const bitmap = await createImageBitmap(file);
                    width = bitmap.width;
                    height = bitmap.height;
                    thumb = await canvasToBlob(scaled(bitmap, width, height, THUMB_SIZE), 'image/jpeg', THUMB_QUALITY);
                    bitmap.close();
                } else {
                    ({ thumb, width, height, duration } = await videoPoster(file));
                }
            } catch { /* a format this browser cannot decode (HEIC...): kept, shown without a preview */ }
            const ext = storedExtension(file);
            this.addShot({
                kind, blob: file, thumb, width, height, duration, mime: file.type || '', ext,
                takenAt: file.lastModified || Date.now(), name: file.name, source, position: null,
            });
        }
    }

    // ---- markup ------------------------------------------------------------------------------------------------

    // style: the tool, colour and thickness the toolbar shows (kept from one photo to the next).
    async openMarkup(id, style) {
        this.closeMarkup();
        const shot = this.shots.get(id);
        const host = this.el('markup-host');
        const canvas = this.el('markup-canvas');
        const input = this.el('markup-text');
        if (!shot || shot.kind !== 'photo' || !host || !canvas) return false;
        const editor = new MarkupEditor(this, shot, host, canvas, input);
        if (style) {
            if (['arrow', 'rect', 'ellipse', 'pen', 'text'].includes(style.tool)) editor.tool = style.tool;
            if (/^#[0-9a-f]{6}$/i.test(style.color || '')) editor.color = style.color;
            if (MARKUP_WIDTHS[style.size]) editor.size = style.size;
        }
        this.editor = editor;
        try {
            await editor.open();
        } catch (error) {
            if (this.editor === editor) this.closeMarkup();
            this.notify('OnNotice', 'markup-failed', error.message || '');
            return false;
        }
        return this.editor === editor;
    }

    markup(command, value) {
        const editor = this.editor;
        if (!editor) return;
        switch (command) {
            case 'tool': editor.tool = value; editor.commitText(); break;
            case 'color': editor.color = value; editor.restyleText(); break;
            case 'size': editor.size = value; editor.restyleText(); break;
            case 'undo': editor.undo(); break;
            case 'redo': editor.redo(); break;
            case 'clear': editor.clear(); break;
        }
    }

    async applyMarkup() {
        const editor = this.editor;
        if (!editor) return null;
        editor.commitText();
        const shot = editor.shot;
        if (!editor.shapes.length) { this.closeMarkup(); return null; } // nothing drawn: the shot is unchanged
        const { blob, thumb, width, height } = await editor.export();
        this.closeMarkup();
        URL.revokeObjectURL(shot.url);
        if (shot.thumbUrl) URL.revokeObjectURL(shot.thumbUrl);
        Object.assign(shot, {
            blob, thumb, width, height, mime: 'image/jpeg', ext: 'jpg', edited: true,
            url: URL.createObjectURL(blob), thumbUrl: URL.createObjectURL(thumb),
        });
        return describeShot(shot);
    }

    closeMarkup() {
        if (this.editor) this.editor.close();
        this.editor = null;
    }

    // ---- events ------------------------------------------------------------------------------------------------

    onClick(e) {
        const markup = e.target.closest('[data-markup]');
        if (markup && this.root.contains(markup) && !markup.disabled) {
            const [command, value] = markup.dataset.markup.split(':');
            this.markup(command, value);
            return;
        }
        const action = e.target.closest('[data-cam-action]');
        if (!action || !this.root.contains(action)) return;
        if (action.dataset.camAction === 'shutter') {
            e.preventDefault();
            this.shutter();
        }
    }

    onChange(e) {
        const input = e.target;
        if (!input || !input.files || !input.dataset || !input.dataset.cam) return;
        const role = input.dataset.cam;
        if (role !== 'import' && role !== 'native') return;
        const files = [...input.files];
        input.value = '';
        if (role === 'native') this.resumeAfterNative();
        this.importFiles(files, role === 'native' ? 'native' : 'import');
    }

    // The phone's own camera app cannot open the camera while this page holds it (Android ends our stream, or the
    // app fails): release it when that picker opens, and ask the component to start it again when the user is back.
    pauseForNative() {
        if (!this.stream) return;
        this.nativePaused = true;
        this.stopStream();
    }

    resumeAfterNative() {
        if (!this.nativePaused || this.disposed) return;
        this.nativePaused = false;
        this.notify('OnResumeRequested');
    }

    // Keeps a text being typed in the markup editor alive when a colour or size is picked: the button must not take
    // the focus (the text would be committed in the old style before the new one applies).
    keepTextFocus(e) {
        if (this.editor && this.editor.textAt && e.target.closest('[data-markup]')) e.preventDefault();
    }

    onInput(e) {
        if (e.target && e.target.dataset && e.target.dataset.cam === 'zoom') this.setZoom(e.target.value);
    }

    onWheel(e) {
        if (this.view !== 'camera' || !e.target.closest('[data-cam="stage"]')) return;
        if (this.zoom.max <= this.zoom.min) return;
        e.preventDefault();
        this.setZoom(this.zoom.value * Math.exp(-e.deltaY * 0.0015));
    }

    onPointerDown(e) {
        this.keepTextFocus(e);
        if (e.target.closest && e.target.closest('[data-cam="native"]')) this.pauseForNative();
        if (e.pointerType !== 'touch' || this.view !== 'camera' || !e.target.closest('[data-cam="stage"]')) return;
        this.pinch.set(e.pointerId, { x: e.clientX, y: e.clientY });
        if (this.pinch.size === 2) {
            this.pinchStart = { distance: pinchDistance(this.pinch), zoom: this.zoom.value };
        }
    }

    onPointerMove(e) {
        if (!this.pinch.has(e.pointerId)) return;
        this.pinch.set(e.pointerId, { x: e.clientX, y: e.clientY });
        if (this.pinch.size === 2 && this.pinchStart && this.pinchStart.distance > 0) {
            this.setZoom(this.pinchStart.zoom * pinchDistance(this.pinch) / this.pinchStart.distance);
        }
    }

    onPointerUp(e) {
        this.pinch.delete(e.pointerId);
        if (this.pinch.size < 2) this.pinchStart = null;
    }

    onKey(e) {
        if (this.disposed || !this.root.isConnected || e.defaultPrevented) return;
        const target = e.target || {};
        const tag = target.tagName || '';
        const typing = (tag === 'INPUT' && !['range', 'checkbox', 'radio', 'button'].includes(target.type)) ||
            tag === 'TEXTAREA' || tag === 'SELECT' || target.isContentEditable;
        const key = e.key;
        // The zoom slider and the settings switches keep their own keys (arrows move the slider, Space toggles).
        if (tag === 'INPUT' && ['range', 'checkbox', 'radio'].includes(target.type) &&
            (key.startsWith('Arrow') || key === ' ' || key === 'Enter' || key === 'Home' || key === 'End')) return;

        if (this.editor) {
            if (typing) return;
            const mod = e.ctrlKey || e.metaKey;
            if (mod && key.toLowerCase() === 'z') { e.preventDefault(); if (e.shiftKey) this.editor.redo(); else this.editor.undo(); return; }
            if (mod && key.toLowerCase() === 'y') { e.preventDefault(); this.editor.redo(); return; }
            if (key === 'Escape') { e.preventDefault(); this.notify('OnKey', 'Escape'); }
            return;
        }
        if (typing) {
            if (key === 'Escape') this.notify('OnKey', 'Escape');
            return;
        }
        if (e.ctrlKey || e.metaKey || e.altKey) return;
        if (key === ' ' || key === 'Enter') {
            if (tag === 'BUTTON' || tag === 'A' || tag === 'LABEL' || tag === 'SUMMARY' || tag === 'INPUT') return;
            if (e.repeat) { e.preventDefault(); return; } // a held key is one photo, not a burst
            if (this.view === 'camera') { e.preventDefault(); this.shutter(); }
            return;
        }
        if (this.view === 'camera' && (key === '+' || key === '=')) { this.setZoom(this.zoom.value * 1.25); return; }
        if (this.view === 'camera' && key === '-') { this.setZoom(this.zoom.value / 1.25); return; }
        const forwarded = ['Escape', 'ArrowLeft', 'ArrowRight', 'Delete'];
        const letters = ['g', 'm', 't'];
        if (forwarded.includes(key) || letters.includes(key.toLowerCase())) {
            if (key === 'Escape' || key.startsWith('Arrow')) e.preventDefault();
            this.notify('OnKey', key.length === 1 ? key.toLowerCase() : key);
        }
    }

    // ---- teardown ----------------------------------------------------------------------------------------------

    dispose() {
        if (this.disposed) return;
        this.cancelCountdown();
        if (this.recording) this.stopRecording(true);
        this.disposed = true;
        this.generation += 1;
        this.stopStream();
        this.releaseMic();
        this.releaseWakeLock();
        if (this.geoWatch != null && navigator.geolocation) navigator.geolocation.clearWatch(this.geoWatch);
        this.geoWatch = null;
        this.closeMarkup();
        clearInterval(this.clock);
        clearInterval(this.recordTicker);
        this.resizeObserver.disconnect();
        for (const [name, handler] of Object.entries(this.handlers)) this.root.removeEventListener(name, handler);
        document.removeEventListener('keydown', this.onKeyDown);
        document.removeEventListener('visibilitychange', this.onVisibility);
        window.removeEventListener('focus', this.onWindowFocus);
        for (const id of [...this.shots.keys()]) this.remove(id);
        if (this.audio) this.audio.close().catch(() => { });
        this.shutterSound.removeAttribute('src');
    }
}

// ---- markup editor ---------------------------------------------------------------------------------------------

const MARKUP_WIDTHS = { s: 0.0025, m: 0.005, l: 0.01 };
const MARKUP_TEXT = { s: 0.025, m: 0.04, l: 0.06 };

class MarkupEditor {
    constructor(studio, shot, host, canvas, input) {
        this.studio = studio;
        this.shot = shot;
        this.host = host;
        this.canvas = canvas;
        this.input = input;
        this.tool = 'arrow';
        this.color = '#ff3b30';
        this.size = 'm';
        this.shapes = [];
        this.undone = [];
        this.current = null;
        this.textAt = null;
    }

    async open() {
        this.image = await createImageBitmap(this.shot.blob);
        this.handlers = {
            pointerdown: e => this.down(e),
            pointermove: e => this.move(e),
            pointerup: e => this.up(e),
            pointercancel: e => this.up(e),
        };
        for (const [name, handler] of Object.entries(this.handlers)) this.canvas.addEventListener(name, handler);
        this.onTextKey = e => {
            if (e.key === 'Enter') { e.preventDefault(); e.stopPropagation(); this.commitText(); }
            else if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); this.cancelText(); }
        };
        this.onTextBlur = () => this.commitText();
        if (this.input) {
            this.input.addEventListener('keydown', this.onTextKey);
            this.input.addEventListener('blur', this.onTextBlur);
        }
        this.observer = new ResizeObserver(() => this.layout());
        this.observer.observe(this.host);
        this.layout();
        this.report();
    }

    close() {
        this.closed = true;
        if (this.observer) this.observer.disconnect();
        if (this.handlers) for (const [name, handler] of Object.entries(this.handlers)) this.canvas.removeEventListener(name, handler);
        if (this.input) {
            this.input.removeEventListener('keydown', this.onTextKey);
            this.input.removeEventListener('blur', this.onTextBlur);
            this.input.hidden = true;
            this.input.value = '';
        }
        if (this.image) this.image.close();
    }

    get base() {
        return Math.max(this.image.width, this.image.height);
    }

    layout() {
        if (this.closed) return;
        const width = this.host.clientWidth;
        const height = this.host.clientHeight;
        const dpr = window.devicePixelRatio || 1;
        this.dpr = dpr;
        this.canvas.width = Math.max(1, Math.round(width * dpr));
        this.canvas.height = Math.max(1, Math.round(height * dpr));
        this.canvas.style.width = `${width}px`;
        this.canvas.style.height = `${height}px`;
        this.scale = Math.min(width / this.image.width, height / this.image.height);
        this.offsetX = (width - this.image.width * this.scale) / 2;
        this.offsetY = (height - this.image.height * this.scale) / 2;
        this.draw();
    }

    // Pointer coordinates are on screen, the layout is in CSS pixels: they differ while an ancestor is scaled (the
    // dialog's opening animation, a zoomed page), so convert through the canvas's real on-screen size.
    toImage(e) {
        const rect = this.canvas.getBoundingClientRect();
        const kx = rect.width / (this.canvas.clientWidth || rect.width) || 1;
        const ky = rect.height / (this.canvas.clientHeight || rect.height) || 1;
        return {
            x: Math.min(this.image.width, Math.max(0, ((e.clientX - rect.left) / kx - this.offsetX) / this.scale)),
            y: Math.min(this.image.height, Math.max(0, ((e.clientY - rect.top) / ky - this.offsetY) / this.scale)),
        };
    }

    draw() {
        const ctx = this.canvas.getContext('2d');
        ctx.setTransform(this.dpr, 0, 0, this.dpr, 0, 0);
        ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);
        ctx.drawImage(this.image, this.offsetX, this.offsetY, this.image.width * this.scale, this.image.height * this.scale);
        ctx.save();
        ctx.translate(this.offsetX, this.offsetY);
        ctx.scale(this.scale, this.scale);
        ctx.beginPath();
        ctx.rect(0, 0, this.image.width, this.image.height);
        ctx.clip();
        const devicePixels = this.dpr * this.scale; // per image pixel: halos are in device pixels
        for (const shape of this.shapes) drawShape(ctx, shape, devicePixels);
        if (this.current) drawShape(ctx, this.current, devicePixels);
        ctx.restore();
    }

    down(e) {
        if (e.pointerType === 'mouse' && e.button !== 0) return;
        e.preventDefault();
        if (this.current) return; // a second finger while drawing: the first one owns the stroke
        const point = this.toImage(e);
        if (this.tool === 'text') { this.placeText(point); return; }
        this.commitText();
        // Keeps the stroke when the pointer leaves the canvas; a pointer the browser cannot capture must not cost the stroke.
        try { this.canvas.setPointerCapture(e.pointerId); } catch { /* not capturable */ }
        this.current = {
            type: this.tool, color: this.color, width: this.base * MARKUP_WIDTHS[this.size],
            points: [point], x1: point.x, y1: point.y, x2: point.x, y2: point.y, pointerId: e.pointerId,
        };
        this.draw();
    }

    move(e) {
        if (!this.current || e.pointerId !== this.current.pointerId) return;
        const point = this.toImage(e);
        const shape = this.current;
        if (shape.type === 'pen') {
            const last = shape.points[shape.points.length - 1];
            if (Math.hypot(point.x - last.x, point.y - last.y) * this.scale < 1.5) return;
            shape.points.push(point);
        }
        shape.x2 = point.x;
        shape.y2 = point.y;
        this.draw();
    }

    up(e) {
        const shape = this.current;
        if (!shape || (e && e.pointerId !== shape.pointerId)) return;
        this.current = null;
        delete shape.pointerId;
        const length = Math.hypot(shape.x2 - shape.x1, shape.y2 - shape.y1) * this.scale;
        const kept = shape.type === 'pen' ? shape.points.length > 1 : length >= 4;
        if (kept) {
            this.shapes.push(shape);
            this.undone = [];
        }
        this.draw();
        this.report();
    }

    placeText(point) {
        this.commitText();
        if (!this.input) return;
        this.textAt = point;
        const size = this.base * MARKUP_TEXT[this.size];
        this.input.hidden = false;
        this.input.value = '';
        this.input.style.left = `${this.offsetX + point.x * this.scale}px`;
        this.input.style.top = `${this.offsetY + point.y * this.scale}px`;
        this.restyleText();
        this.textSize = size;
        setTimeout(() => this.input && this.input.focus(), 0);
    }

    // The box is typed in at 16px and scaled to the text's real size: iOS zooms the whole page into any field under 16px.
    restyleText() {
        if (!this.input || this.input.hidden) return;
        const size = this.base * MARKUP_TEXT[this.size];
        this.textSize = size;
        this.input.style.fontSize = '16px';
        this.input.style.transformOrigin = '0 0';
        this.input.style.transform = `scale(${Math.max(12, size * this.scale) / 16})`;
        this.input.style.color = this.color;
    }

    commitText() {
        if (!this.input || this.input.hidden || !this.textAt) return;
        const text = this.input.value.trim();
        const at = this.textAt;
        this.textAt = null;
        this.input.hidden = true;
        this.input.value = '';
        if (!text) return;
        this.shapes.push({ type: 'text', color: this.color, size: this.base * MARKUP_TEXT[this.size], x1: at.x, y1: at.y, text });
        this.undone = [];
        this.draw();
        this.report();
    }

    cancelText() {
        if (!this.input) return;
        this.textAt = null;
        this.input.hidden = true;
        this.input.value = '';
    }

    undo() {
        if (!this.shapes.length) return;
        this.undone.push(this.shapes.pop());
        this.draw();
        this.report();
    }

    redo() {
        if (!this.undone.length) return;
        this.shapes.push(this.undone.pop());
        this.draw();
        this.report();
    }

    clear() {
        if (!this.shapes.length) return;
        this.undone = [];
        this.shapes = [];
        this.draw();
        this.report();
    }

    report() {
        this.studio.notify('OnMarkupState', this.shapes.length > 0, this.undone.length > 0);
    }

    // At full size; if the browser cannot hold a canvas that large (older iOS caps canvases at 16.7 MP), at the largest
    // size it can.
    async export() {
        const full = this.image.width * this.image.height;
        for (const limit of [Infinity, 16_777_216, 8_000_000]) {
            const factor = full > limit ? Math.sqrt(limit / full) : 1;
            if (limit !== Infinity && factor === 1) continue;
            const canvas = document.createElement('canvas');
            canvas.width = Math.floor(this.image.width * factor);
            canvas.height = Math.floor(this.image.height * factor);
            let small = null;
            try {
                const ctx = canvas.getContext('2d');
                if (!ctx) continue;
                ctx.scale(factor, factor);
                ctx.drawImage(this.image, 0, 0);
                for (const shape of this.shapes) drawShape(ctx, shape, factor);
                const blob = await canvasToBlob(canvas, 'image/jpeg', PHOTO_QUALITY);
                small = scaled(canvas, canvas.width, canvas.height, THUMB_SIZE);
                const thumb = await canvasToBlob(small, 'image/jpeg', THUMB_QUALITY);
                return { blob, thumb, width: canvas.width, height: canvas.height };
            } catch {
                // too large for this browser: try smaller
            } finally {
                release(canvas);
                release(small);
            }
        }
        throw new Error('The image is too large to mark up on this device.');
    }
}

// Every mark gets a soft dark halo so a red arrow still reads on a dark beam and a white one on the sky. The halo is
// sized in image pixels like the mark; the canvas draws blurs in device pixels, hence devicePixels (per image pixel).
function drawShape(ctx, shape, devicePixels = 1) {
    ctx.save();
    ctx.strokeStyle = shape.color;
    ctx.fillStyle = shape.color;
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    ctx.lineWidth = shape.width || 1;
    ctx.shadowColor = 'rgba(0, 0, 0, 0.45)';
    ctx.shadowBlur = Math.max(1, Math.max(2, (shape.width || shape.size / 8) * 0.9) * devicePixels);
    const { x1, y1, x2, y2 } = shape;
    switch (shape.type) {
        case 'pen': {
            const points = shape.points;
            ctx.beginPath();
            ctx.moveTo(points[0].x, points[0].y);
            for (let i = 1; i < points.length - 1; i++) {
                const midX = (points[i].x + points[i + 1].x) / 2;
                const midY = (points[i].y + points[i + 1].y) / 2;
                ctx.quadraticCurveTo(points[i].x, points[i].y, midX, midY);
            }
            const last = points[points.length - 1];
            ctx.lineTo(last.x, last.y);
            ctx.stroke();
            break;
        }
        case 'arrow': {
            const angle = Math.atan2(y2 - y1, x2 - x1);
            const head = Math.max(shape.width * 4.5, 12);
            const baseX = x2 - head * 0.8 * Math.cos(angle);
            const baseY = y2 - head * 0.8 * Math.sin(angle);
            ctx.beginPath();
            ctx.moveTo(x1, y1);
            ctx.lineTo(baseX, baseY);
            ctx.stroke();
            ctx.beginPath();
            ctx.moveTo(x2, y2);
            ctx.lineTo(x2 - head * Math.cos(angle - 0.45), y2 - head * Math.sin(angle - 0.45));
            ctx.lineTo(x2 - head * Math.cos(angle + 0.45), y2 - head * Math.sin(angle + 0.45));
            ctx.closePath();
            ctx.fill();
            break;
        }
        case 'rect':
            ctx.strokeRect(Math.min(x1, x2), Math.min(y1, y2), Math.abs(x2 - x1), Math.abs(y2 - y1));
            break;
        case 'ellipse':
            ctx.beginPath();
            ctx.ellipse((x1 + x2) / 2, (y1 + y2) / 2, Math.abs(x2 - x1) / 2, Math.abs(y2 - y1) / 2, 0, 0, Math.PI * 2);
            ctx.stroke();
            break;
        case 'text': {
            ctx.font = `700 ${shape.size}px system-ui, -apple-system, "Segoe UI", Roboto, sans-serif`;
            ctx.textBaseline = 'top';
            ctx.shadowBlur = Math.max(1, shape.size * 0.25 * devicePixels);
            ctx.shadowColor = 'rgba(0, 0, 0, 0.7)';
            ctx.lineWidth = shape.size * 0.12;
            ctx.strokeStyle = 'rgba(0, 0, 0, 0.55)';
            ctx.strokeText(shape.text, x1, y1);
            ctx.fillText(shape.text, x1, y1);
            break;
        }
    }
    ctx.restore();
}

// ---- helpers -----------------------------------------------------------------------------------------------------

function describeShot(shot) {
    const position = shot.position || null;
    return {
        id: shot.id,
        kind: shot.kind,
        url: shot.url,
        thumbUrl: shot.thumbUrl,
        width: shot.width || 0,
        height: shot.height || 0,
        duration: shot.duration || 0,
        size: shot.blob.size,
        mime: shot.mime || '',
        ext: shot.ext || '',
        takenAt: Math.round(shot.takenAt),
        tzOffset: new Date(shot.takenAt).getTimezoneOffset(),
        name: shot.name || '',
        source: shot.source,
        lat: position ? position.lat : null,
        lon: position ? position.lon : null,
        hasThumb: !!shot.thumb,
        edited: !!shot.edited,
    };
}

function classify(error) {
    switch (error && error.name) {
        case 'NotAllowedError':
        case 'PermissionDeniedError':
        case 'SecurityError':
            return window.isSecureContext === false ? 'insecure' : 'denied';
        case 'NotFoundError':
        case 'DevicesNotFoundError':
        case 'OverconstrainedError':
            return 'notfound';
        case 'NotReadableError':
        case 'TrackStartError':
        case 'AbortError':
            return 'busy';
        default:
            return 'other';
    }
}

function guessFacing(label) {
    if (!label) return '';
    if (/back|rear|environment|arrière|trasera|rück/i.test(label)) return 'environment';
    if (/front|user|facetime|avant|selfie/i.test(label)) return 'user';
    return '';
}

function stopTracks(stream) {
    for (const track of stream.getTracks()) track.stop();
}

function waitFor(condition, milliseconds) {
    return new Promise(resolve => {
        const started = Date.now();
        const check = () => {
            if (condition() || Date.now() - started > milliseconds) resolve();
            else setTimeout(check, 30);
        };
        check();
    });
}

function even(value) {
    return Math.max(2, Math.round(value / 2) * 2);
}

// Gives a canvas's memory back at once (iOS caps the total canvas memory of a page).
function release(canvas) {
    if (canvas && typeof canvas.getContext === 'function') {
        canvas.width = 0;
        canvas.height = 0;
    }
}

function canvasToBlob(canvas, type, quality) {
    return new Promise((resolve, reject) => {
        canvas.toBlob(blob => (blob ? resolve(blob) : reject(new Error('The image could not be encoded.'))), type, quality);
    });
}

// A copy of an image (canvas, bitmap or video frame) no longer than maxSide on its long side.
function scaled(source, width, height, maxSide) {
    width = width || source.width || source.videoWidth;
    height = height || source.height || source.videoHeight;
    const scale = Math.min(1, maxSide / Math.max(width, height));
    const canvas = document.createElement('canvas');
    canvas.width = Math.max(1, Math.round(width * scale));
    canvas.height = Math.max(1, Math.round(height * scale));
    const ctx = canvas.getContext('2d');
    ctx.imageSmoothingQuality = 'high';
    ctx.drawImage(source, 0, 0, canvas.width, canvas.height);
    return canvas;
}

function roundRect(ctx, x, y, width, height, radius) {
    ctx.beginPath();
    if (ctx.roundRect) { ctx.roundRect(x, y, width, height, radius); return; }
    ctx.rect(x, y, width, height);
}

function pinchDistance(points) {
    const [a, b] = [...points.values()];
    return Math.hypot(a.x - b.x, a.y - b.y);
}

function clock(seconds) {
    const m = Math.floor(seconds / 60);
    const s = seconds % 60;
    return `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
}

function formatDate(date, culture) {
    try {
        return new Intl.DateTimeFormat(culture || undefined, {
            year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit',
        }).format(date);
    } catch {
        return date.toLocaleString();
    }
}

function formatPosition(position) {
    const lat = `${Math.abs(position.lat).toFixed(5)}° ${position.lat >= 0 ? 'N' : 'S'}`;
    const lon = `${Math.abs(position.lon).toFixed(5)}° ${position.lon >= 0 ? 'E' : 'W'}`;
    const accuracy = position.accuracy ? `  ±${Math.round(position.accuracy)} m` : '';
    return `${lat}  ${lon}${accuracy}`;
}

function pickRecordingType(withAudio) {
    if (typeof MediaRecorder === 'undefined' || !MediaRecorder.isTypeSupported) return '';
    const candidates = withAudio
        ? ['video/mp4;codecs=avc1.42E01E,mp4a.40.2', 'video/mp4;codecs=avc1,opus', 'video/mp4',
            'video/webm;codecs=vp9,opus', 'video/webm;codecs=vp8,opus', 'video/webm']
        : ['video/mp4;codecs=avc1.42E01E', 'video/mp4;codecs=avc1', 'video/mp4',
            'video/webm;codecs=vp9', 'video/webm;codecs=vp8', 'video/webm'];
    return candidates.find(type => MediaRecorder.isTypeSupported(type)) || '';
}

function bitrateFor(height) {
    const shortSide = height || 0;
    if (shortSide >= 2000) return 12_000_000;
    if (shortSide >= 1000) return 5_000_000;
    if (shortSide >= 700) return 2_500_000;
    return 1_500_000;
}

function extensionFor(type) {
    const mime = (type || '').split(';')[0].toLowerCase();
    return {
        'image/jpeg': 'jpg', 'image/png': 'png', 'image/webp': 'webp', 'image/gif': 'gif',
        'image/bmp': 'bmp',
        'video/mp4': 'mp4', 'video/webm': 'webm', 'video/quicktime': 'mov', 'video/x-m4v': 'm4v',
    }[mime] || '';
}

function extensionOf(file) {
    const match = /\.([a-z0-9]+)$/i.exec(file.name || '');
    return match ? match[1].toLowerCase() : '';
}

function kindOf(file) {
    const ext = extensionOf(file);
    const type = (file.type || '').toLowerCase().split(';')[0];
    if (PHOTO_EXTENSIONS.includes(ext) || (type.startsWith('image/') && extensionFor(type))) return 'photo';
    if (VIDEO_EXTENSIONS.includes(ext) || (type.startsWith('video/') && extensionFor(type))) return 'video';
    return null;
}

// The extension the server will store: the file's own when it is one we accept, else the one its type implies.
function storedExtension(file) {
    const ext = extensionOf(file);
    return PHOTO_EXTENSIONS.includes(ext) || VIDEO_EXTENSIONS.includes(ext) ? ext : extensionFor(file.type);
}

function videoPoster(blob) {
    return new Promise((resolve, reject) => {
        const url = URL.createObjectURL(blob);
        const video = document.createElement('video');
        video.muted = true;
        video.playsInline = true;
        video.preload = 'auto';
        let settled = false;
        const done = (result, error) => {
            if (settled) return;
            settled = true;
            clearTimeout(timer);
            video.removeAttribute('src');
            video.load();
            URL.revokeObjectURL(url);
            if (error) reject(error); else resolve(result);
        };
        const timer = setTimeout(() => done(null, new Error('timeout')), 8000);
        // Seek as soon as the metadata is there: iOS may never load more than metadata for a video nobody plays.
        let seeking = false;
        const seek = () => {
            if (seeking) return;
            seeking = true;
            const duration = Number.isFinite(video.duration) ? video.duration : 0;
            video.currentTime = duration > 1 ? 0.5 : 0;
        };
        video.addEventListener('loadedmetadata', seek, { once: true });
        video.addEventListener('loadeddata', seek, { once: true });
        video.addEventListener('seeked', async () => {
            try {
                const thumb = await canvasToBlob(scaled(video, video.videoWidth, video.videoHeight, THUMB_SIZE), 'image/jpeg', THUMB_QUALITY);
                done({
                    thumb, width: video.videoWidth, height: video.videoHeight,
                    duration: Number.isFinite(video.duration) ? video.duration : 0,
                });
            } catch (error) {
                done(null, error);
            }
        }, { once: true });
        video.addEventListener('error', () => done(null, new Error('decode')), { once: true });
        video.src = url;
        video.load();
    });
}

// ---- WebM duration ---------------------------------------------------------------------------------------------
// MediaRecorder writes WebM as a live stream: the file has no Duration (Chrome) or a Duration of 0 (Firefox), so a
// player shows no length and cannot seek until it has read the whole clip. Insert Duration into the Segment's Info
// element, or overwrite a zero one in place. Only the layouts MediaRecorder writes are handled (Segment of unknown size;
// before Info at most an empty SeekHead, as Firefox writes); anything else is returned untouched.

async function fixWebmDuration(blob, durationMs) {
    try {
        const head = new Uint8Array(await blob.slice(0, Math.min(blob.size, 64 * 1024)).arrayBuffer());
        let id = readEbmlId(head, 0);
        if (!id || id.value !== 0x1A45DFA3) return blob;
        let size = readEbmlSize(head, id.length);
        if (!size || size.unknown) return blob;
        let position = id.length + size.length + size.value;

        id = readEbmlId(head, position);
        if (!id || id.value !== 0x18538067) return blob;
        size = readEbmlSize(head, position + id.length);
        if (!size || !size.unknown) return blob;
        position += id.length + size.length;

        while (position < head.length) {
            const childId = readEbmlId(head, position);
            if (!childId) return blob;
            const childSize = readEbmlSize(head, position + childId.length);
            if (!childSize || childSize.unknown) return blob;
            const dataStart = position + childId.length + childSize.length;
            if (childId.value === 0x114D9B74) {
                if (childSize.value > 0) return blob; // a real SeekHead: its offsets would move
                position = dataStart;                  // Firefox's empty one: nothing points past it
                continue;
            }
            if (childId.value === 0x1549A966) {
                if (dataStart + childSize.value > head.length) return blob;
                const info = head.subarray(dataStart, dataStart + childSize.value);
                let timecodeScale = 1_000_000;
                let existing = null;
                for (let p = 0; p < info.length;) {
                    const elementId = readEbmlId(info, p);
                    const elementSize = elementId && readEbmlSize(info, p + elementId.length);
                    if (!elementId || !elementSize || elementSize.unknown) return blob;
                    const valueStart = p + elementId.length + elementSize.length;
                    if (elementId.value === 0x4489) existing = { start: valueStart, length: elementSize.value };
                    if (elementId.value === 0x2AD7B1) {
                        let scale = 0;
                        for (let i = 0; i < elementSize.value; i++) scale = scale * 256 + info[valueStart + i];
                        if (scale > 0) timecodeScale = scale;
                    }
                    p = valueStart + elementSize.value;
                }
                const ticks = durationMs * 1_000_000 / timecodeScale;
                if (existing) {
                    if (existing.length !== 4 && existing.length !== 8) return blob;
                    const view = new DataView(info.buffer, info.byteOffset + existing.start, existing.length);
                    const current = existing.length === 8 ? view.getFloat64(0) : view.getFloat32(0);
                    if (current > 0) return blob; // a real Duration: keep it
                    const bytes = new Uint8Array(existing.length);
                    if (existing.length === 8) new DataView(bytes.buffer).setFloat64(0, ticks);
                    else new DataView(bytes.buffer).setFloat32(0, ticks);
                    const at = dataStart + existing.start;
                    return new Blob([blob.slice(0, at), bytes, blob.slice(at + existing.length)], { type: blob.type });
                }
                const duration = new Uint8Array(11);
                duration.set([0x44, 0x89, 0x88]);
                new DataView(duration.buffer).setFloat64(3, durationMs * 1_000_000 / timecodeScale);
                const infoHeader = new Uint8Array([
                    ...head.subarray(position, position + childId.length),
                    ...encodeEbmlSize(childSize.value + duration.length, childSize.length),
                ]);
                return new Blob([blob.slice(0, position), infoHeader, info, duration, blob.slice(dataStart + childSize.value)],
                    { type: blob.type });
            }
            position = dataStart + childSize.value;
        }
        return blob;
    } catch {
        return blob;
    }
}

function readEbmlId(bytes, position) {
    const first = bytes[position];
    if (first === undefined || first === 0) return null;
    let length = 1;
    for (let mask = 0x80; length <= 4 && !(first & mask); mask >>= 1) length++;
    if (length > 4 || position + length > bytes.length) return null;
    let value = 0;
    for (let i = 0; i < length; i++) value = value * 256 + bytes[position + i];
    return { length, value };
}

function readEbmlSize(bytes, position) {
    const first = bytes[position];
    if (first === undefined || first === 0) return null;
    let length = 1;
    let mask = 0x80;
    while (length <= 8 && !(first & mask)) { mask >>= 1; length++; }
    if (length > 8 || position + length > bytes.length) return null;
    let value = first & (mask - 1);
    let unknown = value === mask - 1;
    for (let i = 1; i < length; i++) {
        const byte = bytes[position + i];
        value = value * 256 + byte;
        if (byte !== 0xff) unknown = false;
    }
    return { length, value, unknown };
}

function encodeEbmlSize(value, preferredLength) {
    let length = Math.max(1, preferredLength);
    while (length < 8 && value >= 2 ** (7 * length) - 1) length++;
    const bytes = new Uint8Array(length);
    let rest = value;
    for (let i = length - 1; i >= 0; i--) {
        bytes[i] = rest % 256;
        rest = Math.floor(rest / 256);
    }
    bytes[0] |= 0x80 >> (length - 1);
    return bytes;
}
