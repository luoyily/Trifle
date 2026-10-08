using System;
using Trifle.Visuals;

namespace Trifle.Score;

public sealed record ScoreSettings
{
    public bool Enabled { get; init; } = true;
    public double Width { get; init; } = 1;
    public double Y { get; init; } = 0.07;
    public bool RemoveBackground { get; init; }
    public string MainColor { get; init; } = "000000ff";
    public double Brightness { get; init; } = 1;
    public string CursorColor { get; init; } = "00a8cfff";
    public double CursorBrightness { get; init; } = 1;

    public void Validate()
    {
        if (!double.IsFinite(Width) || Width < 0.3 || Width > 1 ||
            !double.IsFinite(Y) || Y < 0 || Y > 0.55)
            throw new ArgumentException("乐谱宽度需要为 30–100%，垂直位置需要为 0–55%。");
        VisualSettings.ValidateColor(MainColor, "乐谱主颜色");
        VisualSettings.ValidateColor(CursorColor, "乐谱光标颜色");
        if (!double.IsFinite(Brightness) || Brightness < 0 || Brightness > 5 ||
            !double.IsFinite(CursorBrightness) || CursorBrightness < 0 || CursorBrightness > 5)
            throw new ArgumentException("乐谱与光标亮度需要为 0–5。");
    }
}
