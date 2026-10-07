using Godot;
using System;
using Trifle.Visuals;

namespace Trifle.App;

public partial class GlowPanel : EffectSection
{
    public event Action<GlowSettings> SettingsChanged;
    private GlowSettings _settings = new();
    private bool _busy;
    private SpinBox _strength;

    public override void _Ready()
    {
        base._Ready();
        _strength = GetNode<SpinBox>("Fields/Content/Strength/Value");
        EnabledChanged += value => { if (!_busy) SettingsChanged?.Invoke(_settings with { Enabled = value }); };
        _strength.ValueChanged += value => { if (!_busy) SettingsChanged?.Invoke(_settings with { Intensity = value }); };
        GetNode<Button>("Fields/Content/Reset").Pressed += () =>
        {
            if (!_busy) SettingsChanged?.Invoke(_settings with { Intensity = new GlowSettings().Intensity });
        };
        Refresh(_settings);
    }

    public void Refresh(GlowSettings settings)
    {
        _settings = settings;
        _strength.SetValueNoSignal(settings.Intensity);
        SetBusy(_busy);
    }

    public void SetBusy(bool busy) { _busy = busy; base.Refresh(_settings.Enabled, busy); }
}
