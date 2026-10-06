using System;
using System.IO;

namespace Trifle.Export;

// A saved draft can reference unavailable destinations; export validates the actual filesystem later.
public sealed record ExportPreferences
{
    public string OutputPath { get; init; } = "";
    public double StartSeconds { get; init; }
    public double? EndSeconds { get; init; }
    // Null is an older file whose full-range intent is inferred once at load.
    public bool? FollowTimelineEnd { get; init; }
    public string FfmpegPath { get; init; } = "ffmpeg";
    public int Width { get; init; } = 1920;
    public int Height { get; init; } = 1080;
    public int FramesPerSecond { get; init; } = 30;
    public EncodingSettings Encoding { get; init; } = new();

    public void Validate(double? songDuration = null)
    {
        if (Encoding == null) throw new ArgumentException("编码设置不能为空。");
        Encoding.Validate();
        if (FollowTimelineEnd == false && !EndSeconds.HasValue)
            throw new ArgumentException("自选导出区间需要指定结束时间。");
        if ((Width, Height) is not ((1920, 1080) or (2560, 1440) or (3840, 2160)) ||
            FramesPerSecond is not (24 or 30 or 60))
            throw new ArgumentException("导出规格需要为 1080p / 1440p / 4K 与 24 / 30 / 60 FPS。");
        if (OutputPath == null || (OutputPath.Length > 0 &&
            !string.Equals(Path.GetExtension(OutputPath), ".mp4", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("导出路径需要为 MP4 文件或留空使用默认路径。");
        if (!double.IsFinite(StartSeconds) || StartSeconds < 0 ||
            (EndSeconds.HasValue && (!double.IsFinite(EndSeconds.Value) ||
                (EndSeconds.Value <= StartSeconds && !(StartSeconds == 0 && EndSeconds.Value == 0)))))
            throw new ArgumentException("导出区间无效。");
        if (songDuration.HasValue && songDuration.Value > 0 &&
            (StartSeconds >= songDuration.Value || (EndSeconds.HasValue &&
                (EndSeconds.Value > songDuration.Value || EndSeconds.Value <= StartSeconds))))
             throw new ArgumentException("导出区间超出当前播放时长，请检查 MIDI 和音频。");
        if (songDuration == 0 && (StartSeconds != 0 || EndSeconds.GetValueOrDefault() != 0))
             throw new ArgumentException("播放时长为零时，导出区间需要为零。");
        if (string.IsNullOrWhiteSpace(FfmpegPath)) throw new ArgumentException("FFmpeg 路径不能为空。");
    }
}
