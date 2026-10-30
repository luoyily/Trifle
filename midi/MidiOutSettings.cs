using System;

namespace Trifle.Midi;

public sealed record MidiOutSettings
{
    public bool Enabled { get; init; }
    public string PortId { get; init; } = "";
    public string PortName { get; init; } = "";

    public void Validate()
    {
        if (PortId == null || PortName == null)
            throw new ArgumentException("MIDI 输出端口参数不能为空值。");
    }
}
