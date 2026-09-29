import {
    addText, canRedo, canUndo, checkpoint, contentDuration, deleteSelection, dropEmptyCheckpoint,
    getProject, isFormField, loadStoredProject, newProject, notify, redo, replaceProject,
    resetProject, serializedProject, setPlayhead, splitSelected, subscribe, undo, updateProject
} from "./project.js";
import { initMedia } from "./media.js";
import { initUpload, refreshHealth } from "./upload.js";
import { initPreview, onProjectEvent, stopPlayback, togglePlayback } from "./preview.js?v=3";
import { initTimeline, reloadTimeline } from "./timeline.js";
import { initInspector } from "./inspector.js";
import { initExport } from "./export.js";

function syncChrome() {
    const project = getProject();
    const name = document.getElementById("projectName");
    const mode = document.getElementById("exportMode");
    if (document.activeElement !== name) name.value = project.name;
    if (document.activeElement !== mode) mode.value = project.exportMode;
    document.getElementById("undoBtn").disabled = !canUndo();
    document.getElementById("redoBtn").disabled = !canRedo();
}

function toast({ message, type }) {
    const stack = document.getElementById("toasts");
    const item = document.createElement("div");
    item.className = `toast-item is-${type || "info"}`;
    item.textContent = message;
    stack.appendChild(item);
    window.setTimeout(() => item.remove(), 4200);
}

function saveProjectFile() {
    const data = serializedProject();
    const blob = new Blob([JSON.stringify(data, null, 2)], { type: "application/json" });
    const link = document.createElement("a");
    link.href = URL.createObjectURL(blob);
    link.download = `${data.name || "project"}.json`;
    link.click();
    URL.revokeObjectURL(link.href);
    notify("Project saved in this browser. A JSON copy was downloaded.", "success");
}

function hasContent(project) {
    return project.clips.length || project.images.length || project.texts.length || project.audios.length;
}

function init() {
    document.addEventListener("editor:toast", (event) => toast(event.detail));
    document.addEventListener("editor:media", () => {
        onProjectEvent("media");
        reloadTimeline();
    });
    loadStoredProject();
    initMedia();
    initUpload();
    initPreview();
    initTimeline();
    initInspector();
    initExport();
    syncChrome();
    refreshHealth();

    subscribe((reason) => {
        if (reason === "change") stopPlayback();
        onProjectEvent(reason);
        syncChrome();
    });

    document.querySelectorAll("[data-bs-toggle='tooltip']").forEach((element) => new bootstrap.Tooltip(element));

    document.getElementById("undoBtn").addEventListener("click", undo);
    document.getElementById("redoBtn").addEventListener("click", redo);
    document.getElementById("addTextBtn").addEventListener("click", addText);
    document.getElementById("splitBtn").addEventListener("click", splitSelected);
    document.getElementById("newProjectBtn").addEventListener("click", () => {
        if (hasContent(getProject()) && !confirm("Start a new project? You can undo this.")) return;
        newProject();
    });
    document.getElementById("resetProjectBtn").addEventListener("click", () => {
        if (!confirm("Reset the project and clear undo history?")) return;
        resetProject();
    });
    document.getElementById("saveProjectBtn").addEventListener("click", saveProjectFile);
    document.getElementById("loadProjectBtn").addEventListener("click", () => document.getElementById("projectFile").click());
    document.getElementById("projectFile").addEventListener("change", async (event) => {
        const file = event.target.files?.[0];
        event.target.value = "";
        if (!file) return;
        try {
            replaceProject(JSON.parse(await file.text()));
            notify("Project loaded.", "success");
        } catch {
            notify("Invalid project data.", "error");
        }
    });

    const name = document.getElementById("projectName");
    let nameArmed = false;
    name.addEventListener("focus", () => { checkpoint(); nameArmed = true; });
    name.addEventListener("input", () => updateProject({ name: name.value }, { history: false, reason: "live" }));
    name.addEventListener("blur", () => { nameArmed = false; dropEmptyCheckpoint(); });

    const mode = document.getElementById("exportMode");
    mode.addEventListener("change", () => updateProject({ exportMode: mode.value }));

    window.addEventListener("keydown", (event) => {
        if (document.querySelector(".modal.show")) return;
        const key = event.key.toLowerCase();
        if ((event.ctrlKey || event.metaKey) && key === "s") {
            event.preventDefault();
            saveProjectFile();
            return;
        }
        if ((event.ctrlKey || event.metaKey) && key === "z") {
            event.preventDefault();
            if (event.shiftKey) redo(); else undo();
            return;
        }
        if ((event.ctrlKey || event.metaKey) && key === "y") {
            event.preventDefault();
            redo();
            return;
        }
        if (isFormField(event.target)) return;
        if (event.code === "Space") {
            event.preventDefault();
            togglePlayback();
        } else if (event.key === "Delete" || event.key === "Backspace") {
            event.preventDefault();
            deleteSelection({ notifyIfEmpty: false });
        } else if (key === "s") {
            splitSelected();
        } else if (event.key === "Home") {
            stopPlayback();
            setPlayhead(0, { persistState: true });
        } else if (event.key === "End") {
            stopPlayback();
            setPlayhead(contentDuration(), { persistState: true });
        }
    });
}

init();
