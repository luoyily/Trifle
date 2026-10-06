using Godot;
using System;
using Trifle.Visuals;

namespace Trifle.App;

public partial class LightEffectsPanel : VBoxContainer
{
    public event Action<LightEffectsSettings> SettingsChanged;
    public event Action<GlowSettings> GlowChanged;
    private LightEffectsSettings _settings = new();
    private GlowSettings _glow = new();
    private bool _syncing, _busy;
    private static readonly LightEffectsSettings Defaults = new();

    private EffectSection Section(string name) => GetNode<EffectSection>("Fields/" + name);
    private SpinBox Number(string path) => GetNode<SpinBox>("Fields/" + path.Replace("/", "/Fields/") + "/Value");
    private T Field<T>(string group, string name) where T : Node => GetNode<T>($"Fields/{group}/Fields/{name}");

    public override void _Ready()
    {
        var header = GetNode<Button>("Header");
        SectionHeading.Bind(header, GetNode<Control>("Fields"), "光效");
        Section("Glow").EnabledChanged += v => RequestGlow(_glow with { Enabled = v });
        Section("Keyboard").EnabledChanged += v => Request(_settings with { KeyboardEnabled = v });
        Section("Hit").EnabledChanged += v => Request(_settings with { HitEnabled = v });
        Section("Area").EnabledChanged += v => Request(_settings with { KeyLightEnabled = v });
        Section("Near").EnabledChanged += v => Request(_settings with { NearEnabled = v });
        Section("Line").EnabledChanged += v => Request(_settings with { LineEnabled = v });
        Section("Halo").EnabledChanged += v => Request(_settings with { HaloEnabled = v });
        Number("Glow/Strength").ValueChanged += v => RequestGlow(_glow with { Intensity = v });
        Bind("Keyboard/Strength", v => _settings with { KeyboardEmission = v });
        Bind("Hit/Strength", v => _settings with { HitEmission = v });
        Bind("Area/Strength", v => _settings with { KeyLightStrength = v });
        Bind("Area/Radius", v => _settings with { KeyLightRadius = v });
        Bind("Near/Strength", v => _settings with { NearStrength = v });
        Bind("Near/Distance", v => _settings with { NearDistance = v });
        Bind("Line/Strength", v => _settings with { LineEmission = v });
        Bind("Line/Width", v => _settings with { LineCoreWidth = v });
        Bind("Halo/Strength", v => _settings with { HaloEmission = v });
        Bind("Halo/Width", v => _settings with { LineWidth = v });
        Bind("LineMotion/Wave", v => _settings with { LineWave = v });
        Bind("LineMotion/Boost", v => _settings with { LineContactBoost = v });
        Bind("Response/Decay", v => _settings with { HitDecay = v });
        Field<ColorPickerButton>("Line", "Color/Value").ColorChanged += c => Request(_settings with { LineColor = c.ToHtml() });
        Field<ColorPickerButton>("Halo", "Color/Value").ColorChanged += c => Request(_settings with { HaloColor = c.ToHtml() });
        Field<CheckButton>("Halo", "Follow").Toggled += v => Request(_settings with { HaloFollowsLine = v });
        Field<CheckButton>("LineMotion", "Tint").Toggled += v => Request(_settings with { TintWithNotes = v });
        Field<Button>("Glow", "Reset").Pressed += () => RequestGlow(_glow with { Intensity = new GlowSettings().Intensity });
        Reset("Keyboard", () => _settings with { KeyboardEmission = Defaults.KeyboardEmission });
        Reset("Hit", () => _settings with { HitEmission = Defaults.HitEmission });
        Reset("Area", () => _settings with { KeyLightStrength = Defaults.KeyLightStrength, KeyLightRadius = Defaults.KeyLightRadius });
        Reset("Near", () => _settings with { NearStrength = Defaults.NearStrength, NearDistance = Defaults.NearDistance });
        Reset("Line", () => _settings with { LineEmission = Defaults.LineEmission, LineCoreWidth = Defaults.LineCoreWidth, LineColor = Defaults.LineColor });
        Reset("Halo", () => _settings with { HaloEmission = Defaults.HaloEmission, LineWidth = Defaults.LineWidth,
            HaloColor = Defaults.HaloColor, HaloFollowsLine = Defaults.HaloFollowsLine });
        Reset("LineMotion", () => _settings with { LineWave = Defaults.LineWave, TintWithNotes = Defaults.TintWithNotes, LineContactBoost = Defaults.LineContactBoost });
        Reset("Response", () => _settings with { HitDecay = Defaults.HitDecay });
        Refresh(_settings, _glow);
    }

    private void Bind(string path, Func<double, LightEffectsSettings> change) => Number(path).ValueChanged += v => Request(change(v));
    private void Reset(string group, Func<LightEffectsSettings> change) => Field<Button>(group, "Reset").Pressed += () => Request(change());
    private void Request(LightEffectsSettings settings) { if (!_syncing && !_busy) SettingsChanged?.Invoke(settings); }
    private void RequestGlow(GlowSettings settings) { if (!_syncing && !_busy) GlowChanged?.Invoke(settings); }

    public void Refresh(LightEffectsSettings settings, GlowSettings glow)
    {
        _syncing = true; _settings = settings; _glow = glow;
        Number("Glow/Strength").SetValueNoSignal(glow.Intensity);
        Number("Keyboard/Strength").SetValueNoSignal(settings.KeyboardEmission);
        Number("Hit/Strength").SetValueNoSignal(settings.HitEmission);
        Number("Area/Strength").SetValueNoSignal(settings.KeyLightStrength);
        Number("Area/Radius").SetValueNoSignal(settings.KeyLightRadius);
        Number("Near/Strength").SetValueNoSignal(settings.NearStrength);
        Number("Near/Distance").SetValueNoSignal(settings.NearDistance);
        Number("Line/Strength").SetValueNoSignal(settings.LineEmission);
        Number("Line/Width").SetValueNoSignal(settings.LineCoreWidth);
        Number("Halo/Strength").SetValueNoSignal(settings.HaloEmission);
        Number("Halo/Width").SetValueNoSignal(settings.LineWidth);
        Number("LineMotion/Wave").SetValueNoSignal(settings.LineWave);
        Number("LineMotion/Boost").SetValueNoSignal(settings.LineContactBoost);
        Number("Response/Decay").SetValueNoSignal(settings.HitDecay);
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
        Section("Glow").Refresh(_glow.Enabled, busy);
        Section("Keyboard").Refresh(_settings.KeyboardEnabled, busy);
        Section("Hit").Refresh(_settings.HitEnabled, busy);
        Section("Area").Refresh(_settings.KeyLightEnabled, busy);
        Section("Near").Refresh(_settings.NearEnabled, busy);
        Section("Line").Refresh(_settings.LineEnabled, busy);
        Section("Halo").Refresh(_settings.HaloEnabled, busy);
        Section("LineMotion").Refresh(_settings.LineEnabled || _settings.HaloEnabled, busy);
        Section("Response").Refresh(true, busy);
        Field<ColorPickerButton>("Halo", "Color/Value").Disabled = busy || !_settings.HaloEnabled || _settings.HaloFollowsLine;
    }

    public bool HasOpenPopup() => Field<ColorPickerButton>("Line", "Color/Value").GetPopup().Visible ||
        Field<ColorPickerButton>("Halo", "Color/Value").GetPopup().Visible;
}
