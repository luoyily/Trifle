using System;

namespace Trifle.Score;

public static class ScorePulse
{
    public const double DurationSeconds = 0.5;
    public static double Gain(double secondsSinceOnset)
    {
        if (!double.IsFinite(secondsSinceOnset) || secondsSinceOnset < 0 || secondsSinceOnset >= DurationSeconds) return 1;
        double remaining = 1 - secondsSinceOnset / DurationSeconds;
        return 1 + 1.5 * remaining * remaining;
    }
}
