using Godot;
using System;
using Trifle.Midi;

namespace Trifle.Visuals;

public enum NoteColorMode { Channel, Track }

// One color lookup shared by notes, keys, and future hit effects.
public sealed class NoteColors
{
    private static readonly Color[] Defaults =
    {
        new("69d2ff"), new("ff89b7"), new("ffc76b"), new("8ce1b0"),
        new("b99dff"), new("66e2dc"), new("ffa38c"), new("d6e58b"),
        new("92b8ff"), new("ed9ae5"), new("deb987"), new("9ed1a3"),
        new("9c9ae9"), new("78cbd6"), new("e5a19b"), new("bdd7a8")
    };
    private readonly Color[] _channelColors = (Color[])Defaults.Clone();
    private Color[] _trackColors = Array.Empty<Color>();
    public NoteColorMode Mode { get; private set; } = NoteColorMode.Channel;

    public void ResetChannels() => Array.Copy(Defaults, _channelColors, Defaults.Length);

    public void SetSong(MidiSong song)
    {
        _trackColors = new Color[song.Tracks.Length];
        int colorIndex = 0;
        foreach (var track in song.Tracks)
        {
            _trackColors[track.Index] = Defaults[colorIndex % Defaults.Length];
            if (track.NoteCount > 0) colorIndex++; // Tempo-only tracks do not consume a color.
        }
    }

    public void SetMode(NoteColorMode mode)
    {
        if (mode != NoteColorMode.Channel && mode != NoteColorMode.Track)
            throw new ArgumentOutOfRangeException(nameof(mode));
        Mode = mode;
    }

    public Color GetColor(MidiNote note) => Mode == NoteColorMode.Track
        ? GetTrackColor(note.TrackIndex) : GetChannelColor(note.Channel);
    public Color GetChannelColor(int channel) => _channelColors[channel];
    public Color GetTrackColor(int trackIndex) => _trackColors[trackIndex];

    public void SetChannelColor(int channel, Color color)
    {
        if (channel < 0 || channel >= _channelColors.Length)
            throw new ArgumentOutOfRangeException(nameof(channel));
        _channelColors[channel] = color;
    }

    public void SetTrackColor(int trackIndex, Color color)
    {
        if (trackIndex < 0 || trackIndex >= _trackColors.Length)
            throw new ArgumentOutOfRangeException(nameof(trackIndex));
        _trackColors[trackIndex] = color;
    }
}
