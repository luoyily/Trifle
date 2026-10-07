using Godot;
using System;
using Trifle.Visuals;

namespace Trifle.App;

public partial class KeyboardLightPanel : VBoxContainer
{
    public event Action<KeyboardLightSettings> SettingsChanged;
    private KeyboardLightSettings _settings = new();
    private bool _syncing, _busy;
    private static readonly KeyboardLightSettings Defaults = new();

    private EffectSection Section(string name) => GetNode<EffectSection>("Fields/Content/" + name);
    private SpinBox Number(string path) => GetNode<SpinBox>("Fields/Content/" + path.Replace("/", "/Fields/Content/") + "/Value");
    private T Field<T>(string group, string name) where T : Node => GetNode<T>($"Fields/Content/{group}/Fields/Content/{name}");

    public override void _Ready()
    {
        var header = GetNode<Button>("Header");
        SectionHeading.Bind(header, GetNode<Control>("Fields"), "灯光");
        Section("Keyboard").EnabledChanged += v => Request(_settings with { KeyboardEnabled = v });
        Section("Hit").EnabledChanged += v => Request(_settings with { HitEnabled = v });
        Section("Area").EnabledChanged += v => Request(_settings with { KeyLightEnabled = v });
        Section("Near").EnabledChanged += v => Request(_settings with { NearEnabled = v });
        Bind("Keyboard/Strength", v => _settings with { KeyboardEmission = v });
        Bind("Hit/Strength", v => _settings with { HitEmission = v });
        Bind("Area/Strength", v => _settings with { KeyLightStrength = v });
        Bind("Area/Radius", v => _settings with { KeyLightRadius = v });
        Bind("Near/Strength", v => _settings with { NearStrength = v });
        Bind("Near/Distance", v => _settings with { NearDistance = v });
        Bind("Hit/Decay", v => _settings with { HitDecay = v });
        Reset("Keyboard", () => _settings with { KeyboardEmission = Defaults.KeyboardEmission });
        Reset("Hit", () => _settings with { HitEmission = Defaults.HitEmission, HitDecay = Defaults.HitDecay });
        Reset("Area", () => _settings with { KeyLightStrength = Defaults.KeyLightStrength, KeyLightRadius = Defaults.KeyLightRadius });
        Reset("Near", () => _settings with { NearStrength = Defaults.NearStrength, NearDistance = Defaults.NearDistance });
        Refresh(_settings);
    }

    private void Bind(string path, Func<double, KeyboardLightSettings> change) => Number(path).ValueChanged += v => Request(change(v));
    private void Reset(string group, Func<KeyboardLightSettings> change) => Field<Button>(group, "Reset").Pressed += () => Request(change());
    private void Request(KeyboardLightSettings settings) { if (!_syncing && !_busy) SettingsChanged?.Invoke(settings); }

    public void Refresh(KeyboardLightSettings settings)
    {
        _syncing = true; _settings = settings;
        Number("Keyboard/Strength").SetValueNoSignal(settings.KeyboardEmission);
        Number("Hit/Strength").SetValueNoSignal(settings.HitEmission);
        Number("Area/Strength").SetValueNoSignal(settings.KeyLightStrength);
        Number("Area/Radius").SetValueNoSignal(settings.KeyLightRadius);
        Number("Near/Strength").SetValueNoSignal(settings.NearStrength);
        Number("Near/Distance").SetValueNoSignal(settings.NearDistance);
        Number("Hit/Decay").SetValueNoSignal(settings.HitDecay);
        _syncing = false;
        SetBusy(_busy);
    }

    public void SetBusy(bool busy)
    {
        _busy = busy;
        Section("Keyboard").Refresh(_settings.KeyboardEnabled, busy);
        Section("Hit").Refresh(_settings.HitEnabled, busy);
        Section("Area").Refresh(_settings.KeyLightEnabled, busy);
        Section("Near").Refresh(_settings.NearEnabled, busy);
    }
}
