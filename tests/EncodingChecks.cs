using Trifle.Export;
using Trifle.Persistence;

internal static class EncodingChecks
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new Exception("FAIL: " + label);
            checks++; Console.WriteLine("PASS: " + label);
        }
        void Reject(EncodingSettings encoding, string label)
        {
            bool rejected = false;
            try { encoding.Validate(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, label);
        }

        string path = Path.Combine(Path.GetTempPath(), "trifle-encoding-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """
                {"kind":"trifle-project","version":1,"midiPath":"song.mid","export":{"height":1080,"width":1920,"framesPerSecond":30}}
                """);
            var old = ProjectStorage.LoadProject(path);
            Check(old.Export.Encoding == new EncodingSettings(), "Existing projects retain the original encoding defaults");
            File.WriteAllText(path, """
                {"kind":"trifle-recovery","version":1,"project":{"kind":"trifle-project","version":1,"midiPath":"song.mid"}}
                """);
            Check(ProjectStorage.LoadRecovery(path).Project.Export.Encoding == new EncodingSettings(),
                "Existing recovery records retain encoding defaults");

            var custom = new EncodingSettings { Encoder = "h265", Crf = 23, Preset = "slow", AudioBitrateKbps = 320 };
            var project = old with { Export = old.Export with { Encoding = custom } };
            ProjectStorage.SaveProject(path, project);
            Check(ProjectStorage.LoadProject(path).Export.Encoding == custom, "All four encoding choices survive project save/load");
            ProjectStorage.SaveRecovery(path, new RecoveryData { Project = project });
            Check(ProjectStorage.LoadRecovery(path).Project.Export.Encoding == custom, "Encoding choices survive recovery save/load");
            Check(!File.ReadAllText(path).Contains("codecName") && !File.ReadAllText(path).Contains("displayName"),
                "Saved data contains choices rather than derived implementation details");

            Reject(custom with { Encoder = "av1" }, "Unsupported encoder rejected");
            Reject(custom with { Crf = -1 }, "Negative CRF rejected");
            Reject(custom with { Crf = 52 }, "Out-of-range CRF rejected");
            Reject(custom with { Preset = "unknown" }, "Unsupported preset rejected");
            Reject(custom with { AudioBitrateKbps = 0 }, "Unsupported audio bitrate rejected");
            var settings = new ExportSettings(Path.ChangeExtension(path, ".mp4"), 0, 1, Encoding: custom);
            settings.Validate();
            Check(settings.EffectiveEncoding == custom && (settings with { Overwrite = true }).EffectiveEncoding == custom,
                "The export and overwrite snapshots preserve encoding choices");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
        Console.WriteLine($"Encoding: {checks} targeted checks passed.");
    }
}
