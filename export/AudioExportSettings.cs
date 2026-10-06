using System;
using System.IO;
using Trifle.Audio;

namespace Trifle.Export;

public sealed record AudioExportSettings(string Path, double OffsetSeconds, double DurationSeconds)
{
    public void Validate()
    {
        new AudioSettings { Path = Path, OffsetSeconds = OffsetSeconds }.Validate();
        if (!System.IO.Path.IsPathFullyQualified(Path) || !File.Exists(Path))
            throw new FileNotFoundException("导出音频文件不存在，请重新选择或关闭音频。", Path);
        if (!double.IsFinite(DurationSeconds) || DurationSeconds <= 0)
            throw new ArgumentException("导出音频时长无效。");
    }
}
