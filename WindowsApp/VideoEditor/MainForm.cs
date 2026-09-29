using System.Runtime.InteropServices;

namespace VideoEditor;

sealed class MainForm : Form
{
    readonly EditorSession _session = new();
    readonly PreviewAudio _audio = new();
    readonly System.Windows.Forms.Timer _play = new() { Interval = 33 };
    readonly System.Windows.Forms.Timer _toastTimer = new() { Interval = 4200 };
    readonly PreviewSurface _preview;
    readonly TimelineControl _timeline;
    readonly InspectorPanel _inspector;
    readonly ListBox _media = new()
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        BackColor = Theme.Panel,
        ForeColor = Theme.Text,
        DrawMode = DrawMode.OwnerDrawFixed,
        ItemHeight = 42,
        IntegralHeight = false
    };
    readonly TextBox _name = Theme.TextBox();
    readonly ComboBox _quality = Theme.Combo();
    readonly Button _undo = Theme.Button("Undo");
    readonly Button _redo = Theme.Button("Redo");
    readonly Button _playBtn = Theme.Button("Play", primary: true);
    readonly Button _ffmpeg = Theme.Button("FFmpeg: Checking");
    readonly TrackBar _seek = new() { Minimum = 0, Maximum = 1000, TickStyle = TickStyle.None, Width = 220, BackColor = Theme.Panel };
    readonly Label _time = new() { AutoSize = true, ForeColor = Theme.Text, Font = Theme.UiFont, Text = "00:00.0 / 00:00.0", Margin = new Padding(8, 10, 8, 0) };
    readonly Label _toast = new() { AutoSize = true, ForeColor = Theme.Muted, Font = Theme.UiFont, Margin = new Padding(8, 12, 8, 0) };
    readonly TrackBar _zoom = new() { Minimum = 24, Maximum = 220, Value = 72, TickStyle = TickStyle.None, Width = 120, BackColor = Theme.Panel };
    bool _playing;
    bool _seekSync;
    bool _nameArmed;
    string _filter = "all";
    DateTime _clock;

    public MainForm()
    {
        _preview = new PreviewSurface(_session);
        _timeline = new TimelineControl(_session);
        _inspector = new InspectorPanel(_session);
        Text = "Video Editor";
        Font = Theme.UiFont;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1360, 840);
        MinimumSize = new Size(1100, 720);
        KeyPreview = true;
        BuildUi();
        _session.Changed += OnChanged;
        _play.Tick += (_, _) => Advance();
        _toastTimer.Tick += (_, _) => { _toast.Text = ""; _toastTimer.Stop(); };
        _media.DoubleClick += (_, _) => AddSelected();
        _media.DrawItem += DrawMediaItem;
        EnableDrop(_media);
        FfmpegTools.Refresh();
        ApplyChrome();
        RefreshMedia();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var enabled = 1;
        DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        StopPlayback();
        _audio.Dispose();
        base.OnFormClosed(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.S)) { SaveProject(); return true; }
        if (keyData == (Keys.Control | Keys.Z)) { _session.Undo(); return true; }
        if (keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z)) { _session.Redo(); return true; }
        if (IsTyping) return base.ProcessCmdKey(ref msg, keyData);
        switch (keyData)
        {
            case Keys.Space: TogglePlay(); return true;
            case Keys.Delete: _session.DeleteSelection(); return true;
            case Keys.S: Split(); return true;
            case Keys.Home: StopPlayback(); _session.SetPlayhead(0); return true;
            case Keys.End: StopPlayback(); _session.SetPlayhead(ClipTiming.ContentDuration(_session.Project)); return true;
            default: return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    bool IsTyping => ActiveControl is TextBox or ComboBox or NumericUpDown;

    void BuildUi()
    {
        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 58,
            Padding = new Padding(8, 6, 8, 0),
            WrapContents = false,
            BackColor = Color.FromArgb(16, 21, 29)
        };
        var brand = new Label
        {
            Text = "Video Editor",
            AutoSize = true,
            ForeColor = Theme.Text,
            Font = Theme.TitleFont,
            Margin = new Padding(4, 12, 12, 0)
        };
        _name.Text = "Untitled Project";
        _name.Width = 220;
        _name.Enter += (_, _) => { _session.Checkpoint(); _nameArmed = true; };
        _name.TextChanged += (_, _) =>
        {
            if (!_nameArmed) return;
            _session.Mutate(project => project.Name = _name.Text, history: false, reason: "live");
        };
        _name.Leave += (_, _) => { _nameArmed = false; _session.DropEmptyCheckpoint(); };
        _undo.Click += (_, _) => _session.Undo();
        _redo.Click += (_, _) => _session.Redo();
        var projectMenu = Theme.Button("Project");
        projectMenu.Click += (_, _) => ShowProjectMenu(projectMenu);
        _quality.Width = 160;
        _quality.Items.AddRange(new object[] { "Fast 720p", "High Quality 1080p" });
        _quality.SelectedIndex = 0;
        _quality.SelectedIndexChanged += (_, _) =>
        {
            if (_quality.Focused)
                _session.Mutate(project => project.ExportMode = _quality.SelectedIndex == 1 ? "high" : "fast");
        };
        var export = Theme.Button("Export MP4", primary: true);
        export.Click += (_, _) => Export();
        _ffmpeg.Click += (_, _) => { FfmpegTools.Refresh(); ApplyChrome(); Notify(FfmpegTools.Ready ? "FFmpeg is ready." : "FFmpeg was not found."); };
        _ffmpeg.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) LocateFfmpeg(); };
        top.Controls.AddRange([brand, _name, _undo, _redo, projectMenu, _quality, export, _ffmpeg, _toast]);

        var transport = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            WrapContents = false,
            BackColor = Theme.Panel,
            Padding = new Padding(8, 4, 8, 0)
        };
        var toStart = Theme.Button("Start");
        toStart.Click += (_, _) => { StopPlayback(); _session.SetPlayhead(0); };
        _playBtn.Click += (_, _) => TogglePlay();
        var stop = Theme.Button("Stop");
        stop.Click += (_, _) => { StopPlayback(); _session.SetPlayhead(0); };
        var toEnd = Theme.Button("End");
        toEnd.Click += (_, _) => { StopPlayback(); _session.SetPlayhead(ClipTiming.ContentDuration(_session.Project)); };
        _seek.Scroll += (_, _) =>
        {
            if (_seekSync) return;
            var duration = Math.Max(0.1, ClipTiming.DisplayDuration(_session.Project));
            _session.SetPlayhead(_seek.Value / 1000.0 * duration);
            if (_playing) _audio.Play(_session.Project, _session.Media, _session.Project.Playhead);
        };
        var mute = Theme.Button("Mute");
        mute.Click += (_, _) =>
        {
            _audio.Muted = !_audio.Muted;
            mute.Text = _audio.Muted ? "Unmute" : "Mute";
            if (!_playing) return;
            if (_audio.Muted) _audio.Stop();
            else _audio.Play(_session.Project, _session.Media, _session.Project.Playhead);
        };
        var addText = Theme.Button("Add Text");
        addText.Click += (_, _) => _session.AddText();
        var split = Theme.Button("Split");
        split.Click += (_, _) => Split();
        transport.Controls.AddRange([toStart, _playBtn, stop, toEnd, _time, _seek, mute, addText, split]);

        var center = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(8) };
        center.Controls.Add(_preview);
        center.Controls.Add(transport);

        var left = BuildMediaPanel();
        var right = new Panel { Dock = DockStyle.Right, Width = 292, BackColor = Theme.Panel, Padding = new Padding(0, 8, 0, 0) };
        var inspectorTitle = Theme.Heading("Inspector");
        inspectorTitle.Dock = DockStyle.Top;
        inspectorTitle.Padding = new Padding(12, 4, 0, 0);
        right.Controls.Add(_inspector);
        right.Controls.Add(inspectorTitle);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 268, BackColor = Theme.Panel };
        var timelineTools = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 40,
            WrapContents = false,
            BackColor = Theme.Panel,
            Padding = new Padding(8, 4, 8, 0)
        };
        var timelineLabel = Theme.Heading("Timeline");
        timelineLabel.Margin = new Padding(4, 8, 12, 0);
        var zoomOut = Theme.Button("-");
        zoomOut.Click += (_, _) => _zoom.Value = Math.Max(_zoom.Minimum, (int)(_timeline.PixelsPerSecond / 1.15));
        var zoomIn = Theme.Button("+");
        zoomIn.Click += (_, _) => _zoom.Value = Math.Min(_zoom.Maximum, (int)(_timeline.PixelsPerSecond * 1.15));
        _zoom.ValueChanged += (_, _) => _timeline.PixelsPerSecond = _zoom.Value;
        var delete = Theme.Button("Delete");
        delete.Click += (_, _) => _session.DeleteSelection();
        var hint = new Label
        {
            Text = "Drag clips to move them. Drag the edges to trim. Ctrl + scroll to zoom.",
            AutoSize = true,
            ForeColor = Theme.Muted,
            Margin = new Padding(12, 10, 0, 0)
        };
        timelineTools.Controls.AddRange([timelineLabel, zoomOut, _zoom, zoomIn, delete, hint]);
        bottom.Controls.Add(_timeline);
        bottom.Controls.Add(timelineTools);

        Controls.Add(center);
        Controls.Add(right);
        Controls.Add(left);
        Controls.Add(bottom);
        Controls.Add(top);
    }

    Panel BuildMediaPanel()
    {
        var left = new Panel { Dock = DockStyle.Left, Width = 280, BackColor = Theme.Panel, Padding = new Padding(8) };
        var header = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, WrapContents = false, BackColor = Theme.Panel };
        var title = Theme.Heading("Media Library");
        title.Margin = new Padding(0, 6, 8, 0);
        var browse = Theme.Button("Browse", primary: true);
        browse.Click += async (_, _) =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Import media",
                Multiselect = true,
                Filter = "Media|*.mp4;*.webm;*.mov;*.wmv;*.jpg;*.jpeg;*.png;*.webp;*.mp3;*.wav;*.aac;*.m4a|All files|*.*"
            };
            if (dialog.ShowDialog(this) == DialogResult.OK) await ImportAsync(dialog.FileNames);
        };
        header.Controls.AddRange([title, browse]);
        var filters = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, WrapContents = false, BackColor = Theme.Panel };
        foreach (var pair in new[] { ("all", "All"), ("video", "Videos"), ("image", "Images"), ("audio", "Audio") })
        {
            var button = Theme.Button(pair.Item2);
            var key = pair.Item1;
            button.Click += (_, _) => { _filter = key; RefreshMedia(); };
            filters.Controls.Add(button);
        }
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, WrapContents = false, BackColor = Theme.Panel };
        var add = Theme.Button("Add to Timeline");
        add.Click += (_, _) => AddSelected();
        var remove = Theme.Button("Remove");
        remove.Click += (_, _) => RemoveSelected();
        actions.Controls.AddRange([add, remove]);
        left.Controls.Add(_media);
        left.Controls.Add(actions);
        left.Controls.Add(filters);
        left.Controls.Add(header);
        return left;
    }

    void OnChanged(string reason)
    {
        if (reason == "change") StopPlayback();
        ApplyChrome();
        _preview.Invalidate();
        _timeline.RefreshLayout();
        _inspector.RefreshView(reason);
        if (_playing) _timeline.EnsurePlayheadVisible();
    }

    void ApplyChrome()
    {
        var project = _session.Project;
        if (!_name.Focused) _name.Text = project.Name;
        if (!_quality.Focused) _quality.SelectedIndex = project.ExportMode == "high" ? 1 : 0;
        _undo.Enabled = _session.CanUndo;
        _redo.Enabled = _session.CanRedo;
        _ffmpeg.Text = FfmpegTools.Ready ? "FFmpeg: Ready" : "FFmpeg: Not Available";
        _ffmpeg.ForeColor = FfmpegTools.Ready ? Theme.Ok : Theme.Warn;
        var content = ClipTiming.ContentDuration(project);
        var display = Math.Max(0.1, ClipTiming.DisplayDuration(project));
        _time.Text = $"{ClipTiming.Format(project.Playhead)} / {ClipTiming.Format(content)}";
        _seekSync = true;
        _seek.Value = (int)Math.Clamp(project.Playhead / display * 1000, 0, 1000);
        _seekSync = false;
        Text = project.Name + " — Video Editor";
    }

    void Advance()
    {
        if (!_playing) return;
        var now = DateTime.UtcNow;
        var delta = (now - _clock).TotalSeconds;
        _clock = now;
        var next = _session.Project.Playhead + delta;
        var end = ClipTiming.ContentDuration(_session.Project);
        if (next >= end)
        {
            _session.SetPlayhead(end);
            StopPlayback();
            return;
        }
        _session.SetPlayhead(next);
    }

    void TogglePlay()
    {
        if (_playing)
        {
            StopPlayback();
            return;
        }
        var end = ClipTiming.ContentDuration(_session.Project);
        if (end < 0.05)
        {
            Notify("Add at least one clip before playing.");
            return;
        }
        if (_session.Project.Playhead >= end - 0.05) _session.SetPlayhead(0);
        _playing = true;
        _clock = DateTime.UtcNow;
        _playBtn.Text = "Pause";
        if (!_audio.Muted) _audio.Play(_session.Project, _session.Media, _session.Project.Playhead);
        _play.Start();
    }

    void StopPlayback()
    {
        if (!_playing && _playBtn.Text == "Play") 
        {
            _audio.Stop();
            return;
        }
        _playing = false;
        _play.Stop();
        _audio.Stop();
        _playBtn.Text = "Play";
    }

    void Split()
    {
        var error = _session.SplitSelected();
        if (error is not null) Notify(error);
    }

    async Task ImportAsync(IEnumerable<string> files)
    {
        UseWaitCursor = true;
        try
        {
            foreach (var file in files)
            {
                (MediaAsset? Asset, string? Error) result;
                try { result = await Task.Run(() => _session.Media.Import(file)); }
                catch (Exception ex)
                {
                    Notify(ex.Message);
                    continue;
                }
                if (result.Error is not null) Notify(result.Error);
                else if (result.Asset is not null) _session.AddMedia(result.Asset);
            }
        }
        finally
        {
            UseWaitCursor = false;
            RefreshMedia();
        }
    }

    void RefreshMedia()
    {
        _media.BeginUpdate();
        _media.Items.Clear();
        foreach (var item in _session.Media.Items)
        {
            if (_filter != "all" && !item.Type.Equals(_filter, StringComparison.OrdinalIgnoreCase)) continue;
            _media.Items.Add(item);
        }
        _media.EndUpdate();
    }

    void AddSelected()
    {
        if (_media.SelectedItem is not MediaAsset asset) { Notify("Select a file in the media library."); return; }
        _session.AddMedia(asset);
    }

    void RemoveSelected()
    {
        if (_media.SelectedItem is not MediaAsset asset) return;
        if (MessageBox.Show(this, "Remove this file and any clips that use it?", "Video Editor", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _session.DeleteMedia(asset.Id);
        RefreshMedia();
    }

    void DrawMediaItem(object? sender, DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (e.Index < 0 || _media.Items[e.Index] is not MediaAsset asset) return;
        var selected = (e.State & DrawItemState.Selected) != 0;
        using var back = new SolidBrush(selected ? Theme.Panel2 : Theme.Panel);
        e.Graphics.FillRectangle(back, e.Bounds);
        var color = asset.Type switch
        {
            "video" => Theme.Video,
            "image" => Theme.Image,
            _ => Theme.Audio
        };
        using var swatch = new SolidBrush(color);
        e.Graphics.FillRectangle(swatch, e.Bounds.X + 8, e.Bounds.Y + 10, 8, e.Bounds.Height - 20);
        var meta = asset.Type == "image" ? $"{asset.Width}×{asset.Height}" : ClipTiming.Format(asset.Duration);
        TextRenderer.DrawText(e.Graphics, asset.Name, Font, new Rectangle(e.Bounds.X + 24, e.Bounds.Y + 4, e.Bounds.Width - 32, 18), Theme.Text, TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, asset.Type + "  " + meta, Font, new Rectangle(e.Bounds.X + 24, e.Bounds.Y + 20, e.Bounds.Width - 32, 16), Theme.Muted, TextFormatFlags.EndEllipsis);
    }

    void ShowProjectMenu(Control anchor)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("New Project", null, (_, _) =>
        {
            if (_session.HasContent() && MessageBox.Show(this, "Start a new project? You can undo this.", "Video Editor", MessageBoxButtons.YesNo) != DialogResult.Yes)
                return;
            _session.NewProject();
        });
        menu.Items.Add("Save Project", null, (_, _) => SaveProject());
        menu.Items.Add("Load Project", null, (_, _) => LoadProject());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Reset Project", null, (_, _) =>
        {
            if (MessageBox.Show(this, "Reset the project and clear undo history?", "Video Editor", MessageBoxButtons.YesNo) != DialogResult.Yes)
                return;
            _session.ResetProject();
        });
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    void SaveProject()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "Video Editor project|*.json",
            FileName = SafeFile(_session.Project.Name) + ".json"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllText(dialog.FileName, _session.ToJson());
        _session.Autosave();
        Notify("Project saved.");
    }

    void LoadProject()
    {
        using var dialog = new OpenFileDialog { Filter = "Video Editor project|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var error = _session.ReplaceFromJson(File.ReadAllText(dialog.FileName));
        if (error is not null) Notify(error);
        else Notify("Project loaded.");
    }

    void Export()
    {
        StopPlayback();
        FfmpegTools.Refresh();
        ApplyChrome();
        if (!FfmpegTools.Ready)
        {
            var answer = MessageBox.Show(this, "FFmpeg was not found. Locate ffmpeg.exe now?", "Video Editor", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
            LocateFfmpeg();
            if (!FfmpegTools.Ready) return;
        }
        using var dialog = new SaveFileDialog
        {
            Filter = "MP4 video|*.mp4",
            FileName = SafeFile(_session.Project.Name) + ".mp4"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        ExportPlan plan;
        try
        {
            plan = ExportCompiler.Compile(_session.Project, _session.Media, dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Video Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var progress = new ExportDialog(plan);
        if (progress.ShowDialog(this) == DialogResult.OK)
            Notify("Export complete: " + Path.GetFileName(dialog.FileName));
    }

    void LocateFfmpeg()
    {
        using var dialog = new OpenFileDialog { Title = "Select ffmpeg.exe", Filter = "ffmpeg|ffmpeg.exe" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        FfmpegTools.Remember(dialog.FileName);
        ApplyChrome();
        Notify(FfmpegTools.Ready ? "FFmpeg is ready." : "FFprobe was not found next to ffmpeg.exe.");
    }

    void EnableDrop(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
        };
        control.DragDrop += async (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) await ImportAsync(files);
        };
    }

    void Notify(string message)
    {
        _toast.Text = message;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    static string SafeFile(string name)
    {
        var cleaned = string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? ' ' : ch)).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "project" : cleaned;
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
