import { getMedia } from "./media.js";
import {
    MIN_DURATION, addMedia, checkpoint, deleteSelection, displayDuration,
    dropEmptyCheckpoint, findItem, formatTime, getProject, getSelection, itemSpan, notify,
    setPlayhead, setSelection, subscribe, updateItem
} from "./project.js";

let pixelsPerSecond = 72;
let drag = null;

function renderTimeline() {
    const project = getProject();
    const duration = displayDuration();
    const width = Math.max(duration * pixelsPerSecond + 40, 600);
    const content = document.getElementById("timelineContent");
    content.style.width = `${width}px`;
    content.innerHTML = "";

    const ruler = document.createElement("div");
    ruler.className = "ruler";
    const step = pixelsPerSecond >= 70 ? 1 : 5;
    for (let second = 0; second <= duration; second += step) {
        const tick = document.createElement("div");
        tick.className = "tick";
        tick.style.left = `${second * pixelsPerSecond}px`;
        tick.textContent = formatTime(second);
        ruler.appendChild(tick);
    }
    content.appendChild(ruler);

    for (const type of ["video", "image", "text", "audio"]) {
        const lane = document.createElement("div");
        lane.className = "lane";
        lane.dataset.track = type;
        const list = { video: project.clips, image: project.images, text: project.texts, audio: project.audios }[type];
        list.forEach((item) => {
            const span = itemSpan(type, item);
            const clip = document.createElement("div");
            const selected = getSelection();
            clip.className = `clip clip-${type}${selected && selected.type === type && selected.id === item.id ? " selected" : ""}`;
            clip.style.left = `${span.start * pixelsPerSecond}px`;
            clip.style.width = `${Math.max(18, (span.end - span.start) * pixelsPerSecond)}px`;
            clip.dataset.type = type;
            clip.dataset.id = item.id;
            const missing = type !== "text" && !getMedia(item.mediaId);
            clip.innerHTML = `<span class="trim-handle" data-edge="start"></span><span class="clip-label"></span><span class="trim-handle" data-edge="end"></span>`;
            clip.querySelector(".clip-label").textContent = missing ? `${item.name || item.content || "Clip"} (missing)` : (item.name || item.content || "Clip");
            lane.appendChild(clip);
        });
        content.appendChild(lane);
    }

    const playhead = document.createElement("div");
    playhead.className = "playhead";
    playhead.style.left = `${project.playhead * pixelsPerSecond}px`;
    content.appendChild(playhead);
}

function updatePlayhead() {
    const playhead = document.querySelector(".playhead");
    if (!playhead) return;
    const x = getProject().playhead * pixelsPerSecond;
    playhead.style.left = `${x}px`;
    const scroller = document.getElementById("timelineScroll");
    if (drag) return;
    const view = scroller.scrollLeft;
    const limit = scroller.clientWidth;
    if (x < view + 30 || x > view + limit - 40) scroller.scrollLeft = Math.max(0, x - limit * 0.3);
}

function timeFromEvent(event) {
    const content = document.getElementById("timelineContent");
    const rect = content.getBoundingClientRect();
    return Math.max(0, (event.clientX - rect.left) / pixelsPerSecond);
}

function applyDrag(event) {
    const delta = (event.clientX - drag.clientX) / pixelsPerSecond;
    const origin = drag.origin;
    const type = drag.type;
    if (drag.mode === "move") {
        const start = Math.max(0, origin.start + delta);
        if (type === "video" || type === "audio") updateItem(type, drag.id, { timelineStart: start }, { history: false, reason: "live" });
        else updateItem(type, drag.id, { start, end: start + (origin.end - origin.start) }, { history: false, reason: "live" });
        return;
    }
    if (type === "image" || type === "text") {
        if (drag.mode === "start") {
            const start = Math.min(origin.end - MIN_DURATION, Math.max(0, origin.start + delta));
            updateItem(type, drag.id, { start }, { history: false, reason: "live" });
        } else {
            updateItem(type, drag.id, { end: Math.max(origin.start + MIN_DURATION, origin.end + delta) }, { history: false, reason: "live" });
        }
        return;
    }
    const speed = origin.speed || 1;
    if (drag.mode === "start") {
        let start = Math.min(origin.end - MIN_DURATION, Math.max(0, origin.start + delta));
        let duration = origin.end - start;
        let inPoint = origin.outPoint - duration * speed;
        if (inPoint < 0) {
            inPoint = 0;
            duration = (origin.outPoint - inPoint) / speed;
            start = origin.end - duration;
        }
        updateItem(type, drag.id, { timelineStart: start, inPoint }, { history: false, reason: "live" });
    } else {
        const end = Math.max(origin.start + MIN_DURATION, origin.end + delta);
        const outPoint = Math.min(origin.sourceDuration, origin.inPoint + (end - origin.start) * speed);
        updateItem(type, drag.id, { outPoint }, { history: false, reason: "live" });
    }
}

function onPointerDown(event) {
    const content = document.getElementById("timelineContent");
    const clip = event.target.closest(".clip");
    if (!clip || !content.contains(clip)) {
        if (event.target.closest(".ruler") || event.target.closest(".lane")) {
            setPlayhead(timeFromEvent(event), { persistState: true });
        }
        return;
    }
    event.stopPropagation();
    const type = clip.dataset.type;
    const id = clip.dataset.id;
    const item = findItem(type, id);
    if (!item) return;
    setSelection({ type, id });
    document.querySelectorAll(".clip.selected").forEach((node) => node.classList.remove("selected"));
    clip.classList.add("selected");
    const handle = event.target.closest(".trim-handle");
    checkpoint();
    const span = itemSpan(type, item);
    drag = {
        type,
        id,
        mode: handle ? handle.dataset.edge : "move",
        clientX: event.clientX,
        origin: { ...span, ...item }
    };
    clip.setPointerCapture(event.pointerId);
}

function onPointerMove(event) {
    if (!drag) return;
    applyDrag(event);
    const clip = document.querySelector(`.clip[data-id="${CSS.escape(drag.id)}"]`);
    const item = findItem(drag.type, drag.id);
    if (clip && item) {
        const span = itemSpan(drag.type, item);
        clip.style.left = `${span.start * pixelsPerSecond}px`;
        clip.style.width = `${Math.max(18, (span.end - span.start) * pixelsPerSecond)}px`;
    }
}

function onPointerUp() {
    if (!drag) return;
    dropEmptyCheckpoint();
    drag = null;
    renderTimeline();
}

export function initTimeline() {
    const content = document.getElementById("timelineContent");
    const scroll = document.getElementById("timelineScroll");
    content.addEventListener("pointerdown", onPointerDown);
    content.addEventListener("pointermove", onPointerMove);
    content.addEventListener("pointerup", onPointerUp);
    content.addEventListener("pointercancel", onPointerUp);
    scroll.addEventListener("wheel", (event) => {
        if (!event.ctrlKey) return;
        event.preventDefault();
        setZoom(pixelsPerSecond * (event.deltaY < 0 ? 1.1 : 0.9));
    }, { passive: false });
    document.getElementById("zoomRange").addEventListener("input", (event) => setZoom(Number(event.target.value)));
    document.getElementById("zoomOutBtn").addEventListener("click", () => setZoom(pixelsPerSecond / 1.15));
    document.getElementById("zoomInBtn").addEventListener("click", () => setZoom(pixelsPerSecond * 1.15));
    document.getElementById("deleteClipBtn").addEventListener("click", deleteSelection);

    content.addEventListener("dragover", (event) => {
        const lane = event.target.closest(".lane");
        if (!lane) return;
        event.preventDefault();
        document.querySelectorAll(".lane").forEach((item) => item.classList.toggle("is-target", item === lane));
    });
    content.addEventListener("dragleave", () => {
        document.querySelectorAll(".lane.is-target").forEach((item) => item.classList.remove("is-target"));
    });
    content.addEventListener("drop", (event) => {
        const lane = event.target.closest(".lane");
        document.querySelectorAll(".lane.is-target").forEach((item) => item.classList.remove("is-target"));
        if (!lane) return;
        event.preventDefault();
        const raw = event.dataTransfer.getData("application/x-media");
        if (!raw) return;
        try {
            const media = JSON.parse(raw);
            const track = lane.dataset.track;
            if (media.type !== track) {
                notify(`Drop ${media.type} files on the ${media.type} track.`, "warning");
                return;
            }
            addMedia(media, timeFromEvent(event));
        } catch {
            notify("That media item could not be added.", "error");
        }
    });

    subscribe((reason) => {
        if (drag) return;
        if (reason === "playhead" || reason === "live") {
            updatePlayhead();
            return;
        }
        renderTimeline();
    });
    renderTimeline();
}

function setZoom(value) {
    pixelsPerSecond = Math.max(24, Math.min(220, value));
    document.getElementById("zoomRange").value = String(pixelsPerSecond);
    renderTimeline();
}

export function reloadTimeline() {
    if (!drag) renderTimeline();
}
