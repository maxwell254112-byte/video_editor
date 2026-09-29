import { refreshMedia } from "./media.js";
import { getLimits as readLimits, notify as toast, setLimits as storeLimits } from "./project.js";

const ACCEPT = {
    mp4: "video", webm: "video", mov: "video", wmv: "video",
    jpg: "image", jpeg: "image", png: "image", webp: "image",
    mp3: "audio", wav: "audio", aac: "audio", m4a: "audio"
};

function extensionOf(name) {
    const parts = String(name).toLowerCase().split(".");
    return parts.length > 1 ? parts.pop() : "";
}

function validateFile(file) {
    const kind = ACCEPT[extensionOf(file.name)];
    if (!kind) return "Unsupported file type.";
    const limits = readLimits();
    if (file.size > (limits[kind] || 0)) return "File is too large.";
    if (file.size <= 0) return "Upload failed.";
    return "";
}

function setProgress(visible, label, percent) {
    const box = document.getElementById("uploadProgress");
    box.classList.toggle("d-none", !visible);
    document.getElementById("uploadLabel").textContent = label;
    document.getElementById("uploadPercent").textContent = `${Math.round(percent)}%`;
    document.getElementById("uploadBar").style.width = `${percent}%`;
}

function uploadOne(file) {
    return new Promise((resolve, reject) => {
        const body = new FormData();
        body.append("files[]", file);
        const xhr = new XMLHttpRequest();
        xhr.open("POST", "api/upload.php");
        xhr.upload.onprogress = (event) => {
            if (event.lengthComputable) {
                setProgress(true, `Uploading ${file.name}`, (event.loaded / event.total) * 100);
            }
        };
        xhr.onload = () => {
            let data = {};
            try { data = JSON.parse(xhr.responseText); } catch { data = {}; }
            if (xhr.status >= 200 && xhr.status < 300 && data.ok) resolve(data);
            else reject(new Error(data.message || "Upload failed."));
        };
        xhr.onerror = () => reject(new Error("Upload failed."));
        xhr.send(body);
    });
}

export async function uploadFiles(fileList) {
    const files = [...fileList];
    if (!files.length) return;
    let uploaded = 0;
    for (const file of files) {
        const problem = validateFile(file);
        if (problem) {
            toast(`${file.name}: ${problem}`, "error");
            continue;
        }
        try {
            setProgress(true, `Uploading ${file.name}`, 0);
            const result = await uploadOne(file);
            uploaded += result.media?.length || 0;
            (result.errors || []).forEach((error) => toast(error, "error"));
            if (result.message && result.errors?.length) toast(result.message, "warning");
        } catch (error) {
            toast(`${file.name}: ${error.message}`, "error");
        }
    }
    setProgress(true, "Processing media…", 100);
    try {
        await refreshMedia();
    } catch (error) {
        toast(error.message, "error");
    }
    setProgress(false, "", 0);
    if (uploaded) toast("Upload successful", "success");
}

export function initUpload() {
    const input = document.getElementById("fileInput");
    const zone = document.getElementById("dropzone");
    document.getElementById("browseBtn").addEventListener("click", () => input.click());
    input.addEventListener("change", () => {
        uploadFiles(input.files);
        input.value = "";
    });
    ["dragenter", "dragover"].forEach((name) => zone.addEventListener(name, (event) => {
        event.preventDefault();
        zone.classList.add("is-dragover");
    }));
    zone.addEventListener("dragleave", () => zone.classList.remove("is-dragover"));
    zone.addEventListener("drop", (event) => {
        event.preventDefault();
        zone.classList.remove("is-dragover");
        uploadFiles(event.dataTransfer.files);
    });
    window.addEventListener("dragover", (event) => event.preventDefault());
    window.addEventListener("drop", (event) => {
        if (!zone.contains(event.target)) event.preventDefault();
    });
    document.getElementById("ffmpegStatus").addEventListener("click", refreshHealth);
}

export async function refreshHealth() {
    const pill = document.getElementById("ffmpegStatus");
    const label = document.getElementById("ffmpegLabel");
    try {
        const response = await fetch("api/health.php");
        const data = await response.json();
        if (data.limits) storeLimits(data.limits);
        const ready = Boolean(data.ffmpeg);
        pill.classList.toggle("is-ready", ready);
        pill.classList.toggle("is-down", !ready);
        label.textContent = ready ? "FFmpeg: Ready" : "FFmpeg: Not Available";
        if (!ready) {
            pill.title = "Install FFmpeg and FFprobe, or place them in the bin folder.";
        } else if (!data.phpCli) {
            pill.title = "FFmpeg is ready. PHP CLI was not found, so export needs php_cli in includes/config.local.php.";
        } else if (!data.writable) {
            pill.title = "FFmpeg is ready, but the upload or export folder is not writable.";
        } else {
            pill.title = data.version ? `FFmpeg ${data.version}` : "FFmpeg is ready.";
        }
    } catch {
        pill.classList.add("is-down");
        label.textContent = "FFmpeg: Not Available";
    }
}
