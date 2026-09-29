namespace VideoEditor;

sealed class FramePump : IDisposable
{
    readonly Control _host;
    readonly object _gate = new();
    readonly Dictionary<string, Bitmap> _stills = new();
    readonly Dictionary<string, Bitmap> _frames = new();
    readonly Dictionary<string, double> _frameTime = new();
    readonly Dictionary<string, double> _wanted = new();
    readonly Dictionary<string, string> _paths = new();
    bool _running;
    bool _disposed;

    public FramePump(Control host) => _host = host;

    public Bitmap? Still(MediaAsset asset)
    {
        lock (_gate)
        {
            if (_stills.TryGetValue(asset.Id, out var cached)) return cached;
        }
        Bitmap? created = LoadStill(asset.FullPath);
        if (created is null) return null;
        lock (_gate)
        {
            if (_stills.TryGetValue(asset.Id, out var cached))
            {
                created.Dispose();
                return cached;
            }
            _stills[asset.Id] = created;
            return created;
        }
    }

    public Bitmap? Frame(MediaAsset asset, double seconds)
    {
        var quantized = Math.Round(Math.Max(0, seconds) * 5) / 5.0;
        lock (_gate)
        {
            _paths[asset.Id] = asset.FullPath;
            _wanted[asset.Id] = quantized;
            if (_frames.TryGetValue(asset.Id, out var frame) &&
                _frameTime.TryGetValue(asset.Id, out var have) &&
                Math.Abs(have - quantized) < 0.001)
            {
                return frame;
            }
        }
        Kick();
        lock (_gate)
        {
            return _frames.TryGetValue(asset.Id, out var frame) ? frame : null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _wanted.Clear();
            foreach (var bitmap in _stills.Values) bitmap.Dispose();
            foreach (var bitmap in _frames.Values) bitmap.Dispose();
            _stills.Clear();
            _frames.Clear();
        }
    }

    void Kick()
    {
        lock (_gate)
        {
            if (_running || _disposed) return;
            _running = true;
        }
        Task.Run(Loop);
    }

    void Loop()
    {
        while (true)
        {
            string? id = null;
            string? path = null;
            var time = 0.0;
            lock (_gate)
            {
                if (_disposed)
                {
                    _running = false;
                    return;
                }
                foreach (var pair in _wanted)
                {
                    if (_frameTime.TryGetValue(pair.Key, out var have) && Math.Abs(have - pair.Value) < 0.001)
                        continue;
                    if (!_paths.TryGetValue(pair.Key, out path)) continue;
                    id = pair.Key;
                    time = pair.Value;
                    break;
                }
                if (id is null)
                {
                    _running = false;
                    return;
                }
            }

            var bitmap = FfmpegTools.GrabFrame(path!, time);
            lock (_gate)
            {
                _frameTime[id] = time;
                if (bitmap is not null)
                {
                    if (_frames.Remove(id, out var previous)) previous.Dispose();
                    _frames[id] = bitmap;
                }
            }
            try { _host.BeginInvoke(new Action(() => _host.Invalidate())); } catch (InvalidOperationException) { }
        }
    }

    static Bitmap? LoadStill(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var image = Image.FromStream(stream, false, false);
            return new Bitmap(image);
        }
        catch (Exception)
        {
            return FfmpegTools.GrabFrame(path, 0);
        }
    }
}
