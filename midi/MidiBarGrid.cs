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
}
