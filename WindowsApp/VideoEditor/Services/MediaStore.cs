using System.Text.Json;

namespace VideoEditor;

sealed class MediaStore
{
    static readonly Dictionary<string, (string Type, long Max)> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = ("video", 500L * 1024 * 1024),
        [".webm"] = ("video", 500L * 1024 * 1024),
        [".mov"] = ("video", 500L * 1024 * 1024),
        [".wmv"] = ("video", 500L * 1024 * 1024),
        [".jpg"] = ("image", 20L * 1024 * 1024),
        [".jpeg"] = ("image", 20L * 1024 * 1024),
        [".png"] = ("image", 20L * 1024 * 1024),
        [".webp"] = ("image", 20L * 1024 * 1024),
        [".mp3"] = ("audio", 50L * 1024 * 1024),
        [".wav"] = ("audio", 50L * 1024 * 1024),
        [".aac"] = ("audio", 50L * 1024 * 1024),
        [".m4a"] = ("audio", 50L * 1024 * 1024)
    };

    readonly List<MediaAsset> _items = new();
    public IReadOnlyList<MediaAsset> Items => _items;

    public MediaAsset? Find(string? id) =>
        string.IsNullOrEmpty(id) ? null : _items.FirstOrDefault(item => item.Id == id);

    public void Load()
    {
        _items.Clear();
        if (!File.Exists(AppPaths.LibraryFile)) return;
        try
        {
            var loaded = JsonSerializer.Deserialize<List<MediaAsset>>(File.ReadAllText(AppPaths.LibraryFile), ProjectJson.Options);
            if (loaded is null) return;
            foreach (var item in loaded)
            {
                if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.FileName)) continue;
                if (!File.Exists(item.FullPath)) continue;
                _items.Add(item);
            }
        }
        catch (JsonException)
        {
            _items.Clear();
        }
    }

    public void Save()
    {
        AppPaths.Ensure();
        File.WriteAllText(AppPaths.LibraryFile, JsonSerializer.Serialize(_items, ProjectJson.Options));
    }

    public (MediaAsset? Asset, string? Error) Import(string sourcePath)
    {
        if (!File.Exists(sourcePath)) return (null, "File was not found.");
        var ext = Path.GetExtension(sourcePath);
        if (!Kinds.TryGetValue(ext, out var kind)) return (null, "Unsupported file type.");
        var info = new FileInfo(sourcePath);
        if (info.Length > kind.Max)
        {
            var limit = kind.Type switch
            {
                "video" => "500 MB",
                "image" => "20 MB",
                _ => "50 MB"
            };
            return (null, $"File is too large. The {kind.Type} limit is {limit}.");
        }

        AppPaths.Ensure();
        var storedName = Guid.NewGuid().ToString("N") + ext.ToLowerInvariant();
        var dest = Path.Combine(AppPaths.Media, storedName);
        File.Copy(sourcePath, dest, overwrite: false);

        var asset = new MediaAsset
        {
            Id = Guid.NewGuid().ToString(),
            Name = Path.GetFileName(sourcePath),
            Type = kind.Type,
            FileName = storedName
        };

        if (kind.Type == "image")
        {
            if (!TryImageSize(dest, out var width, out var height))
            {
                var probe = FfmpegTools.Probe(dest);
                if (probe is not { Width: > 0, Height: > 0 })
                {
                    File.Delete(dest);
                    return (null, FfmpegTools.FfprobePath is null
                        ? "FFprobe is unavailable."
                        : "Media could not be analysed.");
                }
                width = probe.Width;
                height = probe.Height;
            }
            asset.Width = width;
            asset.Height = height;
            asset.HasVideo = false;
            asset.Duration = 0;
        }
        else
        {
            if (FfmpegTools.FfprobePath is null)
            {
                File.Delete(dest);
                return (null, "FFprobe is unavailable.");
            }
            var probe = FfmpegTools.Probe(dest);
            if (probe is null)
            {
                File.Delete(dest);
                return (null, "Media could not be analysed.");
            }
            if (kind.Type == "video" && !probe.HasVideo)
            {
                File.Delete(dest);
                return (null, "Media could not be analysed.");
            }
            if (kind.Type == "audio" && !probe.HasAudio)
            {
                File.Delete(dest);
                return (null, "Media could not be analysed.");
            }
            asset.Duration = Math.Max(ClipTiming.MinDuration, probe.Duration);
            asset.Width = probe.Width;
            asset.Height = probe.Height;
            asset.HasAudio = probe.HasAudio;
            asset.HasVideo = probe.HasVideo;
        }

        _items.Add(asset);
        Save();
        return (asset, null);
    }

    public void Remove(string id)
    {
        var asset = Find(id);
        if (asset is null) return;
        _items.Remove(asset);
        try
        {
            if (File.Exists(asset.FullPath)) File.Delete(asset.FullPath);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        Save();
    }

    static bool TryImageSize(string path, out int width, out int height)
    {
        width = 0;
        height = 0;
        try
        {
            using var stream = File.OpenRead(path);
            using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);
            width = image.Width;
            height = image.Height;
            return width > 0 && height > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
