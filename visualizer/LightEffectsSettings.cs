using System;

namespace Trifle.Visuals;

public sealed record LightEffectsSettings
{
    public bool KeyboardEnabled { get; init; } = true;
    public bool HitEnabled { get; init; } = true;
    public bool NearEnabled { get; init; } = true;
    public bool KeyLightEnabled { get; init; } = true;
    public bool LineEnabled { get; init; } = true;
    public bool HaloEnabled { get; init; } = true;
    public double HaloEmission { get; init; } = 0.4;
    public string LineColor { get; init; } = "769eadff";
    public string HaloColor { get; init; } = "769eadff";
    public bool HaloFollowsLine { get; init; } = true;
    public bool TintWithNotes { get; init; } = true;
    public double LineContactBoost { get; init; } = 0.8;
    public double KeyboardEmission { get; init; }
    public double HitEmission { get; init; }
    public double HitDecay { get; init; } = 0.3;
    public double NearStrength { get; init; }
    public double NearDistance { get; init; } = 140;
    public double LineEmission { get; init; } = 0.4;
    public double LineWidth { get; init; } = 18;
    public double LineWave { get; init; } = 1.8;
    public double LineCoreWidth { get; init; } = 3.5;
    public double KeyLightStrength { get; init; } = 0.9;
    public double KeyLightRadius { get; init; } = 2.5;

    public void Validate()
    {
        VisualSettings.ValidateColor(LineColor, "接触线");
        VisualSettings.ValidateColor(HaloColor, "光带");
        if (!double.IsFinite(HaloEmission) || HaloEmission < 0 || HaloEmission > 3 ||
            !double.IsFinite(LineContactBoost) || LineContactBoost < 0 || LineContactBoost > 4.8)
            throw new ArgumentException("光带亮度 0–3，触键增亮 0–4.8。");
        if (!double.IsFinite(KeyboardEmission) || KeyboardEmission < 0 || KeyboardEmission > 8 ||
            !double.IsFinite(HitEmission) || HitEmission < 0 || HitEmission > 8 ||
            !double.IsFinite(HitDecay) || HitDecay < 0.05 || HitDecay > 2 ||
            !double.IsFinite(NearStrength) || NearStrength < 0 || NearStrength > 4 ||
            !double.IsFinite(NearDistance) || NearDistance < 10 || NearDistance > 400)
            throw new ArgumentException("琴键 / 接触发光 0–8，衰减 0.05–2 秒，附近增亮 0–4、范围 10–400。");
        if (!double.IsFinite(LineEmission) || LineEmission < 0 || LineEmission > 3 ||
            !double.IsFinite(LineWidth) || LineWidth < 4 || LineWidth > 40 ||
            !double.IsFinite(LineWave) || LineWave < 0 || LineWave > 6)
            throw new ArgumentException("接触线亮度 0–3，光带宽度 4–40，波动幅度 0–6。");
        if (!double.IsFinite(LineCoreWidth) || LineCoreWidth < 0.5 || LineCoreWidth > 10 ||
            !double.IsFinite(KeyLightStrength) || KeyLightStrength < 0 || KeyLightStrength > 3 ||
            !double.IsFinite(KeyLightRadius) || KeyLightRadius < 0.5 || KeyLightRadius > 8)
            throw new ArgumentException("接触线亮芯 0.5–10，键盘范围光强度 0–3，单侧范围 0.5–8 个白键。");
    }
}
