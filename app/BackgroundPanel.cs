using Godot;
using System;
using Trifle.Visuals;

namespace Trifle.App;

public partial class BackgroundPanel : VBoxContainer
{
    public event Action<int> FitChanged;
    public event Action<int, Color> GradientChanged;
    public event Action<double, double> AppearanceChanged;
    public event Action<Color> ColorChanged;
    private OptionButton _gradient, _fit;
    private ColorPickerButton _color;
    private SpinBox _opacity, _brightness;
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
        _gradient.AddItem("垂直渐变", (int)BackgroundGradient.Vertical);
        _gradient.AddItem("水平渐变", (int)BackgroundGradient.Horizontal);
        _fit.AddItem("完整显示 · 空白填背景色");
        _fit.AddItem("铺满裁剪 · 居中");
        _gradient.ItemSelected += v => { if (!_syncing && !_busy) GradientChanged?.Invoke(_gradient.GetItemId((int)v), new Color(_settings.EndColor)); };
        _fit.ItemSelected += v => { if (!_syncing && !_busy) FitChanged?.Invoke((int)v); };
        _color.ColorChanged += c => { if (!_syncing && !_busy) ColorChanged?.Invoke(c); };
        foreach (var number in new[] { _opacity, _brightness })
            number.ValueChanged += _ => { if (!_syncing && !_busy) AppearanceChanged?.Invoke(_opacity.Value, _brightness.Value); };
        Refresh(_settings, false);
    }

    public void Refresh(BackgroundSettings settings, bool available)
    {
        _syncing = true; _settings = settings; _available = available;
        _gradient.Select(settings.Gradient == BackgroundGradient.Horizontal ? 1 : 0);
        _fit.Select((int)settings.Fit);
        _color.Color = new Color(settings.Color);
        _opacity.SetValueNoSignal(settings.Opacity);
        _brightness.SetValueNoSignal(settings.Brightness);
        _gradient.Visible = settings.Type == BackgroundType.Gradient;
        _fit.Visible = GetNode<Control>("Fields/Content/Color").Visible = settings.Type == BackgroundType.Image;
        _syncing = false;
        SetBusy(_busy);
    }

    public bool HasOpenPopup() => _gradient.GetPopup().Visible || _fit.GetPopup().Visible || _color.GetPopup().Visible;
    public void SetBusy(bool busy)
    {
        _busy = busy;
        _fit.Disabled = busy || !_available;
        _gradient.Disabled = _color.Disabled = busy;
        _opacity.Editable = _brightness.Editable = !busy;
        ParameterRow.SyncAll(this);
    }
}
