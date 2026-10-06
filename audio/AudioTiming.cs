using System;

namespace Trifle.Audio;

public sealed record AudioWindow(double SourceStart, double Duration, double Delay);

// Preview and export use the same offset sign and never stretch the MIDI timeline.
public static class AudioTiming
{
    public static double SourceTime(double songTime, double offset) => songTime - offset;

    public static double PlaybackDuration(double midiDuration, bool audioEnabled, double offset, double audioDuration)
    {
        if (!double.IsFinite(midiDuration) || midiDuration < 0 || !double.IsFinite(offset) ||
            !double.IsFinite(audioDuration) || audioDuration < 0)
            throw new ArgumentException("播放时长无效。");
        return audioEnabled ? Math.Max(midiDuration, Math.Max(0, offset + audioDuration)) : midiDuration;
    }

    public static AudioWindow Clip(double songStart, double videoDuration, double offset, double audioLength)
    {
        if (!double.IsFinite(songStart) || songStart < 0 || !double.IsFinite(videoDuration) || videoDuration <= 0 ||
            !double.IsFinite(offset) || !double.IsFinite(audioLength) || audioLength < 0)
            throw new ArgumentException("音频截取时间无效。");
        double first = Math.Max(songStart, offset);
        double last = Math.Min(songStart + videoDuration, offset + audioLength);
        return new AudioWindow(Math.Max(0, songStart - offset), Math.Max(0, last - first),
            Math.Clamp(offset - songStart, 0, videoDuration));
    }
}
