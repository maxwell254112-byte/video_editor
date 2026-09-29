using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VideoEditor;

sealed class PreviewAudio : IDisposable
{
    readonly List<IDisposable> _owned = new();
    WaveOutEvent? _output;
    bool _muted;

    public bool Muted
    {
        get => _muted;
        set => _muted = value;
    }

    public void Play(EditorProject project, MediaStore media, double playhead)
    {
        Stop();
        if (_muted) return;
        var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2))
        {
            ReadFully = true
        };
        var inputs = 0;
        foreach (var clip in project.Clips)
        {
            if (project.MuteOriginal || clip.Muted || clip.Volume <= 0.001 || Math.Abs(clip.Speed - 1) > 0.02) continue;
            var asset = media.Find(clip.MediaId);
            if (asset is null || !asset.HasAudio || !File.Exists(asset.FullPath)) continue;
            var span = ClipTiming.Video(clip);
            if (!TryAdd(mixer, asset.FullPath, playhead, span.Start, span.End, clip.InPoint, clip.Volume)) continue;
            inputs++;
        }
        foreach (var clip in project.Audios)
        {
            if (clip.Volume <= 0.001) continue;
            var asset = media.Find(clip.MediaId);
            if (asset is null || !File.Exists(asset.FullPath)) continue;
            var span = ClipTiming.Audio(clip);
            if (!TryAdd(mixer, asset.FullPath, playhead, span.Start, span.End, clip.InPoint, clip.Volume)) continue;
            inputs++;
        }
        if (inputs == 0) return;
        _output = new WaveOutEvent();
        _owned.Add(_output);
        _output.Init(mixer);
        _output.Play();
    }

    public void Stop()
    {
        try { _output?.Stop(); } catch (Exception) { /* device already closed */ }
        foreach (var item in _owned)
        {
            try { item.Dispose(); } catch (Exception) { /* ignore */ }
        }
        _owned.Clear();
        _output = null;
    }

    public void Dispose() => Stop();

    bool TryAdd(MixingSampleProvider mixer, string path, double playhead, double start, double end, double inPoint, double volume)
    {
        if (playhead >= end - 0.02) return false;
        var delay = Math.Max(0, start - playhead);
        var into = Math.Max(0, playhead - start);
        var take = end - Math.Max(playhead, start);
        if (take < 0.05) return false;
        try
        {
            var reader = new AudioFileReader(path) { Volume = (float)Math.Clamp(volume, 0, 1) };
            _owned.Add(reader);
            reader.CurrentTime = TimeSpan.FromSeconds(Math.Max(0, inPoint + into));
            ISampleProvider sample = reader.ToSampleProvider();
            if (sample.WaveFormat.Channels == 1) sample = new MonoToStereoSampleProvider(sample);
            if (sample.WaveFormat.SampleRate != 48000) sample = new WdlResamplingSampleProvider(sample, 48000);
            var offset = new OffsetSampleProvider(sample)
            {
                DelayBy = TimeSpan.FromSeconds(delay),
                Take = TimeSpan.FromSeconds(take)
            };
            mixer.AddMixerInput(offset);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
