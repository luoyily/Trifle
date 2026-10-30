using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Trifle.Midi;

public static class MidiImporter
{
    public static MidiSong Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream, path);
    }

    public static MidiSong Read(Stream stream, string sourcePath = "")
    {
        var file = MidiFile.Read(stream, new ReadingSettings
        {
            SilentNoteOnPolicy = SilentNoteOnPolicy.NoteOff
        });
        if (file.OriginalFormat == MidiFileFormat.MultiSequence)
            throw new InvalidDataException("暂不支持 MIDI Format 2，请使用 Format 0 或 Format 1。");

        var tempoMap = file.GetTempoMap();
        double Seconds(long ticks) =>
            TimeConverter.ConvertTo<MetricTimeSpan>(ticks, tempoMap).TotalMicroseconds / 1_000_000.0;

        var notes = new List<MidiNote>();
        var tracks = new List<MidiTrack>();
        var detection = new NoteDetectionSettings
        {
            // Pair the earliest unclosed Note On with each Note Off (FIFO).
            NoteStartDetectionPolicy = NoteStartDetectionPolicy.FirstNoteOn
        };

        foreach (var chunk in file.GetTrackChunks())
        {
            int trackIndex = tracks.Count;
            var trackNotes = chunk.GetNotes(detection).Select(note => new MidiNote(
                trackIndex, (int)note.Channel, (int)note.NoteNumber, (int)note.Velocity,
                Seconds(note.Time), Seconds(note.Time + note.Length))).ToArray();
            notes.AddRange(trackNotes);
            string name = chunk.Events.OfType<SequenceTrackNameEvent>().FirstOrDefault()?.Text;
            tracks.Add(new MidiTrack(trackIndex,
                string.IsNullOrWhiteSpace(name) ? $"Track {trackIndex + 1}" : name,
                trackNotes.Length, trackNotes.Select(note => note.Channel).Distinct().Order().ToArray()));
        }

        var tempoChanges = tempoMap.GetTempoChanges().Select(change => new MidiTempoChange(
            Seconds(change.Time), change.Value.BeatsPerMinute)).ToList();
        if (tempoChanges.Count == 0 || tempoChanges[0].TimeSeconds > 0)
            tempoChanges.Insert(0, new MidiTempoChange(0, 120));

        var sortedNotes = notes.OrderBy(note => note.StartSeconds)
            .ThenBy(note => note.TrackIndex).ThenBy(note => note.Pitch).ToArray();
        long endTick = file.GetTimedEvents().Select(timed => timed.Time).DefaultIfEmpty(0).Max();
        double duration = System.Math.Max(Seconds(endTick),
            sortedNotes.Select(note => note.EndSeconds).DefaultIfEmpty(0).Max());

        // Keep every channel voice message. The output layer decides what to forward, so
        // program changes and channel-mode messages are still available to future features.
        var channelEvents = new List<MidiChannelEvent>();
        foreach (var timed in file.GetTimedEvents())
        {
            if (timed.Event is not ChannelEvent channel) continue;
            if (!TryEncode(channel, out byte status, out byte data1, out byte data2)) continue;
            channelEvents.Add(new MidiChannelEvent(Seconds(timed.Time), status, data1, data2));
        }
        CloseDanglingNotes(channelEvents, duration);

        return new MidiSong(sourcePath, (int)file.OriginalFormat,
            tracks.ToArray(), tempoChanges.ToArray(), sortedNotes, duration)
        {
            BarGrid = file.TimeDivision is TicksPerQuarterNoteTimeDivision division
                ? new MidiBarGrid(tempoMap, division) : null,
            ChannelEvents = channelEvents.ToArray()
        };
    }

    private static bool TryEncode(ChannelEvent channel, out byte status, out byte data1, out byte data2)
    {
        byte index = (byte)(int)channel.Channel;
        status = data1 = data2 = 0;
        switch (channel)
        {
            case NoteOnEvent note:
                status = (byte)(0x90 | index); data1 = (byte)(int)note.NoteNumber; data2 = (byte)(int)note.Velocity; return true;
            case NoteOffEvent note:
                status = (byte)(0x80 | index); data1 = (byte)(int)note.NoteNumber; data2 = (byte)(int)note.Velocity; return true;
            case ControlChangeEvent control:
                status = (byte)(0xB0 | index); data1 = (byte)(int)control.ControlNumber; data2 = (byte)(int)control.ControlValue; return true;
            case PitchBendEvent bend:
                status = (byte)(0xE0 | index); data1 = (byte)(bend.PitchValue & 0x7F); data2 = (byte)(bend.PitchValue >> 7); return true;
            case ChannelAftertouchEvent pressure:
                status = (byte)(0xD0 | index); data1 = (byte)(int)pressure.AftertouchValue; return true;
            case NoteAftertouchEvent pressure:
                status = (byte)(0xA0 | index); data1 = (byte)(int)pressure.NoteNumber; data2 = (byte)(int)pressure.AftertouchValue; return true;
            case ProgramChangeEvent program:
                status = (byte)(0xC0 | index); data1 = (byte)(int)program.ProgramNumber; return true;
            default:
                return false;
        }
    }

    // Malformed files can leave a Note On without its Note Off. Close those at the end of the
    // song so replaying them can never leave a stuck note on the destination device.
    private static void CloseDanglingNotes(List<MidiChannelEvent> events, double durationSeconds)
    {
        var open = new Dictionary<int, int>();
        foreach (var channelEvent in events)
        {
            int key = ((channelEvent.Status & 0x0F) << 8) | channelEvent.Data1;
            switch (channelEvent.Status & 0xF0)
            {
                case 0x90 when channelEvent.Data2 > 0:
                    open[key] = open.TryGetValue(key, out int count) ? count + 1 : 1;
                    break;
                case 0x80:
                case 0x90:
                    if (open.TryGetValue(key, out int active) && active > 0) open[key] = active - 1;
                    break;
            }
        }
        foreach (var pair in open)
        {
            if (pair.Value <= 0) continue;
            events.Add(new MidiChannelEvent(durationSeconds,
                (byte)(0x80 | (pair.Key >> 8)), (byte)(pair.Key & 0x7F), 0));
        }
    }
}
