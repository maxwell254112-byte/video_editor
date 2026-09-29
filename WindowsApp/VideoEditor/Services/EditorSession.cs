using System.Text.Json;
using System.Text.RegularExpressions;

namespace VideoEditor;

sealed class EditorSession
{
    const int HistoryLimit = 50;
    readonly List<string> _past = new();
    readonly List<string> _future = new();

    public EditorProject Project { get; private set; } = new();
    public MediaStore Media { get; } = new();
    public Selection? Current { get; private set; }
    public bool CanUndo => _past.Count > 0;
    public bool CanRedo => _future.Count > 0;
    public event Action<string>? Changed;

    public EditorSession()
    {
        AppPaths.Ensure();
        Media.Load();
        LoadAutosave();
    }

    public bool HasContent() =>
        Project.Clips.Count > 0 || Project.Images.Count > 0 || Project.Texts.Count > 0 || Project.Audios.Count > 0;

    public void Checkpoint()
    {
        _past.Add(Snapshot());
        if (_past.Count > HistoryLimit) _past.RemoveAt(0);
        _future.Clear();
    }

    public void DropEmptyCheckpoint()
    {
        if (_past.Count == 0) return;
        if (_past[^1] == Snapshot())
        {
            _past.RemoveAt(_past.Count - 1);
            return;
        }
        Changed?.Invoke("commit");
    }

    public void Undo()
    {
        if (_past.Count == 0) return;
        _future.Add(Snapshot());
        Project = Deserialize(_past[^1]);
        _past.RemoveAt(_past.Count - 1);
        Current = null;
        Autosave();
        Changed?.Invoke("change");
    }

    public void Redo()
    {
        if (_future.Count == 0) return;
        _past.Add(Snapshot());
        Project = Deserialize(_future[^1]);
        _future.RemoveAt(_future.Count - 1);
        Current = null;
        Autosave();
        Changed?.Invoke("change");
    }

    public void Mutate(Action<EditorProject> action, bool history = true, string reason = "change")
    {
        if (history) Checkpoint();
        action(Project);
        Normalize(Project);
        Autosave();
        Changed?.Invoke(reason);
    }

    public void SetPlayhead(double time)
    {
        Project.Playhead = Quantize(Clamp(time, 0, ClipTiming.DisplayDuration(Project)));
        Changed?.Invoke("playhead");
    }

    public void SetSelection(Selection? selection)
    {
        Current = selection;
        Changed?.Invoke("selection");
    }

    public void AddMedia(MediaAsset media)
    {
        if (media.Type == "video") AddVideo(media);
        else if (media.Type == "image") AddImage(media);
        else if (media.Type == "audio") AddAudio(media);
    }

    public void AddVideo(MediaAsset media)
    {
        var id = NewId();
        var duration = Math.Max(ClipTiming.MinDuration, media.Duration);
        Mutate(project =>
        {
            var draft = new VideoClip
            {
                Scale = 1,
                Crop = new CropRect()
            };
            var fitted = ClipTiming.VideoLayout(draft, media, project);
            project.Clips.Add(new VideoClip
            {
                Id = id,
                MediaId = media.Id,
                Name = media.Name,
                TimelineStart = Quantize(ClipTiming.TrackEnd(project, "video")),
                InPoint = 0,
                OutPoint = duration,
                SourceDuration = duration,
                Speed = 1,
                Volume = 1,
                X = Quantize((project.Width - fitted.W) / 2),
                Y = Quantize((project.Height - fitted.H) / 2),
                Scale = 1,
                Rotation = 0,
                Crop = new CropRect()
            });
        });
        SetSelection(new Selection("video", id));
    }

    public void AddImage(MediaAsset media)
    {
        var id = NewId();
        Mutate(project =>
        {
            var start = Quantize(project.Playhead);
            var width = Math.Min(480, project.Width * 0.4);
            var aspect = media.Width > 0 ? (double)media.Height / media.Width : 1;
            var height = width * aspect;
            project.Images.Add(new ImageClip
            {
                Id = id,
                MediaId = media.Id,
                Name = media.Name,
                X = Quantize((project.Width - width) / 2),
                Y = Quantize((project.Height - height) / 2),
                Width = width,
                Scale = 1,
                Start = start,
                End = Quantize(start + 5),
                Opacity = 1
            });
        });
        SetSelection(new Selection("image", id));
    }

    public void AddText()
    {
        var id = NewId();
        Mutate(project =>
        {
            var start = Quantize(project.Playhead);
            project.Texts.Add(new TextClip
            {
                Id = id,
                Content = "Your text",
                FontFamily = "Arial",
                FontSize = 64,
                Color = "#FFFFFF",
                Align = "center",
                X = project.Width / 2.0,
                Y = project.Height / 2.0,
                Start = start,
                End = Quantize(start + 5),
                Opacity = 1
            });
        });
        SetSelection(new Selection("text", id));
    }

    public void AddAudio(MediaAsset media)
    {
        var id = NewId();
        var duration = Math.Max(ClipTiming.MinDuration, media.Duration);
        Mutate(project =>
        {
            project.Audios.Add(new AudioClip
            {
                Id = id,
                MediaId = media.Id,
                Name = media.Name,
                TimelineStart = Quantize(ClipTiming.TrackEnd(project, "audio")),
                InPoint = 0,
                OutPoint = duration,
                SourceDuration = duration,
                Volume = 0.8,
                Track = 1
            });
        });
        SetSelection(new Selection("audio", id));
    }

    public bool DeleteSelection()
    {
        if (Current is not Selection selected) return false;
        var id = selected.Id;
        Mutate(project =>
        {
            switch (selected.Type)
            {
                case "video": project.Clips.RemoveAll(item => item.Id == id); break;
                case "image": project.Images.RemoveAll(item => item.Id == id); break;
                case "text": project.Texts.RemoveAll(item => item.Id == id); break;
                case "audio": project.Audios.RemoveAll(item => item.Id == id); break;
            }
        });
        Current = null;
        Changed?.Invoke("selection");
        return true;
    }

    public void DeleteMedia(string mediaId)
    {
        Mutate(project =>
        {
            project.Clips.RemoveAll(item => item.MediaId == mediaId);
            project.Images.RemoveAll(item => item.MediaId == mediaId);
            project.Audios.RemoveAll(item => item.MediaId == mediaId);
        });
        Media.Remove(mediaId);
        if (Current is Selection selected && Find(selected.Type, selected.Id) is null)
        {
            Current = null;
            Changed?.Invoke("selection");
        }
    }

    public string? SplitSelected()
    {
        if (Current is not Selection selected || selected.Type != "video")
            return "Select a video clip to split.";
        var clip = Project.Clips.FirstOrDefault(item => item.Id == selected.Id);
        if (clip is null) return "Select a video clip to split.";
        var span = ClipTiming.Video(clip);
        var time = Project.Playhead;
        if (time <= span.Start + ClipTiming.MinDuration || time >= span.End - ClipTiming.MinDuration)
            return "Move the playhead inside the clip, away from its edges.";
        var secondId = NewId();
        var sourceAt = clip.InPoint + (time - clip.TimelineStart) * clip.Speed;
        Mutate(project =>
        {
            var current = project.Clips.First(item => item.Id == clip.Id);
            var copy = Clone(current);
            current.OutPoint = sourceAt;
            copy.Id = secondId;
            copy.InPoint = sourceAt;
            copy.TimelineStart = time;
            var index = project.Clips.FindIndex(item => item.Id == current.Id);
            project.Clips.Insert(index + 1, copy);
        });
        SetSelection(new Selection("video", secondId));
        return null;
    }

    public void Relink(string type, string id, MediaAsset asset)
    {
        Mutate(project =>
        {
            if (type == "video")
            {
                var clip = project.Clips.FirstOrDefault(item => item.Id == id);
                if (clip is null) return;
                clip.MediaId = asset.Id;
                clip.Name = asset.Name;
                clip.SourceDuration = Math.Max(ClipTiming.MinDuration, asset.Duration);
            }
            else if (type == "image")
            {
                var clip = project.Images.FirstOrDefault(item => item.Id == id);
                if (clip is null) return;
                clip.MediaId = asset.Id;
                clip.Name = asset.Name;
            }
            else if (type == "audio")
            {
                var clip = project.Audios.FirstOrDefault(item => item.Id == id);
                if (clip is null) return;
                clip.MediaId = asset.Id;
                clip.Name = asset.Name;
                clip.SourceDuration = Math.Max(ClipTiming.MinDuration, asset.Duration);
            }
        });
    }

    public object? Find(string type, string id) => type switch
    {
        "video" => Project.Clips.FirstOrDefault(item => item.Id == id),
        "image" => Project.Images.FirstOrDefault(item => item.Id == id),
        "text" => Project.Texts.FirstOrDefault(item => item.Id == id),
        "audio" => Project.Audios.FirstOrDefault(item => item.Id == id),
        _ => null
    };

    public void NewProject()
    {
        Checkpoint();
        Project = new EditorProject();
        Current = null;
        Autosave();
        Changed?.Invoke("change");
    }

    public void ResetProject()
    {
        Project = new EditorProject();
        _past.Clear();
        _future.Clear();
        Current = null;
        Autosave();
        Changed?.Invoke("change");
    }

    public string? ReplaceFromJson(string json)
    {
        try
        {
            var next = JsonSerializer.Deserialize<EditorProject>(json, ProjectJson.Options);
            if (next is null) return "Invalid project data.";
            Checkpoint();
            Project = next;
            Project.Playhead = 0;
            Normalize(Project);
            Current = null;
            Autosave();
            Changed?.Invoke("change");
            return null;
        }
        catch (JsonException)
        {
            return "Invalid project data.";
        }
    }

    public string ToJson() => JsonSerializer.Serialize(Project, ProjectJson.Options);

    public void Autosave()
    {
        try
        {
            AppPaths.Ensure();
            File.WriteAllText(AppPaths.AutosaveFile, ToJson());
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    void LoadAutosave()
    {
        if (!File.Exists(AppPaths.AutosaveFile)) return;
        try
        {
            var loaded = JsonSerializer.Deserialize<EditorProject>(File.ReadAllText(AppPaths.AutosaveFile), ProjectJson.Options);
            if (loaded is null) return;
            Project = loaded;
            Normalize(Project);
        }
        catch (JsonException)
        {
            Project = new EditorProject();
        }
        catch (IOException)
        {
            Project = new EditorProject();
        }
    }

    string Snapshot() => JsonSerializer.Serialize(Project, ProjectJson.Options);

    static EditorProject Deserialize(string json)
    {
        var project = JsonSerializer.Deserialize<EditorProject>(json, ProjectJson.Options) ?? new EditorProject();
        Normalize(project);
        return project;
    }

    static VideoClip Clone(VideoClip clip)
    {
        var copy = JsonSerializer.Deserialize<VideoClip>(JsonSerializer.Serialize(clip, ProjectJson.Options), ProjectJson.Options);
        return copy ?? new VideoClip();
    }

    public static void Normalize(EditorProject project)
    {
        project.Clips ??= new();
        project.Images ??= new();
        project.Texts ??= new();
        project.Audios ??= new();
        project.Filters ??= new();
        project.Name = string.IsNullOrWhiteSpace(project.Name) ? "Untitled Project" : project.Name.Trim();
        if (project.Name.Length > 80) project.Name = project.Name[..80];
        project.Fps = project.Fps is 24 or 25 or 30 or 50 or 60 ? project.Fps : 30;
        project.Width = Even(Clamp(project.Width <= 0 ? 1920 : project.Width, 320, 3840));
        project.Height = Even(Clamp(project.Height <= 0 ? 1080 : project.Height, 240, 2160));
        project.Playhead = Clamp(project.Playhead, 0, 86400);
        project.ExportMode = project.ExportMode == "high" ? "high" : "fast";
        var filters = project.Filters;
        filters.Brightness = Clamp(filters.Brightness, -100, 100);
        filters.Contrast = Clamp(filters.Contrast, -100, 100);
        filters.Saturation = Clamp(filters.Saturation, -100, 100);
        filters.Grayscale = Clamp(filters.Grayscale, 0, 100);
        filters.Blur = Clamp(filters.Blur, 0, 20);
        filters.Sepia = Clamp(filters.Sepia, 0, 100);
        if (project.Clips.Count > 40) project.Clips.RemoveRange(40, project.Clips.Count - 40);
        if (project.Images.Count > 40) project.Images.RemoveRange(40, project.Images.Count - 40);
        if (project.Texts.Count > 40) project.Texts.RemoveRange(40, project.Texts.Count - 40);
        if (project.Audios.Count > 20) project.Audios.RemoveRange(20, project.Audios.Count - 20);
        foreach (var clip in project.Clips) SanitizeVideo(clip);
        foreach (var clip in project.Images) SanitizeImage(clip);
        foreach (var clip in project.Texts) SanitizeText(clip);
        foreach (var clip in project.Audios) SanitizeAudio(clip);
    }

    static void SanitizeVideo(VideoClip item)
    {
        item.Id = string.IsNullOrWhiteSpace(item.Id) ? NewId() : item.Id;
        item.Name ??= "";
        item.Crop ??= new CropRect();
        item.Speed = Clamp(item.Speed, 0.25, 4);
        item.Volume = Clamp(item.Volume, 0, 1);
        item.TimelineStart = Quantize(Clamp(item.TimelineStart, 0, 86400));
        item.SourceDuration = Math.Max(ClipTiming.MinDuration, item.SourceDuration);
        var minSource = ClipTiming.MinDuration * item.Speed;
        item.InPoint = Quantize(Clamp(item.InPoint, 0, Math.Max(0, item.SourceDuration - minSource)));
        item.OutPoint = Quantize(Clamp(item.OutPoint, item.InPoint + minSource, item.SourceDuration));
        item.Scale = Clamp(item.Scale, 0.1, 4);
        item.Rotation = Mod360(item.Rotation);
        item.X = Quantize(item.X);
        item.Y = Quantize(item.Y);
        SanitizeCrop(item.Crop);
    }

    static void SanitizeAudio(AudioClip item)
    {
        item.Id = string.IsNullOrWhiteSpace(item.Id) ? NewId() : item.Id;
        item.Name ??= "";
        item.Volume = Clamp(item.Volume, 0, 1);
        item.Track = (int)Clamp(item.Track <= 0 ? 1 : item.Track, 1, 4);
        item.TimelineStart = Quantize(Clamp(item.TimelineStart, 0, 86400));
        item.SourceDuration = Math.Max(ClipTiming.MinDuration, item.SourceDuration);
        item.InPoint = Quantize(Clamp(item.InPoint, 0, item.SourceDuration - ClipTiming.MinDuration));
        item.OutPoint = Quantize(Clamp(item.OutPoint, item.InPoint + ClipTiming.MinDuration, item.SourceDuration));
    }

    static void SanitizeImage(ImageClip item)
    {
        item.Id = string.IsNullOrWhiteSpace(item.Id) ? NewId() : item.Id;
        item.Name ??= "";
        item.Start = Quantize(Clamp(item.Start, 0, 86400));
        item.End = Quantize(Math.Max(item.Start + ClipTiming.MinDuration, item.End <= 0 ? item.Start + 5 : item.End));
        item.Width = Clamp(item.Width, 10, 3840);
        item.Scale = Clamp(item.Scale, 0.1, 4);
        item.Opacity = Clamp(item.Opacity, 0, 1);
        item.Rotation = Mod360(item.Rotation);
        item.X = Quantize(item.X);
        item.Y = Quantize(item.Y);
    }

    static void SanitizeText(TextClip item)
    {
        item.Id = string.IsNullOrWhiteSpace(item.Id) ? NewId() : item.Id;
        item.Content = (item.Content ?? "").Length > 200 ? item.Content![..200] : item.Content ?? "";
        item.FontFamily = ClipTiming.Fonts.Contains(item.FontFamily) ? item.FontFamily : "Arial";
        item.FontSize = Clamp(item.FontSize, 8, 400);
        item.Color = Regex.IsMatch(item.Color ?? "", "^#[0-9a-fA-F]{6}$") ? item.Color!.ToUpperInvariant() : "#FFFFFF";
        item.Align = item.Align is "left" or "center" or "right" ? item.Align : "center";
        item.Start = Quantize(Clamp(item.Start, 0, 86400));
        item.End = Quantize(Math.Max(item.Start + ClipTiming.MinDuration, item.End <= 0 ? item.Start + 5 : item.End));
        item.Opacity = Clamp(item.Opacity, 0, 1);
        item.Rotation = Mod360(item.Rotation);
        item.X = Quantize(item.X);
        item.Y = Quantize(item.Y);
    }

    static void SanitizeCrop(CropRect crop)
    {
        crop.Top = Clamp(crop.Top, 0, 40);
        crop.Right = Clamp(crop.Right, 0, 40);
        crop.Bottom = Clamp(crop.Bottom, 0, 40);
        crop.Left = Clamp(crop.Left, 0, 40);
        if (crop.Top + crop.Bottom > 80)
        {
            var scale = 80 / (crop.Top + crop.Bottom);
            crop.Top *= scale;
            crop.Bottom *= scale;
        }
        if (crop.Left + crop.Right > 80)
        {
            var scale = 80 / (crop.Left + crop.Right);
            crop.Left *= scale;
            crop.Right *= scale;
        }
    }

    static int Even(double value)
    {
        var number = (int)Math.Round(value);
        return number - (Math.Abs(number) % 2);
    }

    static double Mod360(double value)
    {
        if (!double.IsFinite(value)) return 0;
        var deg = value % 360;
        if (deg < 0) deg += 360;
        return deg;
    }

    static double Clamp(double value, double min, double max)
    {
        if (!double.IsFinite(value)) return min;
        return Math.Min(max, Math.Max(min, value));
    }

    static double Quantize(double value) => Math.Round(Clamp(value, -100000, 100000) * 1000) / 1000;

    static string NewId() => Guid.NewGuid().ToString();
}
