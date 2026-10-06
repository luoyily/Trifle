using System;
using Trifle.Midi;

namespace Trifle.Playback;

// Owns song time only. Godot supplies elapsed seconds; rendering reads TimeSeconds.
public sealed class PlaybackController
{
    private MidiSong _song;
    private int _nextNoteIndex;

    public double TimeSeconds { get; private set; }
    public bool IsPlaying { get; private set; }
    public double DurationSeconds { get; private set; }

    public event Action<MidiNote> NoteHit;

    public void SetSong(MidiSong song)
    {
        _song = song;
        DurationSeconds = song.DurationSeconds;
        Stop();
    }

    public void SetDuration(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        DurationSeconds = Math.Max(_song?.DurationSeconds ?? 0, seconds);
        if (TimeSeconds >= DurationSeconds) Seek(DurationSeconds);
    }

    public void Play()
    {
        if (_song == null || DurationSeconds <= 0) return;
        if (TimeSeconds >= DurationSeconds) Stop();
        IsPlaying = true;
    }

    public void Pause() => IsPlaying = false;

    public void Stop()
    {
        IsPlaying = false;
        TimeSeconds = 0;
        _nextNoteIndex = 0; // Fresh playback includes notes starting at exactly zero once.
    }

    public void Seek(double seconds)
    {
        if (!double.IsFinite(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
        TimeSeconds = Math.Clamp(seconds, 0, DurationSeconds);
        if (_song == null) return;
        // Skip all onsets at or before the destination; seeking does not emit historical hits.
        int low = 0, high = _song.Notes.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (_song.Notes[middle].StartSeconds <= TimeSeconds) low = middle + 1;
            else high = middle;
        }
        _nextNoteIndex = low;
        if (TimeSeconds >= DurationSeconds) Pause();
    }

    public void Advance(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (!IsPlaying || elapsedSeconds == 0) return;
        TimeSeconds = Math.Min(DurationSeconds, TimeSeconds + elapsedSeconds);
        // Catch every onset crossed, including short notes that both start and end within a frame.
        while (_nextNoteIndex < _song.Notes.Length &&
            _song.Notes[_nextNoteIndex].StartSeconds <= TimeSeconds)
        {
            var note = _song.Notes[_nextNoteIndex++];
            NoteHit?.Invoke(note);
        }
        if (TimeSeconds >= DurationSeconds) Pause();
    }
}
