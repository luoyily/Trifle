using System.Text;
using System.IO.Compression;
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
        string EncodeMusicXml(string value)
        {
            using var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            using (var writer = new StreamWriter(archive.CreateEntry("score.xml").Open(), new UTF8Encoding(false)))
                writer.Write(value);
            return Convert.ToBase64String(stream.ToArray());
        }
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
        Check(cropped.Attribute("width").Value == "1000" && cropped.Attribute("height").Value == "208", "Crop raster dimensions use viewBox units instead of millimetres");
        Check(cropped.Attribute("viewBox").Value == "0 496 1000 208", "System crop keeps annotations within the page and avoids the adjacent staff");
        Check(!cropped.Elements().Any(e => e.Attribute("fill")?.Value == "#ffffff") && cropped.Elements().Any(e => e.Attribute("class")?.Value == "Note"), "Crop removes page backing and preserves musical glyphs");
        Check(bundle.Pages[0].Svg == svg, "Cropping leaves the source SVG intact for other systems");
        var annotations = MuseScoreBundle.Parse(Bundle(page: svg.Replace("</svg>", """
            <path class="Tempo" d="M100,100 L120,200 L140,150 Z"/>
            <path class="Tempo" d="M100,810 L120,900 L140,850 Z"/>
            <path class="Text" d="M100,50 L120,100 L140,50 Z"/>
            </svg>
            """)));
        Near(annotations.Rows[0].CropTop, 96, "Tempo annotation expands the crop above normal staff padding");
        var annotated = XDocument.Parse(annotations.SvgForRow(0)).Root;
        Check(annotated.Elements().Count(e => e.Attribute("class")?.Value == "Tempo") == 1 &&
            !annotated.Elements().Any(e => e.Attribute("class")?.Value == "Text"), "System keeps its tempo and excludes adjacent tempo and page-frame text");
        const string revealPage = """
            <svg xmlns="http://www.w3.org/2000/svg" width="1000" height="2000" viewBox="0 0 1000 2000">
              <path class="" fill="#ffffff" d="M0,0 L1000,0 L1000,2000 L0,2000 Z"/>
              <polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="0,500 1000,500"/>
              <polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="0,520 1000,520"/>
              <polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="0,540 1000,540"/>
              <polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="0,560 1000,560"/>
              <polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="0,580 1000,580"/>
              <path class="Clef" fill="#000000" d="M50,500 L70,580 L60,540 Z"/>
              <path class="BarLine" fill="#000000" d="M400,500 L402,580 L400,580 Z"/>
              <path class="MeasureNumber" fill="#000000" d="M410,470 L430,470 L430,490 Z"/>
              <path class="Note" fill="#000000" d="M180,540 L220,540 L220,580 L180,580 Z"/>
              <path class="Note" fill="#000000" d="M580,540 L620,540 L620,580 L580,580 Z"/>
              <path class="Articulation" fill="#000000" d="M590,500 L610,510 L590,520 Z"/>
              <path class="Beam" fill="#000000" d="M150,560 L650,560 L650,565 L150,565 Z"/>
            </svg>
            """;
        var revealBundle = MuseScoreBundle.Parse(Bundle(page: revealPage));
        var reveal = revealBundle.RevealForRow(0);
        Check(reveal.Positions.SequenceEqual(new[] { 200.0, 600.0 }), "Reveal positions deduplicate repeated onsets");
        Check(revealBundle.RevealIndex(0, 199) == -1 && revealBundle.RevealIndex(0, 200) == 0 &&
            revealBundle.RevealIndex(0, 599) == 0 && revealBundle.RevealIndex(0, 600) == 1,
            "Reveal positions follow onset x");
        Near(revealBundle.RevealX(0, -1), revealBundle.Rows[0].Left - 1, "Empty typewriter row starts before the staff");
        Near(revealBundle.RevealX(0, 0), 400, "Reveal boundary splits the gap before the next articulation");
        Check(revealBundle.RevealX(0, 1) > revealBundle.Rows[0].Right, "The final position reveals the system tail");
        const string splitPage = """
            <svg xmlns="http://www.w3.org/2000/svg" width="1000" height="2000" viewBox="0 0 1000 2000">
              <path class="" fill="#ffffff" d="M0,0 L1000,0 L1000,2000 L0,2000 Z"/>
              <polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="100,500 800,500"/>
              <polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="100,540 800,540"/>
              <polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="100,580 800,580"/>
              <polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="100,620 800,620"/>
              <polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="100,660 800,660"/>
              <path class="Note" fill="#000000" d="M200,540 L300,540 L300,580 L200,580 Z"/>
              <path class="Note" fill="#000000" d="M400,540 L500,540 L500,580 L400,580 Z"/>
              <path class="TieSegment" fill="#000000" d="M250,520 L350,520 L350,530 L250,530 Z"/>
            </svg>
            """;
        const string splitMpos = """
            <score><elements>
              <element id="0" page="0" x="1200" y="6000" sx="8400" sy="2400"/>
            </elements></score>
            """;
        const string splitSpos = """
            <score><elements>
              <element id="10" page="0" x="2400" y="6000" sx="1200" sy="2400"/>
              <element id="20" page="0" x="4800" y="6000" sx="1200" sy="2400"/>
            </elements><events>
              <event elid="10" position="0"/><event elid="20" position="1000"/>
            </events></score>
            """;
        var splitBundle = MuseScoreBundle.Parse(Bundle(segments: splitSpos, measures: splitMpos, page: splitPage));
        Near(splitBundle.RevealX(0, 0), 350,
            "A connector overlapping a notehead yields the gap between the noteheads");
        const string closeNotesPage = """
            <svg xmlns="http://www.w3.org/2000/svg" width="1000" height="2000" viewBox="0 0 1000 2000">
              <path class="" fill="#ffffff" d="M0,0 L1000,0 L1000,2000 L0,2000 Z"/>
              <polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="100,500 800,500"/>
              <polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="100,540 800,540"/>
              <polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="100,580 800,580"/>
              <polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="100,620 800,620"/>
              <polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="100,660 800,660"/>
              <path class="Note" fill="#000000" d="M200,540 L300,540 L300,580 L200,580 Z"/>
              <path class="Note" fill="#000000" d="M280,540 L380,540 L380,580 L280,580 Z"/>
            </svg>
            """;
        var closeNotes = MuseScoreBundle.Parse(Bundle(page: closeNotesPage, segments: splitSpos, measures: splitMpos));
        Near(closeNotes.RevealX(0, 0), 381,
            "Overlapping noteheads reveal both chords instead of cutting a small sliver");
        const string clefPage = """
            <svg xmlns="http://www.w3.org/2000/svg" width="1000" height="2000" viewBox="0 0 1000 2000">
              <path class="" fill="#ffffff" d="M0,0 L1000,0 L1000,2000 L0,2000 Z"/>
              <polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="100,500 800,500"/>
              <polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="100,540 800,540"/>
              <polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="100,580 800,580"/>
              <polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="100,620 800,620"/>
              <polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="100,660 800,660"/>
              <path class="Clef" fill="#000000" d="M20,500 L40,580 L30,540 Z"/>
              <path class="Note" fill="#000000" d="M180,540 L220,540 L220,580 L180,580 Z"/>
              <path class="Clef" fill="#000000" d="M450,500 L510,580 L480,540 Z"/>
              <path class="Note" fill="#000000" d="M580,540 L620,540 L620,580 L580,580 Z"/>
            </svg>
            """;
        var clefBundle = MuseScoreBundle.Parse(Bundle(page: clefPage));
        var clefGrid = XDocument.Parse(clefBundle.SvgForRow(0, true, null, ScoreLayer.Grid)).Root;
        Check(clefGrid.Descendants().Count(e => e.Attribute("class")?.Value == "Clef") == 1 &&
            clefGrid.ToString().Contains("M20,") && !clefGrid.ToString().Contains("M450,"),
            "Only the system-start clef stays in the grid layer");
        var clefNotation = XDocument.Parse(clefBundle.SvgForRow(0, true, null, ScoreLayer.Notation)).Root;
        Check(clefNotation.Descendants().Count(e => e.Attribute("class")?.Value == "Clef") == 1 &&
            clefNotation.ToString().Contains("M450,") && !clefNotation.ToString().Contains("M20,"),
            "A mid-system clef moves to the notation layer");
        Near(clefBundle.RevealX(0, 0), 335, "A mid-system clef is revealed with the nearest onset");
        var grid = XDocument.Parse(revealBundle.SvgForRow(0, true, null, ScoreLayer.Grid)).Root;
        Check(grid.Descendants().Any(e => e.Attribute("class")?.Value == "StaffLines") &&
            grid.Descendants().Any(e => e.Attribute("class")?.Value == "Clef") &&
            !grid.Descendants().Any(e => e.Attribute("class")?.Value is "Note" or "Articulation" or "Beam"),
            "Grid layer keeps staff and clef but removes notation");
        var notation = XDocument.Parse(revealBundle.SvgForRow(0, true, null, ScoreLayer.Notation)).Root;
        Check(notation.Descendants().Any(e => e.Attribute("class")?.Value == "Note") &&
            notation.Descendants().Any(e => e.Attribute("class")?.Value == "Beam") &&
            !notation.Descendants().Any(e => e.Attribute("class")?.Value is "StaffLines" or "Clef" or "BarLine" or "MeasureNumber"),
            "Notation layer keeps notes, beams and articulations but removes the grid");
        const string overlapPage = """
            <svg xmlns="http://www.w3.org/2000/svg" width="1000" height="2000" viewBox="0 0 1000 2000">
              <path class="" fill="#ffffff" d="M0,0 L1000,0 L1000,2000 L0,2000 Z"/>
              <polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="0,300 1000,300"/>
              <polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="0,320 1000,320"/>
              <polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="0,340 1000,340"/>
              <polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="0,360 1000,360"/>
              <polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="0,380 1000,380"/>
              <polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="0,600 1000,600"/>
              <polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="0,620 1000,620"/>
              <polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="0,640 1000,640"/>
              <polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="0,660 1000,660"/>
              <polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="0,680 1000,680"/>
              <path class="Note" fill="#000000" d="M180,440 L220,440 L220,480 L180,480 Z"/>
              <path class="Stem" fill="#000000" d="M218,480 L222,480 L222,550 L218,550 Z"/>
              <polyline class="LedgerLine" fill="none" stroke="#000000" stroke-width="2" points="170,420 230,420"/>
              <polyline class="LedgerLine" fill="none" stroke="#000000" stroke-width="2" points="170,440 230,440"/>
              <path class="Note" fill="#000000" d="M480,500 L520,500 L520,540 L480,540 Z"/>
              <path class="Stem" fill="#000000" d="M518,430 L522,430 L522,500 L518,500 Z"/>
              <polyline class="LedgerLine" fill="none" stroke="#000000" stroke-width="2" points="470,540 530,540"/>
              <polyline class="LedgerLine" fill="none" stroke="#000000" stroke-width="2" points="470,560 530,560"/>
              <path class="Articulation" fill="#000000" d="M480,450 L500,460 L480,470 Z"/>
              <path class="MeasureNumber" fill="#000000" d="M500,310 L520,310 L520,330 L500,330 Z"/>
              <path class="MeasureNumber" fill="#000000" d="M500,610 L520,610 L520,630 L500,630 Z"/>
            </svg>
            """;
        const string overlapMeasures = """
            <score><elements>
              <element id="0" page="0" x="1200" y="3600" sx="9600" sy="1200"/>
              <element id="1" page="0" x="1200" y="7200" sx="9600" sy="1200"/>
            </elements></score>
            """;
        const string overlapSegments = """
            <score><elements>
              <element id="10" page="0" x="2400" y="3600" sx="120" sy="1200"/>
              <element id="20" page="0" x="2400" y="7200" sx="120" sy="1200"/>
            </elements><events>
              <event elid="10" position="0"/><event elid="20" position="1000"/>
            </events></score>
            """;
        const string overlapMusicXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE score-partwise PUBLIC "-//Recordare//DTD MusicXML 4.0 Partwise//EN" "http://www.musicxml.org/dtds/partwise.dtd">
            <score-partwise version="4.0">
              <part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
              <part id="P1">
                <measure number="1">
                  <direction default-x="100">
                    <direction-type><words>Marker</words></direction-type>
                  </direction>
                  <note><rest/><duration>4</duration><voice>1</voice><type>whole</type></note>
                </measure>
                <measure number="2">
                  <direction default-x="200">
                    <direction-type><words>Marker</words></direction-type>
                  </direction>
                  <note default-x="184">
                    <pitch><step>C</step><octave>5</octave></pitch>
                    <duration>4</duration><voice>1</voice><type>whole</type>
                    <notations><articulations><staccatissimo default-x="6"/></articulations></notations>
                  </note>
                </measure>
              </part>
            </score-partwise>
            """;
        var overlap = MuseScoreBundle.Parse(JsonSerializer.Serialize(new
        {
            svgs = new[] { Encode(overlapPage) }, sposXML = Encode(overlapSegments),
            mposXML = Encode(overlapMeasures), mxml = EncodeMusicXml(overlapMusicXml),
            metadata = new { duration = 2 }
        }));
        Check(overlap.Rows.Length == 2, "Overlap fixture has two systems");
        Near(overlap.Rows[0].Top - overlap.Rows[0].CropTop, overlap.Rows[1].Top - overlap.Rows[1].CropTop,
            "Overlapping systems share one top offset");
        Near(overlap.Rows[0].CropHeight, overlap.Rows[1].CropHeight, "Overlapping systems share one crop height");
        Check(overlap.Rows[0].CropTop <= 420 && overlap.Rows[0].CropTop + overlap.Rows[0].CropHeight >= 550,
            "First system crop contains its low overhang");
        Check(overlap.Rows[1].CropTop <= 430 && overlap.Rows[1].CropTop + overlap.Rows[1].CropHeight >= 560,
            "Second system crop contains its high overhang");
        Check(overlap.SvgForRow(0).Contains("M180,440") && !overlap.SvgForRow(0).Contains("M480,500"),
            "First system strip keeps its low glyph and drops the interleaved high glyph");
        Check(overlap.SvgForRow(1).Contains("M480,500") && !overlap.SvgForRow(1).Contains("M180,440"),
            "Second system strip keeps its high glyph and drops the interleaved low glyph");
        Check(!overlap.SvgForRow(0).Contains("M480,450") && overlap.SvgForRow(1).Contains("M480,450"),
            "MusicXML anchors keep a detached articulation with its high-note system");
        Check(overlap.SvgForRow(0).Contains("M500,310") && !overlap.SvgForRow(1).Contains("M500,310"),
            "A repeated margin x does not pull a lower measure number to the next system");
        Check(overlap.SvgForRow(1).Contains("M500,610") && !overlap.SvgForRow(0).Contains("M500,610"),
            "Each measure number stays on its own system");
        var fallback = MuseScoreBundle.Parse(JsonSerializer.Serialize(new
        {
            svgs = new[] { Encode(overlapPage) }, sposXML = Encode(overlapSegments),
            mposXML = Encode(overlapMeasures), mxml = Encode("not musicxml"),
            metadata = new { duration = 2 }
        }));
        Check(fallback.SvgForRow(0).Contains("M480,450"), "Invalid MusicXML falls back to geometric ownership");
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
        Check(new ScoreSettings().DisplayEffect == ScoreDisplayEffect.CursorLine, "Cursor line remains the default display effect");
        Reject(() => new ScoreSettings { DisplayEffect = (ScoreDisplayEffect)99 }.Validate(), "Unknown score display effect is rejected");
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
            var style = new ScoreSettings { DisplayEffect = ScoreDisplayEffect.Typewriter, Width = 0.85, Y = 0.12,
                RemoveBackground = true, MainColor = "55aaffff", Brightness = 2.5, CursorColor = "ff8899ff",
                CursorBrightness = 1.4 };
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
            new ProjectData { MidiPath = "song.mid", ScorePath = "piece.json" }.Validate();
            Check(true, "Projects allow independent external MIDI and score references");
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
            var sampleReveal = sample.RevealForRow(5);
            int next = Array.FindIndex(sampleReveal.Positions, x => x > 3500 && x < 3510);
            Check(next > 0, "Real sample keeps the reported row-five onset");
            double boundary = sample.RevealX(5, next - 1);
            Check(boundary > sampleReveal.Positions[next - 1] && boundary < 3500,
                "Real sample stops before the next chord's above-note articulation");
            Console.WriteLine($"Sample: {song.Notes.Length} MIDI notes, {clock.DurationSeconds}s.");
        }
        Console.WriteLine($"Score bundle: {checks} checks passed.");
    }
}
