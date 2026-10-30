using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.Midi;
using WindowsMidiOutPort = Windows.Devices.Midi.IMidiOutPort;

namespace Trifle.Midi;

// Windows MIDI 1.0 (WinRT) destination. Windows MIDI Services translates UMP loopback
// endpoints such as "Default App Loopback (A)" into these byte-stream ports.
public sealed class WinRtMidiOutPort : IMidiOutPort
{
    private readonly WindowsMidiOutPort _port;

    private WinRtMidiOutPort(WindowsMidiOutPort port, string name)
    {
        _port = port;
        Name = name;
    }

    public string Name { get; }

    public static async Task<MidiOutPortInfo[]> EnumerateAsync()
    {
        var devices = await DeviceInformation.FindAllAsync(MidiOutPort.GetDeviceSelector());
        var ports = new List<MidiOutPortInfo>();
        foreach (var device in devices) ports.Add(new MidiOutPortInfo(device.Id, device.Name));
        return ports.ToArray();
    }

    public static async Task<IMidiOutPort> OpenAsync(string id, string name)
    {
        WindowsMidiOutPort port = await MidiOutPort.FromIdAsync(id);
        return port == null ? null : new WinRtMidiOutPort(port, name);
    }

    public void Send(MidiOutMessage message) => _port.SendMessage(Create(message));

    public void Dispose()
    {
        if (_port is IDisposable disposable) disposable.Dispose();
    }

    private static IMidiMessage Create(MidiOutMessage message)
    {
        byte channel = (byte)(message.Status & 0x0F);
        return (message.Status & 0xF0) switch
        {
            0x80 => new MidiNoteOffMessage(channel, message.Data1, message.Data2),
            0x90 => new MidiNoteOnMessage(channel, message.Data1, message.Data2),
            0xA0 => new MidiPolyphonicKeyPressureMessage(channel, message.Data1, message.Data2),
            0xB0 => new MidiControlChangeMessage(channel, message.Data1, message.Data2),
            0xC0 => new MidiProgramChangeMessage(channel, message.Data1),
            0xD0 => new MidiChannelPressureMessage(channel, message.Data1),
            0xE0 => new MidiPitchBendChangeMessage(channel,
                (ushort)((message.Data2 << 7) | message.Data1)),
            _ => throw new NotSupportedException($"Unsupported MIDI status 0x{message.Status:X2}.")
        };
    }
}

// Raises the system timer resolution while the send thread runs so Thread.Sleep(1) really
// waits about a millisecond instead of a scheduler tick.
internal static class MidiTimerResolution
{
    private const uint Milliseconds = 1;

    [System.Runtime.InteropServices.DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint period);

    [System.Runtime.InteropServices.DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint period);

    public static void Begin() => timeBeginPeriod(Milliseconds);
    public static void End() => timeEndPeriod(Milliseconds);
}
