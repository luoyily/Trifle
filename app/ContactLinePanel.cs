using Godot;
using System;
using Trifle.Visuals;

namespace Trifle.App;

public partial class ContactLinePanel : VBoxContainer
{
    public event Action<ContactLineSettings> SettingsChanged;
    private ContactLineSettings _settings = new();
    private bool _syncing, _busy;
    private static readonly ContactLineSettings Defaults = new();

    private EffectSection Section(string name) => GetNode<EffectSection>("Fields/Content/" + name);
    private SpinBox Number(string path) => GetNode<SpinBox>("Fields/Content/" + path.Replace("/", "/Fields/Content/") + "/Value");
    private T Field<T>(string group, string name) where T : Node => GetNode<T>($"Fields/Content/{group}/Fields/Content/{name}");

    public override void _Ready()
    {
        var header = GetNode<Button>("Header");
        SectionHeading.Bind(header, GetNode<Control>("Fields"), "接触线系统");
        Section("Line").EnabledChanged += v => Request(_settings with { LineEnabled = v });
        Section("Halo").EnabledChanged += v => Request(_settings with { HaloEnabled = v });
        Bind("Line/Strength", v => _settings with { LineEmission = v });
        Bind("Line/Width", v => _settings with { LineCoreWidth = v });
        Bind("Halo/Strength", v => _settings with { HaloEmission = v });
        Bind("Halo/Width", v => _settings with { LineWidth = v });
        Bind("LineMotion/Wave", v => _settings with { LineWave = v });
        Bind("LineMotion/Boost", v => _settings with { LineContactBoost = v });
        Field<ColorPickerButton>("Line", "Color/Value").ColorChanged += c => Request(_settings with { LineColor = c.ToHtml() });
        Field<ColorPickerButton>("Halo", "Color/Value").ColorChanged += c => Request(_settings with { HaloColor = c.ToHtml() });
        Field<CheckButton>("Halo", "Follow").Toggled += v => Request(_settings with { HaloFollowsLine = v });
        Field<CheckButton>("LineMotion", "Tint").Toggled += v => Request(_settings with { TintWithNotes = v });
        Reset("Line", () => _settings with { LineEmission = Defaults.LineEmission, LineCoreWidth = Defaults.LineCoreWidth, LineColor = Defaults.LineColor });
        Reset("Halo", () => _settings with { HaloEmission = Defaults.HaloEmission, LineWidth = Defaults.LineWidth,
            HaloColor = Defaults.HaloColor, HaloFollowsLine = Defaults.HaloFollowsLine });
        Reset("LineMotion", () => _settings with { LineWave = Defaults.LineWave, TintWithNotes = Defaults.TintWithNotes, LineContactBoost = Defaults.LineContactBoost });
        Refresh(_settings);
    }

    private void Bind(string path, Func<double, ContactLineSettings> change) => Number(path).ValueChanged += v => Request(change(v));
    private void Reset(string group, Func<ContactLineSettings> change) => Field<Button>(group, "Reset").Pressed += () => Request(change());
    private void Request(ContactLineSettings settings) { if (!_syncing && !_busy) SettingsChanged?.Invoke(settings); }

    public void Refresh(ContactLineSettings settings)
    {
        _syncing = true; _settings = settings;
        Number("Line/Strength").SetValueNoSignal(settings.LineEmission);
        Number("Line/Width").SetValueNoSignal(settings.LineCoreWidth);
        Number("Halo/Strength").SetValueNoSignal(settings.HaloEmission);
        Number("Halo/Width").SetValueNoSignal(settings.LineWidth);
        Number("LineMotion/Wave").SetValueNoSignal(settings.LineWave);
        Number("LineMotion/Boost").SetValueNoSignal(settings.LineContactBoost);
        Field<ColorPickerButton>("Line", "Color/Value").Color = new Color(settings.LineColor);
        Field<ColorPickerButton>("Halo", "Color/Value").Color = new Color(settings.HaloFollowsLine ? settings.LineColor : settings.HaloColor);
        Field<CheckButton>("Halo", "Follow").SetPressedNoSignal(settings.HaloFollowsLine);
        Field<CheckButton>("LineMotion", "Tint").SetPressedNoSignal(settings.TintWithNotes);
        _syncing = false;
        SetBusy(_busy);
    }

    public void SetBusy(bool busy)
    {
        _busy = busy;
        Section("Line").Refresh(_settings.LineEnabled, busy);
        Section("Halo").Refresh(_settings.HaloEnabled, busy);
        Section("LineMotion").Refresh(_settings.LineEnabled || _settings.HaloEnabled, busy);
        Field<ColorPickerButton>("Halo", "Color/Value").Disabled = busy || !_settings.HaloEnabled || _settings.HaloFollowsLine;
    }

    public bool HasOpenPopup() => Field<ColorPickerButton>("Line", "Color/Value").GetPopup().Visible ||
        Field<ColorPickerButton>("Halo", "Color/Value").GetPopup().Visible;
}
