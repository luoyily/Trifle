using System;

namespace Trifle.Visuals;

public sealed record ContactLineSettings
{
    public bool LineEnabled { get; init; } = true;
    public bool HaloEnabled { get; init; } = true;
    public double HaloEmission { get; init; } = 0.4;
    public string LineColor { get; init; } = "769eadff";
    public string HaloColor { get; init; } = "769eadff";
    public bool HaloFollowsLine { get; init; } = true;
    public bool TintWithNotes { get; init; } = true;
    public double LineContactBoost { get; init; } = 0.8;
    public double LineEmission { get; init; } = 0.4;
    public double LineWidth { get; init; } = 18;
    public double LineWave { get; init; } = 1.8;
    public double LineCoreWidth { get; init; } = 3.5;

    public void Validate()
    {

        VisualSettings.ValidateColor(LineColor, "接触线");
        VisualSettings.ValidateColor(HaloColor, "光带");
        if (!double.IsFinite(HaloEmission) || HaloEmission < 0 || HaloEmission > 3 ||
            !double.IsFinite(LineContactBoost) || LineContactBoost < 0 || LineContactBoost > 4.8 ||
            !double.IsFinite(LineEmission) || LineEmission < 0 || LineEmission > 3 ||
            !double.IsFinite(LineWidth) || LineWidth < 4 || LineWidth > 40 ||
            !double.IsFinite(LineWave) || LineWave < 0 || LineWave > 6 ||
            !double.IsFinite(LineCoreWidth) || LineCoreWidth < 0.5 || LineCoreWidth > 10)
            throw new ArgumentException("线 / 光带亮度 0–3，触键增亮 0–4.8，光带宽度 4–40，波幅 0–6，线宽 0.5–10。");
    }
}
