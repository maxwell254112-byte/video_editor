const STORAGE_KEY = "videoEditor.project.v1";
const HISTORY_LIMIT = 50;
export const MIN_DURATION = 0.1;
export const FONTS = ["Arial", "Verdana", "Georgia", "Times New Roman", "Courier New", "Microsoft YaHei", "SimHei", "SimSun"];
export const SPEED_PRESETS = [0.25, 0.5, 0.75, 1, 1.25, 1.5, 2, 4];

const listeners = new Set();
let state = blankProject();
let selection = null;
let past = [];
let future = [];
let limits = { video: 500 * 1024 * 1024, image: 20 * 1024 * 1024, audio: 50 * 1024 * 1024 };

function blankProject() {
    return {
        version: 1,
        name: "Untitled Project",
        fps: 30,
        width: 1920,
        height: 1080,
        playhead: 0,
        clips: [],
        images: [],
        texts: [],
        audios: [],
        filters: { brightness: 0, contrast: 0, saturation: 0, grayscale: 0, blur: 0, sepia: 0 },
        muteOriginal: false,
        exportMode: "fast"
    };
}

function uid() {
    if (globalThis.crypto?.randomUUID) {
        return crypto.randomUUID();
    }
    return `id-${Date.now().toString(16)}-${Math.random().toString(16).slice(2)}`;
}

function clamp(value, min, max) {
    const number = Number(value);
    if (!Number.isFinite(number)) return min;
    return Math.min(max, Math.max(min, number));
}

function q(value) {
    return Math.round(clamp(value, -100000, 100000) * 1000) / 1000;
}

export function escapeHtml(value) {
    return String(value).replace(/[&<>"']/g, (char) => ({
        "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
    }[char]));
}

export function formatTime(seconds) {
    const safe = Math.max(0, Number(seconds) || 0);
    const hours = Math.floor(safe / 3600);
    const minutes = Math.floor((safe % 3600) / 60);
    const secs = safe % 60;
    const whole = Math.floor(secs);
    const tenth = Math.floor((secs - whole) * 10);
    const clock = `${String(minutes).padStart(2, "0")}:${String(whole).padStart(2, "0")}.${tenth}`;
    return hours > 0 ? `${hours}:${clock}` : clock;
}

export function notify(message, type = "info") {
    document.dispatchEvent(new CustomEvent("editor:toast", { detail: { message, type } }));
}

export function isFormField(target) {
    if (!target || !target.tagName) return false;
    return ["INPUT", "TEXTAREA", "SELECT"].includes(target.tagName) || target.isContentEditable;
}

export function getProject() { return state; }
export function getSelection() { return selection; }
export function getLimits() { return limits; }
export function setLimits(next) { limits = { ...limits, ...next }; }
export function canUndo() { return past.length > 0; }
export function canRedo() { return future.length > 0; }

export function subscribe(listener) {
    listeners.add(listener);
    return () => listeners.delete(listener);
}

function emit(reason) {
    listeners.forEach((listener) => listener(reason));
}

function core(project) {
    const copy = JSON.parse(JSON.stringify(project));
    delete copy.playhead;
    return copy;
}

function persist() {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
}

export function checkpoint() {
    past.push(JSON.stringify(core(state)));
    if (past.length > HISTORY_LIMIT) past.shift();
    future = [];
}

export function dropEmptyCheckpoint() {
    if (!past.length) return;
    if (past[past.length - 1] === JSON.stringify(core(state))) past.pop();
}

export function collection(project, type) {
    return { video: project.clips, image: project.images, text: project.texts, audio: project.audios }[type] || [];
}

export function itemSpan(type, item) {
    if (type === "video") {
        const speed = item.speed || 1;
        const duration = Math.max(0, (item.outPoint - item.inPoint) / speed);
        return { start: item.timelineStart, end: item.timelineStart + duration };
    }
    if (type === "audio") {
        return { start: item.timelineStart, end: item.timelineStart + Math.max(0, item.outPoint - item.inPoint) };
    }
    return { start: item.start, end: item.end };
}

export function contentDuration(project = state) {
    let max = 0;
    for (const type of ["video", "image", "text", "audio"]) {
        for (const item of collection(project, type)) {
            if (type === "text" && !String(item.content || "").trim()) continue;
            max = Math.max(max, itemSpan(type, item).end);
        }
    }
    return max;
}

export function displayDuration(project = state) {
    return Math.max(10, contentDuration(project) + 2);
}

function trackEnd(project, type) {
    return collection(project, type).reduce((end, item) => Math.max(end, itemSpan(type, item).end), 0);
}

function sanitizeCrop(crop) {
    const next = {
        top: clamp(crop?.top ?? 0, 0, 40),
        right: clamp(crop?.right ?? 0, 0, 40),
        bottom: clamp(crop?.bottom ?? 0, 0, 40),
        left: clamp(crop?.left ?? 0, 0, 40)
    };
    if (next.top + next.bottom > 80) {
        const scale = 80 / (next.top + next.bottom);
        next.top *= scale;
        next.bottom *= scale;
    }
    if (next.left + next.right > 80) {
        const scale = 80 / (next.left + next.right);
        next.left *= scale;
        next.right *= scale;
    }
    return next;
}

function sanitizeItem(type, item) {
    if (type === "video") {
        item.speed = clamp(item.speed, 0.25, 4);
        item.volume = clamp(item.volume, 0, 1);
        item.timelineStart = q(clamp(item.timelineStart, 0, 86400));
        item.sourceDuration = Math.max(MIN_DURATION, Number(item.sourceDuration) || MIN_DURATION);
        const minSource = MIN_DURATION * item.speed;
        item.inPoint = q(clamp(item.inPoint, 0, Math.max(0, item.sourceDuration - minSource)));
        item.outPoint = q(clamp(item.outPoint, item.inPoint + minSource, item.sourceDuration));
        item.scale = clamp(item.scale, 0.1, 4);
        item.rotation = ((Number(item.rotation) % 360) + 360) % 360;
        item.x = q(item.x);
        item.y = q(item.y);
        item.muted = Boolean(item.muted);
        item.crop = sanitizeCrop(item.crop);
    } else if (type === "audio") {
        item.volume = clamp(item.volume, 0, 1);
        item.track = clamp(item.track || 1, 1, 4);
        item.timelineStart = q(clamp(item.timelineStart, 0, 86400));
        item.sourceDuration = Math.max(MIN_DURATION, Number(item.sourceDuration) || MIN_DURATION);
        item.inPoint = q(clamp(item.inPoint, 0, item.sourceDuration - MIN_DURATION));
        item.outPoint = q(clamp(item.outPoint, item.inPoint + MIN_DURATION, item.sourceDuration));
    } else if (type === "image") {
        item.start = q(clamp(item.start, 0, 86400));
        item.end = q(Math.max(item.start + MIN_DURATION, Number(item.end) || item.start + 5));
        item.width = clamp(item.width, 10, 3840);
        item.scale = clamp(item.scale, 0.1, 4);
        item.opacity = clamp(item.opacity, 0, 1);
        item.rotation = ((Number(item.rotation) % 360) + 360) % 360;
        item.x = q(item.x);
        item.y = q(item.y);
    } else if (type === "text") {
        item.content = String(item.content ?? "").slice(0, 200);
        item.fontFamily = FONTS.includes(item.fontFamily) ? item.fontFamily : "Arial";
        item.fontSize = clamp(item.fontSize, 8, 400);
        item.color = /^#[0-9a-fA-F]{6}$/.test(item.color || "") ? item.color.toUpperCase() : "#FFFFFF";
        item.align = ["left", "center", "right"].includes(item.align) ? item.align : "center";
        item.bold = Boolean(item.bold);
        item.italic = Boolean(item.italic);
        item.start = q(clamp(item.start, 0, 86400));
        item.end = q(Math.max(item.start + MIN_DURATION, Number(item.end) || item.start + 5));
        item.opacity = clamp(item.opacity, 0, 1);
        item.rotation = ((Number(item.rotation) % 360) + 360) % 360;
        item.x = q(item.x);
        item.y = q(item.y);
    }
}

function normalize(data) {
    const project = blankProject();
    if (!data || typeof data !== "object") return project;
    project.name = String(data.name || project.name).trim().slice(0, 80) || "Untitled Project";
    project.fps = [24, 25, 30, 50, 60].includes(Number(data.fps)) ? Number(data.fps) : 30;
    project.width = clamp(data.width || 1920, 320, 3840);
    project.height = clamp(data.height || 1080, 240, 2160);
    project.width -= project.width % 2;
    project.height -= project.height % 2;
    project.playhead = clamp(data.playhead || 0, 0, 86400);
    project.muteOriginal = Boolean(data.muteOriginal);
    project.exportMode = data.exportMode === "high" ? "high" : "fast";
    const filters = data.filters || {};
    project.filters = {
        brightness: clamp(filters.brightness, -100, 100),
        contrast: clamp(filters.contrast, -100, 100),
        saturation: clamp(filters.saturation, -100, 100),
        grayscale: clamp(filters.grayscale, 0, 100),
        blur: clamp(filters.blur, 0, 20),
        sepia: clamp(filters.sepia, 0, 100)
    };
    const take = (list, type, limit) => (Array.isArray(list) ? list.slice(0, limit) : []).map((item) => {
        const copy = { ...item, id: String(item.id || uid()) };
        sanitizeItem(type, copy);
        return copy;
    });
    project.clips = take(data.clips, "video", 40);
    project.images = take(data.images, "image", 40);
    project.texts = take(data.texts, "text", 40);
    project.audios = take(data.audios, "audio", 20);
    return project;
}

export function findItem(type, id, project = state) {
    return collection(project, type).find((item) => item.id === id) || null;
}

export function setSelection(next) {
    selection = next;
    emit("selection");
}

export function mutate(fn, { history = true, reason = "change" } = {}) {
    if (history) checkpoint();
    fn(state);
    persist();
    emit(reason);
}

export function setPlayhead(time, { persistState = false } = {}) {
    state.playhead = q(clamp(time, 0, displayDuration()));
    if (persistState) persist();
    emit("playhead");
}

export function undo() {
    if (!past.length) return;
    future.push(JSON.stringify(core(state)));
    const restored = JSON.parse(past.pop());
    restored.playhead = state.playhead;
    state = restored;
    selection = null;
    persist();
    emit("change");
}

export function redo() {
    if (!future.length) return;
    past.push(JSON.stringify(core(state)));
    const restored = JSON.parse(future.pop());
    restored.playhead = state.playhead;
    state = restored;
    selection = null;
    persist();
    emit("change");
}

export function loadStoredProject() {
    try {
        const raw = localStorage.getItem(STORAGE_KEY);
        state = raw ? normalize(JSON.parse(raw)) : blankProject();
    } catch {
        state = blankProject();
    }
    past = [];
    future = [];
    selection = null;
    emit("change");
}

export function replaceProject(data) {
    checkpoint();
    state = normalize(data);
    state.playhead = 0;
    selection = null;
    persist();
    emit("change");
}

export function newProject() {
    checkpoint();
    state = blankProject();
    selection = null;
    persist();
    emit("change");
}

export function resetProject() {
    state = blankProject();
    past = [];
    future = [];
    selection = null;
    persist();
    emit("change");
}

export function serializedProject() {
    return core(state);
}

export function updateProject(patch, options) {
    mutate((project) => {
        Object.assign(project, patch);
        if (patch.filters) project.filters = { ...project.filters, ...patch.filters };
        const normalized = normalize(project);
        normalized.playhead = project.playhead;
        Object.assign(project, normalized);
    }, options);
}

export function updateFilters(patch, options) {
    mutate((project) => {
        Object.assign(project.filters, patch);
        project.filters = normalize({ filters: project.filters }).filters;
    }, options);
}

export function updateItem(type, id, patch, options) {
    mutate((project) => {
        const item = findItem(type, id, project);
        if (!item) return;
        Object.assign(item, patch);
        sanitizeItem(type, item);
    }, options);
}

function sourceBox(media, crop, rotation) {
    let width = media?.width || 1920;
    let height = media?.height || 1080;
    width *= Math.max(0.1, 1 - ((crop?.left || 0) + (crop?.right || 0)) / 100);
    height *= Math.max(0.1, 1 - ((crop?.top || 0) + (crop?.bottom || 0)) / 100);
    const rad = ((rotation || 0) * Math.PI) / 180;
    return {
        sw: width,
        sh: height,
        bw: Math.abs(width * Math.cos(rad)) + Math.abs(height * Math.sin(rad)),
        bh: Math.abs(width * Math.sin(rad)) + Math.abs(height * Math.cos(rad))
    };
}

export function videoLayout(clip, media, project = state) {
    const box = sourceBox(media, clip.crop, clip.rotation);
    const fit = Math.min((project.width * clip.scale) / box.bw, (project.height * clip.scale) / box.bh);
    return { x: clip.x, y: clip.y, w: box.bw * fit, h: box.bh * fit, sw: box.sw * fit, sh: box.sh * fit };
}

export function imageLayout(item, media) {
    const aspect = media?.width ? media.height / media.width : 0.75;
    const w = item.width * item.scale;
    return { x: item.x, y: item.y, w, h: w * aspect };
}

function insertAt(project, type, at) {
    if (typeof at === "number") return q(Math.max(0, at));
    if (type === "image" || type === "text") return q(project.playhead);
    return q(trackEnd(project, type));
}

export function addMedia(media, at = null) {
    if (!media) return;
    if (media.type === "video") addVideo(media, at);
    else if (media.type === "image") addImage(media, at);
    else if (media.type === "audio") addAudio(media, at);
}

export function addVideo(media, at = null) {
    const id = uid();
    const duration = Math.max(MIN_DURATION, Number(media.duration) || MIN_DURATION);
    mutate((project) => {
        const draft = {
            x: 0, y: 0, scale: 1, rotation: 0,
            crop: { top: 0, right: 0, bottom: 0, left: 0 }
        };
        const fitted = videoLayout(draft, media, project);
        project.clips.push({
            id,
            mediaId: media.id,
            name: media.name,
            timelineStart: insertAt(project, "video", at),
            inPoint: 0,
            outPoint: duration,
            sourceDuration: duration,
            speed: 1,
            volume: 1,
            muted: false,
            x: q((project.width - fitted.w) / 2),
            y: q((project.height - fitted.h) / 2),
            scale: 1,
            rotation: 0,
            crop: { top: 0, right: 0, bottom: 0, left: 0 }
        });
    });
    setSelection({ type: "video", id });
}

export function addImage(media, at = null) {
    const id = uid();
    mutate((project) => {
        const start = insertAt(project, "image", at);
        const width = Math.min(480, project.width * 0.4);
        const aspect = media.width ? media.height / media.width : 1;
        const height = width * aspect;
        project.images.push({
            id,
            mediaId: media.id,
            name: media.name,
            x: q((project.width - width) / 2),
            y: q((project.height - height) / 2),
            width,
            scale: 1,
            start,
            end: q(start + 5),
            opacity: 1,
            rotation: 0
        });
    });
    setSelection({ type: "image", id });
}

export function addText() {
    const id = uid();
    mutate((project) => {
        const start = q(project.playhead);
        project.texts.push({
            id,
            content: "Your text",
            fontFamily: "Arial",
            fontSize: 64,
            color: "#FFFFFF",
            bold: false,
            italic: false,
            align: "center",
            x: project.width / 2,
            y: project.height / 2,
            start,
            end: q(start + 5),
            opacity: 1,
            rotation: 0
        });
    });
    setSelection({ type: "text", id });
}

export function addAudio(media, at = null) {
    const id = uid();
    const duration = Math.max(MIN_DURATION, Number(media.duration) || MIN_DURATION);
    mutate((project) => {
        project.audios.push({
            id,
            mediaId: media.id,
            name: media.name,
            timelineStart: insertAt(project, "audio", at),
            inPoint: 0,
            outPoint: duration,
            sourceDuration: duration,
            volume: 0.8,
            track: 1
        });
    });
    setSelection({ type: "audio", id });
}

export function deleteSelection({ notifyIfEmpty = true } = {}) {
    if (!selection) {
        if (notifyIfEmpty) notify("Select a timeline clip first.", "warning");
        return;
    }
    const { type, id } = selection;
    mutate((project) => {
        const key = { video: "clips", image: "images", text: "texts", audio: "audios" }[type];
        project[key] = project[key].filter((item) => item.id !== id);
    });
    selection = null;
    emit("selection");
}

export function deleteByMedia(mediaId) {
    mutate((project) => {
        project.clips = project.clips.filter((item) => item.mediaId !== mediaId);
        project.images = project.images.filter((item) => item.mediaId !== mediaId);
        project.audios = project.audios.filter((item) => item.mediaId !== mediaId);
    });
    if (selection) {
        const still = findItem(selection.type, selection.id);
        if (!still) selection = null;
    }
}

export function splitSelected() {
    if (!selection || selection.type !== "video") {
        notify("Select a video clip to split.", "warning");
        return false;
    }
    const clip = findItem("video", selection.id);
    if (!clip) return false;
    const span = itemSpan("video", clip);
    const time = state.playhead;
    if (time <= span.start + MIN_DURATION || time >= span.end - MIN_DURATION) {
        notify("Move the playhead inside the clip, away from its edges.", "warning");
        return false;
    }
    const secondId = uid();
    const sourceAt = clip.inPoint + (time - clip.timelineStart) * clip.speed;
    mutate((project) => {
        const current = findItem("video", clip.id, project);
        const copy = JSON.parse(JSON.stringify(current));
        current.outPoint = sourceAt;
        copy.id = secondId;
        copy.inPoint = sourceAt;
        copy.timelineStart = time;
        sanitizeItem("video", current);
        sanitizeItem("video", copy);
        const index = project.clips.findIndex((item) => item.id === current.id);
        project.clips.splice(index + 1, 0, copy);
    });
    setSelection({ type: "video", id: secondId });
    return true;
}
