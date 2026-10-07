using System;
using System.Globalization;
using Trifle.Visuals;

namespace Trifle.Video;

// Decode and fit only. Image/video appearance is handled by the same HDR layer.
public static class VideoFilters
{
    public static string Number(double value) => value.ToString("0.#########", CultureInfo.InvariantCulture);

    // Input seeking combined with stream_loop can rewrite loop timestamps using
    // only the initial tail's duration. Decode that tail once, then concatenate a
    // separate unseeked looping input so subsequent cycles retain source timing.
    public static string Timeline(int input, int? loopInput = null) => loopInput.HasValue
        ? $"[{input}:v:0]setpts=PTS-STARTPTS[tail];[{loopInput}:v:0]setpts=PTS-STARTPTS[loop];[tail][loop]concat=n=2:v=1:a=0,"
        : $"[{input}:v:0]setpts=PTS-STARTPTS,";

    public static string Build(BackgroundSettings background, int width, int height, int fps)
    {
        background.Validate();
        string scale = $"scale={width}:{height}:force_original_aspect_ratio=" +
            (background.Fit == BackgroundFit.Contain ? "decrease" : "increase") + ":force_divisible_by=2:reset_sar=1";
        string fit = background.Fit == BackgroundFit.Contain
            ? $"pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:color=black@0"
            : $"crop={width}:{height}";
        return $"fps={fps}:start_time=0,{scale},format=rgba,{fit},setsar=1";
    }

    public static double SeekTime(double sourceTime, VideoBackgroundSettings settings, VideoMetadata metadata) =>
        settings.Loop ? Math.Max(0, sourceTime) % metadata.DurationSeconds :
        Math.Clamp(sourceTime, 0, Math.Max(0, metadata.DurationSeconds - Math.Max(1 / metadata.FramesPerSecond, 1 / (double)settings.PreviewFramesPerSecond)));
}
