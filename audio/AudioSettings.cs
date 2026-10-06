using System;

namespace Trifle.Audio;

public sealed record AudioSettings
{
    public string Path { get; init; } = "";
    public bool Enabled { get; init; } = true;
    // Positive offset delays audio: source time = song time - offset.
    public double OffsetSeconds { get; init; }

    public void Validate()
    {
        if (Path == null || (Path.Length > 0 &&
            System.IO.Path.GetExtension(Path).ToLowerInvariant() is not (".ogg" or ".mp3" or ".wav")))
            throw new ArgumentException("音频需要为 OGG / Vorbis、MP3 或 WAV 文件，或留空不使用音频。");
        if (!double.IsFinite(OffsetSeconds) || Math.Abs(OffsetSeconds) > 3600)
            throw new ArgumentException("音频偏移需要在 -3600–3600 秒之间。");
    }
}
