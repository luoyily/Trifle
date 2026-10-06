using System;

namespace Trifle.App;

// Display whole elapsed seconds without rounding into the next minute.
// Playback, seeking and audio offsets retain their original precision.
public static class TimeText
{
    public static string Format(double seconds, bool showHours = false)
    {
        long whole = (long)Math.Floor(Math.Max(0, seconds));
        return showHours || whole >= 3600
            ? $"{whole / 3600:00}:{whole / 60 % 60:00}:{whole % 60:00}"
            : $"{whole / 60:00}:{whole % 60:00}";
    }
}
