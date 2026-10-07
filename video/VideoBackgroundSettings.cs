using System;
using System.IO;

namespace Trifle.Video;

public sealed record VideoBackgroundSettings
{
    public string Path { get; init; } = "";
    public int PreviewHeight { get; init; } = 720;
    public int PreviewFramesPerSecond { get; init; } = 30;
    public bool Loop { get; init; } = true;
    public double OffsetSeconds { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public VideoPreviewSpec PreviewSpec => new(PreviewHeight, PreviewFramesPerSecond);

    public static bool IsVideoPath(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant()
        is ".mp4" or ".mkv" or ".mov" or ".webm" or ".avi" or ".m4v";

    public void Validate()
    {
        if (Path == null || (Path.Length > 0 && !IsVideoPath(Path)))
            throw new ArgumentException("背景视频需要为 MP4 / MKV / MOV / WebM / AVI / M4V，或留空。");
        PreviewSpec.Validate();
        if (!double.IsFinite(OffsetSeconds) || Math.Abs(OffsetSeconds) > 3600)
            throw new ArgumentException("视频偏移需要在 -3600–3600 秒之间。");
    }
}
