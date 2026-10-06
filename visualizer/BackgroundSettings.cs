using System;
using System.IO;

namespace Trifle.Visuals;

public enum BackgroundFit { Contain, Cover }
public enum BackgroundGradient { Solid, Vertical, Horizontal }

public sealed record BackgroundSettings
{
    public string Color { get; init; } = "0b1320ff";
    public string ImagePath { get; init; } = "";
    public BackgroundFit Fit { get; init; } = BackgroundFit.Contain;
    public BackgroundGradient Gradient { get; init; }
    public string EndColor { get; init; } = "203551ff";

    public void Validate()
    {
        VisualSettings.ValidateColor(Color, "背景");
        VisualSettings.ValidateColor(EndColor, "渐变终点");
        if (!Enum.IsDefined(Gradient)) throw new ArgumentException("未知背景渐变方向。");
        if (ImagePath == null || (ImagePath.Length > 0 &&
            Path.GetExtension(ImagePath).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".webp")))
            throw new ArgumentException("背景图片需要为 PNG、JPEG 或 WebP 文件，或留空使用背景色。");
        if (Fit is not BackgroundFit.Contain and not BackgroundFit.Cover)
            throw new ArgumentException("未知背景图片填充模式。");
    }
}
