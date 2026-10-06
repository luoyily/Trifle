using Godot;
using System;
using Trifle.Visuals;

namespace Trifle.App;

public partial class ParticlePanel : VBoxContainer
{
    public event Action<ParticleSettings> SettingsChanged;
    private ParticleSettings _settings = new();
    private static readonly ParticleSettings Defaults = new();
    private bool _syncing, _busy;
    private EffectSection Section(string group) => GetNode<EffectSection>("Fields/" + group);
    private SpinBox Number(string path) => GetNode<SpinBox>("Fields/" + path.Replace("/", "/Fields/") + "/Value");

    public override void _Ready()
    {
        var header = GetNode<Button>("Header");
        SectionHeading.Bind(header, GetNode<Control>("Fields"), "粒子与流线");
        Section("Particles").EnabledChanged += v => Request(_settings with { Enabled = v });
        Section("Curves").EnabledChanged += v => Request(_settings with { Curves = v });
        Bind("Particles/Amount", v => _settings with { Amount = (int)v });
        Bind("Particles/Size", v => _settings with { Size = v });
        Bind("Particles/Emission", v => _settings with { Emission = v });
        Bind("Particles/Glow", v => _settings with { Glow = v });
        Bind("Particles/GlowRadius", v => _settings with { GlowRadius = v });
        Bind("Curves/Length", v => _settings with { CurveLength = v });
        Bind("Curves/Strength", v => _settings with { CurveStrength = v });
        Bind("Curves/Width", v => _settings with { CurveWidth = v });
        Bind("Curves/Deformation", v => _settings with { CurveDeformation = v });
        Bind("Curves/Chance", v => _settings with { CurveChance = v / 100 });
        Bind("Curves/Emission", v => _settings with { CurveEmission = v });
        Bind("Curves/Glow", v => _settings with { CurveGlow = v });
        Bind("Motion/Lifetime", v => _settings with { Lifetime = v });
        Bind("Motion/Speed", v => _settings with { Speed = v });
        Bind("Motion/Turbulence", v => _settings with { Turbulence = v });
        Bind("Motion/Spread", v => _settings with { LateralSpread = v });
        Bind("Motion/Flow", v => _settings with { FlowStrength = v });
        Bind("Motion/Density", v => _settings with { DensityVariation = v });
        GetNode<CheckButton>("Fields/Motion/Fields/Beam").Toggled += v => Request(_settings with { Beam = v });
        GetNode<Button>("Fields/Particles/Fields/Reset").Pressed += () => Request(_settings with
        {
            Amount = Defaults.Amount, Size = Defaults.Size, Emission = Defaults.Emission,
            Glow = Defaults.Glow, GlowRadius = Defaults.GlowRadius
        });
        GetNode<Button>("Fields/Curves/Fields/Reset").Pressed += () => Request(_settings with
        {
            CurveLength = Defaults.CurveLength, CurveStrength = Defaults.CurveStrength, CurveWidth = Defaults.CurveWidth,
            CurveDeformation = Defaults.CurveDeformation, CurveChance = Defaults.CurveChance,
            CurveEmission = Defaults.CurveEmission, CurveGlow = Defaults.CurveGlow
        });
        GetNode<Button>("Fields/Motion/Fields/Reset").Pressed += () => Request(_settings with
        {
            Lifetime = Defaults.Lifetime, Speed = Defaults.Speed, Turbulence = Defaults.Turbulence,
            LateralSpread = Defaults.LateralSpread, FlowStrength = Defaults.FlowStrength,
            DensityVariation = Defaults.DensityVariation, Beam = Defaults.Beam
        });
        Refresh(_settings);
    }

    private void Bind(string path, Func<double, ParticleSettings> change) => Number(path).ValueChanged += v => Request(change(v));
    private void Request(ParticleSettings settings) { if (!_syncing && !_busy) SettingsChanged?.Invoke(settings); }

    public void Refresh(ParticleSettings settings)
    {
        _syncing = true; _settings = settings;
        Number("Particles/Amount").SetValueNoSignal(settings.Amount);
        Number("Particles/Size").SetValueNoSignal(settings.Size);
        Number("Particles/Emission").SetValueNoSignal(settings.Emission);
        Number("Particles/Glow").SetValueNoSignal(settings.Glow);
        Number("Particles/GlowRadius").SetValueNoSignal(settings.GlowRadius);
        Number("Curves/Length").SetValueNoSignal(settings.CurveLength);
        Number("Curves/Strength").SetValueNoSignal(settings.CurveStrength);
        Number("Curves/Width").SetValueNoSignal(settings.CurveWidth);
        Number("Curves/Deformation").SetValueNoSignal(settings.CurveDeformation);
        Number("Curves/Chance").SetValueNoSignal(settings.CurveChance * 100);
        Number("Curves/Emission").SetValueNoSignal(settings.CurveEmission);
        Number("Curves/Glow").SetValueNoSignal(settings.CurveGlow);
        Number("Motion/Lifetime").SetValueNoSignal(settings.Lifetime);
        Number("Motion/Speed").SetValueNoSignal(settings.Speed);
        Number("Motion/Turbulence").SetValueNoSignal(settings.Turbulence);
        Number("Motion/Spread").SetValueNoSignal(settings.LateralSpread);
        Number("Motion/Flow").SetValueNoSignal(settings.FlowStrength);
        Number("Motion/Density").SetValueNoSignal(settings.DensityVariation);
        GetNode<CheckButton>("Fields/Motion/Fields/Beam").SetPressedNoSignal(settings.Beam);
        _syncing = false;
        SetBusy(_busy);
    }

    public void SetBusy(bool busy)
    {
        _busy = busy;
        Section("Particles").Refresh(_settings.Enabled, busy);
        Section("Curves").Refresh(_settings.Curves, busy);
        Section("Motion").Refresh(_settings.Enabled || _settings.Curves, busy);
    }
}
