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
            Check(old.ContactLine.LineEmission == 2 && old.ContactLine.HaloEmission == 2 && old.ContactLine.LineContactBoost == 1.5,
                "Legacy line and halo preserve brightness and contact response");
            Check(!old.KeyboardLights.NearEnabled && old.KeyboardLights.NearStrength == 0.5, "Legacy hidden near light stays hidden without losing strength");
            File.WriteAllText(file, legacy.Replace("\"enabled\":false", "\"enabled\":true"));
            Check(ProjectStorage.LoadPreset(file).Visual.Particles.Curves, "Legacy active curves stay active");
            File.WriteAllText(file, legacy.Replace("\"curves\":true,", "").Replace("\"enabled\":false", "\"enabled\":true"));
            Check(ProjectStorage.LoadPreset(file).Visual.Particles.Curves, "Older particle files retain the historical curve default");

            var visual = old with
            {
                Note = old.Note with { EmissionEnabled = false, Emission = 2.1 },
                Particles = old.Particles with { Enabled = false, Curves = true, CurveEmission = 2.4, CurveGlow = 1.1 },
                KeyboardLights = old.KeyboardLights with
                {
                    KeyboardEnabled = false, HitEnabled = false, KeyLightEnabled = false, NearEnabled = true
                },
                ContactLine = old.ContactLine with
                {
                    LineEnabled = false, HaloEnabled = true, HaloEmission = 1.7, LineEmission = 2.3,
                    LineColor = "ff6688ff", HaloColor = "4466ffff", HaloFollowsLine = false, TintWithNotes = false
                }
            };
            bool Same(VisualSettings actual) => actual.Note == visual.Note && actual.KeyboardLights == visual.KeyboardLights && actual.ContactLine == visual.ContactLine
                && actual.Particles == visual.Particles && actual.EffectControlsVersion == 2;
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
            Check(!migrated.Particles.Curves && migrated.ContactLine.HaloEmission == 2, "Nested legacy recovery also migrates");
            bool Reject(Action action) { try { action(); return false; } catch (ArgumentException) { return true; } }
            Check(Reject(() => new ContactLineSettings { LineColor = "invalid" }.Validate()), "Invalid line color rejected");
            Check(Reject(() => new ContactLineSettings { HaloEmission = double.NaN }.Validate()), "Nonfinite halo intensity rejected");
            Check(Reject(() => new ParticleSettings { CurveGlow = 4 }.Validate()), "Out of range curve glow rejected");

            // Released v1 records already have independent switches: only regroup them.
            const string previous = """
                {"kind":"trifle-project","version":1,"midiPath":"song.mid","visual":{
                  "effectControlsVersion":1,
                  "lights":{"keyboardEnabled":false,"hitEnabled":true,"nearEnabled":true,"keyLightEnabled":false,
                    "keyboardEmission":2.2,"hitEmission":1.3,"hitDecay":0.7,"nearStrength":0.4,"nearDistance":250,
                    "keyLightStrength":1.6,"keyLightRadius":4.5,"lineEnabled":false,"haloEnabled":true,
                    "lineEmission":2.4,"haloEmission":1.2,"lineCoreWidth":6,"lineWidth":25,"lineWave":3,
                    "lineColor":"abcdef88","haloColor":"ff0000ff","haloFollowsLine":false,
                    "tintWithNotes":false,"lineContactBoost":3.7},
                  "background":{"imagePath":"missing.png","gradient":"Horizontal","color":"112233ff"}}}
                """;
            File.WriteAllText(file, previous);
            var upgraded = ProjectStorage.LoadProject(file);
            Check(upgraded.Visual.KeyboardLights == new KeyboardLightSettings
            {
                KeyboardEnabled = false, HitEnabled = true, NearEnabled = true, KeyLightEnabled = false,
                KeyboardEmission = 2.2, HitEmission = 1.3, HitDecay = 0.7, NearStrength = 0.4,
                NearDistance = 250, KeyLightStrength = 1.6, KeyLightRadius = 4.5
            }, "V1 keyboard switches, strengths and decay migrate without changing values");
            Check(upgraded.Visual.ContactLine == new ContactLineSettings
            {
                LineEnabled = false, HaloEnabled = true, LineEmission = 2.4, HaloEmission = 1.2,
                LineCoreWidth = 6, LineWidth = 25, LineWave = 3, LineColor = "abcdef88",
                HaloColor = "ff0000ff", HaloFollowsLine = false, TintWithNotes = false, LineContactBoost = 3.7
            }, "V1 line/halo switches, colors, widths and response migrate without changing values");
            Check(upgraded.Visual.Background.Type == BackgroundType.Image &&
                upgraded.Visual.Background.Gradient == BackgroundGradient.Horizontal,
                "Legacy image over a gradient retains both the active image and its backing gradient");
            Check(upgraded.ExpandedSections.Count == 0, "Old projects default to collapsed advanced sections");
            upgraded = upgraded with
            {
                ExpandedSections = new() { ["Margin/Content/Scroll/Groups/Keyboard/Header"] = true,
                    ["Margin/Content/Scroll/Groups/GlowPanel/Header/Expand"] = false }
            };
            ProjectStorage.SaveProject(file, upgraded);
            var saved = JsonNode.Parse(File.ReadAllText(file))!;
            Check(saved["visual"]!["lights"] == null && saved["visual"]!["keyboardLights"] != null &&
                saved["visual"]!["contactLine"] != null, "Saving upgraded projects writes only the new light records");
            var roundTrip = ProjectStorage.LoadProject(file);
            Check(roundTrip.Visual.KeyboardLights == upgraded.Visual.KeyboardLights &&
                roundTrip.Visual.ContactLine == upgraded.Visual.ContactLine, "Loading migrated data again is idempotent");
            Check(roundTrip.ExpandedSections.Count == 2 &&
                roundTrip.ExpandedSections["Margin/Content/Scroll/Groups/Keyboard/Header"], "Project round trip retains section states");
            ProjectStorage.SaveRecovery(file, new RecoveryData { Project = upgraded });
            Check(ProjectStorage.LoadRecovery(file).Project.ExpandedSections.Count == 2, "Recovery retains section states");
            File.WriteAllText(file, previous.Replace("\"imagePath\":\"missing.png\",", ""));
            Check(ProjectStorage.LoadProject(file).Visual.Background.Type == BackgroundType.Gradient,
                "Legacy gradient backgrounds remain gradients");
            Check(Reject(() => new BackgroundSettings { Opacity = -0.1 }.Validate()), "Negative background opacity rejected");
            Check(Reject(() => new BackgroundSettings { Brightness = double.NaN }.Validate()), "Nonfinite background brightness rejected");
            Console.WriteLine($"Effect settings: {checks} targeted checks passed.");
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
            Directory.Delete(directory); // Empty directory only; never recursive.
        }
    }
}
