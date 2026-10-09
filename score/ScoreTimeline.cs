using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Trifle.Midi;

namespace Trifle.Score;

public sealed record ScoreMeasureEvent(int MeasureId, double Seconds);

// Playback timing is separate from the source SVG and nominal MuseScore events.
public sealed class ScoreTimeline
{
    public ScoreEvent[] Events { get; }
    public double DurationSeconds { get; }
    private readonly ScoreRow[] _rows;

    internal ScoreTimeline(ScoreRow[] rows, ScoreEvent[] events, double duration)
    { _rows = rows; Events = events; DurationSeconds = duration; }

    public static ScoreTimeline ForExternalMidi(MuseScoreBundle bundle, MidiSong song, ScoreSyncSettings settings)
    {
        settings.Validate();
        if (song.BarGrid == null) throw new InvalidDataException("外部 MIDI 乐谱同步需要 PPQ 时间格式；不支持 SMPTE 时间格式。");
        var measures = bundle.MeasureEvents;
        if (measures.Length == 0) throw new InvalidDataException("乐谱缺少小节时间表，请重新使用 MuseScore 导出数据包。");
        var seen = new HashSet<int>();
        for (int i = 0; i < measures.Length; i++)
        {
            if (!seen.Add(measures[i].MeasureId) || (i > 0 && measures[i].MeasureId <= measures[i - 1].MeasureId))
                throw new InvalidDataException("外部 MIDI 同步暂不支持含反复的乐谱，请使用乐谱内 MIDI 或展开反复后重新导入。");
            if (i > 0 && measures[i].Seconds <= measures[i - 1].Seconds)
                throw new InvalidDataException("乐谱小节时间表无效。");
        }
        // MuseScore metadata duration is rounded to whole seconds. The bundled SMF
        // keeps the final bar's actual end, including a sustained final chord/rest.
        double end = bundle.DurationSeconds;
        if (bundle.Midi.Length > 0)
        {
            using var stream = new MemoryStream(bundle.Midi);
            end = Math.Max(end, MidiImporter.Read(stream).DurationSeconds);
        }
        if (end <= measures[^1].Seconds) throw new InvalidDataException("乐谱缺少最后一小节的有效时长。");
        var mapped = new ScoreEvent[bundle.Events.Length];
        var starts = Enumerable.Range(0, measures.Length + 1)
            .Select(i => song.BarGrid.StartSeconds(i + settings.MeasureOffset)).ToArray();
        int measure = 0;
        for (int i = 0; i < mapped.Length; i++)
        {
            var onset = bundle.Events[i];
            while (measure + 1 < measures.Length && measures[measure + 1].Seconds <= onset.Seconds) measure++;
            double nominalStart = measures[measure].Seconds;
            double nominalEnd = measure + 1 < measures.Length ? measures[measure + 1].Seconds : end;
            double start = starts[measure], finish = starts[measure + 1];
            double fraction = Math.Clamp((onset.Seconds - nominalStart) / (nominalEnd - nominalStart), 0, 1);
            mapped[i] = onset with { Seconds = start + fraction * (finish - start) };
        }
        return new ScoreTimeline(bundle.Rows, mapped,
            Math.Max(0, starts[^1]));
    }

    public ScoreCursor CursorAt(double seconds)
    {
        if (!double.IsFinite(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
        int low = 0, high = Events.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (Events[middle].Seconds <= seconds) low = middle + 1; else high = middle;
        }
        int index = Math.Max(0, low - 1);
        var current = Events[index];
        double x = current.X;
        if (index + 1 < Events.Length && seconds >= current.Seconds)
        {
            var next = Events[index + 1];
            // Repeated passages in bundled-MIDI mode jump at their onset, never slide backwards.
            if (next.Row != current.Row || next.X >= current.X)
            {
                double endX = next.Row == current.Row ? next.X : Math.Max(current.X, _rows[current.Row].Right);
                double factor = Math.Clamp((seconds - current.Seconds) / Math.Max(1e-9, next.Seconds - current.Seconds), 0, 1);
                x += (endX - x) * factor;
            }
        }
        return new ScoreCursor(index, current.Row, x);
    }
}
