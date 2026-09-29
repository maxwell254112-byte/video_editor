<?php
declare(strict_types=1);
header('X-Content-Type-Options: nosniff');
header('X-Frame-Options: SAMEORIGIN');
header('Referrer-Policy: same-origin');
?>
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>Video Editor</title>
    <link href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css" rel="stylesheet">
    <link href="https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.5.2/css/all.min.css" rel="stylesheet">
    <link href="assets/css/editor.css" rel="stylesheet">
</head>
<body>
    <header class="topbar">
        <div class="brand">
            <span class="logo" aria-hidden="true"><i class="fa-solid fa-clapperboard"></i></span>
            <div>
                <strong>Video Editor</strong>
                <small>Web App</small>
            </div>
        </div>
        <label class="project-name">
            <span class="visually-hidden">Project name</span>
            <input id="projectName" type="text" maxlength="80" value="Untitled Project" spellcheck="false">
        </label>
        <div class="top-actions">
            <button id="undoBtn" class="icon-btn" type="button" title="Undo (Ctrl+Z)" data-bs-toggle="tooltip" disabled>
                <i class="fa-solid fa-rotate-left"></i>
            </button>
            <button id="redoBtn" class="icon-btn" type="button" title="Redo (Ctrl+Y)" data-bs-toggle="tooltip" disabled>
                <i class="fa-solid fa-rotate-right"></i>
            </button>
            <div class="dropdown">
                <button class="btn btn-soft dropdown-toggle" type="button" data-bs-toggle="dropdown">
                    <i class="fa-solid fa-folder-open"></i> Project
                </button>
                <ul class="dropdown-menu dropdown-menu-dark">
                    <li><button id="newProjectBtn" class="dropdown-item" type="button"><i class="fa-solid fa-file"></i> New Project</button></li>
                    <li><button id="saveProjectBtn" class="dropdown-item" type="button"><i class="fa-solid fa-floppy-disk"></i> Save Project</button></li>
                    <li><button id="loadProjectBtn" class="dropdown-item" type="button"><i class="fa-solid fa-upload"></i> Load Project</button></li>
                    <li><hr class="dropdown-divider"></li>
                    <li><button id="resetProjectBtn" class="dropdown-item" type="button"><i class="fa-solid fa-rotate"></i> Reset Project</button></li>
                </ul>
            </div>
            <label class="quality">
                <span class="visually-hidden">Export quality</span>
                <select id="exportMode" class="form-select form-select-sm">
                    <option value="fast">Fast 720p</option>
                    <option value="high">High Quality 1080p</option>
                </select>
            </label>
            <button id="exportBtn" class="btn btn-export" type="button">
                <i class="fa-solid fa-file-export"></i> Export MP4
            </button>
            <div id="ffmpegStatus" class="status-pill is-unknown" title="Checking FFmpeg">
                <span class="status-dot"></span>
                <span id="ffmpegLabel">FFmpeg: Checking</span>
            </div>
        </div>
    </header>

    <div class="workspace">
        <aside class="panel media-panel" aria-label="Media library">
            <div class="panel-head">
                <h2>Media Library</h2>
                <button id="browseBtn" class="btn btn-soft btn-sm" type="button">
                    <i class="fa-solid fa-plus"></i> Browse
                </button>
            </div>
            <div class="media-tabs" role="tablist">
                <button type="button" class="is-active" data-filter="all">All</button>
                <button type="button" data-filter="video">Videos</button>
                <button type="button" data-filter="image">Images</button>
                <button type="button" data-filter="audio">Audio</button>
            </div>
            <div id="dropzone" class="dropzone">
                <div id="uploadProgress" class="upload-progress d-none">
                    <div class="d-flex justify-content-between"><span id="uploadLabel">Uploading…</span><span id="uploadPercent">0%</span></div>
                    <div class="progress"><div id="uploadBar" class="progress-bar"></div></div>
                </div>
                <div id="mediaList" class="media-list"></div>
                <p id="dropHint" class="drop-hint">Drop MP4, WebM, MOV, WMV, JPG, PNG, WEBP, MP3, WAV, AAC, or M4A files here.</p>
            </div>
            <input id="fileInput" type="file" hidden multiple accept=".mp4,.webm,.mov,.wmv,.jpg,.jpeg,.png,.webp,.mp3,.wav,.aac,.m4a,video/*,image/*,audio/*">
        </aside>

        <main class="panel preview-panel" aria-label="Preview">
            <div class="panel-head">
                <h2>Preview</h2>
                <div class="preview-tools">
                    <button id="addTextBtn" class="btn btn-soft btn-sm" type="button"><i class="fa-solid fa-font"></i> Add Text</button>
                    <button id="splitBtn" class="btn btn-soft btn-sm" type="button" title="Split video at playhead (S)"><i class="fa-solid fa-scissors"></i> Split</button>
                </div>
            </div>
            <div id="stage" class="stage">
                <canvas id="previewCanvas" width="1920" height="1080"></canvas>
                <p id="stageEmpty" class="stage-empty">Add media to the timeline to preview the composition.</p>
            </div>
            <div class="transport">
                <button id="skipStartBtn" class="icon-btn" type="button" title="Go to beginning"><i class="fa-solid fa-backward-step"></i></button>
                <button id="playBtn" class="icon-btn play-btn" type="button" title="Play or pause (Space)"><i class="fa-solid fa-play"></i></button>
                <button id="stopBtn" class="icon-btn" type="button" title="Stop"><i class="fa-solid fa-stop"></i></button>
                <button id="skipEndBtn" class="icon-btn" type="button" title="Go to end"><i class="fa-solid fa-forward-step"></i></button>
                <span id="timeLabel" class="time-label">00:00.0 / 00:00.0</span>
                <input id="seekBar" class="seek" type="range" min="0" max="1000" value="0" aria-label="Seek">
                <button id="muteBtn" class="icon-btn" type="button" title="Mute preview"><i class="fa-solid fa-volume-high"></i></button>
            </div>
        </main>

        <aside class="panel inspector-panel" aria-label="Inspector">
            <div class="panel-head"><h2>Inspector</h2></div>
            <div id="inspector" class="inspector-body"></div>
        </aside>

        <section class="panel timeline-panel" aria-label="Timeline">
            <div class="timeline-toolbar">
                <strong>Timeline</strong>
                <button id="zoomOutBtn" class="icon-btn" type="button" title="Zoom out"><i class="fa-solid fa-magnifying-glass-minus"></i></button>
                <input id="zoomRange" type="range" min="24" max="220" value="72" aria-label="Timeline zoom">
                <button id="zoomInBtn" class="icon-btn" type="button" title="Zoom in"><i class="fa-solid fa-magnifying-glass-plus"></i></button>
                <button id="deleteClipBtn" class="btn btn-soft btn-sm" type="button"><i class="fa-solid fa-trash"></i> Delete</button>
                <span class="timeline-hint">Drag clips to move them. Drag the edges to trim. Ctrl + scroll to zoom.</span>
            </div>
            <div class="timeline-body">
                <div class="track-labels">
                    <div class="label-spacer"></div>
                    <div>Video</div>
                    <div>Image</div>
                    <div>Text</div>
                    <div>Audio</div>
                </div>
                <div id="timelineScroll" class="timeline-scroll">
                    <div id="timelineContent" class="timeline-content"></div>
                </div>
            </div>
        </section>
    </div>

    <div id="toasts" class="toast-stack" aria-live="polite"></div>
    <input id="projectFile" type="file" accept="application/json,.json" hidden>

    <div class="modal fade" id="exportModal" tabindex="-1" aria-labelledby="exportTitle" aria-hidden="true">
        <div class="modal-dialog modal-dialog-centered">
            <div class="modal-content editor-modal">
                <div class="modal-header">
                    <h2 id="exportTitle" class="modal-title h5">Export MP4</h2>
                    <button type="button" class="btn-close btn-close-white" data-bs-dismiss="modal" aria-label="Close"></button>
                </div>
                <div class="modal-body">
                    <p id="exportStatusText" class="mb-2">Preparing export…</p>
                    <div class="progress mb-2" role="progressbar" aria-valuemin="0" aria-valuemax="100">
                        <div id="exportBar" class="progress-bar" style="width:0%">0%</div>
                    </div>
                    <p id="exportElapsed" class="text-secondary mb-1"></p>
                    <p id="exportError" class="text-danger mb-0"></p>
                </div>
                <div class="modal-footer">
                    <a id="exportDownload" class="btn btn-export d-none" href="#" download>Download MP4</a>
                    <button type="button" class="btn btn-soft" data-bs-dismiss="modal">Close</button>
                </div>
            </div>
        </div>
    </div>

    <script src="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/js/bootstrap.bundle.min.js"></script>
    <script type="module" src="assets/js/app.js?v=3"></script>
</body>
</html>
