using System;

namespace Trifle.App;

public sealed record PreviewSettings
{
    public int Height { get; init; } = 1080;
    public int FramesPerSecond { get; init; } = 60;
    public int Width => Height * 16 / 9;
    public void Validate()
    {
        if (Height is not (540 or 720 or 1080 or 1440 or 2160) || FramesPerSecond is not (24 or 30 or 60))
            throw new ArgumentException("预览规格需要为 540p / 720p / 1080p / 1440p / 4K 与 24 / 30 / 60 FPS。");
    }
}
