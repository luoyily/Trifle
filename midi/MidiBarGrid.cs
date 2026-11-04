using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace Trifle.Midi;

// The importer's tempo map includes default 120 BPM / 4/4 and all FF51 / FF58 changes.
public sealed class MidiBarGrid
{
    private readonly TempoMap _tempoMap;
    public int TicksPerQuarterNote { get; }

    internal MidiBarGrid(TempoMap tempoMap, TicksPerQuarterNoteTimeDivision division)
    { _tempoMap = tempoMap; TicksPerQuarterNote = division.TicksPerQuarterNote; }

    // Zero-based bar index. Negative offsets extend the initial grid before song time zero.
    public double StartSeconds(int bar)
    {
        if (bar < 0) return bar * StartSeconds(1);
        return TimeConverter.ConvertTo<MetricTimeSpan>(new BarBeatTicksTimeSpan(bar), _tempoMap)
            .TotalMicroseconds / 1_000_000.0;
    }

    // Number of bars that begin before the performance ends. A trailing partial bar
    // still counts, because its grid slot exists even if the MIDI ends mid-bar.
    public int CountBars(double endSeconds)
    {
        if (!double.IsFinite(endSeconds) || endSeconds <= 0) return 0;
        int low = 0, high = 1;
        while (StartSeconds(high) < endSeconds)
        {
            low = high;
            if (high >= 1_048_576) return high;
            high *= 2;
        }
        while (low + 1 < high)
        {
            int middle = low + (high - low) / 2;
            if (StartSeconds(middle) < endSeconds) low = middle; else high = middle;
        }
        return low + 1;
    }
}
