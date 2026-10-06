using System;

namespace Trifle.Visuals;

public enum NoteShape { Rectangle, RoundedRectangle }

public sealed record NoteAppearance
{
    public NoteShape Shape { get; init; } = NoteShape.Rectangle;
    // Pixels on the 1920x1080 canvas; capped to half of each note's width/height in the shader.
    public double CornerRadius { get; init; } = 6;
    public double Opacity { get; init; } = 1;
    public double Brightness { get; init; } = 1;
    public double Emission { get; init; }
    public bool EmissionEnabled { get; init; } = true;

    public void Validate()
    {
        if (Shape is not NoteShape.Rectangle and not NoteShape.RoundedRectangle)
            throw new ArgumentException("未知音符形状。");
        if (!double.IsFinite(CornerRadius) || CornerRadius < 0 || CornerRadius > 32 ||
            !double.IsFinite(Opacity) || Opacity < 0 || Opacity > 1 ||
            !double.IsFinite(Brightness) || Brightness < 0 || Brightness > 4 ||
            !double.IsFinite(Emission) || Emission < 0 || Emission > 8)
            throw new ArgumentException("音符参数需要满足：圆角 0–32、透明度 0–1、亮度 0–4、发光 0–8。");
    }
}
