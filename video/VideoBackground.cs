using Godot;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Trifle.Export;
using Trifle.Visuals;

namespace Trifle.Video;

// A bounded decoder publishes textures to the same HDR layer used by images.
public partial class VideoBackground : Node
{
    public event Action StatusChanged;
    public event Action<Texture2D> FrameChanged;
    public bool Available => _metadata != null && _source != null && Error.Length == 0;
    public string Error { get; private set; } = "";
    public string Status { get; private set; } = "未选择视频";
    public long PresentedFrame { get; private set; } = -1;
    public double PresentedTime { get; private set; }
    public double TimelineTime => _time;
    public int RestartCount { get; private set; }
    public int QueuedFrames => _source?.QueuedFrames ?? 0;
    public bool Loading => _starting || _pending;
    public VideoMetadata Metadata => _metadata;
    private BackgroundSettings _settings = new();
    private string _ffmpeg = "ffmpeg", _metadataPath = "", _metadataTool = "";
    private VideoMetadata _metadata;
    private VideoFrameSource _source;
    private CancellationTokenSource _loadingCancellation;
    private Task _loadingTask = Task.CompletedTask;
    private Image _image;
    private ImageTexture _texture;
    private VideoFrameSource _exportSource;
    private long _exportIndex = -1;
    private double _exportOrigin;
    private double _time, _originSourceTime;
    private long _restartAt;
    private int _version;
    private bool _pending, _starting, _suspended, _exiting, _failed;

    public void Configure(BackgroundSettings settings, string ffmpeg, double time, bool reload = false)
    {
        settings.Validate();
        time = Math.Max(0, time);
        bool changed = settings.Type != _settings.Type || settings.Video != _settings.Video ||
            settings.Fit != _settings.Fit || ffmpeg != _ffmpeg || reload;
        _time = time;
        _settings = settings;
        _ffmpeg = ffmpeg;
        if (!changed) return;
        if (reload) _metadataPath = "";
        RequestRestart(immediate: true);
    }

    public void SetTime(double seconds, bool force = false)
    {
        if (!double.IsFinite(seconds)) return;
        bool jump = seconds < _time - 0.001 || seconds > _time + 0.5;
        _time = Math.Max(0, seconds);
        if ((jump || force) && _settings.Type == BackgroundType.Video)
            RequestRestart(immediate: false);
    }

    private void RequestRestart(bool immediate)
    {
        _version++;
        _loadingCancellation?.Cancel();
        _pending = true;
        _failed = false;
        Error = "";
        _restartAt = Stopwatch.GetTimestamp() + (immediate ? 0 : Stopwatch.Frequency / 8);
        PresentedFrame = -1;
        FrameChanged?.Invoke(null);
        SetStatus(_settings.Video.Path.Length == 0 ? "未选择视频" : "正在载入视频…");
    }

    public override void _Process(double delta)
    {
        if (_exiting || _suspended) return;
        if (_pending && !_starting && Stopwatch.GetTimestamp() >= _restartAt)
            _loadingTask = RestartAsync();
        if (_settings.Type != BackgroundType.Video || _source == null || _pending) return;
        if (_failed) return;
        if (_source.Error.Length > 0 && !_failed)
        {
            _failed = true;
            Error = "背景视频解码失败：" + _settings.Video.Path + "\n" + _source.Error;
            FrameChanged?.Invoke(null);
            SetStatus("解码失败，请检查视频和 FFmpeg 路径");
            return;
        }
        double sourceTime = _time - _settings.Video.OffsetSeconds;
        if (sourceTime < 0) { FrameChanged?.Invoke(null); return; }
        VideoFrame latest = null;
        while (_source.TryPeek(out var next) && _originSourceTime + next.TimeSeconds <= sourceTime + 0.00001)
        {
            if (!_source.TryRead(out var frame)) break;
            latest?.Dispose();
            latest = frame;
        }
        if (latest == null) return;
        using (latest)
        {
            Publish(latest, _source.Spec);
            PresentedFrame = latest.Index;
            PresentedTime = _originSourceTime + latest.TimeSeconds + _settings.Video.OffsetSeconds;
        }
    }

    private void Publish(VideoFrame frame, VideoPreviewSpec spec)
    {
        if (_texture == null)
        {
            _image = Image.CreateFromData(spec.Width, spec.Height, false, Image.Format.Rgba8, frame.Pixels);
            _texture = ImageTexture.CreateFromImage(_image);
        }
        else
        {
            _image.SetData(spec.Width, spec.Height, false, Image.Format.Rgba8, frame.Pixels);
            _texture.Update(_image);
        }
        FrameChanged?.Invoke(_texture);
    }

    public async Task BeginExportAsync(ExportSettings settings, string ffmpeg, CancellationToken token)
    {
        await SuspendAsync();
        ReleaseTexture();
        _exportIndex = -1;
        // An empty video selection uses the same fill as an empty image selection.
        if (_settings.Video.Path.Length == 0) return;
        var metadata = await VideoMetadata.ProbeAsync(ffmpeg, _settings.Video.Path, token);
        var spec = new VideoPreviewSpec(settings.Height, settings.FramesPerSecond, settings.Width);
        _exportOrigin = Math.Max(0, settings.StartSeconds - _settings.Video.OffsetSeconds);
        _exportSource = new VideoFrameSource(ffmpeg, _settings.Video.Path, spec, _settings.Video.Loop,
            VideoFilters.SeekTime(_exportOrigin, _settings.Video, metadata),
            VideoFilters.Build(_settings, spec.Width, spec.Height, spec.FramesPerSecond), holdLastFrame: true);
    }

    public async Task PresentExportFrameAsync(double seconds, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        double sourceTime = seconds - _settings.Video.OffsetSeconds;
        if (_exportSource == null || sourceTime < 0) { FrameChanged?.Invoke(null); return; }
        long target = (long)Math.Floor((sourceTime - _exportOrigin) * _exportSource.Spec.FramesPerSecond + 1e-8);
        while (_exportIndex < target)
        {
            using var frame = await _exportSource.ReadAsync(token);
            _exportIndex = frame.Index;
            if (_exportIndex == target) Publish(frame, _exportSource.Spec);
        }
    }

    public async Task EndExportAsync()
    {
        var source = _exportSource;
        _exportSource = null;
        if (source != null) await source.DisposeAsync();
        ReleaseTexture();
    }

    private async Task RestartAsync()
    {
        _starting = true;
        _pending = false;
        int version = _version;
        _loadingCancellation = new CancellationTokenSource();
        var cancellation = _loadingCancellation;
        var previous = _source;
        _source = null;
        try
        {
            if (previous != null) await previous.DisposeAsync();
            if (version != _version || _exiting || _suspended) return;
            ReleaseTexture();
            if (_settings.Type != BackgroundType.Video || _settings.Video.Path.Length == 0)
            { _metadata = null; SetStatus("未选择视频"); return; }
            var video = _settings.Video;
            if (_metadata == null || _metadataPath != video.Path || _metadataTool != _ffmpeg)
            {
                var metadata = await VideoMetadata.ProbeAsync(_ffmpeg, video.Path, cancellation.Token);
                if (version != _version || _exiting || _suspended) return;
                _metadata = metadata;
                _metadataPath = video.Path;
                _metadataTool = _ffmpeg;
            }
            cancellation.Token.ThrowIfCancellationRequested();
            _originSourceTime = Math.Max(0, _time - video.OffsetSeconds);
            var spec = video.PreviewSpec;
            _source = new VideoFrameSource(_ffmpeg, video.Path, spec, video.Loop,
                VideoFilters.SeekTime(_originSourceTime, video, _metadata),
                VideoFilters.Build(_settings, spec.Width, spec.Height, spec.FramesPerSecond), holdLastFrame: true);
            RestartCount++;
            SetStatus($"{spec.Height}p · {spec.FramesPerSecond} FPS · {_metadata.DurationSeconds:F1} 秒");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (version == _version && !_exiting && !_suspended)
            {
                _failed = true;
                _metadata = null;
                Error = exception.Message;
                SetStatus("视频不可用，请重新选择或检查 FFmpeg 路径");
            }
        }
        finally
        {
            if (_loadingCancellation == cancellation) _loadingCancellation = null;
            cancellation.Dispose();
            _starting = false;
            if (!_exiting && !_suspended) StatusChanged?.Invoke();
        }
    }

    private void SetStatus(string status)
    {
        if (_exiting) return;
        Status = status;
        StatusChanged?.Invoke();
    }

    public async Task SuspendAsync()
    {
        _suspended = true;
        _version++;
        _pending = false;
        _loadingCancellation?.Cancel();
        await _loadingTask;
        var previous = _source;
        _source = null;
        if (previous != null) await previous.DisposeAsync();
    }

    public void Resume(double time)
    {
        _suspended = false;
        _time = time;
        RequestRestart(immediate: true);
    }

    private void ReleaseTexture()
    {
        FrameChanged?.Invoke(null);
        _image?.Dispose(); _image = null;
        _texture?.Dispose(); _texture = null;
    }

    public override void _ExitTree()
    {
        _exiting = true;
        _loadingCancellation?.Cancel();
        _source?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        FrameChanged?.Invoke(null);
        _image?.Dispose(); _texture?.Dispose();
        if (_exportSource != null) _ = _exportSource.DisposeAsync();
    }
}
