using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Trifle.Midi;
using Windows.Devices.Enumeration;
using Windows.Devices.Midi;
using Windows.Storage.Streams;

internal static class MidiOutChecks
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new Exception("FAIL: " + label);
            checks++; Console.WriteLine("PASS: " + label);
        }
        void Near(double actual, double expected, string label) =>
            Check(Math.Abs(actual - expected) < 0.000001, label);

        var forwarding = new[]
        {
            Event(0.0, 0x90, 60, 100),
            Event(0.5, 0xB0, 64, 127),
            Event(0.6, 0xE0, 0, 64),
            Event(1.0, 0x80, 60, 0),
            Event(1.1, 0xC0, 5, 0),
            Event(1.2, 0xB0, 0, 0),
            Event(1.3, 0xB0, 123, 0),
            Event(1.4, 0xD0, 40, 0)
        };
        var scheduler = new MidiOutScheduler();
        scheduler.SetEvents(forwarding);
        var output = new List<MidiOutMessage>();
        scheduler.Advance(2.0, output);
        Check(output.Count == 5, "Note, pedal, bend, channel pressure forwarded; program/bank/mode dropped");
        Check(output[0] == new MidiOutMessage(0x90, 60, 100), "Note on keeps channel, pitch and velocity");
        Check(output[1] == new MidiOutMessage(0xB0, 64, 127), "Sustain pedal forwarded");
        Check(output[2] == new MidiOutMessage(0xE0, 0, 64), "Pitch bend forwarded as LSB/MSB");
        Check(output[3] == new MidiOutMessage(0x80, 60, 0), "Note off forwarded");
        Check(output[4] == new MidiOutMessage(0xD0, 40, 0), "Channel pressure forwarded");

        scheduler.Flush(output);
        Check(output.Count == 5, "Nothing left to flush after all notes ended");

        var sustained = new[]
        {
            Event(0.0, 0x90, 60, 100),
            Event(0.1, 0x90, 64, 100),
            Event(10.0, 0x80, 60, 0),
            Event(10.1, 0x80, 64, 0)
        };
        scheduler.SetEvents(sustained);
        output.Clear();
        scheduler.Advance(2.0, output);
        Check(output.Count == 2, "Two notes sounding");
        output.Clear();
        scheduler.Flush(output);
        Check(output.Count == 3, "Flush releases both notes and sends one All Notes Off");
        Check(output[0] == new MidiOutMessage(0x80, 60, 0) && output[1] == new MidiOutMessage(0x80, 64, 0),
            "Flush emits explicit note offs");
        Check(output[2] == new MidiOutMessage(0xB0, 123, 0), "All Notes Off follows on the used channel");
        output.Clear();
        scheduler.Flush(output);
        Check(output.Count == 0, "Repeated flush is empty");

        var twoChannels = new[]
        {
            Event(0.0, 0x91, 60, 100),
            Event(0.0, 0x92, 62, 100),
            Event(5.0, 0x81, 60, 0),
            Event(5.0, 0x82, 62, 0)
        };
        scheduler.SetEvents(twoChannels);
        scheduler.Advance(1.0, output);
        output.Clear();
        scheduler.Flush(output);
        Check(output.Count == 4 &&
            output.Any(m => m == new MidiOutMessage(0xB0 | 1, 123, 0)) &&
            output.Any(m => m == new MidiOutMessage(0xB0 | 2, 123, 0)),
            "All Notes Off is sent per affected channel");

        var seeks = new[]
        {
            Event(0.0, 0x90, 60, 100),
            Event(1.0, 0x90, 62, 100),
            Event(2.0, 0x80, 62, 0),
            Event(2.5, 0x80, 60, 0)
        };
        scheduler.SetEvents(seeks);
        output.Clear();
        scheduler.Advance(1.5, output);
        Check(output.Count == 2, "Playback before a seek emits both onsets");
        output.Clear();
        scheduler.Flush(output);
        output.Clear();
        scheduler.Seek(1.0);
        scheduler.Advance(2.6, output);
        Check(output.Count == 3, "Seek replays only events at or after the target");
        Check(output[0] == new MidiOutMessage(0x90, 62, 100) && output[1] == new MidiOutMessage(0x80, 62, 0),
            "Seek does not resend the past note");
        Check(output[2] == new MidiOutMessage(0x80, 60, 0),
            "A skipped onset still forwards its harmless explicit note off");
        output.Clear();
        scheduler.Flush(output);
        Check(output.Count == 0, "Seek left the past note out of the sounding set");

        scheduler.SetEvents(new[] { Event(0.0, 0x90, 72, 100), Event(1.0, 0x80, 72, 0) });
        output.Clear();
        scheduler.Seek(0.0);
        scheduler.Advance(0.001, output);
        Check(output.Count == 1, "A note exactly at the start time still fires");

        scheduler.SetEvents(new[]
        {
            Event(0.0, 0x90, 60, 80),
            Event(0.1, 0x90, 60, 90),
            Event(0.2, 0x80, 60, 0),
            Event(0.3, 0x80, 60, 0)
        });
        output.Clear();
        scheduler.Advance(1.0, output);
        Check(output.Count == 4, "Overlapping same-pitch notes replay both offs verbatim");
        output.Clear();
        scheduler.Flush(output);
        Check(output.Count == 0, "Overlapping same-pitch notes leave nothing sounding");

        scheduler.SetEvents(new[] { Event(0.0, 0x90, 60, 100), Event(1.0, 0x80, 60, 0) });
        output.Clear();
        scheduler.Advance(2.0, output);
        Check(output.Count == 2, "Note on and off both emitted");
        output.Clear();
        scheduler.Advance(3.0, output);
        Check(output.Count == 0, "Advancing past the end does not repeat events");

        RunImportChecks(Check, Near);
        Console.WriteLine($"MIDI out: {checks} targeted checks passed.");
    }

    private static void RunImportChecks(Action<bool, string> check, Action<double, double, string> near)
    {
        var file = new MidiFile(
            new TrackChunk(new SetTempoEvent(500000), new SetTempoEvent(1000000) { DeltaTime = 480 }),
            new TrackChunk(
                new ControlChangeEvent((SevenBitNumber)64, (SevenBitNumber)127),
                new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90) { DeltaTime = 240 },
                new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 240 },
                new PitchBendEvent(8192),
                new ProgramChangeEvent((SevenBitNumber)5) { DeltaTime = 240 }))
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480)
        };
        MidiSong song;
        using (var stream = new MemoryStream())
        {
            file.Write(stream);
            stream.Position = 0;
            song = MidiImporter.Read(stream, "fixture.mid");
        }
        check(song.ChannelEvents.Length == 5, "Importer keeps every channel voice message");
        check(song.ChannelEvents[0] == new MidiChannelEvent(0, 0xB0, 64, 127), "Pedal at time zero keeps values");
        check(song.ChannelEvents[1] == new MidiChannelEvent(0.25, 0x90, 60, 90), "Note on uses the tempo map");
        check(song.ChannelEvents[2] == new MidiChannelEvent(0.5, 0x80, 60, 0), "Note off uses the tempo map");
        check(song.ChannelEvents[3] == new MidiChannelEvent(0.5, 0xE0, 0, 64), "Pitch bend encoded as LSB/MSB");
        check(song.ChannelEvents[4] == new MidiChannelEvent(1.0, 0xC0, 5, 0), "Program change preserved for the output policy");
        near(song.ChannelEvents[2].TimeSeconds, 0.5, "Second tempo segment starts at 0.5 s");

        var danglingFile = new MidiFile(new TrackChunk(
            new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90),
            new ControlChangeEvent((SevenBitNumber)64, (SevenBitNumber)0) { DeltaTime = 480 }))
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480)
        };
        MidiSong dangling;
        using (var stream = new MemoryStream())
        {
            danglingFile.Write(stream);
            stream.Position = 0;
            dangling = MidiImporter.Read(stream, "dangling.mid");
        }
        var last = dangling.ChannelEvents[^1];
        check(last.Status == 0x80 && last.Data1 == 60, "A dangling Note On is closed with a synthetic Note Off");
        near(last.TimeSeconds, 0.5, "Synthetic Note Off lands at the end of the song");
    }

    // Live loopback check: sends through the production port wrapper and listens on the
    // paired B endpoint. Requires the Windows MIDI Services loopback pair.
    public static async Task RunLiveAsync()
    {
        var ports = await WinRtMidiOutPort.EnumerateAsync();
        foreach (var port in ports) Console.WriteLine("MIDI OUT: " + port.Name);
        var source = ports.FirstOrDefault(port => port.Name.Contains("Loopback (A)"));
        if (source.Id == null)
        {
            Console.WriteLine("SKIP: loopback A endpoint not found.");
            return;
        }
        using var output = await WinRtMidiOutPort.OpenAsync(source.Id, source.Name);
        if (output == null) throw new Exception("FAIL: could not open " + source.Name);

        var inputs = await DeviceInformation.FindAllAsync(MidiInPort.GetDeviceSelector());
        var target = inputs.FirstOrDefault(device => device.Name.Contains("Loopback (B)"));
        if (target == null)
        {
            Console.WriteLine("SKIP: loopback B endpoint not found.");
            return;
        }
        using var input = await MidiInPort.FromIdAsync(target.Id);
        if (input == null) throw new Exception("FAIL: could not open " + target.Name);

        var received = new List<byte[]>();
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        input.MessageReceived += (_, args) =>
        {
            var buffer = args.Message.RawData;
            var data = new byte[buffer.Length];
            using (var reader = DataReader.FromBuffer(buffer)) reader.ReadBytes(data);
            lock (received) received.Add(data);
            if (received.Count >= 6) done.TrySetResult(true);
        };
        await Task.Delay(300);

        for (int i = 0; i < 3; i++)
        {
            output.Send(new MidiOutMessage(0x90, (byte)(60 + i), 100));
            await Task.Delay(120);
            output.Send(new MidiOutMessage(0x80, (byte)(60 + i), 0));
            await Task.Delay(120);
        }
        await Task.WhenAny(done.Task, Task.Delay(5000));

        List<byte[]> snapshot;
        lock (received) snapshot = received.ToList();
        if (snapshot.Count < 6)
            throw new Exception($"FAIL: expected 6 loopback messages, got {snapshot.Count}.");
        if (!snapshot.Any(data => data.Length == 3 && data[0] == 0x90 && data[1] == 60 && data[2] == 100))
            throw new Exception("FAIL: loopback did not carry Note On 60 velocity 100.");
        if (!snapshot.Any(data => data.Length == 3 && data[0] == 0x80 && data[1] == 62))
            throw new Exception("FAIL: loopback did not carry Note Off 62.");
        Console.WriteLine($"PASS: loopback A -> B carried {snapshot.Count} messages through the production port.");
    }

    private static MidiChannelEvent Event(double seconds, byte status, byte data1, byte data2) =>
        new(seconds, status, data1, data2);
}
