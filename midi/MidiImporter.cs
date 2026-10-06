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

        return new MidiSong(sourcePath, (int)file.OriginalFormat,
            tracks.ToArray(), tempoChanges.ToArray(), sortedNotes, duration);
    }
}
