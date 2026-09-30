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

const PHOTO_EXTENSIONS = ['jpg', 'jpeg', 'png', 'webp', 'gif', 'heic', 'heif'];
const VIDEO_EXTENSIONS = ['mp4', 'webm', 'mov', 'm4v'];

export function create(root, dotnet, options) {
    return new CameraStudio(root, dotnet, options || {});
}

export function loadSettings() {
    try { return JSON.parse(localStorage.getItem(SETTINGS_KEY) || 'null'); } catch { return null; }
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
        };
        for (const [name, handler] of Object.entries(this.handlers)) {
            root.addEventListener(name, handler, name === 'wheel' ? { passive: false } : undefined);
        }
        this.onKeyDown = e => this.onKey(e);
        document.addEventListener('keydown', this.onKeyDown);
        this.onVisibility = () => this.checkStream();
        document.addEventListener('visibilitychange', this.onVisibility);

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
            if (this.stream === stream && !this.disposed) this.notify('OnStreamEnded');
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
        const devices = await this.listDevices();
        if (devices.length < 2) return result.ok ? result : this.start({});
        const index = devices.findIndex(d => d.id === previous);
        return this.start({ deviceId: devices[(index + 1) % devices.length].id });
    }

    // Resolution 'auto' follows the mode: the sharpest the camera gives for photos, 1080p for video (a 4K clip is
    // several times larger to keep in memory and to upload, for little gain on a site inspection).
    async retune() {
        const track = this.videoTrack();
        if (!track || this.recording) return;
        const size = this.sizeFor(this.settings.resolution, this.settings.mode);
        try {
            await track.applyConstraints({ width: { ideal: size.width }, height: { ideal: size.height }, frameRate: { ideal: 30 } });
        } catch { /* keeps the current format */ }
        const video = this.el('video');
        if (video) await waitFor(() => video.videoWidth > 0, 2000);
        this.layoutFrame();
        if (this.stream && !this.disposed) this.notify('OnStreamInfo', { ok: true, ...(await this.describeStream()) });
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
        if (format && this.stream) this.retune();
        if (settings.mode === 'video' && settings.mic) this.ensureMic();
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
        if (this.disposed || this.view !== 'camera') return;
        if (this.countdown) { this.cancelCountdown(); return; }
        if (this.settings.mode === 'video') {
            if (this.recording) { this.stopRecording(); return; }
            if (this.recordingStarting) return;
            this.afterTimer(() => this.startRecording());
        } else {
            this.afterTimer(() => this.capturePhoto());
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
        const canvas = this.grabFrame(video);
        if (this.settings.stamp) this.drawStamp(canvas.getContext('2d'), canvas.width, canvas.height, new Date(takenAt));
        this.feedback();
        const position = this.settings.location ? this.position : null;
        try {
            const blob = await canvasToBlob(canvas, 'image/jpeg', PHOTO_QUALITY);
            const thumb = await canvasToBlob(scaled(canvas, canvas.width, canvas.height, THUMB_SIZE), 'image/jpeg', THUMB_QUALITY);
            this.addShot({
                kind: 'photo', blob, thumb, width: canvas.width, height: canvas.height, duration: 0,
                mime: 'image/jpeg', ext: 'jpg', takenAt, source: 'camera', position,
            });
        } catch (error) {
            this.notify('OnNotice', 'capture-failed', error.message || String(error));
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
        if (this.micPromise) return this.micPromise;
        this.micPromise = (async () => {
            try {
                const mic = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true } });
                const stillWanted = !this.disposed && this.settings.mic && (this.settings.mode === 'video' || this.recording);
                if (!stillWanted) { stopTracks(mic); return null; }
                this.mic = mic;
                return mic;
            } catch (error) {
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
        try {
            const composite = this.settings.stamp || (!this.zoom.native && this.zoom.value > 1.01);
            const compositor = composite ? this.startCompositor(video) : null;
            const tracks = compositor ? compositor.stream.getVideoTracks() : this.stream.getVideoTracks();
            const mic = this.settings.mic ? await this.ensureMic() : null;
            if (this.disposed || !this.stream) { if (compositor) compositor.stop(); return; }
            const audio = mic ? mic.getAudioTracks() : [];
            const mime = pickRecordingType(audio.length > 0);
            const height = compositor ? compositor.height : video.videoHeight;
            let recorder;
            try {
                recorder = new MediaRecorder(new MediaStream([...tracks, ...audio]),
                    { mimeType: mime || undefined, videoBitsPerSecond: bitrateFor(height) });
            } catch {
                try {
                    recorder = new MediaRecorder(new MediaStream([...tracks, ...audio]));
                } catch (error) {
                    if (compositor) compositor.stop();
                    this.notify('OnNotice', 'record-failed', error.message || error.name || '');
                    return;
                }
            }

            const poster = scaled(this.grabFrame(video, 1280), 0, 0, THUMB_SIZE);
            const session = {
                recorder, compositor, chunks: [], startedAt: Date.now(), poster,
                width: compositor ? compositor.width : video.videoWidth, height,
                position: this.settings.location ? this.position : null, discard: false,
            };
            recorder.ondataavailable = e => { if (e.data && e.data.size) session.chunks.push(e.data); };
            recorder.onerror = e => this.notify('OnNotice', 'record-failed', (e.error && e.error.message) || '');
            recorder.onstop = () => this.finishRecording(session);
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
        this.recording = null;
        session.discard = !!discard;
        session.duration = (Date.now() - session.startedAt) / 1000;
        clearInterval(this.recordTicker);
        delete this.root.dataset.recording;
        const label = this.el('rec-time');
        if (label) label.textContent = '';
        try { session.recorder.stop(); } catch { this.finishRecording(session); }
        this.releaseWakeLock();
        if (!discard) this.beep(440, 120);
        this.notify('OnRecording', false);
    }

    async finishRecording(session) {
        if (session.finished) return;
        session.finished = true;
        if (session.compositor) session.compositor.stop();
        if (this.settings.mode !== 'video' || !this.settings.mic) this.releaseMic();
        if (session.discard || this.disposed || !session.chunks.length) return;
        const type = (session.recorder.mimeType || session.chunks[0].type || 'video/webm').split(';')[0];
        let blob = new Blob(session.chunks, { type });
        if (type === 'video/webm') blob = await fixWebmDuration(blob, session.duration * 1000);
        let thumb = null;
        try { thumb = await canvasToBlob(session.poster, 'image/jpeg', THUMB_QUALITY); } catch { /* no poster */ }
        this.addShot({
            kind: 'video', blob, thumb, width: session.width, height: session.height, duration: session.duration,
            mime: type, ext: extensionFor(type), takenAt: session.startedAt, source: 'camera', position: session.position,
        });
    }

    // Stamp and digital zoom are not in the camera's own frames, so a clip that needs them is recorded from a canvas
    // that redraws each frame (capped at 1080p: a phone cannot composite 4K in real time).
    startCompositor(video) {
        const factor = () => (this.zoom.native ? 1 : this.zoom.value);
        const sourceWidth = video.videoWidth;
        const sourceHeight = video.videoHeight;
        const scale = Math.min(1, COMPOSITE_MAX_SIDE / Math.max(sourceWidth, sourceHeight));
        const canvas = document.createElement('canvas');
        canvas.width = even(sourceWidth * scale);
        canvas.height = even(sourceHeight * scale);
        const ctx = canvas.getContext('2d');
        let running = true;
        const draw = () => {
            if (!running) return;
            const z = factor();
            const cropWidth = sourceWidth / z;
            const cropHeight = sourceHeight / z;
            ctx.drawImage(video, (sourceWidth - cropWidth) / 2, (sourceHeight - cropHeight) / 2, cropWidth, cropHeight,
                0, 0, canvas.width, canvas.height);
            if (this.settings.stamp) this.drawStamp(ctx, canvas.width, canvas.height, new Date());
            if (video.requestVideoFrameCallback) video.requestVideoFrameCallback(draw);
            else requestAnimationFrame(draw);
        };
        draw();
        const stream = canvas.captureStream(30);
        return {
            stream, width: canvas.width, height: canvas.height,
            stop() { running = false; stopTracks(stream); },
        };
    }

    async holdWakeLock() {
        try { this.wakeLock = navigator.wakeLock ? await navigator.wakeLock.request('screen') : null; } catch { this.wakeLock = null; }
    }

    releaseWakeLock() {
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
        if (this.disposed) { this.remove(id); return; }
        this.notify('OnShot', describeShot(shot));
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

    async openMarkup(id) {
        this.closeMarkup();
        const shot = this.shots.get(id);
        const host = this.el('markup-host');
        const canvas = this.el('markup-canvas');
        const input = this.el('markup-text');
        if (!shot || shot.kind !== 'photo' || !host || !canvas) return false;
        const editor = new MarkupEditor(this, shot, host, canvas, input);
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
        if (!editor.shapes.length) { this.closeMarkup(); return describeShot(shot); }
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
        this.importFiles(files, role === 'native' ? 'native' : 'import');
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
            if (tag === 'BUTTON' || tag === 'A' || tag === 'LABEL' || tag === 'SUMMARY') return;
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
        for (const shape of this.shapes) drawShape(ctx, shape);
        if (this.current) drawShape(ctx, this.current);
        ctx.restore();
    }

    down(e) {
        if (e.pointerType === 'mouse' && e.button !== 0) return;
        e.preventDefault();
        const point = this.toImage(e);
        if (this.tool === 'text') { this.placeText(point); return; }
        this.commitText();
        // Keeps the stroke when the pointer leaves the canvas; a pointer the browser cannot capture must not cost the stroke.
        try { this.canvas.setPointerCapture(e.pointerId); } catch { /* not capturable */ }
        this.current = {
            type: this.tool, color: this.color, width: this.base * MARKUP_WIDTHS[this.size],
            points: [point], x1: point.x, y1: point.y, x2: point.x, y2: point.y,
        };
        this.draw();
    }

    move(e) {
        if (!this.current) return;
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

    up() {
        const shape = this.current;
        if (!shape) return;
        this.current = null;
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

    restyleText() {
        if (!this.input || this.input.hidden) return;
        const size = this.base * MARKUP_TEXT[this.size];
        this.textSize = size;
        this.input.style.fontSize = `${Math.max(12, size * this.scale)}px`;
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

    async export() {
        const canvas = document.createElement('canvas');
        canvas.width = this.image.width;
        canvas.height = this.image.height;
        const ctx = canvas.getContext('2d');
        ctx.drawImage(this.image, 0, 0);
        for (const shape of this.shapes) drawShape(ctx, shape);
        const blob = await canvasToBlob(canvas, 'image/jpeg', PHOTO_QUALITY);
        const thumb = await canvasToBlob(scaled(canvas, canvas.width, canvas.height, THUMB_SIZE), 'image/jpeg', THUMB_QUALITY);
        return { blob, thumb, width: canvas.width, height: canvas.height };
    }
}

// Every mark gets a soft dark halo so a red arrow still reads on a dark beam and a white one on the sky.
function drawShape(ctx, shape) {
    ctx.save();
    ctx.strokeStyle = shape.color;
    ctx.fillStyle = shape.color;
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    ctx.lineWidth = shape.width || 1;
    ctx.shadowColor = 'rgba(0, 0, 0, 0.45)';
    ctx.shadowBlur = Math.max(2, (shape.width || shape.size / 8) * 0.9);
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
            ctx.shadowBlur = shape.size * 0.25;
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
        'image/heic': 'heic', 'image/heif': 'heif',
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
        video.addEventListener('loadeddata', () => {
            const duration = Number.isFinite(video.duration) ? video.duration : 0;
            video.currentTime = duration > 1 ? 0.5 : 0;
        }, { once: true });
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
    });
}

// ---- WebM duration ---------------------------------------------------------------------------------------------
// MediaRecorder writes WebM as a live stream: the file has no Duration, so a player shows no length and cannot seek
// until it has read the whole clip. Insert Duration into the Segment's Info element. Only the layout MediaRecorder
// writes is handled (Segment of unknown size, no SeekHead before Info); anything else is returned untouched.

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
            if (childId.value === 0x114D9B74) return blob; // SeekHead: its offsets would move
            if (childId.value === 0x1549A966) {
                if (dataStart + childSize.value > head.length) return blob;
                const info = head.subarray(dataStart, dataStart + childSize.value);
                let timecodeScale = 1_000_000;
                for (let p = 0; p < info.length;) {
                    const elementId = readEbmlId(info, p);
                    const elementSize = elementId && readEbmlSize(info, p + elementId.length);
                    if (!elementId || !elementSize || elementSize.unknown) return blob;
                    const valueStart = p + elementId.length + elementSize.length;
                    if (elementId.value === 0x4489) return blob; // already has a Duration
                    if (elementId.value === 0x2AD7B1) {
                        let scale = 0;
                        for (let i = 0; i < elementSize.value; i++) scale = scale * 256 + info[valueStart + i];
                        if (scale > 0) timecodeScale = scale;
                    }
                    p = valueStart + elementSize.value;
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
