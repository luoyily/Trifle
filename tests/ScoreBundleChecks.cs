using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Trifle.Midi;
using Trifle.Playback;
using Trifle.Persistence;
using Trifle.Score;
using Trifle.Visuals;

internal static class ScoreBundleChecks
{
    public static void Run(string samplePath)
    {
        int checks = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new Exception("FAIL: " + label);
            checks++; Console.WriteLine("PASS: " + label);
        }
        void Near(double actual, double expected, string label) => Check(Math.Abs(actual - expected) < 1e-6, label);
        void Reject(Action action, string label)
        {
            bool failed = false;
            try { action(); }
            catch (Exception e) when (e is InvalidDataException or FormatException or XmlException or JsonException or ArgumentException)
            { failed = true; }
            Check(failed, label);
        }
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="10mm" height="20mm" viewBox="0 0 1000 2000">
              <path class="" fill="#ffffff" d="M0,0 L1000,0 L1000,2000 L0,2000 Z"/>
              <path class="Note" fill="#000000" d="M100,500 L200,500 L200,550 Z"/>
            </svg>
            """;
        const string mpos = """
            <score><elements>
              <element id="0" page="0" x="1200" y="6000" sx="8400" sy="2400"/>
              <element id="1" page="0" x="9600" y="6000" sx="1200" sy="2400"/>
              <element id="2" page="0" x="1200" y="12000" sx="9600" sy="2400"/>
              <element id="3" page="1" x="1200" y="4800" sx="9600" sy="2400"/>
            </elements></score>
            """;
        const string spos = """
            <score><elements>
              <element id="10" page="0" x="2400" y="5999" sx="120" sy="2400"/>
              <element id="30" page="0" x="7200" y="5999" sx="120" sy="2400"/>
              <element id="40" page="0" x="1800" y="12000" sx="120" sy="2400"/>
              <element id="60" page="1" x="3600" y="4800" sx="120" sy="2400"/>
            </elements><events>
              <event elid="10" position="0"/><event elid="30" position="1000"/>
              <event elid="40" position="2000"/><event elid="60" position="3000"/>
              <event elid="10" position="4000"/><event elid="30" position="5000"/>
              <event elid="10" position="6000"/>
            </events></score>
            """;
        string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        string Bundle(string segments = spos, string measures = mpos, string page = svg) => JsonSerializer.Serialize(new
        {
            svgs = new[] { Encode(page), Encode(page) }, sposXML = Encode(segments), mposXML = Encode(measures),
            metadata = new { title = "Fixture", duration = 7 }
        });
        var bundle = MuseScoreBundle.Parse(Bundle());
        Check(bundle.Pages.Length == 2 && bundle.Rows.Length == 3 && bundle.Events.Length == 7, "Measures grouped into ordered systems across pages");
        Check(bundle.Title == "Fixture" && bundle.DurationSeconds == 7, "Metadata duration includes the final sustained interval");
        Near(bundle.Rows[0].Top, 500, "Position units divided by 12");
        Check(bundle.Rows[0].FirstMeasure == 1 && bundle.Rows[0].LastMeasure == 2, "System includes all its measures");
        Near(bundle.CursorAt(0.5).X, 400, "Cursor interpolates to next chord in the same system");
        Near(bundle.CursorAt(1.5).X, 750, "Cursor approaches the right edge before switching systems");
        Check(bundle.CursorAt(1.999).Row == 0 && bundle.CursorAt(2).Row == 1, "System switches exactly at the next onset");
        Near(bundle.CursorAt(2).X, 150, "System switch starts at the target segment");
        Check(bundle.Rows[bundle.CursorAt(3).Row].Page == 1, "Page switch follows the event, not elapsed page duration");
        Check(bundle.CursorAt(4).Row == 0, "Repeat jumps back to an earlier page and system");
        Near(bundle.CursorAt(5.5).X, 600, "Same-system repeat holds instead of sliding backwards");
        Near(bundle.CursorAt(6).X, 200, "Same-system repeat jumps at the repeat event");
        Near(bundle.CursorAt(-5).X, 200, "Before-start cursor stays at the first segment");
        Near(bundle.CursorAt(100).X, 200, "After-end cursor stays at the final segment");
        Near(bundle.CursorAt(0.5).X, 400, "Backward seeks are independent of previous cursor state");
        var simultaneous = MuseScoreBundle.Parse(Bundle(spos.Replace("position=\"1000\"", "position=\"0\"")));
        Near(simultaneous.CursorAt(0).X, 600, "Equal timestamps deterministically choose the final segment");
        var cropped = XDocument.Parse(bundle.SvgForRow(0)).Root;
        Check(cropped.Attribute("width").Value == "1000" && cropped.Attribute("height").Value == "700", "Crop raster dimensions use viewBox units instead of millimetres");
        Check(cropped.Attribute("viewBox").Value == "0 150 1000 700", "System crop keeps annotations within the page and avoids the adjacent staff");
        Check(!cropped.Elements().Any(e => e.Attribute("fill")?.Value == "#ffffff") && cropped.Elements().Any(e => e.Attribute("class")?.Value == "Note"), "Crop removes page backing and preserves musical glyphs");
        Check(bundle.Pages[0].Svg == svg, "Cropping leaves the source SVG intact for other systems");
        var annotations = MuseScoreBundle.Parse(Bundle(page: svg.Replace("</svg>", """
            <path class="Tempo" d="M100,100 L120,200 L140,150 Z"/>
            <path class="Tempo" d="M100,810 L120,900 L140,850 Z"/>
            <path class="Text" d="M100,50 L120,100 L140,50 Z"/>
            </svg>
            """)));
        Near(annotations.Rows[0].CropTop, 40, "Tempo annotation expands the crop above normal staff padding");
        var annotated = XDocument.Parse(annotations.SvgForRow(0)).Root;
        Check(annotated.Elements().Count(e => e.Attribute("class")?.Value == "Tempo") == 1 &&
            !annotated.Elements().Any(e => e.Attribute("class")?.Value == "Text"), "System keeps its tempo and excludes adjacent tempo and page-frame text");
        var coloredSource = MuseScoreBundle.Parse(Bundle(page: svg.Replace("</svg>", """
            <path class="Stem" stroke="#000000" fill="none" d="M100,500 L100,600"/>
            <path class="ImplicitFill" d="M200,500 L220,550 L240,500 Z"/>
            </svg>
            """)));
        var colored = XDocument.Parse(coloredSource.SvgForRow(0, true, "55aaffff")).Root;
        Check(colored.Elements().Single().Attribute("fill")?.Value == "#55aaff", "Theme color supplies inherited glyph fill");
        Check(colored.Descendants().Single(e => e.Attribute("class")?.Value == "Note").Attribute("fill")?.Value == "#55aaff", "Theme replaces explicit note fill");
        var stem = colored.Descendants().Single(e => e.Attribute("class")?.Value == "Stem");
        Check(stem.Attribute("stroke")?.Value == "#55aaff" && stem.Attribute("fill")?.Value == "none", "Theme recolors strokes without filling open paths");
        var opaque = XDocument.Parse(coloredSource.SvgForRow(0, false, "55aaffff")).Root;
        Check(opaque.Elements().Any(e => e.Attribute("class")?.Value == "" && e.Attribute("fill")?.Value == "#ffffff"), "Opaque theme retains the original white page background");
        var translucent = XDocument.Parse(coloredSource.SvgForRow(0, true, "55aaff80")).Root;
        Near(double.Parse(translucent.Elements().Single().Attribute("opacity").Value, System.Globalization.CultureInfo.InvariantCulture), 128 / 255.0, "Theme alpha applies to the complete notation group");
        Reject(() => bundle.SvgForRow(0, true, "invalid"), "Malformed notation color is rejected");
        new ScoreSettings().Validate();
        Near(ScorePulse.Gain(0), 2.5, "Onset cursor pulse peaks at a fixed gain");
        Near(ScorePulse.Gain(0.25), 1.375, "Cursor pulse decays using song time");
        Near(ScorePulse.Gain(0.5), 1, "Cursor pulse returns exactly to its base brightness after 0.5s");
        Near(ScorePulse.Gain(-0.1), 1, "Before-onset cursor does not pulse");
        Reject(() => new ScoreSettings { Brightness = double.NaN }.Validate(), "Nonfinite notation brightness is rejected");
        Reject(() => new ScoreSettings { CursorBrightness = 6 }.Validate(), "Unbounded cursor brightness is rejected");
        Reject(() => new ScoreSettings { CursorColor = "bad" }.Validate(), "Malformed cursor color is rejected");
        Reject(() => new ScoreSettings { Width = 0.1 }.Validate(), "Unusable notation width is rejected");
        Reject(() => new ScoreSettings { Y = double.NaN }.Validate(), "Nonfinite notation position is rejected");
        Reject(() => new VisualSettings { Score = null }.Validate(), "Null score settings block is rejected");
        string storage = Path.Combine(Path.GetTempPath(), "trifle-score-storage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storage);
        try
        {
            string documentPath = Path.Combine(storage, "piece.trifle.json");
            string scorePath = Path.Combine(storage, "scores", "piece.json");
            var style = new ScoreSettings { Width = 0.85, Y = 0.12, RemoveBackground = true, MainColor = "55aaffff",
                Brightness = 2.5, CursorColor = "ff8899ff", CursorBrightness = 1.4 };
            var data = new ProjectData { MidiPath = scorePath, MidiFromScore = true, ScorePath = scorePath, Visual = new VisualSettings { Score = style }, TimeSeconds = 13.5 };
            ProjectStorage.SaveProject(documentPath, data);
            var stored = ProjectStorage.LoadProject(documentPath);
            Check(stored.MidiFromScore && stored.MidiPath == "scores/piece.json" && stored.ScorePath == stored.MidiPath, "Project saves portable references to bundled MIDI and score");
            Check(ProjectStorage.ResolveReference(stored.ScorePath, documentPath) == scorePath && stored.Visual.Score == style, "Project restores score reference and appearance");
            string presetPath = Path.Combine(storage, "style.trifle-preset.json");
            ProjectStorage.SavePreset(presetPath, new VisualPreset { Visual = data.Visual });
            Check(ProjectStorage.LoadPreset(presetPath).Visual.Score == style && !File.ReadAllText(presetPath).Contains("scorePath"), "Preset saves notation appearance without replacing the music source");
            string recoveryPath = Path.Combine(storage, "recovery.json");
            ProjectStorage.SaveRecovery(recoveryPath, new RecoveryData { Project = data });
            var recovered = ProjectStorage.LoadRecovery(recoveryPath).Project;
            Check(recovered.MidiFromScore && recovered.ScorePath == scorePath && recovered.TimeSeconds == 13.5, "Recovery keeps absolute bundled source and playback position");
            File.WriteAllText(documentPath, """{"kind":"trifle-project","version":1,"midiPath":"song.mid"}""");
            var legacy = ProjectStorage.LoadProject(documentPath);
            Check(!legacy.MidiFromScore && legacy.ScorePath == "" && legacy.Visual.Score == new ScoreSettings(), "Legacy projects load unchanged with default score settings");
            Reject(() => new ProjectData { MidiPath = "song.mid", ScorePath = "piece.json" }.Validate(), "External MIDI score alignment is excluded from the minimal feature set");
        }
        finally
        {
            foreach (string file in new[] { "piece.trifle.json", "style.trifle-preset.json", "recovery.json" })
                File.Delete(Path.Combine(storage, file));
            Directory.Delete(storage);
        }
        Reject(() => MuseScoreBundle.Parse("{}"), "Missing score media is rejected");
        Reject(() => MuseScoreBundle.Parse(Bundle(spos.Replace("elid=\"10\"", "elid=\"999\""))), "Dangling playback references are rejected");
        Reject(() => MuseScoreBundle.Parse(Bundle(spos.Replace("5999", "NaN"))), "Nonfinite positions are rejected");
        Reject(() => MuseScoreBundle.Parse(Bundle(measures: mpos.Replace("page=\"1\"", "page=\"2\""))), "Out-of-range page indices are rejected");
        Reject(() => MuseScoreBundle.Parse(Bundle(page: "<!DOCTYPE svg [<!ENTITY x SYSTEM 'file:///missing'>]>" + svg)), "External XML entities are rejected");
        Reject(() => bundle.CursorAt(double.NaN), "Nonfinite playback times are rejected");
        if (samplePath != null && File.Exists(samplePath))
        {
            var sample = MuseScoreBundle.Read(samplePath);
            Check(sample.Pages.Length == 2 && sample.Rows.Length == 12 && sample.Events.Length == 238, "Real MuseScore bundle has 2 pages, 12 systems, 238 segments");
            Check(sample.Rows[0].FirstMeasure == 1 && sample.Rows[^1].LastMeasure == 72, "Real sample covers all 72 measures");
            using var midi = new MemoryStream(sample.Midi);
            var song = MidiImporter.Read(midi, samplePath);
            var clock = new PlaybackController(); clock.SetSong(song); clock.SetDuration(sample.DurationSeconds);
            Check(song.Notes.Length == 460 && clock.DurationSeconds == Math.Max(song.DurationSeconds, sample.DurationSeconds), "Bundled MIDI imports; shared clock retains the full MIDI tail despite rounded metadata");
            foreach (var row in Enumerable.Range(0, sample.Rows.Length))
            {
                var start = sample.Events.First(e => e.Row == row);
                if (sample.CursorAt(start.Seconds).Row != row) throw new Exception("Real sample system mismatch: " + row);
            }
            Check(true, "All 12 real system boundaries locate the correct row");
            Console.WriteLine($"Sample: {song.Notes.Length} MIDI notes, {clock.DurationSeconds}s.");
        }
        Console.WriteLine($"Score bundle: {checks} checks passed.");
    }
}
