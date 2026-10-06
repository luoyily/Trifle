using System;
using System.IO;

namespace Trifle.Export;

public sealed record ExportSettings(
    string OutputPath, double StartSeconds, double EndSeconds,
    int Width = 1920, int Height = 1080, int FramesPerSecond = 30, bool Overwrite = false,
    EncodingSettings Encoding = null)
{
    public EncodingSettings EffectiveEncoding => Encoding ?? new EncodingSettings();
    public int FrameCount
    {
        get
        {
            double frames = (EndSeconds - StartSeconds) * FramesPerSecond;
            double rounded = Math.Round(frames);
            // Avoid an extra frame caused only by floating-point subtraction at exact boundaries.
            if (rounded > 0 && Math.Abs(frames - rounded) < 1e-9) frames = rounded;
            double count = Math.Ceiling(frames);
            if (!double.IsFinite(count) || count <= 0 || count > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(EndSeconds), "导出区间必须至少包含一帧。");
            return (int)count;
        }
    }

    public double FrameTime(int frame)
    {
        if (frame < 0 || frame >= FrameCount) throw new ArgumentOutOfRangeException(nameof(frame));
        return StartSeconds + frame / (double)FramesPerSecond;
    }

    public void Validate()
    {
        EffectiveEncoding.Validate();
        if (!double.IsFinite(StartSeconds) || StartSeconds < 0 || !double.IsFinite(EndSeconds) || EndSeconds <= StartSeconds)
            throw new ArgumentException("结束时间需要大于开始时间，开始时间不能为负数。");
        if (FramesPerSecond <= 0 || FramesPerSecond > 240)
            throw new ArgumentOutOfRangeException(nameof(FramesPerSecond));
        if (Width <= 0 || Height <= 0 || Width % 2 != 0 || Height % 2 != 0)
            throw new ArgumentException("视频输出宽高需要为正偶数。");
        _ = FrameCount;
        if (string.IsNullOrWhiteSpace(OutputPath) || !Path.IsPathFullyQualified(OutputPath) ||
            !string.Equals(Path.GetExtension(OutputPath), ".mp4", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("请选择 MP4 文件的绝对输出路径。");
        if (!Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(OutputPath))))
            throw new DirectoryNotFoundException("输出目录不存在。");
        if (!Overwrite && File.Exists(OutputPath))
            throw new IOException("同名文件已存在，请选择其他文件名或确认覆盖。");
    }
}
