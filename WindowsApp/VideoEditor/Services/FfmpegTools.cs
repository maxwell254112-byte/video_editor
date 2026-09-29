using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace VideoEditor;

sealed class ProbeResult
{
    public double Duration { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public bool HasVideo { get; init; }
    public bool HasAudio { get; init; }
    public string VideoCodec { get; init; } = "";
}

static class FfmpegTools
{
    public static void Remember(string ffmpegExe)
    {
        var dir = Path.GetDirectoryName(ffmpegExe);
        if (string.IsNullOrWhiteSpace(dir)) return;
        AppPaths.Ensure();
        File.WriteAllText(Path.Combine(AppPaths.Root, "ffmpeg.dir"), dir);
        Refresh();
    }

    static string? RememberedDirectory()
    {
        var file = Path.Combine(AppPaths.Root, "ffmpeg.dir");
        if (!File.Exists(file)) return null;
        try
        {
            var dir = File.ReadAllText(file).Trim();
            return Directory.Exists(dir) ? dir : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static string? FfmpegPath { get; private set; }
    public static string? FfprobePath { get; private set; }
    public static string Version { get; private set; } = "";
    public static bool Ready => FfmpegPath is not null && FfprobePath is not null && Version.Length > 0;

    public static void Refresh()
    {
        FfmpegPath = Find("ffmpeg");
        FfprobePath = Find("ffprobe");
        Version = "";
        if (FfmpegPath is null) return;
        var (code, stdout, _) = Run(FfmpegPath, ["-version"], 8000);
        if (code != 0) return;
        var line = stdout.Split('\n', 2)[0];
        const string marker = "ffmpeg version ";
        var index = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            var rest = line[(index + marker.Length)..].Trim();
            Version = rest.Split(' ', 2)[0];
        }
    }

    public static ProbeResult? Probe(string path)
    {
        if (FfprobePath is null || !File.Exists(path)) return null;
        var (code, stdout, _) = Run(FfprobePath, [
            "-v", "quiet", "-print_format", "json", "-show_format", "-show_streams", path
        ], 40000);
        if (code != 0 || string.IsNullOrWhiteSpace(stdout)) return null;
        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            double duration = 0;
            if (root.TryGetProperty("format", out var format) &&
                format.TryGetProperty("duration", out var durationValue) &&
                double.TryParse(durationValue.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                duration = parsed;
            }
            var width = 0;
            var height = 0;
            var hasVideo = false;
            var hasAudio = false;
            var codec = "";
            if (root.TryGetProperty("streams", out var streams))
            {
                foreach (var stream in streams.EnumerateArray())
                {
                    var type = stream.TryGetProperty("codec_type", out var typeValue) ? typeValue.GetString() : "";
                    if (type == "video" && !hasVideo)
                    {
                        hasVideo = true;
                        width = stream.TryGetProperty("width", out var w) ? w.GetInt32() : 0;
                        height = stream.TryGetProperty("height", out var h) ? h.GetInt32() : 0;
                        codec = stream.TryGetProperty("codec_name", out var c) ? c.GetString() ?? "" : "";
                    }
                    else if (type == "audio")
                    {
                        hasAudio = true;
                    }
                }
            }
            return new ProbeResult
            {
                Duration = Math.Max(0, duration),
                Width = width,
                Height = height,
                HasVideo = hasVideo,
                HasAudio = hasAudio,
                VideoCodec = codec.ToLowerInvariant()
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static Bitmap? GrabFrame(string path, double seconds)
    {
        if (FfmpegPath is null || !File.Exists(path)) return null;
        var stamp = seconds.ToString("0.###", CultureInfo.InvariantCulture);
        var (code, data) = RunBytes(FfmpegPath, [
            "-hide_banner", "-loglevel", "error", "-ss", stamp, "-i", path,
            "-frames:v", "1", "-f", "image2pipe", "-vcodec", "mjpeg", "pipe:1"
        ], 20000);
        if (code != 0 || data.Length < 64) return null;
        try
        {
            using var stream = new MemoryStream(data);
            using var image = Image.FromStream(stream);
            return new Bitmap(image);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public static (int Code, string Stdout, string Stderr) Run(string file, IReadOnlyList<string> args, int timeoutMs)
    {
        using var process = Start(file, args, redirectOut: true);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMs))
        {
            TryKill(process);
            return (-1, "", "timed out");
        }
        process.WaitForExit();
        return (process.ExitCode, stdout.Result, stderr.Result);
    }

    public static (int Code, byte[] Data) RunBytes(string file, IReadOnlyList<string> args, int timeoutMs)
    {
        using var process = Start(file, args, redirectOut: true);
        using var buffer = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(buffer);
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMs))
        {
            TryKill(process);
            return (-1, []);
        }
        process.WaitForExit();
        copy.Wait(2000);
        _ = stderr.Result;
        return (process.ExitCode, buffer.ToArray());
    }

    static Process Start(string file, IReadOnlyList<string> args, bool redirectOut)
    {
        var info = new ProcessStartInfo
        {
            FileName = file,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = redirectOut,
            RedirectStandardError = true
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        var process = new Process { StartInfo = info };
        if (!process.Start()) throw new InvalidOperationException("Could not start " + Path.GetFileName(file));
        return process;
    }

    static void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch (Exception) { /* already exited */ }
    }

    static string? Find(string tool)
    {
        var names = new[] { tool + ".exe", tool };
        var roots = new List<string>
        {
            AppPaths.ExeDirectory,
            Path.Combine(AppPaths.ExeDirectory, "bin"),
            AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, "bin")
        };
        var remembered = RememberedDirectory();
        if (!string.IsNullOrWhiteSpace(remembered)) roots.Insert(0, remembered);
        var dir = new DirectoryInfo(AppPaths.ExeDirectory);
        for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            roots.Add(Path.Combine(dir.FullName, "bin"));
        }
        roots.Add(Path.Combine("C:", "ffmpeg", "bin"));
        foreach (var root in roots)
        {
            foreach (var name in names)
            {
                var path = Path.Combine(root, name);
                if (File.Exists(path)) return path;
            }
        }
        var located = LocateOnPath(tool);
        return located is not null && File.Exists(located) ? located : null;
    }

    static string? LocateOnPath(string tool)
    {
        try
        {
            var (code, stdout, _) = Run(Path.Combine(Environment.SystemDirectory, "where.exe"), [tool], 5000);
            if (code != 0) return null;
            foreach (var line in stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Trim();
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch (Exception)
        {
            return null;
        }
        return null;
    }
}
