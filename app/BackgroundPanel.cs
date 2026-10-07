using Godot;
using System;
using Trifle.Visuals;
using Trifle.Video;

namespace Trifle.App;

public partial class BackgroundPanel : VBoxContainer
{
    public event Action<int> FitChanged;
    public event Action<int, Color> GradientChanged;
    public event Action<double, double, double> AppearanceChanged;
    public event Action<Color> ColorChanged;
    public event Action<VideoBackgroundSettings> VideoChanged;
    private OptionButton _gradient, _fit;
    private ColorPickerButton _color;
    private SpinBox _opacity, _brightness, _blur;
    private EffectSection _videoSection;
    private OptionButton _videoResolution, _videoFps;
    private CheckButton _loop;
    private SpinBox _offset;
    private Label _videoStatus;
    private static readonly int[] VideoHeights = { 480, 720, 1080 };
    private static readonly int[] VideoRates = { 15, 30 };
    private BackgroundSettings _settings = new();
    private bool _busy, _available, _syncing;

    public override void _Ready()
    {
        SectionHeading.Bind(GetNode<Button>("Header"), GetNode<Control>("Fields"), "背景");
        _gradient = GetNode<OptionButton>("Fields/Content/Gradient");
        _fit = GetNode<OptionButton>("Fields/Content/Fit");
        _color = GetNode<ColorPickerButton>("Fields/Content/Color/Value");
        _opacity = GetNode<SpinBox>("Fields/Content/Opacity/Value");
        _brightness = GetNode<SpinBox>("Fields/Content/Brightness/Value");
        _blur = GetNode<SpinBox>("Fields/Content/Blur/Value");
        _videoSection = GetNode<EffectSection>("Fields/Content/Video");
        const string video = "Fields/Content/Video/Fields/Content/";
        _videoResolution = GetNode<OptionButton>(video + "Resolution");
        _videoFps = GetNode<OptionButton>(video + "FrameRate");
        _loop = GetNode<CheckButton>(video + "Loop");
        _offset = GetNode<SpinBox>(video + "Offset/Value");
        _videoStatus = GetNode<Label>(video + "Status");
        foreach (int height in VideoHeights) _videoResolution.AddItem("视频预览：" + height + "p");
        foreach (int fps in VideoRates) _videoFps.AddItem("视频预览：" + fps + " FPS");
        void RequestVideo() { if (!_syncing && !_busy) VideoChanged?.Invoke(_settings.Video with
        {
            PreviewHeight = VideoHeights[_videoResolution.Selected], PreviewFramesPerSecond = VideoRates[_videoFps.Selected],
            Loop = _loop.ButtonPressed, OffsetSeconds = _offset.Value
        }); }
        _videoResolution.ItemSelected += _ => RequestVideo();
        _videoFps.ItemSelected += _ => RequestVideo();
        _loop.Toggled += _ => RequestVideo();
        _offset.ValueChanged += _ => RequestVideo();
        _gradient.AddItem("垂直渐变", (int)BackgroundGradient.Vertical);
        _gradient.AddItem("水平渐变", (int)BackgroundGradient.Horizontal);
        _fit.AddItem("完整显示 · 空白填背景色");
        _fit.AddItem("铺满裁剪 · 居中");
        _gradient.ItemSelected += v => { if (!_syncing && !_busy) GradientChanged?.Invoke(_gradient.GetItemId((int)v), new Color(_settings.EndColor)); };
        _fit.ItemSelected += v => { if (!_syncing && !_busy) FitChanged?.Invoke((int)v); };
        _color.ColorChanged += c => { if (!_syncing && !_busy) ColorChanged?.Invoke(c); };
        foreach (var number in new[] { _opacity, _brightness, _blur })
            number.ValueChanged += _ => { if (!_syncing && !_busy) AppearanceChanged?.Invoke(_opacity.Value / 100, _brightness.Value / 100, _blur.Value); };
        Refresh(_settings, false);
    }

    public void Refresh(BackgroundSettings settings, bool available, string videoStatus = "未选择视频", string videoError = "")
    {
        _syncing = true; _settings = settings; _available = available;
        _gradient.Select(settings.Gradient == BackgroundGradient.Horizontal ? 1 : 0);
        _fit.Select((int)settings.Fit);
        _color.Color = new Color(settings.Color);
        _opacity.SetValueNoSignal(settings.Opacity * 100);
        _brightness.SetValueNoSignal(settings.Brightness * 100);
        _blur.SetValueNoSignal(settings.Blur);
        GetNode<Control>("Fields/Content/Blur").Visible = settings.Type is BackgroundType.Image or BackgroundType.Video;
        _gradient.Visible = settings.Type == BackgroundType.Gradient;
        _fit.Visible = GetNode<Control>("Fields/Content/Color").Visible = settings.Type is BackgroundType.Image or BackgroundType.Video;
        _videoSection.Visible = settings.Type == BackgroundType.Video;
        _videoResolution.Select(Array.IndexOf(VideoHeights, settings.Video.PreviewHeight));
        _videoFps.Select(Array.IndexOf(VideoRates, settings.Video.PreviewFramesPerSecond));
        _loop.SetPressedNoSignal(settings.Video.Loop);
        _offset.SetValueNoSignal(settings.Video.OffsetSeconds);
        _videoStatus.Text = videoStatus;
        _videoStatus.TooltipText = videoError.Length > 0 ? videoError : videoStatus;
        _syncing = false;
        SetBusy(_busy);
    }

    public bool HasOpenPopup() => _gradient.GetPopup().Visible || _fit.GetPopup().Visible || _color.GetPopup().Visible ||
        _videoResolution.GetPopup().Visible || _videoFps.GetPopup().Visible;
    public void SetBusy(bool busy)
    {
        _busy = busy;
        _fit.Disabled = busy || (_settings.Type == BackgroundType.Image && !_available);
        _gradient.Disabled = _color.Disabled = busy;
        _opacity.Editable = _brightness.Editable = _blur.Editable = !busy;
        _videoSection.Refresh(true, busy);
        ParameterRow.SyncAll(this);
    }
}
