using Trifle.App;
using Trifle.Midi;
using Trifle.Playback;
using Trifle.Persistence;

internal static class TransportChecks
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new Exception("FAIL: " + label);
            checks++; Console.WriteLine("PASS: " + label);
        }
        Check(TimeText.Format(0) == "00:00", "Zero time");
        Check(TimeText.Format(59.999) == "00:59" && TimeText.Format(60) == "01:00", "No early rounding at a minute boundary");
        Check(TimeText.Format(200.27915) == "03:20", "Playback time omits fractions");
        Check(TimeText.Format(3599.999) == "59:59" && TimeText.Format(3600) == "01:00:00", "Hour boundary");
        Check(TimeText.Format(5, true) == "00:00:05", "Long songs use consistent hour fields from the beginning");
        Check(TimeText.Format(90061) == "25:01:01", "Hours do not wrap after one day");

        var song = new MidiSong("", 1, Array.Empty<MidiTrack>(), Array.Empty<MidiTempoChange>(), Array.Empty<MidiNote>(), 7200.5);
        var playback = new PlaybackController();
        playback.SetSong(song); playback.Play(); playback.Advance(0.12345);
        Check(Math.Abs(playback.TimeSeconds - 0.12345) < 0.000001, "Playback advances one song second per elapsed second");
        playback.Pause(); playback.Advance(0.5);
        Check(Math.Abs(playback.TimeSeconds - 0.12345) < 0.000001, "Pause keeps precise time");
        playback.Seek(200.27915);
        Check(playback.TimeSeconds == 200.27915, "Seeking retains subsecond precision");
        playback.Stop();
        Check(playback.TimeSeconds == 0 && !playback.IsPlaying, "Stop resets and pauses");

        string path = Path.Combine(Path.GetTempPath(), "trifle-transport-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """
                {"kind":"trifle-project","version":1,"midiPath":"song.mid","timeSeconds":200.27915,"playbackSpeed":4}
                """);
            var old = ProjectStorage.LoadProject(path);
            Check(old.TimeSeconds == 200.27915, "Old projects with speed fields still load without changing position");
            ProjectStorage.SaveProject(path, old);
            Check(!File.ReadAllText(path).Contains("playbackSpeed"), "Resaved projects omit the removed speed field");
            File.WriteAllText(path, """
                {"kind":"trifle-recovery","version":1,"project":{"kind":"trifle-project","version":1,"midiPath":"song.mid","playbackSpeed":0.25}}
                """);
            var recovery = ProjectStorage.LoadRecovery(path);
            ProjectStorage.SaveRecovery(path, recovery);
            Check(!File.ReadAllText(path).Contains("playbackSpeed"), "Old recovery records also drop speed on save");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
        Console.WriteLine($"Transport: {checks} targeted checks passed.");
    }
}
