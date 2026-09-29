namespace VideoEditor;

sealed class InspectorPanel : UserControl
{
    readonly EditorSession _session;
    readonly FlowLayoutPanel _flow = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        Padding = new Padding(12, 8, 12, 16),
        BackColor = Theme.Panel
    };
    bool _loading;

    public InspectorPanel(EditorSession session)
    {
        _session = session;
        Dock = DockStyle.Fill;
        BackColor = Theme.Panel;
        Controls.Add(_flow);
        Rebuild();
    }

    public void RefreshView(string reason)
    {
        if (reason is "playhead" or "live") return;
        if (IsHandleCreated) BeginInvoke(new Action(Rebuild));
        else Rebuild();
    }

    void Rebuild()
    {
        _loading = true;
        _flow.SuspendLayout();
        _flow.Controls.Clear();
        var project = _session.Project;
        var selected = _session.Current;
        object? item = selected is Selection current ? _session.Find(current.Type, current.Id) : null;
        if (selected is null || item is null)
        {
            Add(Theme.Heading("No selection"));
            Add(Hint("Select a clip on the timeline, or add text, images, and audio."));
        }
        else if (item is not TextClip && selected is Selection clip && _session.Media.Find(MediaId(item)) is null)
        {
            Add(Theme.Heading("Missing media"));
            Add(Hint("This clip points to a file that is no longer in the library."));
        }

        switch (item)
        {
            case VideoClip video: BuildVideo(video); break;
            case ImageClip image: BuildImage(image); break;
            case TextClip text: BuildText(text); break;
            case AudioClip audio: BuildAudio(audio); break;
        }
        BuildProject(project);
        if (item is not null)
        {
            var remove = Theme.Button("Delete clip");
            remove.Click += (_, _) => _session.DeleteSelection();
            Add(remove);
        }
        _flow.ResumeLayout();
        _loading = false;
    }

    void BuildVideo(VideoClip item)
    {
        Add(Theme.Heading("Video Clip"));
        TextField("Clip name", item.Name, value => Patch("video", item.Id, clip => { if (clip is VideoClip video) video.Name = value; }));
        Number("Timeline start (s)", (decimal)item.TimelineStart, 0, 86400, 0.1m, 1, value => Patch("video", item.Id, clip => { if (clip is VideoClip video) video.TimelineStart = (double)value; }));
        Number("In point (s)", (decimal)item.InPoint, 0, 86400, 0.1m, 1, value => Patch("video", item.Id, clip => { if (clip is VideoClip video) video.InPoint = (double)value; }));
        Number("Out point (s)", (decimal)item.OutPoint, 0, 86400, 0.1m, 1, value => Patch("video", item.Id, clip => { if (clip is VideoClip video) video.OutPoint = (double)value; }));
        Add(Hint($"Source {ClipTiming.Format(item.SourceDuration)} · Timeline {ClipTiming.Format((item.OutPoint - item.InPoint) / Math.Max(0.25, item.Speed))}"));
        var speed = Theme.Combo();
        speed.Width = 220;
        foreach (var preset in ClipTiming.Speeds) speed.Items.Add(preset);
        speed.SelectedItem = ClipTiming.Speeds.FirstOrDefault(value => Math.Abs(value - item.Speed) < 0.001);
        if (speed.SelectedIndex < 0) speed.SelectedIndex = 3;
        speed.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || speed.SelectedItem is not double value) return;
            Patch("video", item.Id, clip => { if (clip is VideoClip video) video.Speed = value; });
        };
        Add(Theme.Caption("Playback speed"));
        Add(speed);
        Number("Custom speed", (decimal)item.Speed, 0.25m, 4, 0.05m, 2, value => Patch("video", item.Id, clip => { if (clip is VideoClip video) video.Speed = (double)value; }));
        Slider("Volume", (int)Math.Round(item.Volume * 100), 0, 100, value => Patch("video", item.Id, clip => { if (clip is VideoClip video) video.Volume = value / 100.0; }));
        Check("Mute clip audio", item.Muted, value => Patch("video", item.Id, clip => { if (clip is VideoClip video) video.Muted = value; }));
        Number("X position", (decimal)item.X, -4000, 8000, 1, 0, value => Patch("video", item.Id, clip => { if (clip is VideoClip video) video.X = (double)value; }));
        Number("Y position", (decimal)item.Y, -4000, 8000, 1, 0, value => Patch("video", item.Id, clip => { if (clip is VideoClip video) video.Y = (double)value; }));
        Slider("Scale", (int)Math.Round(item.Scale * 100), 10, 400, value => Patch("video", item.Id, clip => { if (clip is VideoClip video) video.Scale = value / 100.0; }));
        Number("Rotation", (decimal)item.Rotation, 0, 359, 1, 0, value => Patch("video", item.Id, clip => { if (clip is VideoClip video) video.Rotation = (double)value; }));
        RotationButtons("video", item.Id);
        Slider("Crop top %", (int)Math.Round(item.Crop.Top), 0, 40, value => Patch("video", item.Id, clip => { if (clip is VideoClip video) video.Crop.Top = value; }));
        Slider("Crop right %", (int)Math.Round(item.Crop.Right), 0, 40, value => Patch("video", item.Id, clip => { if (clip is VideoClip video) video.Crop.Right = value; }));
        Slider("Crop bottom %", (int)Math.Round(item.Crop.Bottom), 0, 40, value => Patch("video", item.Id, clip => { if (clip is VideoClip video) video.Crop.Bottom = value; }));
        Slider("Crop left %", (int)Math.Round(item.Crop.Left), 0, 40, value => Patch("video", item.Id, clip => { if (clip is VideoClip video) video.Crop.Left = value; }));
        var split = Theme.Button("Split at playhead");
        split.Click += (_, _) =>
        {
            var error = _session.SplitSelected();
            if (error is not null) MessageBox.Show(this, error, "Video Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        Add(split);
    }

    void BuildImage(ImageClip item)
    {
        Add(Theme.Heading("Image Overlay"));
        Number("X position", (decimal)item.X, -4000, 8000, 1, 0, value => Patch("image", item.Id, clip => { if (clip is ImageClip image) image.X = (double)value; }));
        Number("Y position", (decimal)item.Y, -4000, 8000, 1, 0, value => Patch("image", item.Id, clip => { if (clip is ImageClip image) image.Y = (double)value; }));
        Number("Width", (decimal)item.Width, 10, 3840, 1, 0, value => Patch("image", item.Id, clip => { if (clip is ImageClip image) image.Width = (double)value; }));
        Slider("Scale", (int)Math.Round(item.Scale * 100), 10, 400, value => Patch("image", item.Id, clip => { if (clip is ImageClip image) image.Scale = value / 100.0; }));
        Number("Start time (s)", (decimal)item.Start, 0, 86400, 0.1m, 1, value => Patch("image", item.Id, clip => { if (clip is ImageClip image) image.Start = (double)value; }));
        Number("End time (s)", (decimal)item.End, 0, 86400, 0.1m, 1, value => Patch("image", item.Id, clip => { if (clip is ImageClip image) image.End = (double)value; }));
        Slider("Opacity", (int)Math.Round(item.Opacity * 100), 0, 100, value => Patch("image", item.Id, clip => { if (clip is ImageClip image) image.Opacity = value / 100.0; }));
        Number("Rotation", (decimal)item.Rotation, 0, 359, 1, 0, value => Patch("image", item.Id, clip => { if (clip is ImageClip image) image.Rotation = (double)value; }));
        RotationButtons("image", item.Id);
    }

    void BuildText(TextClip item)
    {
        Add(Theme.Heading("Text Overlay"));
        var content = new TextBox
        {
            Text = item.Content,
            Multiline = true,
            Height = 64,
            Width = 240,
            BackColor = Theme.Panel2,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };
        Wire(content, () => Patch("text", item.Id, clip => { if (clip is TextClip text) text.Content = content.Text; }));
        Add(Theme.Caption("Text content"));
        Add(content);
        var fonts = Theme.Combo();
        fonts.Width = 220;
        fonts.Items.AddRange(ClipTiming.Fonts);
        fonts.SelectedItem = item.FontFamily;
        fonts.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || fonts.SelectedItem is not string family) return;
            Patch("text", item.Id, clip => { if (clip is TextClip text) text.FontFamily = family; });
        };
        Add(Theme.Caption("Font family"));
        Add(fonts);
        Number("Font size", (decimal)item.FontSize, 8, 400, 1, 0, value => Patch("text", item.Id, clip => { if (clip is TextClip text) text.FontSize = (double)value; }));
        var color = Theme.Button(item.Color);
        color.BackColor = ColorTranslator.FromHtml(item.Color);
        color.Click += (_, _) =>
        {
            using var dialog = new ColorDialog { Color = color.BackColor, FullOpen = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var hex = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
            Patch("text", item.Id, clip => { if (clip is TextClip text) text.Color = hex; });
        };
        Add(Theme.Caption("Font color"));
        Add(color);
        Check("Bold", item.Bold, value => Patch("text", item.Id, clip => { if (clip is TextClip text) text.Bold = value; }));
        Check("Italic", item.Italic, value => Patch("text", item.Id, clip => { if (clip is TextClip text) text.Italic = value; }));
        var align = Theme.Combo();
        align.Width = 220;
        align.Items.AddRange(new object[] { "left", "center", "right" });
        align.SelectedItem = item.Align;
        align.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || align.SelectedItem is not string value) return;
            Patch("text", item.Id, clip => { if (clip is TextClip text) text.Align = value; });
        };
        Add(Theme.Caption("Alignment"));
        Add(align);
        Number("X position", (decimal)item.X, -4000, 8000, 1, 0, value => Patch("text", item.Id, clip => { if (clip is TextClip text) text.X = (double)value; }));
        Number("Y position", (decimal)item.Y, -4000, 8000, 1, 0, value => Patch("text", item.Id, clip => { if (clip is TextClip text) text.Y = (double)value; }));
        Number("Start time (s)", (decimal)item.Start, 0, 86400, 0.1m, 1, value => Patch("text", item.Id, clip => { if (clip is TextClip text) text.Start = (double)value; }));
        Number("End time (s)", (decimal)item.End, 0, 86400, 0.1m, 1, value => Patch("text", item.Id, clip => { if (clip is TextClip text) text.End = (double)value; }));
        Slider("Opacity", (int)Math.Round(item.Opacity * 100), 0, 100, value => Patch("text", item.Id, clip => { if (clip is TextClip text) text.Opacity = value / 100.0; }));
        Number("Rotation", (decimal)item.Rotation, 0, 359, 1, 0, value => Patch("text", item.Id, clip => { if (clip is TextClip text) text.Rotation = (double)value; }));
    }

    void BuildAudio(AudioClip item)
    {
        Add(Theme.Heading("Audio Clip"));
        Add(Hint($"{item.Name} · source {ClipTiming.Format(item.SourceDuration)}"));
        Number("Start time (s)", (decimal)item.TimelineStart, 0, 86400, 0.1m, 1, value => Patch("audio", item.Id, clip => { if (clip is AudioClip audio) audio.TimelineStart = (double)value; }));
        Number("In point (s)", (decimal)item.InPoint, 0, 86400, 0.1m, 1, value => Patch("audio", item.Id, clip => { if (clip is AudioClip audio) audio.InPoint = (double)value; }));
        Number("Out point (s)", (decimal)item.OutPoint, 0, 86400, 0.1m, 1, value => Patch("audio", item.Id, clip => { if (clip is AudioClip audio) audio.OutPoint = (double)value; }));
        Slider("Volume", (int)Math.Round(item.Volume * 100), 0, 100, value => Patch("audio", item.Id, clip => { if (clip is AudioClip audio) audio.Volume = value / 100.0; }));
        Number("Track number", item.Track, 1, 4, 1, 0, value => Patch("audio", item.Id, clip => { if (clip is AudioClip audio) audio.Track = (int)value; }));
    }

    void BuildProject(EditorProject project)
    {
        Add(Theme.Heading("Project"));
        var canvas = Theme.Combo();
        canvas.Width = 220;
        var sizes = new List<string> { "1280x720", "1920x1080", "1080x1920" };
        var current = $"{project.Width}x{project.Height}";
        if (!sizes.Contains(current)) sizes.Insert(0, current);
        canvas.Items.AddRange(sizes.Cast<object>().ToArray());
        canvas.SelectedItem = current;
        canvas.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || canvas.SelectedItem is not string value) return;
            var parts = value.Split('x');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var width) || !int.TryParse(parts[1], out var height)) return;
            _session.Mutate(doc => { doc.Width = width; doc.Height = height; }, history: false, reason: "live");
        };
        Add(Theme.Caption("Canvas"));
        Add(canvas);
        var fps = Theme.Combo();
        fps.Width = 220;
        foreach (var value in new[] { 24, 25, 30, 50, 60 }) fps.Items.Add(value);
        fps.SelectedItem = project.Fps;
        fps.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || fps.SelectedItem is not int value) return;
            _session.Mutate(doc => doc.Fps = value, history: false, reason: "live");
        };
        Add(Theme.Caption("FPS"));
        Add(fps);
        Check("Mute original video audio", project.MuteOriginal, value => _session.Mutate(doc => doc.MuteOriginal = value, history: false, reason: "live"));
        Add(Theme.Heading("Filters"));
        Filter("Brightness", (int)Math.Round(project.Filters.Brightness), -100, 100, value => _session.Mutate(doc => doc.Filters.Brightness = value, history: false, reason: "live"));
        Filter("Contrast", (int)Math.Round(project.Filters.Contrast), -100, 100, value => _session.Mutate(doc => doc.Filters.Contrast = value, history: false, reason: "live"));
        Filter("Saturation", (int)Math.Round(project.Filters.Saturation), -100, 100, value => _session.Mutate(doc => doc.Filters.Saturation = value, history: false, reason: "live"));
        Filter("Grayscale", (int)Math.Round(project.Filters.Grayscale), 0, 100, value => _session.Mutate(doc => doc.Filters.Grayscale = value, history: false, reason: "live"));
        Filter("Blur", (int)Math.Round(project.Filters.Blur * 2), 0, 40, value => _session.Mutate(doc => doc.Filters.Blur = value / 2.0, history: false, reason: "live"));
        Filter("Sepia", (int)Math.Round(project.Filters.Sepia), 0, 100, value => _session.Mutate(doc => doc.Filters.Sepia = value, history: false, reason: "live"));
    }

    void RotationButtons(string type, string id)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Panel, Margin = new Padding(0, 4, 0, 4) };
        foreach (var angle in new[] { 0, 90, 180, 270 })
        {
            var button = Theme.Button(angle + "°");
            var value = angle;
            button.Click += (_, _) => Patch(type, id, clip =>
            {
                switch (clip)
                {
                    case VideoClip video: video.Rotation = value; break;
                    case ImageClip image: image.Rotation = value; break;
                    case TextClip text: text.Rotation = value; break;
                }
            });
            row.Controls.Add(button);
        }
        Add(row);
    }

    void Patch(string type, string id, Action<object> apply) =>
        _session.Mutate(project =>
        {
            var found = _session.Find(type, id);
            if (found is not null) apply(found);
        }, history: false, reason: "live");

    void TextField(string label, string value, Action<string> apply)
    {
        var box = Theme.TextBox();
        box.Text = value;
        box.Width = 240;
        Wire(box, () => apply(box.Text));
        Add(Theme.Caption(label));
        Add(box);
    }

    void Number(string label, decimal value, decimal min, decimal max, decimal step, int decimals, Action<decimal> apply)
    {
        var box = Theme.Number(min, max, step, decimals);
        box.Width = 240;
        box.Value = Math.Clamp(value, min, max);
        Wire(box, () => apply(box.Value));
        Add(Theme.Caption(label));
        Add(box);
    }

    void Slider(string label, int value, int min, int max, Action<int> apply)
    {
        var caption = Theme.Caption($"{label}: {value}");
        var bar = new TrackBar { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = 240, TickFrequency = Math.Max(1, (max - min) / 10), BackColor = Theme.Panel };
        bar.ValueChanged += (_, _) =>
        {
            caption.Text = $"{label}: {bar.Value}";
            if (!_loading) apply(bar.Value);
        };
        bar.MouseDown += (_, _) => { if (!_loading) _session.Checkpoint(); };
        bar.MouseUp += (_, _) => _session.DropEmptyCheckpoint();
        Add(caption);
        Add(bar);
    }

    void Filter(string label, int value, int min, int max, Action<int> apply) => Slider(label, value, min, max, apply);

    void Check(string label, bool value, Action<bool> apply)
    {
        var box = new CheckBox { Text = label, Checked = value, AutoSize = true, ForeColor = Theme.Text, BackColor = Theme.Panel, Margin = new Padding(0, 6, 0, 2) };
        box.CheckedChanged += (_, _) => { if (!_loading) apply(box.Checked); };
        Add(box);
    }

    void Wire(Control control, Action apply)
    {
        var armed = false;
        control.Enter += (_, _) =>
        {
            if (_loading || armed) return;
            _session.Checkpoint();
            armed = true;
        };
        void Commit()
        {
            if (_loading) return;
            if (!armed)
            {
                _session.Checkpoint();
                armed = true;
            }
            apply();
        }
        if (control is NumericUpDown number) number.ValueChanged += (_, _) => Commit();
        else if (control is TextBox text) text.TextChanged += (_, _) => Commit();
        control.Leave += (_, _) =>
        {
            if (!armed) return;
            armed = false;
            _session.DropEmptyCheckpoint();
        };
    }

    static string MediaId(object item) => item switch
    {
        VideoClip video => video.MediaId,
        ImageClip image => image.MediaId,
        AudioClip audio => audio.MediaId,
        _ => ""
    };

    void Add(Control control)
    {
        control.Margin = new Padding(0, control.Margin.Top, 0, control.Margin.Bottom);
        _flow.Controls.Add(control);
    }

    static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(250, 0),
        ForeColor = Theme.Muted,
        Font = Theme.UiFont,
        Margin = new Padding(0, 0, 0, 8)
    };
}
