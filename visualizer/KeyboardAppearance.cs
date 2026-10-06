using System;

namespace Trifle.Visuals;

// Fractions of the visual canvas, independent of window size and export resolution.
public sealed record KeyboardAppearance
{
    public double X { get; init; } = 64.0 / 1920;
    public double Y { get; init; } = 900.0 / 1080;
    public double Width { get; init; } = 1792.0 / 1920;
    public double Height { get; init; } = 180.0 / 1080;
    public string WhiteColor { get; init; } = "e8e9e6ff";
    public string BlackColor { get; init; } = "202329ff";

    public void Validate()
    {
        if (!double.IsFinite(X) || !double.IsFinite(Y) || !double.IsFinite(Width) || !double.IsFinite(Height) ||
            X < 0 || Y < 0.01 || Width < 0.05 || Height < 0.02 || X + Width > 1 + 1e-9 || Y + Height > 1 + 1e-9)
            throw new ArgumentException("键盘需要位于画面内；宽度至少 5%，高度至少 2%，顶部至少保留 1% 音符区域。");
        VisualSettings.ValidateColor(WhiteColor, "白键");
        VisualSettings.ValidateColor(BlackColor, "黑键");
    }
}
