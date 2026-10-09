using Godot;
using System;
using System.IO;
using System.Threading.Tasks;
using Trifle.Export;
using Trifle.Midi;
using Trifle.Audio;

namespace Trifle.App;

public partial class ExportDialog : Window
{
    public event Action<ExportSettings, string> ExportRequested;
    public event Action CancelRequested;
    public event Action FfmpegPathChanged;
    public string FfmpegPath => _ffmpeg.Text.Trim();

    public void SetFfmpegPath(string path)
    {
        _ffmpeg.Text = path;
        FfmpegPathChanged?.Invoke();
    }
    private LineEdit _path;
    private LineEdit _ffmpeg;
    private Button _ffmpegBrowse;
    private RichTextLabel _ffmpegHint;
    private FileDialog _ffmpegDialog;
    private int _ffmpegDetection;
    private SpinBox _start;
    private SpinBox _end;
    private Button _begin;
    private Button _cancel;
    private Button _browse;
    private Label _message;
    private ProgressBar _progress;
    private FileDialog _save;
    private ConfirmationDialog _overwrite;
    private bool _busy;
    private bool _fullDuration = true;
    private bool _syncingRange;
    private ExportSettings _pending;
    private AudioSettings _audio = new();
    private string _contents = "MIDI 音符 · 键盘 · 纯色背景";
    private string _assetWarning = "";
    private string _assetDetails = "";
    private OptionButton _size, _fps;
    private OptionButton _encoder, _preset, _audioQuality;
    private SpinBox _crf;
    private static readonly int[] Heights = { 1080, 1440, 2160 };
    private static readonly int[] Rates = { 24, 30, 60 };
    private static readonly string[] Encoders = { "h264", "h265" };
    private static readonly string[] Presets = { "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow" };
    private static readonly int[] AudioBitrates = { 128, 192, 256, 320 };

    public override void _Ready()
    {
        AppLocale.BindTitle(this, "导出视频");
        const string ui = "Margin/Content/";
        _path = GetNode<LineEdit>(ui + "Output/Path");
        _ffmpeg = GetNode<LineEdit>(ui + "Ffmpeg/Path");
        _start = GetNode<SpinBox>(ui + "Range/Start");
        _end = GetNode<SpinBox>(ui + "Range/End");
        _end.ValueChanged += value =>
        {
            if (!_syncingRange) _fullDuration = Math.Abs(value - _end.MaxValue) < 0.000001;
        };
        _begin = GetNode<Button>(ui + "Actions/Begin");
        _cancel = GetNode<Button>(ui + "Actions/Cancel");
        _browse = GetNode<Button>(ui + "Output/Browse");
        _message = GetNode<Label>(ui + "Message");
        _progress = GetNode<ProgressBar>(ui + "Progress");
        _save = GetNode<FileDialog>("SaveDialog");
        AppLocale.BindTitle(_save, "选择视频输出文件");
        _overwrite = GetNode<ConfirmationDialog>("Overwrite");
        _overwrite.Title = AppLocale.T("确认覆盖");
        _size = GetNode<OptionButton>(ui + "Format/Size");
        _fps = GetNode<OptionButton>(ui + "Format/Fps");
        _size.AddItem("1080p · 1920 × 1080"); _size.AddItem("1440p · 2560 × 1440"); _size.AddItem("4K · 3840 × 2160");
        foreach (int rate in Rates) _fps.AddItem($"{rate} FPS");
        _fps.Select(1);
        _encoder = GetNode<OptionButton>(ui + "Encoding/Encoder");
        _crf = GetNode<SpinBox>(ui + "Encoding/Crf");
        _preset = GetNode<OptionButton>(ui + "Encoding/Preset");
        _audioQuality = GetNode<OptionButton>(ui + "Encoding/AudioQuality");
        _encoder.AddItem("H.264"); _encoder.AddItem("H.265");
        foreach (string preset in Presets) _preset.AddItem(preset);
        foreach (int bitrate in AudioBitrates) _audioQuality.AddItem($"AAC · {bitrate} kbps");
        ApplyEncoding(new EncodingSettings());
        _ffmpegBrowse = GetNode<Button>(ui + "Ffmpeg/Browse");
        _ffmpegHint = GetNode<RichTextLabel>(ui + "FfmpegHint");
        _ffmpegDialog = GetNode<FileDialog>("FfmpegDialog");
        AppLocale.BindTitle(_ffmpegDialog, "选择 FFmpeg 可执行文件");
        _ffmpegHint.MetaClicked += meta => OS.ShellOpen(meta.AsString());
        _ffmpeg.TextSubmitted += _ => FfmpegEdited();
        _ffmpeg.FocusExited += FfmpegEdited;
        _ffmpegBrowse.Pressed += () =>
        {
            string text = _ffmpeg.Text.Trim();
            if (File.Exists(text)) _ffmpegDialog.CurrentDir = Path.GetDirectoryName(text);
            _ffmpegDialog.PopupCenteredRatio(0.7f);
        };
        _ffmpegDialog.FileSelected += path => { _ffmpeg.Text = path; FfmpegEdited(); };
        _browse.Pressed += () =>
        {
            string parent = Path.GetDirectoryName(_path.Text);
            if (Directory.Exists(parent)) _save.CurrentDir = parent;
            _save.CurrentFile = Path.GetFileName(_path.Text);
            _save.PopupCenteredRatio(0.7f);
        };
        _save.FileSelected += path => _path.Text = path;
        _begin.Pressed += RequestExport;
        _cancel.Pressed += () => CancelRequested?.Invoke();
        GetNode<Button>(ui + "Actions/Close").Pressed += TryClose;
        CloseRequested += TryClose;
        _overwrite.Confirmed += () => ExportRequested?.Invoke(_pending with { Overwrite = true }, _ffmpeg.Text.Trim());
    }

    public void ResetForSong(MidiSong song)
    {
        _fullDuration = true;
        SetDuration(song.DurationSeconds);
        _start.Value = 0;
        _end.Value = song.DurationSeconds;
        string directory = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyVideos);
        if (!Directory.Exists(directory)) directory = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        _path.Text = Path.Combine(directory, Path.GetFileNameWithoutExtension(song.SourcePath) + ".mp4");
    }

    public void SetDuration(double seconds)
    {
        _syncingRange = true;
        _start.MaxValue = _end.MaxValue = seconds;
        if (_fullDuration) _end.Value = seconds;
        if (_start.Value >= _end.Value) _start.Value = 0;
        _syncingRange = false;
    }

    public ExportPreferences GetPreferences() => new()
    {
        OutputPath = _path.Text.Trim(), StartSeconds = _start.Value,
        EndSeconds = _fullDuration ? null : _end.Value, FfmpegPath = _ffmpeg.Text.Trim(),
        FollowTimelineEnd = _fullDuration,
        Height = Heights[_size.Selected], Width = Heights[_size.Selected] * 16 / 9, FramesPerSecond = Rates[_fps.Selected],
        Encoding = GetEncoding()
    };

    public void ApplyPreferences(ExportPreferences preferences, double midiDuration)
    {
        // Older files saved a numeric MIDI end even for the default full export.
        _fullDuration = preferences.FollowTimelineEnd ?? (!preferences.EndSeconds.HasValue ||
            Math.Abs(preferences.EndSeconds.Value - midiDuration) < 0.000001 ||
            Math.Abs(preferences.EndSeconds.Value - _end.MaxValue) < 0.000001);
        _syncingRange = true;
        if (preferences.OutputPath.Length > 0) _path.Text = preferences.OutputPath;
        _start.Value = preferences.StartSeconds;
        _end.Value = _fullDuration ? _end.MaxValue : preferences.EndSeconds.Value;
        _syncingRange = false;
        _ffmpeg.Text = preferences.FfmpegPath;
        _size.Select(Array.IndexOf(Heights, preferences.Height));
        _fps.Select(Array.IndexOf(Rates, preferences.FramesPerSecond));
        ApplyEncoding(preferences.Encoding);
        FfmpegPathChanged?.Invoke();
    }

    private EncodingSettings GetEncoding() => new()
    {
        Encoder = Encoders[_encoder.Selected], Crf = (int)_crf.Value,
        Preset = Presets[_preset.Selected], AudioBitrateKbps = AudioBitrates[_audioQuality.Selected]
    };

    private void ApplyEncoding(EncodingSettings encoding)
    {
        _encoder.Select(Array.IndexOf(Encoders, encoding.Encoder));
        _crf.Value = encoding.Crf;
        _preset.Select(Array.IndexOf(Presets, encoding.Preset));
        _audioQuality.Select(Array.IndexOf(AudioBitrates, encoding.AudioBitrateKbps));
    }

    public void Open()
    {
        string sound = _audio.Enabled && _audio.Path.Length > 0
            ? string.Format(AppLocale.T("音频：{0}，偏移 {1} 秒（AAC）"), Path.GetFileName(_audio.Path), _audio.OffsetSeconds.ToString("+0.###;-0.###;0"))
            : AppLocale.T("无音频");
        _message.Text = string.Format(AppLocale.T("包含内容：{0}"), _contents) + "\n" + sound +
            (_assetWarning.Length > 0 ? "\n" + string.Format(AppLocale.T("注意：{0}"), _assetWarning) : "");
        _message.TooltipText = _audio.Path + (_assetDetails.Length > 0 ? "\n" + _assetDetails : "");
        _progress.Value = 0;
        PopupCentered();
        if (!_busy) _ = RunDetectionAsync(fill: true);
    }

    // Re-checks the current text after every edit; on failure the download hint shows and on
    // success the tooltip reports the resolved location and version.
    private void FfmpegEdited()
    {
        FfmpegPathChanged?.Invoke();
        if (!_busy) _ = RunDetectionAsync(fill: false);
    }

    private async Task RunDetectionAsync(bool fill)
    {
        int generation = ++_ffmpegDetection;
        string requested = _ffmpeg.Text.Trim();
        var probe = await FfmpegLocator.DetectAsync(requested);
        if (generation != _ffmpegDetection || !IsInsideTree()) return;
        // Filling only happens for a probe of the untouched text, so typing is never clobbered.
        if (probe.Found && fill && !string.Equals(probe.Path, requested, StringComparison.OrdinalIgnoreCase))
        {
            _ffmpeg.Text = probe.Path;
            FfmpegPathChanged?.Invoke();
        }
        _ffmpeg.TooltipText = probe.Found
            ? AppLocale.T("已找到：") + probe.Resolved + (probe.Version.Length > 0 ? "\n" + probe.Version : "")
            : AppLocale.T("未找到可用的 FFmpeg，请见下方提示。");
        _ffmpegHint.Visible = !probe.Found;
    }

    public void SetAudio(AudioSettings settings)
    {
        _audio = settings;
        if (_audioQuality != null) _audioQuality.Disabled = _busy || !_audio.Enabled || _audio.Path.Length == 0;
    }

    public void SetContents(string contents, string warning, string details = "")
    {
        _contents = contents;
        _assetWarning = warning;
        _assetDetails = details;
    }

    public override void _Input(InputEvent input)
    {
        if (Visible && !_save.Visible && !_overwrite.Visible && !_ffmpegDialog.Visible &&
            input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            TryClose();
            SetInputAsHandled();
        }
    }

    private void RequestExport()
    {
        try
        {
            int height = Heights[_size.Selected];
            var settings = new ExportSettings(_path.Text.Trim(), _start.Value, _end.Value, height * 16 / 9, height, Rates[_fps.Selected],
                Encoding: GetEncoding());
            // Validate the configuration before showing the concrete overwrite question.
            (settings with { Overwrite = true }).Validate();
            if (File.Exists(settings.OutputPath))
            {
                _pending = settings;
                _overwrite.DialogText = string.Format(AppLocale.T("覆盖这个视频文件？\n{0}\n导出成功后才替换原文件。"), settings.OutputPath);
                _overwrite.PopupCentered();
            }
            else ExportRequested?.Invoke(settings, _ffmpeg.Text.Trim());
        }
        catch (Exception error) { Finish(string.Format(AppLocale.T("设置无效：{0}"), error.Message)); }
    }

    public void Begin(ExportSettings settings, string ffmpeg)
    {
        _path.Text = settings.OutputPath;
        _start.Value = settings.StartSeconds;
        _end.Value = settings.EndSeconds;
        _ffmpeg.Text = ffmpeg;
        _size.Select(Array.IndexOf(Heights, settings.Height));
        _fps.Select(Array.IndexOf(Rates, settings.FramesPerSecond));
        ApplyEncoding(settings.EffectiveEncoding);
        SetBusy(true);
        _message.Text = "正在检查 FFmpeg…";
        _progress.Value = 0;
        PopupCentered();
    }

    public void Report(ExportProgress progress)
    {
        double fraction = progress.TotalFrames == 0 ? 0 : progress.Frames / (double)progress.TotalFrames;
        _progress.Value = progress.Stage switch
        {
            "rendering" => fraction * 70,
            "encoding" => 70 + fraction * 30,
            "completed" => 100,
            _ => 0
        };
        _message.Text = progress.Stage switch
        {
            "rendering" => string.Format(AppLocale.T("渲染帧：{0} / {1}"), progress.Frames, progress.TotalFrames),
            "encoding" => string.Format(AppLocale.T("视频编码：{0} / {1}"), progress.Frames, progress.TotalFrames),
            "completed" => "导出完成。",
            _ => "正在检查 FFmpeg…"
        };
    }

    public void Finish(string message) { SetBusy(false); _message.Text = message; }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _path.Editable = _ffmpeg.Editable = _start.Editable = _end.Editable = !busy;
        _begin.Disabled = _browse.Disabled = _ffmpegBrowse.Disabled = busy;
        _size.Disabled = _fps.Disabled = busy;
        _encoder.Disabled = _preset.Disabled = busy;
        _crf.Editable = !busy;
        _audioQuality.Disabled = busy || !_audio.Enabled || _audio.Path.Length == 0;
        GetNode<Button>("Margin/Content/Actions/Close").Disabled = busy;
        _cancel.Disabled = !busy;
    }

    private void TryClose() { if (!_busy) Hide(); }
}
