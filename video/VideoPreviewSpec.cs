using System;

namespace Trifle.Video;

public sealed record VideoPreviewSpec(int Height = 720, int FramesPerSecond = 30, int OutputWidth = 0)
{
    public int Width => OutputWidth > 0 ? OutputWidth : Height switch { 480 => 854, 720 => 1280, 1080 => 1920, _ => 0 };
    public int FrameBytes => checked(Width * Height * 4);

    public void Validate()
    {
        if (OutputWidth != 0)
        {
            if (Width <= 0 || Height <= 0 || Width % 2 != 0 || Height % 2 != 0 || FramesPerSecond <= 0 || FramesPerSecond > 240)
                throw new ArgumentException("视频导出解码需要正偶数尺寸和 1–240 FPS。");
            return;
        }
        if (Width == 0 || FramesPerSecond is not (15 or 30))
            throw new ArgumentException("视频预览需要为 480p / 720p / 1080p 与 15 / 30 FPS。");
    }
}
