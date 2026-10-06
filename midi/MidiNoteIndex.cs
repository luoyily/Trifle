using System;

namespace Trifle.Midi;

// Notes are sorted by onset. Prefix end times retain long notes when seeking into a later passage.
public sealed class MidiNoteIndex
{
    public MidiNote[] Notes { get; }
    private readonly double[] _latestEnd;

    public MidiNoteIndex(MidiNote[] notes)
    {
        Notes = notes;
        _latestEnd = new double[notes.Length];
        double end = 0;
        for (int i = 0; i < notes.Length; i++)
        {
            if (i > 0 && notes[i].StartSeconds < notes[i - 1].StartSeconds)
                throw new ArgumentException("音符索引需要按起点排序。");
            end = Math.Max(end, notes[i].EndSeconds);
            _latestEnd[i] = end;
        }
    }

    public int FirstStartingAtOrAfter(double time) => StartBound(time, false);
    public int FirstStartingAfter(double time) => StartBound(time, true);

    private int StartBound(double time, bool includeBoundary)
    {
        int lo = 0, hi = Notes.Length;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (Notes[mid].StartSeconds < time || (includeBoundary && Notes[mid].StartSeconds == time)) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    public int FirstStillRelevant(double time)
    {
        int lo = 0, hi = Notes.Length;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (_latestEnd[mid] <= time) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
}
