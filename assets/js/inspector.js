import { getMedia } from "./media.js";
import {
    FONTS, SPEED_PRESETS, checkpoint, deleteSelection, dropEmptyCheckpoint, escapeHtml,
    findItem, formatTime, getProject, getSelection, splitSelected, subscribe, updateFilters,
    updateItem, updateProject
} from "./project.js";

function watch(element, apply) {
    let armed = false;
    const arm = () => {
        if (armed) return;
        checkpoint();
        armed = true;
    };
    element.addEventListener("focus", arm);
    element.addEventListener("pointerdown", arm);
    const finish = () => {
        if (!armed) return;
        armed = false;
        dropEmptyCheckpoint();
    };
    element.addEventListener("input", () => apply());
    element.addEventListener("change", () => {
        if (!armed) arm();
        apply();
        finish();
    });
    element.addEventListener("blur", finish);
}

function numberBox(label, value, attrs, dataset) {
    return `<div class="field"><label>${label}</label><input class="form-control form-control-sm" type="number" value="${escapeHtml(value)}" ${attrs} ${dataset}></div>`;
}

function slider(label, value, min, max, step, dataset) {
    return `<div class="field"><label>${label}<output>${escapeHtml(value)}</output></label><input type="range" min="${min}" max="${max}" step="${step}" value="${escapeHtml(value)}" ${dataset}></div>`;
}

function bindAll(root, apply) {
    root.querySelectorAll("input, select, textarea").forEach((element) => {
        watch(element, () => apply(element));
        if (element.type === "range") {
            element.addEventListener("input", () => {
                const output = element.parentElement.querySelector("output");
                if (output) output.textContent = element.value;
            });
        }
    });
    root.querySelectorAll("[data-rotation]").forEach((button) => {
        button.addEventListener("click", () => {
            const selected = getSelection();
            if (!selected) return;
            updateItem(selected.type, selected.id, { rotation: Number(button.dataset.rotation) });
        });
    });
}

function selectedItem() {
    const selected = getSelection();
    if (!selected) return null;
    const item = findItem(selected.type, selected.id);
    return item ? { type: selected.type, item } : null;
}

function renderVideo(item) {
    const speedOptions = SPEED_PRESETS.map((speed) => `<option value="${speed}" ${Number(item.speed) === speed ? "selected" : ""}>${speed}x</option>`).join("");
    return `
        <section class="inspector-block">
            <h3>Video Clip</h3>
            <div class="field"><label>Clip name</label><input class="form-control form-control-sm" data-key="name" value="${escapeHtml(item.name)}"></div>
            ${numberBox("Timeline start (s)", item.timelineStart, 'min="0" step="0.1"', 'data-key="timelineStart"')}
            ${numberBox("In point (s)", item.inPoint, 'min="0" step="0.1"', 'data-key="inPoint"')}
            ${numberBox("Out point (s)", item.outPoint, 'min="0" step="0.1"', 'data-key="outPoint"')}
            <p class="text-secondary small">Source duration ${formatTime(item.sourceDuration)} · Timeline duration ${formatTime((item.outPoint - item.inPoint) / item.speed)}</p>
            <div class="field"><label>Playback speed</label><select class="form-select form-select-sm" data-key="speed">${speedOptions}</select></div>
            ${numberBox("Custom speed", item.speed, 'min="0.25" max="4" step="0.05"', 'data-key="speed"')}
            ${slider("Volume", Math.round(item.volume * 100), 0, 100, 1, 'data-key="volume"')}
            <label class="check-row"><span>Mute clip audio</span><input type="checkbox" data-key="muted" ${item.muted ? "checked" : ""}></label>
            <div class="row g-2">
                <div class="col-6">${numberBox("X position", item.x, 'step="1"', 'data-key="x"')}</div>
                <div class="col-6">${numberBox("Y position", item.y, 'step="1"', 'data-key="y"')}</div>
            </div>
            ${slider("Scale", item.scale, 0.1, 4, 0.05, 'data-key="scale"')}
            ${numberBox("Rotation", Math.round(item.rotation), 'min="0" max="359" step="1"', 'data-key="rotation"')}
            <div class="preset-row mb-2">
                <button class="btn btn-soft btn-sm" type="button" data-rotation="0">0°</button>
                <button class="btn btn-soft btn-sm" type="button" data-rotation="90">90°</button>
                <button class="btn btn-soft btn-sm" type="button" data-rotation="180">180°</button>
                <button class="btn btn-soft btn-sm" type="button" data-rotation="270">270°</button>
            </div>
            ${slider("Crop top %", item.crop.top, 0, 40, 1, 'data-crop="top"')}
            ${slider("Crop right %", item.crop.right, 0, 40, 1, 'data-crop="right"')}
            ${slider("Crop bottom %", item.crop.bottom, 0, 40, 1, 'data-crop="bottom"')}
            ${slider("Crop left %", item.crop.left, 0, 40, 1, 'data-crop="left"')}
            <button class="btn btn-soft btn-sm" type="button" data-action="split"><i class="fa-solid fa-scissors"></i> Split at playhead</button>
        </section>`;
}

function renderImage(item) {
    return `
        <section class="inspector-block">
            <h3>Image Overlay</h3>
            <div class="row g-2">
                <div class="col-6">${numberBox("X position", item.x, 'step="1"', 'data-key="x"')}</div>
                <div class="col-6">${numberBox("Y position", item.y, 'step="1"', 'data-key="y"')}</div>
            </div>
            ${numberBox("Width", item.width, 'min="10" max="3840" step="1"', 'data-key="width"')}
            ${slider("Scale", item.scale, 0.1, 4, 0.05, 'data-key="scale"')}
            ${numberBox("Start time (s)", item.start, 'min="0" step="0.1"', 'data-key="start"')}
            ${numberBox("End time (s)", item.end, 'min="0" step="0.1"', 'data-key="end"')}
            ${slider("Opacity", Math.round(item.opacity * 100), 0, 100, 1, 'data-key="opacity"')}
            ${numberBox("Rotation", Math.round(item.rotation), 'min="0" max="359" step="1"', 'data-key="rotation"')}
            <div class="preset-row">
                <button class="btn btn-soft btn-sm" type="button" data-rotation="0">0°</button>
                <button class="btn btn-soft btn-sm" type="button" data-rotation="90">90°</button>
                <button class="btn btn-soft btn-sm" type="button" data-rotation="180">180°</button>
                <button class="btn btn-soft btn-sm" type="button" data-rotation="270">270°</button>
            </div>
        </section>`;
}

function renderText(item) {
    const fonts = FONTS.map((font) => `<option ${font === item.fontFamily ? "selected" : ""}>${escapeHtml(font)}</option>`).join("");
    return `
        <section class="inspector-block">
            <h3>Text Overlay</h3>
            <div class="field"><label>Text content</label><textarea class="form-control" rows="3" data-key="content">${escapeHtml(item.content)}</textarea></div>
            <div class="field"><label>Font family</label><select class="form-select form-select-sm" data-key="fontFamily">${fonts}</select></div>
            ${numberBox("Font size", item.fontSize, 'min="8" max="400" step="1"', 'data-key="fontSize"')}
            <div class="field"><label>Font color</label><div class="color-row"><input type="color" data-key="color" value="${escapeHtml(item.color)}"><input class="form-control form-control-sm" data-key="colorText" value="${escapeHtml(item.color)}"></div></div>
            <div class="inline-checks">
                <label class="check-row"><span>Bold</span><input type="checkbox" data-key="bold" ${item.bold ? "checked" : ""}></label>
                <label class="check-row"><span>Italic</span><input type="checkbox" data-key="italic" ${item.italic ? "checked" : ""}></label>
            </div>
            <div class="field"><label>Alignment</label><select class="form-select form-select-sm" data-key="align">
                <option value="left" ${item.align === "left" ? "selected" : ""}>Left</option>
                <option value="center" ${item.align === "center" ? "selected" : ""}>Center</option>
                <option value="right" ${item.align === "right" ? "selected" : ""}>Right</option>
            </select></div>
            <div class="row g-2">
                <div class="col-6">${numberBox("X position", item.x, 'step="1"', 'data-key="x"')}</div>
                <div class="col-6">${numberBox("Y position", item.y, 'step="1"', 'data-key="y"')}</div>
            </div>
            ${numberBox("Start time (s)", item.start, 'min="0" step="0.1"', 'data-key="start"')}
            ${numberBox("End time (s)", item.end, 'min="0" step="0.1"', 'data-key="end"')}
            ${slider("Opacity", Math.round(item.opacity * 100), 0, 100, 1, 'data-key="opacity"')}
            ${numberBox("Rotation", Math.round(item.rotation), 'min="0" max="359" step="1"', 'data-key="rotation"')}
        </section>`;
}

function renderAudio(item) {
    return `
        <section class="inspector-block">
            <h3>Audio Clip</h3>
            <p class="small text-secondary">${escapeHtml(item.name)} · source ${formatTime(item.sourceDuration)}</p>
            ${numberBox("Start time (s)", item.timelineStart, 'min="0" step="0.1"', 'data-key="timelineStart"')}
            ${numberBox("In point (s)", item.inPoint, 'min="0" step="0.1"', 'data-key="inPoint"')}
            ${numberBox("Out point (s)", item.outPoint, 'min="0" step="0.1"', 'data-key="outPoint"')}
            ${slider("Volume", Math.round(item.volume * 100), 0, 100, 1, 'data-key="volume"')}
            ${numberBox("Track number", item.track, 'min="1" max="4" step="1"', 'data-key="track"')}
        </section>`;
}

function renderShared(project) {
    const filters = project.filters;
    const size = `${project.width}x${project.height}`;
    const sizes = ["1280x720", "1920x1080", "1080x1920"];
    if (!sizes.includes(size)) sizes.unshift(size);
    return `
        <section class="inspector-block">
            <h3>Project</h3>
            <div class="field"><label>Canvas</label><select class="form-select form-select-sm" data-project="canvas">${sizes.map((value) => `<option ${value === size ? "selected" : ""}>${value}</option>`).join("")}</select></div>
            <div class="field"><label>FPS</label><select class="form-select form-select-sm" data-project="fps">${[24, 25, 30, 50, 60].map((fps) => `<option value="${fps}" ${project.fps === fps ? "selected" : ""}>${fps}</option>`).join("")}</select></div>
            <label class="check-row"><span>Mute original video audio</span><input type="checkbox" data-project="muteOriginal" ${project.muteOriginal ? "checked" : ""}></label>
        </section>
        <section class="inspector-block">
            <h3>Filters</h3>
            ${slider("Brightness", filters.brightness, -100, 100, 1, 'data-filter="brightness"')}
            ${slider("Contrast", filters.contrast, -100, 100, 1, 'data-filter="contrast"')}
            ${slider("Saturation", filters.saturation, -100, 100, 1, 'data-filter="saturation"')}
            ${slider("Grayscale", filters.grayscale, 0, 100, 1, 'data-filter="grayscale"')}
            ${slider("Blur", filters.blur, 0, 20, 0.5, 'data-filter="blur"')}
            ${slider("Sepia", filters.sepia, 0, 100, 1, 'data-filter="sepia"')}
        </section>`;
}

function applyElement(element) {
    const current = selectedItem();
    if (element.dataset.filter) {
        updateFilters({ [element.dataset.filter]: Number(element.value) }, { history: false, reason: "live" });
        return;
    }
    if (element.dataset.project) {
        if (element.dataset.project === "canvas") {
            const [width, height] = element.value.split("x").map(Number);
            updateProject({ width, height }, { history: false, reason: "live" });
        } else if (element.dataset.project === "fps") {
            updateProject({ fps: Number(element.value) }, { history: false, reason: "live" });
        } else if (element.dataset.project === "muteOriginal") {
            updateProject({ muteOriginal: element.checked }, { history: false, reason: "live" });
        }
        return;
    }
    if (!current) return;
    const key = element.dataset.key;
    if (element.dataset.crop) {
        updateItem(current.type, current.item.id, { crop: { ...current.item.crop, [element.dataset.crop]: Number(element.value) } }, { history: false, reason: "live" });
        return;
    }
    if (!key) return;
    let value = element.type === "checkbox" ? element.checked : element.value;
    if (["volume", "opacity"].includes(key)) value = Number(value) / 100;
    else if (element.type === "number" || element.type === "range") value = Number(value);
    if (key === "colorText") {
        updateItem(current.type, current.item.id, { color: element.value }, { history: false, reason: "live" });
        return;
    }
    updateItem(current.type, current.item.id, { [key]: value }, { history: false, reason: "live" });
}

function render() {
    const root = document.getElementById("inspector");
    const project = getProject();
    const current = selectedItem();
    let html = "";
    if (!current) {
        html += `<section class="inspector-block"><h3>No selection</h3><p class="small text-secondary mb-0">Select a clip on the timeline, or add text, images, and audio.</p></section>`;
    } else if (current.type !== "text" && !getMedia(current.item.mediaId)) {
        html += `<section class="inspector-block"><h3>Missing media</h3><p class="small text-secondary">This clip points to a file that is no longer in the library.</p></section>`;
    }
    if (current?.type === "video") html += renderVideo(current.item);
    if (current?.type === "image") html += renderImage(current.item);
    if (current?.type === "text") html += renderText(current.item);
    if (current?.type === "audio") html += renderAudio(current.item);
    html += renderShared(project);
    if (current) html += `<button class="btn btn-soft w-100" type="button" data-action="delete"><i class="fa-solid fa-trash"></i> Delete clip</button>`;
    root.innerHTML = html;
    bindAll(root, applyElement);
    root.querySelector("[data-action='split']")?.addEventListener("click", splitSelected);
    root.querySelector("[data-action='delete']")?.addEventListener("click", deleteSelection);
}

export function initInspector() {
    subscribe((reason) => {
        if (reason === "playhead" || reason === "live") return;
        if (reason !== "change" && rootHasFocus()) return;
        render();
    });
    render();
}

function rootHasFocus() {
    const root = document.getElementById("inspector");
    return root.contains(document.activeElement);
}
