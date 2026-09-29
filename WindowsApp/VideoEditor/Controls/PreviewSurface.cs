using System.Drawing.Imaging;

namespace VideoEditor;

sealed class PreviewSurface : Control
{
    readonly EditorSession _session;
    readonly FramePump _frames;
    readonly List<Hit> _hits = new();
    Bitmap? _buffer;
    Drag? _drag;

    public PreviewSurface(EditorSession session)
    {
        _session = session;
        _frames = new FramePump(this);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Bg;
        Dock = DockStyle.Fill;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _frames.Dispose();
            _buffer?.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var project = _session.Project;
        var stage = StageRect(project);
        e.Graphics.Clear(Theme.Bg);
        if (stage.Width < 2 || stage.Height < 2) return;
        EnsureBuffer(stage.Size);
        if (_buffer is null) return;

        using (var g = Graphics.FromImage(_buffer))
        {
            g.Clear(Color.Black);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            var sx = stage.Width / (float)project.Width;
            var sy = stage.Height / (float)project.Height;
            g.ScaleTransform(sx, sy);
            _hits.Clear();
            if (!_session.HasContent())
            {
                DrawEmpty(g, project);
            }
            else
            {
                foreach (var clip in project.Clips) DrawVideo(g, project, clip);
                foreach (var image in project.Images) DrawImageClip(g, project, image);
                foreach (var text in project.Texts) DrawText(g, project, text);
            }
        }

        var matrix = BuildMatrix(project.Filters);
        if (matrix is null)
        {
            e.Graphics.DrawImage(_buffer, stage);
        }
        else
        {
            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(matrix);
            e.Graphics.DrawImage(_buffer, stage, 0, 0, _buffer.Width, _buffer.Height, GraphicsUnit.Pixel, attributes);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var point = ToProject(e.Location);
        for (var i = _hits.Count - 1; i >= 0; i--)
        {
            if (!_hits[i].Rect.Contains(point)) continue;
            var hit = _hits[i];
            _session.SetSelection(new Selection(hit.Type, hit.Id));
            _session.Checkpoint();
            _drag = new Drag(hit.Type, hit.Id, point, ReadPosition(hit.Type, hit.Id));
            return;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag is not Drag drag || e.Button != MouseButtons.Left) return;
        var point = ToProject(e.Location);
        var dx = point.X - drag.Start.X;
        var dy = point.Y - drag.Start.Y;
        _session.Mutate(project =>
        {
            var item = _session.Find(drag.Type, drag.Id);
            switch (item)
            {
                case VideoClip video:
                    video.X = drag.Origin.X + dx;
                    video.Y = drag.Origin.Y + dy;
                    break;
                case ImageClip image:
                    image.X = drag.Origin.X + dx;
                    image.Y = drag.Origin.Y + dy;
                    break;
                case TextClip text:
                    text.X = drag.Origin.X + dx;
                    text.Y = drag.Origin.Y + dy;
                    break;
            }
        }, history: false, reason: "live");
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_drag is null) return;
        _drag = null;
        _session.DropEmptyCheckpoint();
    }

    void DrawEmpty(Graphics g, EditorProject project)
    {
        using var brush = new SolidBrush(Theme.Muted);
        using var font = new Font("Segoe UI", 28f);
        const string message = "Add media to the timeline to preview the composition.";
        var size = g.MeasureString(message, font);
        g.DrawString(message, font, brush, (float)((project.Width - size.Width) / 2), (float)((project.Height - size.Height) / 2));
    }

    void DrawVideo(Graphics g, EditorProject project, VideoClip clip)
    {
        var span = ClipTiming.Video(clip);
        if (project.Playhead < span.Start || project.Playhead > span.End) return;
        var media = _session.Media.Find(clip.MediaId);
        var layout = ClipTiming.VideoLayout(clip, media, project);
        var rect = new RectangleF((float)layout.X, (float)layout.Y, (float)Math.Max(8, layout.W), (float)Math.Max(8, layout.H));
        _hits.Add(new Hit("video", clip.Id, rect));
        if (media is null)
        {
            PaintMissing(g, rect, clip.Name);
            return;
        }
        var sourceTime = clip.InPoint + Math.Max(0, project.Playhead - span.Start) * clip.Speed;
        var frame = _frames.Frame(media, sourceTime);
        var state = g.Save();
        g.TranslateTransform((float)(layout.X + layout.W / 2), (float)(layout.Y + layout.H / 2));
        g.RotateTransform((float)clip.Rotation);
        if (frame is null)
        {
            PaintMissing(g, new RectangleF((float)(-layout.Sw / 2), (float)(-layout.Sh / 2), (float)layout.Sw, (float)layout.Sh), "Loading video…");
        }
        else
        {
            var crop = clip.Crop ?? new CropRect();
            var sx = (float)(frame.Width * crop.Left / 100.0);
            var sy = (float)(frame.Height * crop.Top / 100.0);
            var sw = (float)(frame.Width * Math.Max(0.1, 1 - (crop.Left + crop.Right) / 100.0));
            var sh = (float)(frame.Height * Math.Max(0.1, 1 - (crop.Top + crop.Bottom) / 100.0));
            var dest = new RectangleF((float)(-layout.Sw / 2), (float)(-layout.Sh / 2), (float)layout.Sw, (float)layout.Sh);
            g.DrawImage(frame, dest, new RectangleF(sx, sy, Math.Max(1, sw), Math.Max(1, sh)), GraphicsUnit.Pixel);
        }
        g.Restore(state);
        if (IsSelected("video", clip.Id)) DrawSelection(g, rect);
    }

    void DrawImageClip(Graphics g, EditorProject project, ImageClip item)
    {
        if (project.Playhead < item.Start || project.Playhead > item.End) return;
        var media = _session.Media.Find(item.MediaId);
        var layout = ClipTiming.ImageLayout(item, media);
        var rect = new RectangleF((float)layout.X, (float)layout.Y, (float)layout.W, (float)layout.H);
        _hits.Add(new Hit("image", item.Id, rect));
        var state = g.Save();
        g.TranslateTransform(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        g.RotateTransform((float)item.Rotation);
        var dest = new RectangleF(-rect.Width / 2, -rect.Height / 2, rect.Width, rect.Height);
        if (media is null)
        {
            PaintMissing(g, dest, item.Name);
        }
        else
        {
            var still = _frames.Still(media);
            if (still is null) PaintMissing(g, dest, item.Name);
            else
            {
                var color = Color.FromArgb((int)(Math.Clamp(item.Opacity, 0, 1) * 255), Color.White);
                using var attributes = new ImageAttributes();
                var matrix = new ColorMatrix { Matrix33 = color.A / 255f };
                attributes.SetColorMatrix(matrix);
                g.DrawImage(still, Rectangle.Round(dest), 0, 0, still.Width, still.Height, GraphicsUnit.Pixel, attributes);
            }
        }
        g.Restore(state);
        if (IsSelected("image", item.Id)) DrawSelection(g, rect);
    }

    void DrawText(Graphics g, EditorProject project, TextClip item)
    {
        if (string.IsNullOrWhiteSpace(item.Content) || project.Playhead < item.Start || project.Playhead > item.End) return;
        var style = FontStyle.Regular;
        if (item.Bold) style |= FontStyle.Bold;
        if (item.Italic) style |= FontStyle.Italic;
        using var font = new Font(item.FontFamily, (float)Math.Max(8, item.FontSize), style, GraphicsUnit.Pixel);
        var size = g.MeasureString(item.Content, font);
        var x = (float)item.X;
        if (item.Align == "center") x -= size.Width / 2;
        else if (item.Align == "right") x -= size.Width;
        var y = (float)item.Y - size.Height / 2;
        var rect = new RectangleF(x, y, size.Width, size.Height);
        _hits.Add(new Hit("text", item.Id, rect));
        var state = g.Save();
        g.TranslateTransform((float)item.X, (float)item.Y);
        g.RotateTransform((float)item.Rotation);
        g.TranslateTransform(-(float)item.X, -(float)item.Y);
        var color = ColorTranslator.FromHtml(item.Color);
        using var brush = new SolidBrush(Color.FromArgb((int)(Math.Clamp(item.Opacity, 0, 1) * 255), color));
        g.DrawString(item.Content, font, brush, x, y);
        g.Restore(state);
        if (IsSelected("text", item.Id)) DrawSelection(g, rect);
    }

    static void PaintMissing(Graphics g, RectangleF rect, string label)
    {
        using var fill = new SolidBrush(Color.FromArgb(31, 41, 55));
        using var text = new SolidBrush(Theme.Text);
        using var font = new Font("Segoe UI", 22f, FontStyle.Regular, GraphicsUnit.Pixel);
        g.FillRectangle(fill, rect);
        g.DrawString(string.IsNullOrWhiteSpace(label) ? "Missing media" : label, font, text, rect.X + 12, rect.Y + 12);
    }

    static void DrawSelection(Graphics g, RectangleF rect)
    {
        using var pen = new Pen(Theme.Accent, 3f);
        g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
    }

    bool IsSelected(string type, string id) =>
        _session.Current is Selection selected && selected.Type == type && selected.Id == id;

    PointF ReadPosition(string type, string id) => _session.Find(type, id) switch
    {
        VideoClip video => new PointF((float)video.X, (float)video.Y),
        ImageClip image => new PointF((float)image.X, (float)image.Y),
        TextClip text => new PointF((float)text.X, (float)text.Y),
        _ => PointF.Empty
    };

    Rectangle StageRect(EditorProject project)
    {
        if (project.Width <= 0 || project.Height <= 0 || ClientSize.Width < 4 || ClientSize.Height < 4)
            return Rectangle.Empty;
        var scale = Math.Min(ClientSize.Width / (double)project.Width, ClientSize.Height / (double)project.Height);
        var width = Math.Max(2, (int)(project.Width * scale));
        var height = Math.Max(2, (int)(project.Height * scale));
        return new Rectangle((ClientSize.Width - width) / 2, (ClientSize.Height - height) / 2, width, height);
    }

    PointF ToProject(Point point)
    {
        var project = _session.Project;
        var stage = StageRect(project);
        if (stage.Width < 2 || stage.Height < 2) return PointF.Empty;
        return new PointF(
            (point.X - stage.X) / (float)stage.Width * project.Width,
            (point.Y - stage.Y) / (float)stage.Height * project.Height);
    }

    void EnsureBuffer(Size size)
    {
        if (_buffer is not null && _buffer.Width == size.Width && _buffer.Height == size.Height) return;
        _buffer?.Dispose();
        _buffer = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), PixelFormat.Format32bppPArgb);
    }

    static ColorMatrix? BuildMatrix(ColorFilters filters)
    {
        var neutral = Math.Abs(filters.Brightness) < 0.5 && Math.Abs(filters.Contrast) < 0.5 &&
                      Math.Abs(filters.Saturation) < 0.5 && filters.Grayscale < 0.5 && filters.Sepia < 0.5 && filters.Blur < 0.5;
        if (neutral) return null;
        var sat = (float)Math.Clamp((filters.Saturation + 100) / 100.0 * (1 - filters.Grayscale / 100.0), 0, 2);
        const float rw = 0.3086f, gw = 0.6094f, bw = 0.0820f;
        var inv = 1 - sat;
        float m00 = inv * rw + sat, m01 = inv * gw, m02 = inv * bw;
        float m10 = inv * rw, m11 = inv * gw + sat, m12 = inv * bw;
        float m20 = inv * rw, m21 = inv * gw, m22 = inv * bw + sat;
        var sep = (float)Math.Clamp(filters.Sepia / 100.0, 0, 1);
        m00 = Lerp(m00, 0.393f, sep); m01 = Lerp(m01, 0.769f, sep); m02 = Lerp(m02, 0.189f, sep);
        m10 = Lerp(m10, 0.349f, sep); m11 = Lerp(m11, 0.686f, sep); m12 = Lerp(m12, 0.168f, sep);
        m20 = Lerp(m20, 0.272f, sep); m21 = Lerp(m21, 0.534f, sep); m22 = Lerp(m22, 0.131f, sep);
        var contrast = (float)Math.Clamp((filters.Contrast + 100) / 100.0, 0, 2);
        var shift = (1 - contrast) / 2f + (float)(filters.Brightness / 200.0);
        return new ColorMatrix
        {
            Matrix00 = m00 * contrast, Matrix01 = m01 * contrast, Matrix02 = m02 * contrast,
            Matrix10 = m10 * contrast, Matrix11 = m11 * contrast, Matrix12 = m12 * contrast,
            Matrix20 = m20 * contrast, Matrix21 = m21 * contrast, Matrix22 = m22 * contrast,
            Matrix33 = 1, Matrix40 = shift, Matrix41 = shift, Matrix42 = shift, Matrix44 = 1
        };
    }

    static float Lerp(float a, float b, float t) => a + (b - a) * t;

    readonly record struct Hit(string Type, string Id, RectangleF Rect);
    readonly record struct Drag(string Type, string Id, PointF Start, PointF Origin);
}
