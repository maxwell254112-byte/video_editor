import { addMedia, deleteByMedia, formatTime, notify } from "./project.js";

let items = [];
let filter = "all";

export function allMedia() {
    return items;
}

export function getMedia(id) {
    return items.find((item) => item.id === id) || null;
}

export async function refreshMedia() {
    const response = await fetch("api/media.php");
    const data = await response.json();
    if (!response.ok || !data.ok) {
        throw new Error(data.message || "The media library could not be loaded.");
    }
    items = data.media || [];
    renderMedia();
    document.dispatchEvent(new CustomEvent("editor:media"));
}

function formatBytes(size) {
    if (size < 1024) return `${size} B`;
    if (size < 1048576) return `${(size / 1024).toFixed(1)} KB`;
    return `${(size / 1048576).toFixed(1)} MB`;
}

function renderMedia() {
    const list = document.getElementById("mediaList");
    const visible = items.filter((item) => filter === "all" || item.type === filter);
    list.innerHTML = "";
    if (!visible.length) {
        return;
    }
    visible.forEach((item) => {
        const card = document.createElement("article");
        card.className = "media-card";
        card.draggable = true;
        card.innerHTML = `
            <div class="thumb">${item.thumbUrl ? "" : `<i class="fa-solid ${item.type === "audio" ? "fa-music" : item.type === "image" ? "fa-image" : "fa-film"}"></i>`}</div>
            <div>
                <h3></h3>
                <p></p>
                <div class="media-actions">
                    <button class="btn btn-soft btn-sm add-btn" type="button">Add to Timeline</button>
                    <button class="btn btn-soft btn-sm delete-btn" type="button" title="Delete media"><i class="fa-solid fa-trash"></i></button>
                </div>
            </div>`;
        const thumb = card.querySelector(".thumb");
        if (item.thumbUrl) {
            thumb.style.backgroundImage = `url("${item.thumbUrl}")`;
        }
        card.querySelector("h3").textContent = item.name;
        const meta = item.duration ? `${formatTime(item.duration)} · ` : "";
        card.querySelector("p").textContent = `${meta}${formatBytes(item.size)}`;
        card.querySelector(".add-btn").addEventListener("click", () => addMedia(item));
        card.querySelector(".delete-btn").addEventListener("click", () => removeMedia(item));
        card.addEventListener("dragstart", (event) => {
            event.dataTransfer.setData("application/x-media", JSON.stringify(item));
            event.dataTransfer.effectAllowed = "copy";
        });
        list.appendChild(card);
    });
}

async function removeMedia(item) {
    if (!confirm(`Delete “${item.name}” from the library?`)) return;
    const response = await fetch("api/media.php", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ action: "delete", id: item.id })
    });
    const data = await response.json();
    if (!response.ok || !data.ok) {
        notify(data.message || "Media could not be deleted.", "error");
        return;
    }
    deleteByMedia(item.id);
    await refreshMedia();
    notify("Media deleted.", "success");
}

export function initMedia() {
    document.querySelectorAll(".media-tabs button").forEach((button) => {
        button.addEventListener("click", () => {
            filter = button.dataset.filter;
            document.querySelectorAll(".media-tabs button").forEach((item) => item.classList.toggle("is-active", item === button));
            renderMedia();
        });
    });
    refreshMedia().catch((error) => notify(error.message, "error"));
}
