using System;

namespace Trifle.Visuals;

public sealed record GlowSettings
{
    public bool Enabled { get; init; }
    public double Intensity { get; init; } = 1;

    public void Validate()
    {
        if (!double.IsFinite(Intensity) || Intensity < 0 || Intensity > 3)
            throw new ArgumentException("Glow 强度需要在 0–3 之间。");
    }
}
