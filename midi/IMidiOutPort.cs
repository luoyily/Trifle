using System;

namespace Trifle.Midi;

public readonly record struct MidiOutPortInfo(string Id, string Name);

// One opened destination. Implementations are the only place that knows a platform API.
public interface IMidiOutPort : IDisposable
{
    string Name { get; }
    void Send(MidiOutMessage message);
}
