import { getMedia } from "./media.js";
import {
    contentDuration, displayDuration, formatTime, getProject, getSelection, imageLayout,
    itemSpan, notify, setPlayhead, setSelection, videoLayout, checkpoint, dropEmptyCheckpoint, updateItem
} from "./project.js";

const videos = new Map();
const audios = new Map();
const images = new Map();
let monitorMuted = false;
let playing = false;
let clockBase = 0;
let timeBase = 0;
let raf = 0;
let hits = [];

function canvasPoint(event) {
    const canvas = document.getElementById("previewCanvas");
    const rect = canvas.getBoundingClientRect();
    const project = getProject();
    return {
        x: ((event.clientX - rect.left) / rect.width) * project.width,
        y: ((event.clientY - rect.top) / rect.height) * project.height
    };
}

function mediaUrl(media) {
    if (!media) return "";
    return media.previewUrl || media.url;
}

function mediaSink() {
    let sink = document.getElementById("mediaSink");
    if (!sink) {
        sink = document.createElement("div");
        sink.id = "mediaSink";
        sink.setAttribute("aria-hidden", "true");
        sink.style.cssText = "position:fixed;left:0;top:0;width:0;height:0;overflow:hidden;opacity:0;pointer-events:none";
        document.body.appendChild(sink);
    }
    return sink;
}

function getVideo(id, url) {
    let video = videos.get(id);
    if (!video || video.dataset.src !== url) {
        video = document.createElement("video");
        video.muted = true;
        video.defaultMuted = true;
        video.playsInline = true;
        video.preload = "auto";
        video.src = url;
        video.dataset.src = url;
        video.addEventListener("seeked", () => { if (!playing) draw(); });
        video.addEventListener("timeupdate", () => { if (playing) draw(); });
        video.addEventListener("error", () => { video.dataset.failed = "1"; draw(); });
        mediaSink().appendChild(video);
        videos.set(id, video);
    }
    return video;
}

function getAudio(id, url) {
    let audio = audios.get(id);
    if (!audio || audio.dataset.src !== url) {
        audio = new Audio(url);
        audio.preload = "auto";
        audio.dataset.src = url;
        audios.set(id, audio);
    }
    return audio;
}

function getImage(id, url) {
    let image = images.get(id);
    if (!image) {
        image = new Image();
        image.src = url;
        image.addEventListener("load", () => draw());
        images.set(id, image);
    }
    return image;
}

function cssFilter(filters) {
    const neutral = !filters.brightness && !filters.contrast && !filters.saturation && !filters.grayscale && !filters.blur && !filters.sepia;
    if (neutral) return "none";
    const saturation = (100 + Number(filters.saturation || 0)) * (1 - Number(filters.grayscale || 0) / 100);
    return `brightness(${100 + Number(filters.brightness)}%) contrast(${100 + Number(filters.contrast)}%) saturate(${saturation}%) blur(${Number(filters.blur)}px) sepia(${Number(filters.sepia)}%)`;
}

function syncElement(element, shouldPlay, sourceTime, rate, volume) {
    const picture = element instanceof HTMLVideoElement;
    element.playbackRate = rate;
    if (picture) {
        element.muted = true;
        element.volume = 0;
    } else {
        element.volume = monitorMuted ? 0 : Math.max(0, Math.min(1, volume));
    }
    const silentAudio = !picture && (volume <= 0 || monitorMuted);
    if (!shouldPlay || silentAudio) {
        if (!element.paused) element.pause();
        if (!shouldPlay && Math.abs(element.currentTime - sourceTime) > 0.08) {
            try { element.currentTime = sourceTime; } catch { /* The media may not be seekable yet. */ }
        }
        return;
    }
    const drift = Math.abs((element.currentTime || 0) - sourceTime);
    if (drift > 0.45) {
        try { element.currentTime = sourceTime; } catch { /* Ignore seek races during load. */ }
    }
    if (element.paused) element.play().catch(() => {});
}

function drawVideo(ctx, clip, media, project) {
    const span = itemSpan("video", clip);
    const time = project.playhead;
    const active = time >= span.start && time <= span.end;
    const sourceTime = clip.inPoint + Math.max(0, time - span.start) * clip.speed;
    const layout = videoLayout(clip, media, project);
    if (!media) {
        paintMissing(ctx, layout, clip.name);
        return;
    }
    const video = getVideo(clip.id, mediaUrl(media));
    if (active) {
        syncElement(video, playing, Math.min(sourceTime, Math.max(0, (video.duration || sourceTime) - 0.05)), clip.speed, 0);
        if (video.dataset.failed === "1") {
            paintMissing(ctx, layout, "Preview unavailable — export can still include it");
        } else if (video.readyState >= 2) {
            ctx.save();
            ctx.translate(layout.x + layout.w / 2, layout.y + layout.h / 2);
            ctx.rotate((clip.rotation * Math.PI) / 180);
            const crop = clip.crop || {};
            const sx = (media.width || video.videoWidth) * (crop.left || 0) / 100;
            const sy = (media.height || video.videoHeight) * (crop.top || 0) / 100;
            const sw = (media.width || video.videoWidth) * Math.max(0.1, 1 - ((crop.left || 0) + (crop.right || 0)) / 100);
            const sh = (media.height || video.videoHeight) * Math.max(0.1, 1 - ((crop.top || 0) + (crop.bottom || 0)) / 100);
            ctx.drawImage(video, sx, sy, sw, sh, -layout.sw / 2, -layout.sh / 2, layout.sw, layout.sh);
            ctx.restore();
        } else {
            paintMissing(ctx, layout, "Loading video…");
        }
        if (media.hasAudio && !project.muteOriginal && !clip.muted) {
            const audio = getAudio(`v-${clip.id}`, mediaUrl(media));
            syncElement(audio, playing, sourceTime, clip.speed, clip.volume);
        } else {
            const audio = audios.get(`v-${clip.id}`);
            if (audio && !audio.paused) audio.pause();
        }
    } else {
        video.pause();
        const audio = audios.get(`v-${clip.id}`);
        if (audio && !audio.paused) audio.pause();
    }
    if (active) hits.push({ type: "video", id: clip.id, x: layout.x, y: layout.y, w: layout.w, h: layout.h });
}

function paintMissing(ctx, layout, label) {
    ctx.save();
    ctx.fillStyle = "#1f2937";
    ctx.fillRect(layout.x, layout.y, Math.max(layout.w, 40), Math.max(layout.h, 40));
    ctx.fillStyle = "#cbd5e1";
    ctx.font = "28px Segoe UI, sans-serif";
    ctx.fillText(label, layout.x + 12, layout.y + 36);
    ctx.restore();
}

function drawImageLayer(ctx, item, media) {
    const project = getProject();
    const span = itemSpan("image", item);
    if (project.playhead < span.start || project.playhead > span.end) return;
    const layout = imageLayout(item, media);
    ctx.save();
    ctx.globalAlpha = item.opacity;
    ctx.translate(layout.x + layout.w / 2, layout.y + layout.h / 2);
    ctx.rotate((item.rotation * Math.PI) / 180);
    if (media) {
        const image = getImage(item.id, media.url);
        if (image.complete && image.naturalWidth) ctx.drawImage(image, -layout.w / 2, -layout.h / 2, layout.w, layout.h);
    } else {
        ctx.fillStyle = "#334155";
        ctx.fillRect(-layout.w / 2, -layout.h / 2, layout.w, layout.h);
    }
    ctx.restore();
    hits.push({ type: "image", id: item.id, x: layout.x, y: layout.y, w: layout.w, h: layout.h });
}

function drawBackgroundAudio(clip) {
    const media = getMedia(clip.mediaId);
    if (!media) return;
    const project = getProject();
    const span = itemSpan("audio", clip);
    const active = project.playhead >= span.start && project.playhead <= span.end;
    const sourceTime = clip.inPoint + Math.max(0, project.playhead - span.start);
    const audio = getAudio(`a-${clip.id}`, media.url);
    syncElement(audio, playing && active, sourceTime, 1, active ? clip.volume : 0);
}

function drawText(ctx, item) {
    const project = getProject();
    if (!item.content.trim() || project.playhead < item.start || project.playhead > item.end) return;
    ctx.save();
    ctx.globalAlpha = item.opacity;
    ctx.translate(item.x, item.y);
    ctx.rotate((item.rotation * Math.PI) / 180);
    ctx.fillStyle = item.color;
    ctx.textAlign = item.align;
    ctx.textBaseline = "middle";
    ctx.font = `${item.italic ? "italic " : ""}${item.bold ? "bold " : ""}${item.fontSize}px ${item.fontFamily}`;
    ctx.fillText(item.content, 0, 0);
    const width = ctx.measureText(item.content).width;
    ctx.restore();
    hits.push({
        type: "text",
        id: item.id,
        anchorX: item.x,
        anchorY: item.y,
        width,
        height: item.fontSize,
        align: item.align,
        rotation: item.rotation
    });
}

function drawSelection(ctx) {
    const selected = getSelection();
    if (!selected) return;
    const hit = [...hits].reverse().find((item) => item.type === selected.type && item.id === selected.id);
    if (!hit) return;
    const canvas = document.getElementById("previewCanvas");
    const rect = canvas.getBoundingClientRect();
    ctx.save();
    ctx.filter = "none";
    ctx.strokeStyle = "#93c5fd";
    ctx.lineWidth = rect.width ? (canvas.width / rect.width) * 2 : 2;
    if (hit.anchorX !== undefined) {
        ctx.translate(hit.anchorX, hit.anchorY);
        ctx.rotate((hit.rotation * Math.PI) / 180);
        let left = 0;
        if (hit.align === "center") left = -hit.width / 2;
        if (hit.align === "right") left = -hit.width;
        ctx.strokeRect(left - 6, -hit.height / 2 - 6, hit.width + 12, hit.height + 12);
    } else {
        ctx.strokeRect(hit.x, hit.y, hit.w, hit.h);
    }
    ctx.restore();
}

export function draw() {
    const canvas = document.getElementById("previewCanvas");
    const project = getProject();
    if (canvas.width !== project.width) canvas.width = project.width;
    if (canvas.height !== project.height) canvas.height = project.height;
    const ctx = canvas.getContext("2d");
    hits = [];
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.filter = cssFilter(project.filters);
    ctx.fillStyle = "black";
    ctx.fillRect(0, 0, canvas.width, canvas.height);
    project.clips.forEach((clip) => drawVideo(ctx, clip, getMedia(clip.mediaId), project));
    project.images.forEach((item) => drawImageLayer(ctx, item, getMedia(item.mediaId)));
    project.texts.forEach((item) => drawText(ctx, item));
    project.audios.forEach((clip) => drawBackgroundAudio(clip));
    ctx.filter = "none";
    drawSelection(ctx);
    const empty = document.getElementById("stageEmpty");
    empty.hidden = contentDuration() > 0;
    updateTransport();
}

function updateTransport() {
    const project = getProject();
    const total = contentDuration();
    document.getElementById("timeLabel").textContent = `${formatTime(Math.min(project.playhead, total || project.playhead))} / ${formatTime(total)}`;
    const seek = document.getElementById("seekBar");
    const span = displayDuration();
    seek.value = String(span ? (project.playhead / span) * 1000 : 0);
    const icon = document.querySelector("#playBtn i");
    icon.className = playing ? "fa-solid fa-pause" : "fa-solid fa-play";
}

function pauseAll() {
    videos.forEach((video) => video.pause());
    audios.forEach((audio) => audio.pause());
}

export function stopPlayback() {
    playing = false;
    cancelAnimationFrame(raf);
    pauseAll();
    draw();
}

export function togglePlayback() {
    if (playing) {
        stopPlayback();
        setPlayhead(getProject().playhead, { persistState: true });
        return;
    }
    const end = contentDuration();
    if (end <= 0) {
        notify("Add media to the timeline to preview.");
        return;
    }
    if (getProject().playhead >= end - 0.05) setPlayhead(0);
    playing = true;
    timeBase = getProject().playhead;
    clockBase = performance.now();
    draw();
    const frame = (now) => {
        if (!playing) return;
        const limit = contentDuration();
        let time = timeBase + (now - clockBase) / 1000;
        if (time >= limit) {
            playing = false;
            setPlayhead(limit, { persistState: true });
            pauseAll();
            draw();
            return;
        }
        setPlayhead(time);
        raf = requestAnimationFrame(frame);
    };
    raf = requestAnimationFrame(frame);
}

function hitAt(point) {
    for (let index = hits.length - 1; index >= 0; index -= 1) {
        const hit = hits[index];
        if (hit.anchorX !== undefined) {
            const dx = point.x - hit.anchorX;
            const dy = point.y - hit.anchorY;
            const rad = (-hit.rotation * Math.PI) / 180;
            const localX = dx * Math.cos(rad) - dy * Math.sin(rad);
            const localY = dx * Math.sin(rad) + dy * Math.cos(rad);
            let left = 0;
            let right = hit.width;
            if (hit.align === "center") { left = -hit.width / 2; right = hit.width / 2; }
            if (hit.align === "right") { left = -hit.width; right = 0; }
            if (localX >= left && localX <= right && localY >= -hit.height / 2 && localY <= hit.height / 2) return hit;
        } else if (point.x >= hit.x && point.x <= hit.x + hit.w && point.y >= hit.y && point.y <= hit.y + hit.h) {
            return hit;
        }
    }
    return null;
}

function layoutStage() {
    const stage = document.getElementById("stage");
    const canvas = document.getElementById("previewCanvas");
    const project = getProject();
    const rect = stage.getBoundingClientRect();
    const ratio = project.width / project.height;
    let width = rect.width;
    let height = width / ratio;
    if (height > rect.height) {
        height = rect.height;
        width = height * ratio;
    }
    canvas.style.width = `${Math.max(10, width)}px`;
    canvas.style.height = `${Math.max(10, height)}px`;
}

export function initPreview() {
    const canvas = document.getElementById("previewCanvas");
    const seek = document.getElementById("seekBar");
    document.getElementById("playBtn").addEventListener("click", togglePlayback);
    document.getElementById("stopBtn").addEventListener("click", () => {
        stopPlayback();
        setPlayhead(0, { persistState: true });
    });
    document.getElementById("skipStartBtn").addEventListener("click", () => {
        stopPlayback();
        setPlayhead(0, { persistState: true });
    });
    document.getElementById("skipEndBtn").addEventListener("click", () => {
        stopPlayback();
        setPlayhead(contentDuration(), { persistState: true });
    });
    document.getElementById("muteBtn").addEventListener("click", () => {
        monitorMuted = !monitorMuted;
        document.querySelector("#muteBtn i").className = monitorMuted ? "fa-solid fa-volume-xmark" : "fa-solid fa-volume-high";
        if (monitorMuted) audios.forEach((audio) => { audio.volume = 0; });
    });
    seek.addEventListener("input", () => {
        stopPlayback();
        setPlayhead((Number(seek.value) / 1000) * displayDuration());
    });
    seek.addEventListener("change", () => setPlayhead(getProject().playhead, { persistState: true }));

    let drag = null;
    canvas.addEventListener("pointerdown", (event) => {
        const point = canvasPoint(event);
        const hit = hitAt(point);
        if (!hit) {
            setSelection(null);
            return;
        }
        setSelection({ type: hit.type, id: hit.id });
        checkpoint();
        drag = { ...hit, originX: point.x, originY: point.y, item: { ...getSelection() } };
        const selected = getSelection();
        const current = selected ? { ...selected } : null;
        drag.item = current;
        drag.start = current ? { ...findCurrent(current) } : null;
        canvas.setPointerCapture(event.pointerId);
    });
    canvas.addEventListener("pointermove", (event) => {
        const point = canvasPoint(event);
        canvas.style.cursor = hitAt(point) ? "move" : "default";
        if (!drag?.start) return;
        const dx = point.x - drag.originX;
        const dy = point.y - drag.originY;
        updateItem(drag.type, drag.id, { x: drag.start.x + dx, y: drag.start.y + dy }, { history: false, reason: "live" });
    });
    const finishDrag = () => {
        if (!drag) return;
        dropEmptyCheckpoint();
        drag = null;
    };
    canvas.addEventListener("pointerup", finishDrag);
    canvas.addEventListener("pointercancel", finishDrag);
    new ResizeObserver(layoutStage).observe(document.getElementById("stage"));
    layoutStage();
    draw();
}

function findCurrent(selection) {
    const project = getProject();
    const list = { video: project.clips, image: project.images, text: project.texts }[selection.type] || [];
    return list.find((item) => item.id === selection.id);
}

export function onProjectEvent(reason) {
    if (reason === "change") {
        const project = getProject();
        const aliveVideos = new Set(project.clips.map((clip) => clip.id));
        const aliveAudio = new Set([
            ...project.clips.map((clip) => `v-${clip.id}`),
            ...project.audios.map((clip) => `a-${clip.id}`)
        ]);
        videos.forEach((video, id) => {
            if (!aliveVideos.has(id)) {
                video.pause();
                video.removeAttribute("src");
                videos.delete(id);
            }
        });
        audios.forEach((audio, id) => {
            if (!aliveAudio.has(id)) {
                audio.pause();
                audio.removeAttribute("src");
                audios.delete(id);
            }
        });
    }
    layoutStage();
    draw();
}
