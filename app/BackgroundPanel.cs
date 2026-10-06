using Godot;
using System;
using System.IO;
using Trifle.Visuals;

namespace Trifle.App;

public partial class BackgroundPanel : VBoxContainer
{
    public event Action<Color> ColorChanged;
    public event Action<string> ImageRequested;
    public event Action ClearRequested;
    public event Action<int> FitChanged;
    public event Action<int, Color> GradientChanged;
    private OptionButton _gradient;
    private ColorPickerButton _endColor;
    private ColorPickerButton _color;
    private Label _image;
    private OptionButton _fit;
    private Button _load;
    private Button _clear;
    private FileDialog _files;
    private bool _busy;
    private bool _hasReference;
    private bool _hasImage;
    private bool _syncing;

    public override void _Ready()
    {
        var header = GetNode<Button>("Header");
        SectionHeading.Bind(header, GetNode<Control>("Fields"), "背景");
        _color = GetNode<ColorPickerButton>("Fields/Color/Value");
        _image = GetNode<Label>("Fields/Image");
        _fit = GetNode<OptionButton>("Fields/Fit");
        _load = GetNode<Button>("Fields/Actions/Load");
        _clear = GetNode<Button>("Fields/Actions/Clear");
        _files = GetNode<FileDialog>("Files");
        _gradient = GetNode<OptionButton>("Fields/Gradient");
        _endColor = GetNode<ColorPickerButton>("Fields/EndColor/Value");
        _gradient.AddItem("纯色"); _gradient.AddItem("垂直渐变"); _gradient.AddItem("水平渐变");
        _gradient.ItemSelected += v => { if (!_busy && !_syncing) GradientChanged?.Invoke((int)v, _endColor.Color); };
        _endColor.ColorChanged += v => { if (!_busy && !_syncing) GradientChanged?.Invoke(_gradient.Selected, v); };
        _files.CurrentDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyPictures);
        _fit.AddItem("完整显示 · 空白填背景色");
        _fit.AddItem("铺满裁剪 · 居中");
        _color.ColorChanged += color => { if (!_busy && !_syncing) ColorChanged?.Invoke(color); };
        _fit.ItemSelected += mode => { if (!_busy && !_syncing) FitChanged?.Invoke((int)mode); };
        _load.Pressed += () => { if (!_busy) _files.PopupCenteredRatio(0.7f); };
        _clear.Pressed += () => { if (!_busy) ClearRequested?.Invoke(); };
        _files.FileSelected += path => { if (!_busy) ImageRequested?.Invoke(path); };
        Refresh(new BackgroundSettings(), false);
    }

    public void Refresh(BackgroundSettings settings, bool available)
    {
        _syncing = true;
        _hasReference = settings.ImagePath.Length > 0;
        _hasImage = available;
        _color.Color = new Color(settings.Color);
        _gradient.Select((int)settings.Gradient);
        _endColor.Color = new Color(settings.EndColor);
        _image.Text = !_hasReference ? "未选择图片" : Path.GetFileName(settings.ImagePath) + (available ? "" : " · 不可用");
        _image.TooltipText = settings.ImagePath;
        _fit.Select((int)settings.Fit);
        _syncing = false;
        SetBusy(_busy);
    }

    public bool HasOpenPopup() => _files.Visible || _fit.GetPopup().Visible || _color.GetPopup().Visible || _endColor.GetPopup().Visible || _gradient.GetPopup().Visible;

    public void SetBusy(bool busy)
    {
        _busy = busy;
        _load.Disabled = _color.Disabled = busy;
        _gradient.Disabled = _endColor.Disabled = busy;
        _clear.Disabled = busy || !_hasReference;
        _fit.Disabled = busy || !_hasImage;
    }
}
