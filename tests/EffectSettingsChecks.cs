using System.Text.Json.Nodes;
using Trifle.Persistence;
using Trifle.Visuals;

// Narrow storage checks for the settings pass; does not run the MIDI/export suite.
internal static class EffectSettingsChecks
{
    public static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "trifle-effect-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "settings.json");
        int checks = 0;
        void Check(bool pass, string label)
        {
            if (!pass) throw new Exception("FAIL: " + label);
            checks++; Console.WriteLine("PASS: " + label);
        }
        try
        {
            const string legacy = """
                {"kind":"trifle-preset","version":1,"visual":{
                  "particles":{"enabled":false,"curves":true,"emission":1.4,"glow":0.9},
                  "lights":{"lineEmission":2,"hitEmission":1.4,"keyboardEmission":0,"nearStrength":0.5}}}
                """;
            File.WriteAllText(file, legacy);
            var old = ProjectStorage.LoadPreset(file).Visual;
            Check(!old.Particles.Enabled && !old.Particles.Curves, "Legacy particle master off keeps both layers hidden");
            Check(old.Particles.CurveEmission == 1.4 && old.Particles.CurveGlow == 0.9, "Legacy curve lighting inherits the visible values");
            Check(old.Lights.LineEmission == 2 && old.Lights.HaloEmission == 2 && old.Lights.LineContactBoost == 1.5,
                "Legacy line and halo preserve brightness and contact response");
            Check(!old.Lights.NearEnabled && old.Lights.NearStrength == 0.5, "Legacy hidden near light stays hidden without losing strength");
            File.WriteAllText(file, legacy.Replace("\"enabled\":false", "\"enabled\":true"));
            Check(ProjectStorage.LoadPreset(file).Visual.Particles.Curves, "Legacy active curves stay active");
            File.WriteAllText(file, legacy.Replace("\"curves\":true,", "").Replace("\"enabled\":false", "\"enabled\":true"));
            Check(ProjectStorage.LoadPreset(file).Visual.Particles.Curves, "Older particle files retain the historical curve default");

            var visual = old with
            {
                Note = old.Note with { EmissionEnabled = false, Emission = 2.1 },
                Particles = old.Particles with { Enabled = false, Curves = true, CurveEmission = 2.4, CurveGlow = 1.1 },
                Lights = old.Lights with
                {
                    KeyboardEnabled = false, HitEnabled = false, KeyLightEnabled = false, NearEnabled = true,
                    LineEnabled = false, HaloEnabled = true, HaloEmission = 1.7, LineEmission = 2.3,
                    LineColor = "ff6688ff", HaloColor = "4466ffff", HaloFollowsLine = false, TintWithNotes = false
                }
            };
            bool Same(VisualSettings actual) => actual.Note == visual.Note && actual.Lights == visual.Lights
                && actual.Particles == visual.Particles && actual.EffectControlsVersion == 1;
            ProjectStorage.SavePreset(file, new VisualPreset { Visual = visual });
            Check(Same(ProjectStorage.LoadPreset(file).Visual), "Preset round trip preserves independent switches, intensities and colors");
            var project = new ProjectData { MidiPath = Path.Combine(directory, "song.mid"), Visual = visual };
            ProjectStorage.SaveProject(file, project);
            Check(Same(ProjectStorage.LoadProject(file).Visual), "Project round trip preserves independent settings");
            ProjectStorage.SaveRecovery(file, new RecoveryData { Project = project });
            Check(Same(ProjectStorage.LoadRecovery(file).Project.Visual), "Recovery round trip preserves independent settings");

            var recovery = JsonNode.Parse(File.ReadAllText(file))!;
            recovery["project"]!["visual"] = JsonNode.Parse(legacy)!["visual"]!.DeepClone();
            File.WriteAllText(file, recovery.ToJsonString());
            var migrated = ProjectStorage.LoadRecovery(file).Project.Visual;
            Check(!migrated.Particles.Curves && migrated.Lights.HaloEmission == 2, "Nested legacy recovery also migrates");
            bool Reject(Action action) { try { action(); return false; } catch (ArgumentException) { return true; } }
            Check(Reject(() => new LightEffectsSettings { LineColor = "invalid" }.Validate()), "Invalid line color rejected");
            Check(Reject(() => new LightEffectsSettings { HaloEmission = double.NaN }.Validate()), "Nonfinite halo intensity rejected");
            Check(Reject(() => new ParticleSettings { CurveGlow = 4 }.Validate()), "Out of range curve glow rejected");
            Console.WriteLine($"Effect settings: {checks} targeted checks passed.");
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
            Directory.Delete(directory); // Empty directory only; never recursive.
        }
    }
}
