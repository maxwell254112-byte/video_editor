using System.Diagnostics;
using System.Text;

namespace VideoEditor;

sealed class ExportDialog : Form
{
    readonly ExportPlan _plan;
    readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 100, Value = 0, Dock = DockStyle.Top, Height = 22 };
    readonly Label _status = new() { Dock = DockStyle.Top, Height = 48, ForeColor = Theme.Text, Text = "Preparing export…" };
    readonly Label _elapsed = new() { Dock = DockStyle.Top, Height = 24, ForeColor = Theme.Muted };
    readonly Button _close = Theme.Button("Close");
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 400 };
    readonly StringBuilder _error = new();
    Process? _process;
    DateTime _started;
    bool _finished;

    public ExportDialog(ExportPlan plan)
    {
        _plan = plan;
        Text = "Export MP4";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(460, 180);
        BackColor = Theme.Panel;
        Font = Theme.UiFont;
        Padding = new Padding(16);
        _close.Dock = DockStyle.Bottom;
        _close.Enabled = false;
        _close.Click += (_, _) => Close();
        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 8), BackColor = Theme.Panel };
        host.Controls.Add(_elapsed);
        host.Controls.Add(_bar);
        host.Controls.Add(_status);
        Controls.Add(host);
        Controls.Add(_close);
        _timer.Tick += (_, _) => Poll();
        Shown += (_, _) => StartExport();
        FormClosing += (_, e) =>
        {
            if (_finished) return;
            if (MessageBox.Show(this, "Cancel this export?", "Video Editor", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            StopProcess();
        };
    }

    void StartExport()
    {
        var info = new ProcessStartInfo
        {
            FileName = _plan.Ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        foreach (var arg in _plan.Arguments) info.ArgumentList.Add(arg);
        _process = new Process { StartInfo = info, EnableRaisingEvents = true };
        _process.ErrorDataReceived += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(args.Data) || _error.Length > 6000) return;
            _error.AppendLine(args.Data);
        };
        _process.Exited += (_, _) => BeginInvoke(Finish);
        try
        {
            if (!_process.Start()) throw new InvalidOperationException("Export failed.");
            _process.BeginErrorReadLine();
            _started = DateTime.UtcNow;
            _status.Text = "Exporting…";
            _timer.Start();
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            _finished = true;
            _close.Enabled = true;
        }
    }

    void Poll()
    {
        var progress = (int)Math.Round(ExportCompiler.ReadProgress(_plan.ProgressFile, _plan.Duration));
        _bar.Value = Math.Clamp(progress, 0, 100);
        _status.Text = $"Exporting… {_bar.Value}%";
        _elapsed.Text = "Elapsed " + (DateTime.UtcNow - _started).ToString(@"mm\:ss");
    }

    void Finish()
    {
        if (_finished) return;
        _finished = true;
        _timer.Stop();
        var code = _process?.ExitCode ?? -1;
        var exists = File.Exists(_plan.Output) && new FileInfo(_plan.Output).Length > 0;
        if (code == 0 && exists)
        {
            _bar.Value = 100;
            _status.Text = "Export complete.";
            DialogResult = DialogResult.OK;
        }
        else
        {
            _status.Text = "Export failed.";
            _elapsed.Text = _error.Length == 0 ? "FFmpeg did not produce a file." : _error.ToString().Trim();
            DialogResult = DialogResult.Abort;
        }
        _close.Enabled = true;
    }

    void StopProcess()
    {
        _timer.Stop();
        try
        {
            if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true);
        }
        catch (Exception) { /* already exited */ }
        _finished = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _process?.Dispose();
        }
        base.Dispose(disposing);
    }
}
