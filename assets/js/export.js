import { contentDuration, getProject, notify, serializedProject } from "./project.js";

let modal;
let pollTimer = 0;
let exporting = false;

function formatElapsed(seconds) {
    const mins = Math.floor(seconds / 60);
    const secs = seconds % 60;
    return `${mins}:${String(secs).padStart(2, "0")}`;
}

function showJob(job) {
    const bar = document.getElementById("exportBar");
    const status = document.getElementById("exportStatusText");
    const elapsed = document.getElementById("exportElapsed");
    const error = document.getElementById("exportError");
    const download = document.getElementById("exportDownload");
    const percent = Math.max(0, Math.min(100, Number(job.progress) || 0));
    bar.style.width = `${percent}%`;
    bar.textContent = `${Math.round(percent)}%`;
    bar.parentElement.setAttribute("aria-valuenow", String(Math.round(percent)));
    const labels = { queued: "Queued", running: "Rendering", done: "Export complete", error: "Export failed" };
    status.textContent = labels[job.status] || "Working";
    elapsed.textContent = job.elapsed ? `Processing time ${formatElapsed(job.elapsed)}` : "";
    error.textContent = job.error || "";
    if (job.status === "done" && job.downloadUrl) {
        download.href = job.downloadUrl;
        download.classList.remove("d-none");
        download.textContent = "Download MP4";
    } else {
        download.classList.add("d-none");
    }
}

async function poll(jobId) {
    try {
        const response = await fetch(`api/export_status.php?job=${encodeURIComponent(jobId)}`);
        const data = await response.json();
        if (!response.ok || !data.ok) throw new Error(data.message || "Export job not found.");
        showJob(data.job);
        if (data.job.status === "done") {
            exporting = false;
            notify("Export complete. Your MP4 is ready.", "success");
            return;
        }
        if (data.job.status === "error") {
            exporting = false;
            notify(data.job.error || "Export failed.", "error");
            return;
        }
        pollTimer = window.setTimeout(() => poll(jobId), 1000);
    } catch (error) {
        exporting = false;
        document.getElementById("exportError").textContent = error.message;
        notify(error.message, "error");
    }
}

export async function startExport() {
    if (exporting) {
        notify("An export is already running.", "warning");
        modal.show();
        return;
    }
    if (contentDuration() <= 0 || (getProject().clips.length + getProject().images.length + getProject().texts.length) === 0) {
        notify("Add at least one video, image, or text element before exporting.", "warning");
        return;
    }
    exporting = true;
    showJob({ status: "queued", progress: 0, elapsed: 0, error: "" });
    modal.show();
    try {
        const response = await fetch("api/export.php", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(serializedProject())
        });
        const data = await response.json();
        if (!response.ok || !data.ok) throw new Error(data.message || "Export failed.");
        poll(data.job.id);
    } catch (error) {
        exporting = false;
        showJob({ status: "error", progress: 0, elapsed: 0, error: error.message });
        notify(error.message, "error");
    }
}

export function initExport() {
    modal = new bootstrap.Modal(document.getElementById("exportModal"));
    document.getElementById("exportBtn").addEventListener("click", startExport);
    document.getElementById("exportModal").addEventListener("hidden.bs.modal", () => {
        if (!exporting) window.clearTimeout(pollTimer);
    });
}
