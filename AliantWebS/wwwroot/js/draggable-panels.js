// Store active panel instances and dock zones
const activePanels = new Map();
const dockZones = new Map();

// Set the Blazor component reference
window.setBlazorPanelManager = function(dotNetRef) {
    window.blazorPanelManager = dotNetRef;
};

// Initialize draggable panels
window.initDraggablePanels = function () {
    setTimeout(() => {
        initPanel('selectorPanel', 'selectorPanelHeader', 'selectorDockZone');
        initPanel('svgPanel', 'svgPanelHeader', 'svgDockZone');
    }, 100);
};

// Register dock zones (called from Blazor)
window.registerDockZone = function(panelId, dockZoneId) {
    const dockZone = document.getElementById(dockZoneId);
    if (dockZone) {
        dockZones.set(panelId, {
            element: dockZone,
            rect: dockZone.getBoundingClientRect()
        });
    }
};

function initPanel(panelId, headerId, dockZoneId) {
    const panel = document.getElementById(panelId);
    const header = document.getElementById(headerId);

    if (!panel || !header) return;

    // Register dock zone
    window.registerDockZone(panelId, dockZoneId);

    // Clean up existing instance if any
    if (activePanels.has(panelId)) {
        const oldInstance = activePanels.get(panelId);
        oldInstance.cleanup();
    }

    // Create new draggable and resizable instance
    const instance = makeDraggableAndResizable(panel, header, panelId);
    activePanels.set(panelId, instance);
}

function makeDraggableAndResizable(panel, header, panelId) {
    let isDragging = false;
    let isResizing = false;
    let hasMoved = false;
    let hasDetached = false;
    let offsetX;
    let offsetY;
    let startX;
    let startY;
    let startWidth;
    let startHeight;
    let startLeft;
    let startTop;

    // Drag functionality
    function dragStart(e) {
        // Don't drag when clicking buttons or other interactive elements
        if (e.target.tagName === 'BUTTON' || e.target.closest('button')) return;
        
        const rect = panel.getBoundingClientRect();
        offsetX = e.clientX - rect.left;
        offsetY = e.clientY - rect.top;
        startX = e.clientX;
        startY = e.clientY;
        
        isDragging = true;
        hasMoved = false;
        hasDetached = false;
        header.style.cursor = 'grabbing';
        
        panel.classList.add('is-dragging');
        e.preventDefault();
    }

    function drag(e) {
        if (!isDragging) return;
        e.preventDefault();
        
        const moveX = Math.abs(e.clientX - startX);
        const moveY = Math.abs(e.clientY - startY);
        
        if (!hasMoved && (moveX > 5 || moveY > 5)) {
            hasMoved = true;
            
            const isCurrentlyDocked = !panel.classList.contains('floating-panel');
            
            if (isCurrentlyDocked) {
                hasDetached = true;
                triggerDetach(panelId);
                
                setTimeout(() => {
                    const floatingPanel = document.getElementById(panelId);
                    if (floatingPanel && isDragging) {
                        const newRect = floatingPanel.getBoundingClientRect();
                        offsetX = e.clientX - newRect.left;
                        offsetY = e.clientY - newRect.top;
                    }
                }, 100);
                return;
            }
        }

        if (hasMoved && panel.classList.contains('floating-panel')) {
            const newX = e.clientX - offsetX;
            const newY = e.clientY - offsetY;

            const minVisibleHeader = 50;
            const maxX = window.innerWidth - minVisibleHeader;
            const maxY = window.innerHeight - minVisibleHeader;
            
            const constrainedX = Math.max(-panel.offsetWidth + minVisibleHeader, Math.min(newX, maxX));
            const constrainedY = Math.max(0, Math.min(newY, maxY));

            panel.style.left = constrainedX + 'px';
            panel.style.top = constrainedY + 'px';

            highlightDockZone(panelId, e.clientX, e.clientY);
        }
    }

    function dragEnd(e) {
        if (!isDragging) return;
        
        isDragging = false;
        hasDetached = false;
        header.style.cursor = 'move';
        panel.classList.remove('is-dragging');

        if (hasMoved) {
            const dockZoneInfo = dockZones.get(panelId);
            if (dockZoneInfo && isOverDockZone(panelId, e.clientX, e.clientY)) {
                triggerDock(panelId);
            }
        }

        clearDockHighlights();
    }

    // Resize functionality
    function resizeStart(e) {
        // Check if click is in the resize handle area (bottom-right 20x20px)
        const rect = panel.getBoundingClientRect();
        const handleSize = 20;
        
        if (e.clientX >= rect.right - handleSize && 
            e.clientY >= rect.bottom - handleSize) {
            
            isResizing = true;
            startX = e.clientX;
            startY = e.clientY;
            startWidth = panel.offsetWidth;
            startHeight = panel.offsetHeight;
            startLeft = panel.offsetLeft;
            startTop = panel.offsetTop;
            
            e.preventDefault();
            e.stopPropagation();
            
            document.body.style.cursor = 'nwse-resize';
        }
    }

    function resize(e) {
        if (!isResizing) return;
        e.preventDefault();
        
        const deltaX = e.clientX - startX;
        const deltaY = e.clientY - startY;
        
        const newWidth = Math.max(300, startWidth + deltaX);
        const newHeight = Math.max(200, startHeight + deltaY);
        
        // Ensure panel doesn't go off-screen
        const maxWidth = window.innerWidth - startLeft - 10;
        const maxHeight = window.innerHeight - startTop - 10;
        
        panel.style.width = Math.min(newWidth, maxWidth) + 'px';
        panel.style.height = Math.min(newHeight, maxHeight) + 'px';
    }

    function resizeEnd() {
        if (!isResizing) return;
        
        isResizing = false;
        document.body.style.cursor = '';
    }

    // Add event listeners for dragging
    header.addEventListener('mousedown', dragStart);
    document.addEventListener('mousemove', drag);
    document.addEventListener('mouseup', dragEnd);

    // Add event listeners for resizing (on the panel, not just the handle)
    panel.addEventListener('mousedown', resizeStart);
    document.addEventListener('mousemove', resize);
    document.addEventListener('mouseup', resizeEnd);

    // Return cleanup function
    return {
        cleanup: function() {
            header.removeEventListener('mousedown', dragStart);
            document.removeEventListener('mousemove', drag);
            document.removeEventListener('mouseup', dragEnd);
            
            panel.removeEventListener('mousedown', resizeStart);
            document.removeEventListener('mousemove', resize);
            document.removeEventListener('mouseup', resizeEnd);
        }
    };
}

function isOverDockZone(panelId, mouseX, mouseY) {
    const dockZoneInfo = dockZones.get(panelId);
    if (!dockZoneInfo) return false;

    const rect = dockZoneInfo.element.getBoundingClientRect();
    dockZoneInfo.rect = rect;

    return mouseX >= rect.left && 
           mouseX <= rect.right && 
           mouseY >= rect.top && 
           mouseY <= rect.bottom;
}

function highlightDockZone(panelId, mouseX, mouseY) {
    const dockZoneInfo = dockZones.get(panelId);
    if (!dockZoneInfo) return;

    if (isOverDockZone(panelId, mouseX, mouseY)) {
        dockZoneInfo.element.classList.add('dock-zone-highlight');
    } else {
        dockZoneInfo.element.classList.remove('dock-zone-highlight');
    }
}

function clearDockHighlights() {
    dockZones.forEach(info => {
        info.element.classList.remove('dock-zone-highlight');
    });
}

function triggerDetach(panelId) {
    if (window.blazorPanelManager) {
        window.blazorPanelManager.invokeMethodAsync('DetachPanel', panelId);
    }
}

function triggerDock(panelId) {
    if (window.blazorPanelManager) {
        window.blazorPanelManager.invokeMethodAsync('DockPanel', panelId);
    }
}

// Clean up when panels are removed
window.cleanupPanel = function(panelId) {
    if (activePanels.has(panelId)) {
        const instance = activePanels.get(panelId);
        instance.cleanup();
        activePanels.delete(panelId);
    }
    dockZones.delete(panelId);
};

// Update dock zones when window resizes
window.addEventListener('resize', () => {
    dockZones.forEach((info, panelId) => {
        if (info.element) {
            info.rect = info.element.getBoundingClientRect();
        }
    });
});
// Generic drag support for any floating panel/modal (e.g. the DXF export settings dialog).
// The panel is usually centred by a flex overlay; on the first drag it is pinned to its current
// position and switched to absolute so it can be moved, then clamped inside its parent.
window.makeElementDraggable = function (panelId, handleId) {
    const panel = document.getElementById(panelId);
    if (!panel || panel.dataset.draggableInit === '1') return;
    const handle = document.getElementById(handleId) || panel;

    panel.dataset.draggableInit = '1';
    handle.style.cursor = 'move';
    handle.style.userSelect = 'none';

    let startX = 0, startY = 0, originLeft = 0, originTop = 0, dragging = false;

    const parentOf = function () { return panel.offsetParent || panel.parentElement; };

    const onMove = function (e) {
        if (!dragging) return;
        const pr = parentOf().getBoundingClientRect();
        let left = originLeft + (e.clientX - startX);
        let top = originTop + (e.clientY - startY);
        left = Math.max(0, Math.min(left, pr.width - panel.offsetWidth));
        top = Math.max(0, Math.min(top, pr.height - panel.offsetHeight));
        panel.style.left = left + 'px';
        panel.style.top = top + 'px';
    };

    const onUp = function () {
        dragging = false;
        document.removeEventListener('mousemove', onMove);
        document.removeEventListener('mouseup', onUp);
    };

    handle.addEventListener('mousedown', function (e) {
        if (e.button !== 0) return;
        const pr = parentOf().getBoundingClientRect();
        const r = panel.getBoundingClientRect();

        // Pin where it currently sits, then take it out of the flex centring.
        originLeft = r.left - pr.left;
        originTop = r.top - pr.top;
        panel.style.position = 'absolute';
        panel.style.margin = '0';
        panel.style.left = originLeft + 'px';
        panel.style.top = originTop + 'px';

        startX = e.clientX;
        startY = e.clientY;
        dragging = true;
        e.preventDefault();
        document.addEventListener('mousemove', onMove);
        document.addEventListener('mouseup', onUp);
    });
};

// Export the CAD plan (an inline <svg>) to a JPEG, honouring the DXF dialog's layer checkboxes.
// The layers are <g class="dxf-layer-*"> groups; unticked ones are hidden on a CLONE so the live
// plan is never touched. External <image href="..."> references are not fetched when an SVG is
// rasterised into a canvas, so the background picture is inlined as a data URI first.
window.exportSvgToJpg = async function (svgId, baseName) {
    const LAYERS = [
        { chk: 'dxfChkMotherGrids',    cls: 'dxf-layer-mothergrids' },
        { chk: 'dxfChkPalonniers',     cls: 'dxf-layer-palonniers' },
        { chk: 'dxfChkCharges',        cls: 'dxf-layer-charges' },
        { chk: 'dxfChkPalonnierLoads', cls: 'dxf-layer-palonnierloads' },
        { chk: 'dxfChkIntersections',  cls: 'dxf-layer-intersections' },
        { chk: 'dxfChkSensors',        cls: 'dxf-layer-sensors' },
        { chk: 'dxfChkOrigin',         cls: 'dxf-layer-origin' },
        { chk: 'dxfChkBackground',     cls: 'dxf-layer-background' }
    ];

    try {
        const svg = document.getElementById(svgId);
        if (!svg) return;

        const rect = svg.getBoundingClientRect();
        if (!rect.width || !rect.height) return;
        // (journalExport is defined at the bottom of this file — see the export-journal bridge)

        const d = new Date();
        const p2 = function (n) { return String(n).padStart(2, '0'); };
        const stamp = d.getFullYear() + '-' + p2(d.getMonth() + 1) + '-' + p2(d.getDate())
                    + '_' + p2(d.getHours()) + '-' + p2(d.getMinutes());
        const fileName = String(baseName || 'plan').replace(/[\/:*?"<>|]/g, '_') + '_' + stamp + '.jpg';

        // Ask where to save FIRST, while the click's user activation is still valid.
        let handle = null;
        if (window.showSaveFilePicker) {
            try {
                handle = await window.showSaveFilePicker({
                    suggestedName: fileName,
                    types: [{ description: 'Image JPEG', accept: { 'image/jpeg': ['.jpg', '.jpeg'] } }]
                });
            } catch (e) {
                if (e && e.name === 'AbortError') return;   // cancelled
                handle = null;
            }
        }

        const clone = svg.cloneNode(true);
        clone.setAttribute('width', rect.width);
        clone.setAttribute('height', rect.height);
        clone.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
        clone.setAttribute('xmlns:xlink', 'http://www.w3.org/1999/xlink');

        let keepBackground = true;
        LAYERS.forEach(function (l) {
            const chk = document.getElementById(l.chk);
            const on = chk ? chk.checked : true;
            if (l.cls === 'dxf-layer-background') keepBackground = on;
            if (!on) clone.querySelectorAll('.' + l.cls).forEach(function (g) { g.remove(); });
        });

        // Editing aids - the hanging points of the selected palonnier - are never part of an export.
        clone.querySelectorAll('.plan-editing-only').forEach(function (g) { g.remove(); });

        // Inline (or drop) raster images — an external href would silently render as nothing.
        const images = Array.prototype.slice.call(clone.querySelectorAll('image'));
        for (let i = 0; i < images.length; i++) {
            const img = images[i];
            const href = img.getAttribute('href') || img.getAttribute('xlink:href');
            if (!keepBackground || !href) { img.remove(); continue; }
            if (href.indexOf('data:') === 0) continue;
            try {
                const resp = await fetch(href);
                const blob = await resp.blob();
                const dataUrl = await new Promise(function (res, rej) {
                    const fr = new FileReader();
                    fr.onload = function () { res(fr.result); };
                    fr.onerror = rej;
                    fr.readAsDataURL(blob);
                });
                img.setAttribute('href', dataUrl);
                img.removeAttribute('xlink:href');
            } catch (e) {
                img.remove();
            }
        }

        const xml = new XMLSerializer().serializeToString(clone);
        const svgUrl = URL.createObjectURL(new Blob([xml], { type: 'image/svg+xml;charset=utf-8' }));

        const bitmap = await new Promise(function (res, rej) {
            const im = new Image();
            im.onload = function () { res(im); };
            im.onerror = rej;
            im.src = svgUrl;
        });

        const scale = 2;
        const out = document.createElement('canvas');
        out.width = Math.round(rect.width * scale);
        out.height = Math.round(rect.height * scale);
        const ctx = out.getContext('2d');
        ctx.scale(scale, scale);
        ctx.fillStyle = '#ffffff';                       // JPEG has no alpha
        ctx.fillRect(0, 0, rect.width, rect.height);
        ctx.drawImage(bitmap, 0, 0, rect.width, rect.height);
        URL.revokeObjectURL(svgUrl);

        out.toBlob(async function (blob) {
            if (!blob) return;
            if (handle) {
                try {
                    const w = await handle.createWritable();
                    await w.write(blob);
                    await w.close();
                    if (window.journalExport) window.journalExport('export.jpg.plan', fileName, '');
                    const cancel = document.getElementById('dxfCancelBtn');
                    if (cancel) cancel.click();          // close the dialog on success
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
            if (window.journalExport) window.journalExport('export.jpg.plan', fileName, '');
            const cancel = document.getElementById('dxfCancelBtn');
            if (cancel) cancel.click();
        }, 'image/jpeg', 0.92);
    } catch (error) {
        console.error('Failed to export SVG to JPG:', error);
    }
};


// ── Export-journal bridge ────────────────────────────────────────────────────────────────────
// The JPG export buttons use a plain onclick, not @onclick: the Save As picker needs the click's
// user activation, and a Blazor server round-trip expires it. So the server never sees the click.
// A component in the layout registers a .NET reference here, and the export handlers call
// journalExport() once the file has actually been written — a cancelled dialog records nothing.
// The identity is resolved server-side from the circuit, so the page cannot claim to be someone else.
window.__journalRef = null;

window.registerJournalRef = function (ref) {
    window.__journalRef = ref;
};

window.journalExport = function (action, fileName, details) {
    try {
        if (window.__journalRef) {
            window.__journalRef.invokeMethodAsync('OnClientExport', action, fileName, details || '');
        }
    } catch (e) {
        console.warn('journal notification failed', e);
    }
};
