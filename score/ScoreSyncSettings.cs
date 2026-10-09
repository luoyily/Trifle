using System;

namespace Trifle.Score;

public sealed record ScoreSyncSettings
{
    public int MeasureOffset { get; init; }
    public void Validate()
    {
        if (MeasureOffset < -999 || MeasureOffset > 999)
            throw new ArgumentException("乐谱小节偏移需要为 -999–999。");
    }
}
