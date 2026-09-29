using System.Text.Json;
using System.Text.Json.Serialization;

namespace VideoEditor;

static class ProjectJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };
}

sealed class CropRect
{
    public double Top { get; set; }
    public double Right { get; set; }
    public double Bottom { get; set; }
    public double Left { get; set; }
}

sealed class ColorFilters
{
    public double Brightness { get; set; }
    public double Contrast { get; set; }
    public double Saturation { get; set; }
    public double Grayscale { get; set; }
    public double Blur { get; set; }
    public double Sepia { get; set; }
}

sealed class VideoClip
{
    public string Id { get; set; } = "";
    public string MediaId { get; set; } = "";
    public string Name { get; set; } = "";
    public double TimelineStart { get; set; }
    public double InPoint { get; set; }
    public double OutPoint { get; set; }
    public double SourceDuration { get; set; }
    public double Speed { get; set; } = 1;
    public double Volume { get; set; } = 1;
    public bool Muted { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Scale { get; set; } = 1;
    public double Rotation { get; set; }
    public CropRect Crop { get; set; } = new();
}

sealed class ImageClip
{
    public string Id { get; set; } = "";
    public string MediaId { get; set; } = "";
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 480;
    public double Scale { get; set; } = 1;
    public double Start { get; set; }
    public double End { get; set; } = 5;
    public double Opacity { get; set; } = 1;
    public double Rotation { get; set; }
}

sealed class TextClip
{
    public string Id { get; set; } = "";
    public string Content { get; set; } = "Your text";
    public string FontFamily { get; set; } = "Arial";
    public double FontSize { get; set; } = 64;
    public string Color { get; set; } = "#FFFFFF";
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public string Align { get; set; } = "center";
    public double X { get; set; }
    public double Y { get; set; }
    public double Start { get; set; }
    public double End { get; set; } = 5;
    public double Opacity { get; set; } = 1;
    public double Rotation { get; set; }
}

sealed class AudioClip
{
    public string Id { get; set; } = "";
    public string MediaId { get; set; } = "";
    public string Name { get; set; } = "";
    public double TimelineStart { get; set; }
    public double InPoint { get; set; }
    public double OutPoint { get; set; }
    public double SourceDuration { get; set; }
    public double Volume { get; set; } = 0.8;
    public int Track { get; set; } = 1;
}

sealed class EditorProject
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "Untitled Project";
    public int Fps { get; set; } = 30;
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public double Playhead { get; set; }
    public List<VideoClip> Clips { get; set; } = new();
    public List<ImageClip> Images { get; set; } = new();
    public List<TextClip> Texts { get; set; } = new();
    public List<AudioClip> Audios { get; set; } = new();
    public ColorFilters Filters { get; set; } = new();
    public bool MuteOriginal { get; set; }
    public string ExportMode { get; set; } = "fast";
}

sealed class MediaAsset
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string FileName { get; set; } = "";
    public double Duration { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool HasAudio { get; set; }
    public bool HasVideo { get; set; }

    [JsonIgnore]
    public string FullPath => Path.Combine(AppPaths.Media, FileName);

    public override string ToString() => Name;
}

readonly record struct Selection(string Type, string Id);

readonly record struct TimeRange(double Start, double End)
{
    public double Duration => Math.Max(0, End - Start);
}

static class ClipTiming
{
    public const double MinDuration = 0.1;
    public static readonly string[] Fonts =
    [
        "Arial", "Verdana", "Georgia", "Times New Roman", "Courier New",
        "Microsoft YaHei", "SimHei", "SimSun"
    ];
    public static readonly double[] Speeds = [0.25, 0.5, 0.75, 1, 1.25, 1.5, 2, 4];

    public static TimeRange Span(string type, object item) => type switch
    {
        "video" => Video((VideoClip)item),
        "audio" => Audio((AudioClip)item),
        "image" => new TimeRange(((ImageClip)item).Start, ((ImageClip)item).End),
        "text" => new TimeRange(((TextClip)item).Start, ((TextClip)item).End),
        _ => new TimeRange(0, 0)
    };

    public static TimeRange Video(VideoClip clip)
    {
        var speed = clip.Speed <= 0 ? 1 : clip.Speed;
        var duration = Math.Max(0, (clip.OutPoint - clip.InPoint) / speed);
        return new TimeRange(clip.TimelineStart, clip.TimelineStart + duration);
    }

    public static TimeRange Audio(AudioClip clip) =>
        new(clip.TimelineStart, clip.TimelineStart + Math.Max(0, clip.OutPoint - clip.InPoint));

    public static double ContentDuration(EditorProject project)
    {
        double max = 0;
        foreach (var clip in project.Clips) max = Math.Max(max, Video(clip).End);
        foreach (var image in project.Images) max = Math.Max(max, image.End);
        foreach (var text in project.Texts)
        {
            if (!string.IsNullOrWhiteSpace(text.Content)) max = Math.Max(max, text.End);
        }
        foreach (var audio in project.Audios) max = Math.Max(max, Audio(audio).End);
        return max;
    }

    public static double DisplayDuration(EditorProject project) => Math.Max(10, ContentDuration(project) + 2);

    public static double TrackEnd(EditorProject project, string type)
    {
        double end = 0;
        if (type == "video")
        {
            foreach (var clip in project.Clips) end = Math.Max(end, Video(clip).End);
        }
        else if (type == "audio")
        {
            foreach (var clip in project.Audios) end = Math.Max(end, Audio(clip).End);
        }
        else if (type == "image")
        {
            foreach (var clip in project.Images) end = Math.Max(end, clip.End);
        }
        else if (type == "text")
        {
            foreach (var clip in project.Texts) end = Math.Max(end, clip.End);
        }
        return end;
    }

    public static string Format(double seconds)
    {
        var safe = Math.Max(0, double.IsFinite(seconds) ? seconds : 0);
        var hours = (int)(safe / 3600);
        var minutes = (int)((safe % 3600) / 60);
        var secs = safe % 60;
        var whole = (int)secs;
        var tenth = (int)((secs - whole) * 10);
        var clock = $"{minutes:00}:{whole:00}.{tenth}";
        return hours > 0 ? $"{hours}:{clock}" : clock;
    }

    public static (double X, double Y, double W, double H, double Sw, double Sh) VideoLayout(
        VideoClip clip, MediaAsset? media, EditorProject project)
    {
        double width = media is { Width: > 0 } ? media.Width : 1920;
        double height = media is { Height: > 0 } ? media.Height : 1080;
        var crop = clip.Crop ?? new CropRect();
        width *= Math.Max(0.1, 1 - (crop.Left + crop.Right) / 100);
        height *= Math.Max(0.1, 1 - (crop.Top + crop.Bottom) / 100);
        var rad = clip.Rotation * Math.PI / 180;
        var bw = Math.Abs(width * Math.Cos(rad)) + Math.Abs(height * Math.Sin(rad));
        var bh = Math.Abs(width * Math.Sin(rad)) + Math.Abs(height * Math.Cos(rad));
        var fit = Math.Min(project.Width * clip.Scale / Math.Max(1, bw), project.Height * clip.Scale / Math.Max(1, bh));
        return (clip.X, clip.Y, bw * fit, bh * fit, width * fit, height * fit);
    }

    public static (double X, double Y, double W, double H) ImageLayout(ImageClip item, MediaAsset? media)
    {
        var aspect = media is { Width: > 0 } ? (double)media.Height / media.Width : 0.75;
        var w = item.Width * item.Scale;
        return (item.X, item.Y, w, w * aspect);
    }
}
