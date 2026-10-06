using System;

namespace Trifle.Visuals;

public sealed record ParticleSettings
{
    public bool Enabled { get; init; }
    public int Amount { get; init; } = 40;
    public double Lifetime { get; init; } = 1.6;
    public double Speed { get; init; } = 180;
    public double Size { get; init; } = 4.8;
    public double Emission { get; init; } = 1;
    public double Turbulence { get; init; } = 0.45;
    public bool Beam { get; init; }
    public bool Curves { get; init; }
    public double Glow { get; init; } = 0.85;
    public double GlowRadius { get; init; } = 2.4;
    public double FlowStrength { get; init; } = 1;
    // Kept to read second-pass projects. Independent curves no longer use a trail duration.
    public double TrailSeconds { get; init; } = 0.38;
    public double DensityVariation { get; init; } = 0.85;
    public double CurveLength { get; init; } = 0.65;
    public double CurveStrength { get; init; } = 0.7;
    public double CurveWidth { get; init; } = 6;
    public double LateralSpread { get; init; } = 1;
    public double CurveDeformation { get; init; } = 0.5;
    public double CurveChance { get; init; } = 0.55;
    public double CurveEmission { get; init; } = 1;
    public double CurveGlow { get; init; } = 0.85;

    public void Validate()
    {
        if (Amount < 8 || Amount > 128) throw new ArgumentException("每秒粒子数量需要在 8–128 之间。");
        Range(Lifetime, 0.2, 4, "粒子寿命");
        Range(Speed, 30, 400, "粒子速度");
        Range(Size, 1, 12, "粒子尺寸");
        Range(Emission, 0, 4, "粒子发光");
        Range(Turbulence, 0, 2, "湍流强度");
        Range(Glow, 0, 3, "粒子柔光");
        Range(GlowRadius, 1, 5, "粒子柔光范围");
        Range(FlowStrength, 0, 3, "整体弯曲");
        Range(TrailSeconds, 0.08, 0.8, "拖尾时长");
        Range(DensityVariation, 0, 1, "聚团程度");
        Range(CurveLength, 0.2, 1.2, "流线长度");
        Range(CurveStrength, 0, 2, "流线亮度");
        Range(CurveWidth, 1, 14, "流线粗细");
        Range(LateralSpread, 0, 3, "左右扩散强度");
        Range(CurveDeformation, 0, 1.5, "流线扰动");
        Range(CurveChance, 0, 1, "流线出现概率");
        Range(CurveEmission, 0, 4, "流线发光");
        Range(CurveGlow, 0, 3, "流线柔光");
    }

    private static void Range(double value, double min, double max, string label)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            throw new ArgumentException($"{label}需要在 {min}–{max} 之间。");
    }
}
