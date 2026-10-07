using System;

namespace Trifle.Visuals;

public sealed record KeyboardLightSettings
{
    public bool KeyboardEnabled { get; init; } = true;
    public bool HitEnabled { get; init; } = true;
    public bool NearEnabled { get; init; } = true;
    public bool KeyLightEnabled { get; init; } = true;
    public double KeyboardEmission { get; init; }
    public double HitEmission { get; init; }
    public double HitDecay { get; init; } = 0.3;
    public double NearStrength { get; init; }
    public double NearDistance { get; init; } = 140;
    public double KeyLightStrength { get; init; } = 0.9;
    public double KeyLightRadius { get; init; } = 2.5;

    public void Validate()
    {

        if (!double.IsFinite(KeyboardEmission) || KeyboardEmission < 0 || KeyboardEmission > 8 ||
            !double.IsFinite(HitEmission) || HitEmission < 0 || HitEmission > 8 ||
            !double.IsFinite(HitDecay) || HitDecay < 0.05 || HitDecay > 2 ||
            !double.IsFinite(NearStrength) || NearStrength < 0 || NearStrength > 4 ||
            !double.IsFinite(NearDistance) || NearDistance < 10 || NearDistance > 400 ||
            !double.IsFinite(KeyLightStrength) || KeyLightStrength < 0 || KeyLightStrength > 3 ||
            !double.IsFinite(KeyLightRadius) || KeyLightRadius < 0.5 || KeyLightRadius > 8)
            throw new ArgumentException("琴键 / 接触发光 0–8，衰减 0.05–2 秒，附近增亮 0–4、范围 10–400；范围光强度 0–3、范围 0.5–8 个白键。");
    }
}
