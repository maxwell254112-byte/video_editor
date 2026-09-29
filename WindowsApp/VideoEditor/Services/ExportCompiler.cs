using System.Globalization;
using System.Text;

namespace VideoEditor;

sealed class ExportPlan
{
    public required string Ffmpeg { get; init; }
    public required List<string> Arguments { get; init; }
    public required string Output { get; init; }
    public required string ProgressFile { get; init; }
    public required string JobDirectory { get; init; }
    public double Duration { get; init; }
}

static class ExportCompiler
{
    public static ExportPlan Compile(EditorProject project, MediaStore media, string output)
    {
        if (FfmpegTools.FfmpegPath is not string ffmpeg)
            throw new InvalidOperationException("FFmpeg is unavailable.");

        var duration = Math.Round(ClipTiming.ContentDuration(project), 3);
        if (duration < 0.1)
            throw new InvalidOperationException("Add at least one video, image, text, or audio clip before exporting.");

        var high = project.ExportMode == "high";
        var outW = high ? 1920 : 1280;
        var outH = high ? 1080 : 720;
        var fps = project.Fps;
        var sx = outW / (double)Math.Max(1, project.Width);
        var sy = outH / (double)Math.Max(1, project.Height);

        var jobDir = Path.Combine(AppPaths.Jobs, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(jobDir);
        var progressFile = Path.Combine(jobDir, "progress.txt");

        var inputs = new List<(string Path, bool Loop)>();
        var chains = new List<string>();
        var audioLabels = new List<string>();
        var inputIndex = 1;
        chains.Add($"[0:v]format=yuv420p,fps={fps}[base0]");
        var current = "base0";

        for (var clipIndex = 0; clipIndex < project.Clips.Count; clipIndex++)
        {
            var clip = project.Clips[clipIndex];
            var asset = media.Find(clip.MediaId);
            if (asset is null || asset.Type != "video" || !File.Exists(asset.FullPath))
                throw new InvalidOperationException("Missing media.");
            inputs.Add((asset.FullPath, false));
            var speed = clip.Speed;
            var start = clip.TimelineStart;
            var filters = new List<string>
            {
                "trim=start=" + Ff(clip.InPoint) + ":end=" + Ff(clip.OutPoint),
                "setpts=PTS-STARTPTS",
                "fps=" + fps,
                "setpts='PTS/" + Ff(speed) + "+" + Ff(start) + "/TB'"
            };
            var crop = CropFilter(clip.Crop);
            if (crop.Length > 0) filters.Add(crop);
            var rotation = RotationFilter(clip.Rotation);
            if (rotation.Length > 0) filters.Add(rotation);
            var targetW = Even(Math.Max(2, (int)Math.Round(outW * clip.Scale)));
            var targetH = Even(Math.Max(2, (int)Math.Round(outH * clip.Scale)));
            filters.Add($"scale={targetW}:{targetH}:force_original_aspect_ratio=decrease");
            filters.Add("scale=trunc(iw/2)*2:trunc(ih/2)*2");
            filters.Add("setsar=1");
            var orthogonal = ((int)Math.Round(clip.Rotation) % 360) is 0 or 90 or 180 or 270;
            filters.Add(orthogonal ? "format=yuv420p" : "format=yuva420p");
            var label = "cv" + clipIndex;
            chains.Add($"[{inputIndex}:v]{string.Join(',', filters)}[{label}]");
            var x = (int)Math.Round(clip.X * sx);
            var y = (int)Math.Round(clip.Y * sy);
            var next = "basev" + (clipIndex + 1);
            chains.Add($"[{current}][{label}]overlay=x={x}:y={y}:eof_action=pass[{next}]");
            current = next;

            var includeAudio = asset.HasAudio && !project.MuteOriginal && !clip.Muted && clip.Volume > 0.001;
            if (includeAudio)
            {
                var aLabel = "ca" + clipIndex;
                var audio = new List<string>
                {
                    $"[{inputIndex}:a]atrim=start={Ff(clip.InPoint)}:end={Ff(clip.OutPoint)}",
                    "asetpts=PTS-STARTPTS",
                    AtempoChain(speed),
                    "aresample=48000",
                    "aformat=sample_fmts=fltp:channel_layouts=stereo",
                    "volume=" + Ff(clip.Volume)
                };
                var delay = (int)Math.Round(start * 1000);
                if (delay > 0) audio.Add($"adelay={delay}|{delay}");
                chains.Add(string.Join(',', audio) + "[" + aLabel + "]");
                audioLabels.Add(aLabel);
            }
            inputIndex++;
        }

        for (var imageIndex = 0; imageIndex < project.Images.Count; imageIndex++)
        {
            var image = project.Images[imageIndex];
            var asset = media.Find(image.MediaId);
            if (asset is null || asset.Type != "image" || !File.Exists(asset.FullPath))
                throw new InvalidOperationException("Missing media.");
            inputs.Add((asset.FullPath, true));
            var displayW = Even(Math.Max(2, (int)Math.Round(image.Width * image.Scale * sx)));
            var filters = new List<string> { "scale=" + displayW + ":-2", "format=rgba" };
            if (image.Opacity < 0.999) filters.Add("colorchannelmixer=aa=" + Ff(image.Opacity));
            var rotation = RotationFilter(image.Rotation);
            if (rotation.Length > 0) filters.Add(rotation);
            var label = "im" + imageIndex;
            chains.Add($"[{inputIndex}:v]{string.Join(',', filters)}[{label}]");
            var mediaW = Math.Max(1, asset.Width);
            var mediaH = Math.Max(1, asset.Height);
            var displayH = Even(Math.Max(2, (int)Math.Round(displayW * (mediaH / (double)mediaW))));
            var radians = image.Rotation * Math.PI / 180.0;
            var boxW = Math.Abs(displayW * Math.Cos(radians)) + Math.Abs(displayH * Math.Sin(radians));
            var boxH = Math.Abs(displayW * Math.Sin(radians)) + Math.Abs(displayH * Math.Cos(radians));
            var centerX = image.X * sx + displayW / 2.0;
            var centerY = image.Y * sy + displayH / 2.0;
            var x = (int)Math.Round(centerX - boxW / 2);
            var y = (int)Math.Round(centerY - boxH / 2);
            var next = "basei" + imageIndex;
            chains.Add($"[{current}][{label}]overlay=x={x}:y={y}:eof_action=pass:{Between(image.Start, image.End)}[{next}]");
            current = next;
            inputIndex++;
        }

        var textIndex = 0;
        foreach (var text in project.Texts)
        {
            var content = (text.Content ?? "").Trim();
            if (content.Length == 0) continue;
            var font = ResolveFont(text.FontFamily, text.Bold, text.Italic)
                ?? throw new InvalidOperationException("A required font file could not be found.");
            var textFile = Path.Combine(jobDir, "text-" + textIndex + ".txt");
            File.WriteAllText(textFile, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var fontSize = Math.Max(8, (int)Math.Round(text.FontSize * sy));
            var anchorX = text.X * sx;
            var anchorY = text.Y * sy;
            var color = (text.Color ?? "#FFFFFF").TrimStart('#');
            var fontColor = "0x" + color + "@" + Ff(text.Opacity);
            var fontIndex = font.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase) ? ":fontindex=0" : "";
            var drawBase = "drawtext=fontfile='" + EscapePath(font) + "':textfile='" + EscapePath(textFile) +
                           $"':fontsize={fontSize}:fontcolor={fontColor}:expansion=none:line_spacing=6{fontIndex}";
            var next = "baset" + textIndex;
            var rotation = (int)Math.Round(text.Rotation) % 360;
            if (rotation == 0)
            {
                var xExpr = text.Align switch
                {
                    "center" => "(" + Ff(anchorX) + "-text_w/2)",
                    "right" => "(" + Ff(anchorX) + "-text_w)",
                    _ => Ff(anchorX)
                };
                var yExpr = "(" + Ff(anchorY) + "-text_h/2)";
                chains.Add($"[{current}]{drawBase}:x='{xExpr}':y='{yExpr}':{Between(text.Start, text.End)}[{next}]");
            }
            else
            {
                var estimate = (int)Math.Ceiling(content.Length * fontSize * 1.2) + fontSize;
                var boxW = Even(Math.Max(64, estimate * 2));
                var boxH = Even(Math.Max(64, fontSize * 4));
                var localX = text.Align switch
                {
                    "center" => "(w-text_w)/2",
                    "right" => "(w/2-text_w)",
                    _ => "w/2"
                };
                var radians = rotation * Math.PI / 180.0;
                var rotatedW = Math.Abs(boxW * Math.Cos(radians)) + Math.Abs(boxH * Math.Sin(radians));
                var rotatedH = Math.Abs(boxW * Math.Sin(radians)) + Math.Abs(boxH * Math.Cos(radians));
                var overlayX = (int)Math.Round(anchorX - rotatedW / 2);
                var overlayY = (int)Math.Round(anchorY - rotatedH / 2);
                var spin = RotationFilter(rotation);
                var label = "tx" + textIndex;
                chains.Add($"color=c=black@0:s={boxW}x{boxH}:r={fps}:d={Ff(duration)},format=rgba,{drawBase}:x='{localX}':y='(h-text_h)/2',{spin}[{label}]");
                chains.Add($"[{current}][{label}]overlay=x={overlayX}:y={overlayY}:eof_action=pass:{Between(text.Start, text.End)}[{next}]");
            }
            current = next;
            textIndex++;
        }

        var visual = VisualFilters(project.Filters);
        if (visual.Count > 0)
        {
            chains.Add($"[{current}]{string.Join(',', visual)}[basef]");
            current = "basef";
        }
        if (project.Filters.Sepia > 0.05)
        {
            var amount = Ff(project.Filters.Sepia / 100);
            chains.Add($"[{current}]split[sepA][sepB]");
            chains.Add("[sepB]colorchannelmixer=.393:.769:.189:0:.349:.686:.168:0:.272:.534:.131[sepC]");
            chains.Add($"[sepA][sepC]blend=all_expr='A*(1-{amount})+B*{amount}'[baseSep]");
            current = "baseSep";
        }
        chains.Add($"[{current}]format=yuv420p[vout]");

        for (var audioIndex = 0; audioIndex < project.Audios.Count; audioIndex++)
        {
            var audioClip = project.Audios[audioIndex];
            var asset = media.Find(audioClip.MediaId);
            if (asset is null || asset.Type != "audio" || !File.Exists(asset.FullPath))
                throw new InvalidOperationException("Missing media.");
            if (audioClip.Volume <= 0.001) continue;
            inputs.Add((asset.FullPath, false));
            var aLabel = "bg" + audioIndex;
            var audio = new List<string>
            {
                $"[{inputIndex}:a]atrim=start={Ff(audioClip.InPoint)}:end={Ff(audioClip.OutPoint)}",
                "asetpts=PTS-STARTPTS",
                "aresample=48000",
                "aformat=sample_fmts=fltp:channel_layouts=stereo",
                "volume=" + Ff(audioClip.Volume)
            };
            var delay = (int)Math.Round(audioClip.TimelineStart * 1000);
            if (delay > 0) audio.Add($"adelay={delay}|{delay}");
            chains.Add(string.Join(',', audio) + "[" + aLabel + "]");
            audioLabels.Add(aLabel);
            inputIndex++;
        }

        if (audioLabels.Count == 0)
            chains.Add("anullsrc=channel_layout=stereo:sample_rate=48000,atrim=duration=" + Ff(duration) + ",asetpts=PTS-STARTPTS[aout]");
        else if (audioLabels.Count == 1)
            chains.Add("[" + audioLabels[0] + "]anull[aout]");
        else
        {
            var mixIn = string.Concat(audioLabels.Select(label => "[" + label + "]"));
            chains.Add($"{mixIn}amix=inputs={audioLabels.Count}:duration=longest:dropout_transition=0:normalize=0[aout]");
        }

        var filterFile = Path.Combine(jobDir, "filter.txt");
        File.WriteAllText(filterFile, string.Join(';', chains), new UTF8Encoding(false));
        var preset = high ? "medium" : "veryfast";
        var crf = high ? "18" : "26";
        var audioBitrate = high ? "192k" : "160k";
        var command = new List<string>
        {
            "-y", "-hide_banner",
            "-f", "lavfi", "-i", $"color=c=black:s={outW}x{outH}:r={fps}:d={Ff(duration)}"
        };
        foreach (var input in inputs)
        {
            if (input.Loop)
            {
                command.Add("-loop");
                command.Add("1");
                command.Add("-framerate");
                command.Add(fps.ToString(CultureInfo.InvariantCulture));
            }
            command.Add("-i");
            command.Add(input.Path);
        }
        command.AddRange([
            "-filter_complex_script", filterFile,
            "-map", "[vout]",
            "-map", "[aout]",
            "-t", Ff(duration),
            "-r", fps.ToString(CultureInfo.InvariantCulture),
            "-c:v", "libx264",
            "-preset", preset,
            "-crf", crf,
            "-pix_fmt", "yuv420p",
            "-c:a", "aac",
            "-b:a", audioBitrate,
            "-movflags", "+faststart",
            "-progress", progressFile,
            "-nostats",
            output
        ]);

        return new ExportPlan
        {
            Ffmpeg = ffmpeg,
            Arguments = command,
            Output = output,
            ProgressFile = progressFile,
            JobDirectory = jobDir,
            Duration = duration
        };
    }

    public static double ReadProgress(string progressFile, double duration)
    {
        if (!File.Exists(progressFile) || duration <= 0) return 0;
        string tail;
        try
        {
            using var stream = new FileStream(progressFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length > 8192) stream.Seek(-8192, SeekOrigin.End);
            using var reader = new StreamReader(stream);
            tail = reader.ReadToEnd();
        }
        catch (IOException)
        {
            return 0;
        }
        if (tail.Contains("progress=end", StringComparison.Ordinal)) return 100;
        double seconds = 0;
        var found = false;
        foreach (var line in tail.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("out_time=", StringComparison.Ordinal)) continue;
            var stamp = trimmed["out_time=".Length..];
            var parts = stamp.Split(':');
            if (parts.Length != 3) continue;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours)) continue;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)) continue;
            if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var secs)) continue;
            seconds = hours * 3600 + minutes * 60 + secs;
            found = true;
        }
        if (!found) return 0;
        return Math.Max(0, Math.Min(99, seconds / duration * 100));
    }

    static List<string> VisualFilters(ColorFilters filters)
    {
        var brightness = filters.Brightness / 100;
        var contrast = (filters.Contrast + 100) / 100;
        var saturation = (filters.Saturation + 100) / 100;
        var gray = filters.Grayscale / 100;
        saturation *= 1 - gray;
        var parts = new List<string>();
        if (Math.Abs(brightness) > 0.001 || Math.Abs(contrast - 1) > 0.001 || Math.Abs(saturation - 1) > 0.001)
            parts.Add($"eq=brightness={Ff(brightness)}:contrast={Ff(contrast)}:saturation={Ff(saturation)}");
        if (filters.Blur > 0.05)
            parts.Add("gblur=sigma=" + Ff(Math.Min(20, filters.Blur)));
        return parts;
    }

    static string CropFilter(CropRect crop)
    {
        var left = crop.Left / 100;
        var right = crop.Right / 100;
        var top = crop.Top / 100;
        var bottom = crop.Bottom / 100;
        if (left + right + top + bottom < 0.001) return "";
        var cw = Math.Max(0.1, 1 - left - right);
        var ch = Math.Max(0.1, 1 - top - bottom);
        return "crop='trunc(iw*" + Ff(cw) + "/2)*2':'trunc(ih*" + Ff(ch) + "/2)*2':'iw*" + Ff(left) + "':'ih*" + Ff(top) + "'";
    }

    static string RotationFilter(double degrees)
    {
        var deg = (int)Math.Round(degrees) % 360;
        if (deg < 0) deg += 360;
        return deg switch
        {
            0 => "",
            90 => "transpose=1",
            180 => "hflip,vflip",
            270 => "transpose=2",
            _ => "format=rgba,rotate=" + Ff(degrees) + "*PI/180:c=none:ow=rotw(" + Ff(degrees) + "*PI/180):oh=roth(" + Ff(degrees) + "*PI/180)"
        };
    }

    static string AtempoChain(double speed)
    {
        var parts = new List<string>();
        var guard = 0;
        while (speed > 2 && guard < 6)
        {
            parts.Add("atempo=2");
            speed /= 2;
            guard++;
        }
        while (speed < 0.5 && guard < 6)
        {
            parts.Add("atempo=0.5");
            speed /= 0.5;
            guard++;
        }
        parts.Add("atempo=" + Ff(speed));
        return string.Join(',', parts);
    }

    static string? ResolveFont(string family, bool bold, bool italic)
    {
        var fonts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
        var catalog = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Arial"] = ["arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf"],
            ["Verdana"] = ["verdana.ttf", "verdanab.ttf", "verdanai.ttf", "verdanaz.ttf"],
            ["Georgia"] = ["georgia.ttf", "georgiab.ttf", "georgiai.ttf", "georgiaz.ttf"],
            ["Times New Roman"] = ["times.ttf", "timesbd.ttf", "timesi.ttf", "timesbi.ttf"],
            ["Courier New"] = ["cour.ttf", "courbd.ttf", "couri.ttf", "courbi.ttf"],
            ["Microsoft YaHei"] = ["msyh.ttc", "msyhbd.ttc", "msyh.ttc", "msyhbd.ttc"],
            ["SimHei"] = ["simhei.ttf", "simhei.ttf", "simhei.ttf", "simhei.ttf"],
            ["SimSun"] = ["simsun.ttc", "simsun.ttc", "simsun.ttc", "simsun.ttc"]
        };
        if (!catalog.TryGetValue(family, out var files)) files = catalog["Arial"];
        var key = bold && italic ? 3 : bold ? 1 : italic ? 2 : 0;
        foreach (var name in new[] { files[key], files[0], "arial.ttf", "segoeui.ttf" })
        {
            var path = Path.Combine(fonts, name);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    static string Between(double start, double end) => $"enable='between(t\\,{Ff(start)}\\,{Ff(end)})'";

    static string EscapePath(string path)
    {
        path = path.Replace('\\', '/').Replace(":", "\\:").Replace("'", "\\'");
        return path;
    }

    static string Ff(double number)
    {
        if (!double.IsFinite(number)) return "0";
        var formatted = number.ToString("0.0000", CultureInfo.InvariantCulture);
        if (formatted.Contains('.')) formatted = formatted.TrimEnd('0').TrimEnd('.');
        return formatted is "" or "-" or "-0" ? "0" : formatted;
    }

    static int Even(int value) => value - (value % 2);
}
