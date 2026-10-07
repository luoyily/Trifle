using Godot;
using System;

namespace Trifle.App;

public partial class PreviewPanel : VBoxContainer
{
    public event Action<PreviewSettings> SettingsChanged;
    private static readonly int[] Heights = { 540, 720, 1080, 1440, 2160 };
    private static readonly int[] Rates = { 24, 30, 60 };
    private OptionButton _size, _fps;
    private bool _busy;

    public override void _Ready()
    {
        var header = GetNode<Button>("Header");
        SectionHeading.Bind(header, GetNode<Control>("Fields"), "预览分辨率");
        _size = GetNode<OptionButton>("Fields/Content/Size");
        _fps = GetNode<OptionButton>("Fields/Content/Fps");
        foreach (int height in Heights) _size.AddItem(height == 2160 ? "4K · 3840 × 2160" : $"{height}p · {height * 16 / 9} × {height}");
        foreach (int fps in Rates) _fps.AddItem($"{fps} FPS");
        _size.ItemSelected += _ => Request();
        _fps.ItemSelected += _ => Request();
        Refresh(new PreviewSettings());
    }

    private void Request()
    {
        if (!_busy) SettingsChanged?.Invoke(new PreviewSettings { Height = Heights[_size.Selected], FramesPerSecond = Rates[_fps.Selected] });
    }

    public void Refresh(PreviewSettings settings) { _size.Select(Array.IndexOf(Heights, settings.Height)); _fps.Select(Array.IndexOf(Rates, settings.FramesPerSecond)); }
    public void SetBusy(bool busy) { _busy = busy; _size.Disabled = _fps.Disabled = busy; }
    public bool HasOpenPopup() => _size.GetPopup().Visible || _fps.GetPopup().Visible;
}
