using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Common;
using System.Text;
using System.Text.Json;
using Trifle.Midi;
using Trifle.Score;
using Trifle.Persistence;

static class ScoreSyncChecks
{
    public static void Run(string samplePath)
    {
        int checks = 0;
        void Check(bool condition, string label)
        { if (!condition) throw new Exception("FAIL: " + label); checks++; Console.WriteLine("PASS: " + label); }
        void Near(double actual, double expected, string label) => Check(Math.Abs(actual - expected) < 0.00001, label);
        void Reject(Action action, string label)
        {
            try { action(); } catch (ArgumentException) { Check(true, label); return; }
            catch (InvalidDataException) { Check(true, label); return; }
            throw new Exception("FAIL: " + label);
        }
        MidiSong Read(params MidiEvent[] events)
        {
            var file = new MidiFile(new TrackChunk(events)) { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };
            using var stream = new MemoryStream(); file.Write(stream); stream.Position = 0;
            return MidiImporter.Read(stream, "external.mid");
        }
        string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        const string measures = "<score><elements><element id='0' page='0' x='1200' y='2400' sx='4800' sy='1200'/><element id='1' page='0' x='6000' y='2400' sx='4800' sy='1200'/><element id='2' page='0' x='1200' y='6000' sx='9600' sy='1200'/></elements><events><event elid='0' position='0'/><event elid='1' position='2000'/><event elid='2' position='4000'/></events></score>";
        const string onsets = "<score><elements><element id='10' page='0' x='2400' y='2400' sx='120' sy='1200'/><element id='11' page='0' x='3600' y='2400' sx='120' sy='1200'/><element id='12' page='0' x='6600' y='2400' sx='120' sy='1200'/><element id='13' page='0' x='8400' y='2400' sx='120' sy='1200'/><element id='20' page='0' x='2400' y='6000' sx='120' sy='1200'/><element id='21' page='0' x='6000' y='6000' sx='120' sy='1200'/></elements><events><event elid='10' position='0'/><event elid='11' position='1000'/><event elid='12' position='2000'/><event elid='13' position='3000'/><event elid='20' position='4000'/><event elid='21' position='5000'/></events></score>";
        MuseScoreBundle Bundle(string mpos = measures) => MuseScoreBundle.Parse(JsonSerializer.Serialize(new {
            svgs = new[] { Encode("<svg xmlns='http://www.w3.org/2000/svg' width='1000' height='1000' viewBox='0 0 1000 1000'><path d='M100,200 L200,200 L200,220 Z'/></svg>") },
            mposXML = Encode(mpos), sposXML = Encode(onsets), metadata = new { duration = 6 }
        }));
        var bundle = Bundle();
        var defaultSong = Read(new TextEvent("End") { DeltaTime = 5760 });
        Check(defaultSong.BarGrid.TicksPerQuarterNote == 480, "Importer keeps MIDI PPQ for the bar grid");
        Near(defaultSong.BarGrid.StartSeconds(1), 2, "Missing tempo and signature default to 120 BPM and 4/4");
        var slow = Read(new SetTempoEvent(1000000), new TextEvent("End") { DeltaTime = 5760 });
        var timeline = ScoreTimeline.ForExternalMidi(bundle, slow, new());
        for (int i = 0; i < 6; i++) Near(timeline.Events[i].Seconds, i * 2, "Slow MIDI stretches onset " + i + " within its bar");
        Near(timeline.DurationSeconds, 12, "Mapped duration reaches the final MIDI bar boundary");
        Check(timeline.CursorAt(7.999).Row == 0 && timeline.CursorAt(8).Row == 1, "Mapped row boundary switches at the external bar start");
        Near(bundle.Events[1].Seconds, 1, "External mapping leaves nominal MuseScore events unchanged");
        var variable = Read(new SetTempoEvent(500000), new TimeSignatureEvent(4, 4),
            new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)70) { DeltaTime = 123 },
            new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 117 },
            new SetTempoEvent(1000000) { DeltaTime = 240 },
            new TimeSignatureEvent(3, 4) { DeltaTime = 1440 }, new TextEvent("End") { DeltaTime = 2880 });
        Near(variable.BarGrid.StartSeconds(1), 3.5, "Tempo change inside a bar is integrated exactly");
        Near(variable.BarGrid.StartSeconds(2), 6.5, "A 3/4 signature change changes the next bar length");
        var originalNotes = variable.Notes.ToArray();
        timeline = ScoreTimeline.ForExternalMidi(bundle, variable, new());
        Near(timeline.Events[1].Seconds, 1.75, "First bar distributes nominal onsets across its full MIDI window");
        Near(timeline.Events[3].Seconds, 5, "Second bar uses its own window with no accumulated drift");
        Check(variable.Notes.SequenceEqual(originalNotes), "Mapping preserves unquantized performance notes");
        Near(variable.Notes[0].StartSeconds, .128125, "Off-grid note onset remains unchanged");
        timeline = ScoreTimeline.ForExternalMidi(bundle, slow, new() { MeasureOffset = 1 });
        Near(timeline.Events[0].Seconds, 4, "Positive offset inserts one MIDI bar before the score");
        Near(timeline.Events[4].Seconds, 12, "Positive offset also applies at later row boundaries");
        timeline = ScoreTimeline.ForExternalMidi(bundle, slow, new() { MeasureOffset = -1 });
        Near(timeline.Events[0].Seconds, -4, "Negative offset maps the skipped score bar before zero");
        Check(timeline.CursorAt(0).Event == 2, "Playback at zero starts in the aligned later score bar");
        Reject(() => ScoreTimeline.ForExternalMidi(Bundle(measures.Replace("elid='2'", "elid='0'")), slow, new()), "External mode rejects repeated measure playback");
        Reject(() => ScoreTimeline.ForExternalMidi(Bundle(measures.Replace("<events>", "<unused>").Replace("</events>", "</unused>")), slow, new()), "Missing measure timing is rejected without guessing");
        Reject(() => ScoreTimeline.ForExternalMidi(bundle, slow with { BarGrid = null }, new()), "A MIDI without PPQ grid cannot use external synchronization");
        Reject(() => new ScoreSyncSettings { MeasureOffset = 1000 }.Validate(), "Out-of-range offsets are rejected");
        string directory = Path.Combine(Path.GetTempPath(), "TrifleSyncChecks-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        string document = Path.Combine(directory, "project.trifle.json"), recovery = Path.Combine(directory, "recovery.json");
        try
        {
            var project = new ProjectData { MidiPath = Path.Combine(directory, "performance.mid"), ScorePath = Path.Combine(directory, "score.mscz"), ExternalMidiPath = Path.Combine(directory, "performance.mid"), ScoreSync = new() { MeasureOffset = -1 } };
            ProjectStorage.SaveProject(document, project);
            var stored = ProjectStorage.LoadProject(document);
            Check(!stored.MidiFromScore && stored.MidiPath == "performance.mid" && stored.ScorePath == "score.mscz" && stored.ExternalMidiPath == "performance.mid", "Project stores independent portable music references");
            Check(stored.ScoreSync.MeasureOffset == -1, "Project stores the synchronization offset");
            ProjectStorage.SaveRecovery(recovery, new RecoveryData { Project = project });
            Check(ProjectStorage.LoadRecovery(recovery).Project.ScoreSync == project.ScoreSync, "Recovery preserves external score synchronization");
            File.WriteAllText(document, "{\"kind\":\"trifle-project\",\"version\":1,\"midiPath\":\"old.mid\"}");
            Check(ProjectStorage.LoadProject(document).ScoreSync.MeasureOffset == 0, "Old projects retain default synchronization settings");
        }
        finally { File.Delete(document); File.Delete(recovery); Directory.Delete(directory); }
        if (File.Exists(samplePath))
        {
            var sample = MuseScoreBundle.Read(samplePath);
            using var stream = new MemoryStream(sample.Midi);
            var song = MidiImporter.Read(stream);
            timeline = ScoreTimeline.ForExternalMidi(sample, song, new());
            Check(sample.MeasureEvents.Length == 72 && timeline.Events.Length == 238, "Real sample maps all 72 measures and 238 onsets");
            Check(sample.MeasureEvents.Select((measure, index) =>
                Math.Abs(song.BarGrid.StartSeconds(index) - measure.Seconds)).All(delta => delta < .002),
                "Real sample measure boundaries align within export millisecond precision");
        }
        Console.WriteLine($"Score synchronization: {checks} checks passed.");
    }
}
