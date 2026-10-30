namespace Trifle.Midi;

// A channel voice message with its absolute song time, kept in file order. The scheduler
// replays these instead of synthesizing note pairs so overlaps and velocities stay exact.
public readonly record struct MidiChannelEvent(
    double TimeSeconds, byte Status, byte Data1, byte Data2)
{
    public int Channel => Status & 0x0F;
    public int Kind => Status & 0xF0;
}
