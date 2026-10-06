namespace Trifle.Midi;

public sealed record MidiNote(
    int TrackIndex, int Channel, int Pitch, int Velocity,
    double StartSeconds, double EndSeconds)
{
    public double DurationSeconds => EndSeconds - StartSeconds;
    public bool IsActiveAt(double seconds) => StartSeconds <= seconds && seconds < EndSeconds;
}

public sealed record MidiTrack(int Index, string Name, int NoteCount, int[] Channels);
public sealed record MidiTempoChange(double TimeSeconds, double BeatsPerMinute);

public sealed record MidiSong(
    string SourcePath, int Format, MidiTrack[] Tracks,
    MidiTempoChange[] TempoChanges, MidiNote[] Notes, double DurationSeconds);
