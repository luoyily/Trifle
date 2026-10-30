using System;
using System.Collections.Generic;

namespace Trifle.Midi;

public readonly record struct MidiOutMessage(byte Status, byte Data1, byte Data2)
{
    public int Channel => Status & 0x0F;
    public int Kind => Status & 0xF0;
}

// Replays raw channel events against song time. Filtering is an output policy: program and
// bank changes stay out so the destination keeps its own patch, and channel-mode messages
// stay out because this player owns note lifetime.
public sealed class MidiOutScheduler
{
    private MidiChannelEvent[] _events = Array.Empty<MidiChannelEvent>();
    private readonly Dictionary<int, int> _sounding = new();
    private int _index;
    private double _timeSeconds;

    public double TimeSeconds => _timeSeconds;
    public int EventCount => _events.Length;

    public void SetEvents(MidiChannelEvent[] events)
    {
        _events = events ?? Array.Empty<MidiChannelEvent>();
        Seek(0);
    }

    // Positions before the first event at or after `seconds`, so a note exactly on the
    // playback start still fires. Callers flush before repositioning.
    public void Seek(double seconds)
    {
        _timeSeconds = Math.Max(0, seconds);
        int low = 0, high = _events.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (_events[middle].TimeSeconds < _timeSeconds) low = middle + 1;
            else high = middle;
        }
        _index = low;
        _sounding.Clear();
    }

    // Emits every forwarded event in (_timeSeconds, upTo] and advances the cursor.
    public void Advance(double upTo, List<MidiOutMessage> output)
    {
        if (upTo <= _timeSeconds) return;
        while (_index < _events.Length && _events[_index].TimeSeconds <= upTo)
        {
            var channelEvent = _events[_index++];
            if (!IsForwarded(channelEvent)) continue;
            Track(channelEvent);
            output.Add(new MidiOutMessage(channelEvent.Status, channelEvent.Data1, channelEvent.Data2));
        }
        _timeSeconds = upTo;
    }

    // Releases everything still sounding, plus All Notes Off per affected channel as a
    // safety net. Used on pause, stop, seek, song changes and shutdown.
    public void Flush(List<MidiOutMessage> output)
    {
        var channels = new HashSet<int>();
        foreach (var pair in _sounding)
        {
            if (pair.Value <= 0) continue;
            output.Add(new MidiOutMessage((byte)(0x80 | (pair.Key >> 8)), (byte)(pair.Key & 0x7F), 0));
            channels.Add(pair.Key >> 8);
        }
        foreach (int channel in channels)
            output.Add(new MidiOutMessage((byte)(0xB0 | channel), 123, 0));
        _sounding.Clear();
    }

    private void Track(in MidiChannelEvent channelEvent)
    {
        int key = ((channelEvent.Status & 0x0F) << 8) | channelEvent.Data1;
        switch (channelEvent.Status & 0xF0)
        {
            case 0x90 when channelEvent.Data2 > 0:
                _sounding[key] = _sounding.TryGetValue(key, out int count) ? count + 1 : 1;
                break;
            case 0x80:
            case 0x90:
                if (_sounding.TryGetValue(key, out int active) && active > 0) _sounding[key] = active - 1;
                break;
        }
    }

    private static bool IsForwarded(in MidiChannelEvent channelEvent)
    {
        switch (channelEvent.Status & 0xF0)
        {
            case 0x80: // note off
            case 0x90: // note on
            case 0xA0: // polyphonic key pressure
            case 0xD0: // channel pressure
            case 0xE0: // pitch bend
                return true;
            case 0xB0: // control change
                if (channelEvent.Data1 is 0 or 32) return false; // bank select belongs to a patch
                return channelEvent.Data1 < 120;                 // 120-127 are channel-mode messages
            default:
                return false; // program change and anything else stays out
        }
    }
}
