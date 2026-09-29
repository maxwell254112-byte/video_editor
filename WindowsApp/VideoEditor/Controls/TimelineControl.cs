namespace VideoEditor;

sealed class TimelineControl : UserControl
{
    const int LabelWidth = 72;
    const int RulerHeight = 28;
    const int TrackHeight = 52;
    static readonly (string Type, string Label, Color Color)[] Tracks =
    [
        ("video", "Video", Theme.Video),
        ("image", "Image", Theme.Image),
        ("text", "Text", Theme.TextClip),
        ("audio", "Audio", Theme.Audio)
    ];

    readonly EditorSession _session;
    readonly HScrollBar _scroll = new() { Dock = DockStyle.Bottom, Height = 18 };
    double _pps = 72;
    DragState? _drag;

    public TimelineControl(EditorSession session)
    {
        _session = session;
        Dock = DockStyle.Fill;
        BackColor = Theme.Panel;
        DoubleBuffered = true;
        _scroll.Scroll += (_, _) => Invalidate();
        Controls.Add(_scroll);
    }

    public double PixelsPerSecond
    {
        get => _pps;
        set
        {
            _pps = Math.Clamp(value, 24, 220);
            SyncScroll();
            Invalidate();
        }
    }

    public void EnsurePlayheadVisible()
    {
        var x = (int)(_session.Project.Playhead * _pps);
        var view = Math.Max(1, Viewport);
        if (x < _scroll.Value + 30 || x > _scroll.Value + view - 40)
            SetScroll(Math.Max(0, x - (int)(view * 0.3)));
    }

    public void RefreshLayout()
    {
        SyncScroll();
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        SyncScroll();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if ((ModifierKeys & Keys.Control) == Keys.Control)
        {
            PixelsPerSecond = _pps * (e.Delta > 0 ? 1.1 : 0.9);
            return;
        }
        SetScroll(_scroll.Value - e.Delta);
        base.OnMouseWheel(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left) return;
        var time = TimeAt(e.X);
        var track = TrackAt(e.Y);
        if (track >= 0)
        {
            var hit = HitClip(Tracks[track].Type, e.X);
            if (hit is ClipHit clip)
            {
                _session.SetSelection(new Selection(clip.Type, clip.Id));
                _session.Checkpoint();
                _drag = CaptureDrag(clip, e.X);
                this.Capture = true;
                return;
            }
        }
        _session.SetPlayhead(time);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag is DragState drag && e.Button == MouseButtons.Left)
        {
            ApplyDrag(drag, e.X);
            return;
        }
        var track = TrackAt(e.Y);
        Cursor = track >= 0 && HitClip(Tracks[track].Type, e.X) is ClipHit clip && clip.Edge != "move"
            ? Cursors.SizeWE
            : Cursors.Default;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_drag is null) return;
        _drag = null;
        this.Capture = false;
        _session.DropEmptyCheckpoint();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Panel);
        var project = _session.Project;
        using var labelFont = new Font("Segoe UI", 9f);
        using var tickFont = new Font("Segoe UI", 8f);
        using var line = new Pen(Theme.Line);
        using var text = new SolidBrush(Theme.Text);
        using var muted = new SolidBrush(Theme.Muted);
        var duration = ClipTiming.DisplayDuration(project);
        for (var i = 0; i < Tracks.Length; i++)
        {
            var top = RulerHeight + i * TrackHeight;
            var lane = new Rectangle(LabelWidth, top, Math.Max(0, Width - LabelWidth), TrackHeight - 1);
            using var laneBrush = new SolidBrush(i % 2 == 0 ? Theme.Panel : Theme.Panel2);
            g.FillRectangle(laneBrush, lane);
            g.DrawLine(line, 0, top + TrackHeight - 1, Width, top + TrackHeight - 1);
            g.DrawString(Tracks[i].Label, labelFont, text, 10, top + 16);
            DrawTrack(g, Tracks[i].Type, Tracks[i].Color, top);
        }
        using (var rulerBrush = new SolidBrush(Theme.Bg))
            g.FillRectangle(rulerBrush, 0, 0, Width, RulerHeight);
        g.DrawLine(line, 0, RulerHeight - 1, Width, RulerHeight - 1);
        var step = _pps >= 90 ? 1 : _pps >= 40 ? 2 : 5;
        for (var second = 0; second <= duration + 0.01; second += step)
        {
            var x = XAt(second);
            if (x < LabelWidth - 4 || x > Width) continue;
            g.DrawLine(line, x, 16, x, RulerHeight);
            g.DrawString(ClipTiming.Format(second), tickFont, muted, x + 3, 2);
        }
        var playhead = XAt(project.Playhead);
        using var playPen = new Pen(Color.White, 2);
        g.DrawLine(playPen, playhead, 0, playhead, Height - _scroll.Height);
        Point[] head = [new Point(playhead - 6, 0), new Point(playhead + 6, 0), new Point(playhead, 10)];
        g.FillPolygon(Brushes.White, head);
        using var shade = new SolidBrush(Theme.Panel);
        g.FillRectangle(shade, 0, 0, LabelWidth, Height - _scroll.Height);
        g.DrawLine(line, LabelWidth, 0, LabelWidth, Height);
        for (var i = 0; i < Tracks.Length; i++)
        {
            var top = RulerHeight + i * TrackHeight;
            g.DrawString(Tracks[i].Label, labelFont, text, 10, top + 16);
        }
    }

    void DrawTrack(Graphics g, string type, Color color, int top)
    {
        foreach (var clip in Enumerate(type))
        {
            var span = ClipTiming.Span(type, clip.Item);
            var x = XAt(span.Start);
            var width = Math.Max(10, (int)(span.Duration * _pps));
            if (x + width < LabelWidth || x > Width) continue;
            var rect = new Rectangle(x, top + 8, width, TrackHeight - 18);
            var selected = _session.Current is Selection current && current.Type == type && current.Id == clip.Id;
            using var brush = new SolidBrush(selected ? ControlPaint.Light(color) : color);
            g.FillRectangle(brush, rect);
            using var pen = new Pen(selected ? Color.White : Color.FromArgb(80, Color.Black));
            g.DrawRectangle(pen, rect);
            var name = clip.Item switch
            {
                VideoClip video => video.Name,
                ImageClip image => image.Name,
                AudioClip audio => audio.Name,
                TextClip text => text.Content,
                _ => ""
            };
            using var font = new Font("Segoe UI", 8.5f);
            var bounds = new RectangleF(rect.X + 8, rect.Y, Math.Max(0, rect.Width - 16), rect.Height);
            g.DrawString(name, font, Brushes.White, bounds);
        }
    }

    void ApplyDrag(DragState drag, int x)
    {
        var delta = (x - drag.StartX) / _pps;
        _session.Mutate(project =>
        {
            var item = _session.Find(drag.Type, drag.Id);
            if (item is null) return;
            if (drag.Mode == "move")
            {
                var start = Math.Max(0, drag.Start + delta);
                if (item is VideoClip video) video.TimelineStart = start;
                else if (item is AudioClip audio) audio.TimelineStart = start;
                else if (item is ImageClip image)
                {
                    var length = drag.End - drag.Start;
                    image.Start = start;
                    image.End = start + length;
                }
                else if (item is TextClip text)
                {
                    var length = drag.End - drag.Start;
                    text.Start = start;
                    text.End = start + length;
                }
                return;
            }
            if (item is ImageClip imageClip)
            {
                if (drag.Mode == "start") imageClip.Start = Math.Min(drag.End - ClipTiming.MinDuration, Math.Max(0, drag.Start + delta));
                else imageClip.End = Math.Max(drag.Start + ClipTiming.MinDuration, drag.End + delta);
                return;
            }
            if (item is TextClip textClip)
            {
                if (drag.Mode == "start") textClip.Start = Math.Min(drag.End - ClipTiming.MinDuration, Math.Max(0, drag.Start + delta));
                else textClip.End = Math.Max(drag.Start + ClipTiming.MinDuration, drag.End + delta);
                return;
            }
            var speed = drag.Speed <= 0 ? 1 : drag.Speed;
            if (drag.Mode == "start")
            {
                var start = Math.Min(drag.End - ClipTiming.MinDuration, Math.Max(0, drag.Start + delta));
                var length = drag.End - start;
                var inPoint = drag.OutPoint - length * speed;
                if (inPoint < 0)
                {
                    inPoint = 0;
                    length = (drag.OutPoint - inPoint) / speed;
                    start = drag.End - length;
                }
                if (item is VideoClip video)
                {
                    video.TimelineStart = start;
                    video.InPoint = inPoint;
                }
                else if (item is AudioClip audio)
                {
                    audio.TimelineStart = start;
                    audio.InPoint = inPoint;
                }
            }
            else
            {
                var end = Math.Max(drag.Start + ClipTiming.MinDuration, drag.End + delta);
                var outPoint = Math.Min(drag.SourceDuration, drag.InPoint + (end - drag.Start) * speed);
                if (item is VideoClip video) video.OutPoint = outPoint;
                else if (item is AudioClip audio) audio.OutPoint = outPoint;
            }
        }, history: false, reason: "live");
    }

    DragState CaptureDrag(ClipHit clip, int x)
    {
        var span = ClipTiming.Span(clip.Type, clip.Item);
        var speed = 1.0;
        var inPoint = 0.0;
        var outPoint = span.Duration;
        var source = span.Duration;
        if (clip.Item is VideoClip video)
        {
            speed = video.Speed;
            inPoint = video.InPoint;
            outPoint = video.OutPoint;
            source = video.SourceDuration;
        }
        else if (clip.Item is AudioClip audio)
        {
            inPoint = audio.InPoint;
            outPoint = audio.OutPoint;
            source = audio.SourceDuration;
        }
        return new DragState(clip.Type, clip.Id, clip.Edge, x, span.Start, span.End, inPoint, outPoint, source, speed);
    }

    ClipHit? HitClip(string type, int x)
    {
        ClipHit? found = null;
        foreach (var clip in Enumerate(type))
        {
            var span = ClipTiming.Span(type, clip.Item);
            var left = XAt(span.Start);
            var right = left + Math.Max(10, (int)(span.Duration * _pps));
            if (x < left || x > right) continue;
            var edge = x <= left + 8 ? "start" : x >= right - 8 ? "end" : "move";
            found = new ClipHit(type, clip.Id, clip.Item, edge);
        }
        return found;
    }

    IEnumerable<(string Id, object Item)> Enumerate(string type)
    {
        var project = _session.Project;
        switch (type)
        {
            case "video":
                foreach (var item in project.Clips) yield return (item.Id, item);
                break;
            case "image":
                foreach (var item in project.Images) yield return (item.Id, item);
                break;
            case "text":
                foreach (var item in project.Texts) yield return (item.Id, item);
                break;
            case "audio":
                foreach (var item in project.Audios) yield return (item.Id, item);
                break;
        }
    }

    int TrackAt(int y)
    {
        if (y < RulerHeight) return -1;
        var index = (y - RulerHeight) / TrackHeight;
        return index >= 0 && index < Tracks.Length ? index : -1;
    }

    double TimeAt(int x) => Math.Max(0, (x + _scroll.Value - LabelWidth) / _pps);
    int XAt(double time) => LabelWidth + (int)(time * _pps) - _scroll.Value;
    int Viewport => Math.Max(1, Width - LabelWidth);

    void SyncScroll()
    {
        var content = (int)(ClipTiming.DisplayDuration(_session.Project) * _pps);
        var max = Math.Max(0, content - Viewport);
        var large = Math.Max(1, Viewport);
        if (_scroll.Value > max) _scroll.Value = 0;
        _scroll.LargeChange = large;
        _scroll.SmallChange = 24;
        _scroll.Maximum = Math.Max(large, max + large - 1);
        if (_scroll.Value > max) _scroll.Value = max;
    }

    void SetScroll(int value)
    {
        var max = Math.Max(0, _scroll.Maximum - _scroll.LargeChange + 1);
        _scroll.Value = Math.Clamp(value, 0, max);
        Invalidate();
    }

    sealed record ClipHit(string Type, string Id, object Item, string Edge);
    sealed record DragState(
        string Type, string Id, string Mode, int StartX,
        double Start, double End, double InPoint, double OutPoint, double SourceDuration, double Speed);
}
