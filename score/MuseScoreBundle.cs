using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Trifle.Score;

public sealed record ScorePage(string Svg, double Width, double Height);
public sealed record ScoreRow(int Page, double Top, double Height, double Left, double Right,
    int FirstMeasure, int LastMeasure, double CropTop, double CropHeight);
public sealed record ScoreEvent(double Seconds, int ElementId, int Row, double X);
public sealed record ScoreCursor(int Event, int Row, double X);
public enum ScoreLayer { All, Grid, Notation }
public sealed record ScoreReveal(double EmptyX, double[] Positions, double[] Boundaries);

// MuseScore --score-media authors element positions on a raster at 12x the PNG export
// resolution (positionswriter.cpp scales the configured PNG DPI by 12), while the SVG
// viewBox follows the PDF export DPI. The two resolutions are independent preferences, so
// the real scale is measured from the packaged PNG width at load time; this constant is
// only the fallback for bundles without usable PNGs.
public sealed class MuseScoreBundle
{
    public const double CoordinateScale = 12;
    public string Title { get; }
    public ScorePage[] Pages { get; }
    public ScoreRow[] Rows { get; }
    public ScoreEvent[] Events { get; }
    public ScoreMeasureEvent[] MeasureEvents { get; }
    public ScoreTimeline Timeline { get; }
    public byte[] Midi { get; }
    public double DurationSeconds { get; }
    private readonly HashSet<int>[] _rowElements;
    private readonly double[] _rowStaffSpace;
    private readonly double[] _rowFirstOnset;
    private readonly ScoreReveal[] _reveals;
    private MuseScoreBundle(string title, ScorePage[] pages, ScoreRow[] rows,
        ScoreEvent[] events, ScoreMeasureEvent[] measureEvents, byte[] midi, double duration,
        HashSet<int>[] rowElements, double[] rowStaffSpace)
    {
        Title = title; Pages = pages; Rows = rows; Events = events; Midi = midi; DurationSeconds = duration;
        MeasureEvents = measureEvents; Timeline = new ScoreTimeline(rows, events, duration);
        _rowElements = rowElements; _rowStaffSpace = rowStaffSpace;
        _rowFirstOnset = Enumerable.Range(0, rows.Length).Select(row =>
            events.Where(item => item.Row == row).Select(item => item.X).DefaultIfEmpty(double.PositiveInfinity).Min()).ToArray();
        _reveals = BuildReveals();
    }

    public static MuseScoreBundle Read(string path) => Parse(File.ReadAllText(path));

    public static MuseScoreBundle Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var data = document.RootElement;
        string Decode(JsonElement value) => Encoding.UTF8.GetString(Convert.FromBase64String(value.GetString()
            ?? throw new InvalidDataException("乐谱数据字段不能为空。")));
        if (!data.TryGetProperty("svgs", out var svgs) || svgs.ValueKind != JsonValueKind.Array || svgs.GetArrayLength() == 0 ||
            !data.TryGetProperty("sposXML", out var spos) || !data.TryGetProperty("mposXML", out var mpos))
            throw new InvalidDataException("请选择 MuseScore --score-media 导出的 JSON 数据包（需包含 SVG 与位置表）。");
        var pages = svgs.EnumerateArray().Select(svg =>
        {
            string text = Decode(svg);
            var root = Xml(text).Root;
            if (root == null || root.Name.LocalName != "svg") throw new InvalidDataException("数据包包含无效 SVG。");
            double[] box = (root.Attribute("viewBox")?.Value ?? "").Split(new[] { ' ', ',', '\t', '\n', '\r' },
                StringSplitOptions.RemoveEmptyEntries).Select(Number).ToArray();
            if (box.Length != 4 || box[0] != 0 || box[1] != 0 || box[2] <= 0 || box[3] <= 0)
                throw new InvalidDataException("SVG 需要以 (0,0) 为原点的有效 viewBox。");
            return new ScorePage(text, box[2], box[3]);
        }).ToArray();

        var positionScale = PositionScale(pages[0], data);
        var measures = Elements(Decode(mpos), pages.Length, positionScale);
        var segments = Elements(Decode(spos), pages.Length, positionScale);
        var rows = measures.Values.GroupBy(e => (e.Page, Y: Math.Round(e.Y))).OrderBy(g => g.Key.Page).ThenBy(g => g.Key.Y)
            .Select(g => new ScoreRow(g.Key.Page, g.Min(e => e.Y), g.Max(e => e.Height), g.Min(e => e.X),
                g.Max(e => e.X + e.Width), g.Min(e => e.Id) + 1, g.Max(e => e.Id) + 1, 0, 0)).ToArray();
        if (rows.Length == 0) throw new InvalidDataException("数据包没有可显示的乐谱系统行。");
        var geometries = pages.Select(PageGeometry.Read).ToArray();
        var anchors = SemanticAnchors(data, measures, rows, geometries);
        var rowElements = AssignRows(rows, geometries, anchors);
        for (int i = 0; i < rows.Length; i++)
            if (rows[i].Left < 0 || rows[i].Right > pages[rows[i].Page].Width + 2)
                throw new InvalidDataException("乐谱位置超出 SVG 页面，请使用默认参数导出（不要使用 -r）。");

        var eventRoot = Xml(Decode(spos)).Root;
        var eventNodes = eventRoot?.Elements().FirstOrDefault(e => e.Name.LocalName == "events")?.Elements()
            ?? throw new InvalidDataException("数据包缺少乐谱播放事件。");
        var events = eventNodes.Select(e =>
        {
            int id = Integer(e, "elid");
            if (!segments.TryGetValue(id, out var segment)) throw new InvalidDataException("乐谱事件引用了不存在的发音点。");
            int row = Enumerable.Range(0, rows.Length).Where(i => rows[i].Page == segment.Page)
                .OrderBy(i => Math.Abs(rows[i].Top - segment.Y)).FirstOrDefault(-1);
            if (row < 0 || Math.Abs(rows[row].Top - segment.Y) > 2 || segment.X < 0 || segment.X > pages[segment.Page].Width)
                throw new InvalidDataException("发音点与系统行不匹配，请使用 MuseScore 默认导出参数。");
            double seconds = Attribute(e, "position") / 1000;
            if (seconds < 0) throw new InvalidDataException("乐谱事件时间不能为负数。");
            return new ScoreEvent(seconds, id, row, segment.X);
        }).OrderBy(e => e.Seconds).ToArray();
        if (events.Length == 0) throw new InvalidDataException("数据包没有可同步的乐谱事件。");
        double duration = events[^1].Seconds;
        string title = "乐谱";
        if (data.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object)
        {
            if (metadata.TryGetProperty("title", out var name) && name.ValueKind == JsonValueKind.String)
                title = name.GetString() ?? title;
            if (metadata.TryGetProperty("duration", out var value) && value.ValueKind == JsonValueKind.Number &&
                value.TryGetDouble(out var seconds) && double.IsFinite(seconds))
                duration = Math.Max(duration, seconds);
        }
        byte[] midi = data.TryGetProperty("midi", out var encodedMidi) && encodedMidi.ValueKind == JsonValueKind.String
            ? Convert.FromBase64String(encodedMidi.GetString()) : Array.Empty<byte>();
        var measureEvents = Xml(Decode(mpos)).Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "events")?.Elements()
            .Select(e => new ScoreMeasureEvent(Integer(e, "elid"), Attribute(e, "position") / 1000)).ToArray()
            ?? Array.Empty<ScoreMeasureEvent>();
        if (measureEvents.Any(e => !measures.ContainsKey(e.MeasureId) || e.Seconds < 0))
            throw new InvalidDataException("乐谱小节播放事件无效。");
        var rowStaffSpace = rows.Select(row => geometries[row.Page].StaffSpace).ToArray();
        return new MuseScoreBundle(title, pages, rows, events, measureEvents, midi, duration, rowElements, rowStaffSpace);
    }

    public ScoreCursor CursorAt(double seconds) => Timeline.CursorAt(seconds);

    public string SvgForRow(int index, bool removeBackground = true, string mainColor = null,
        ScoreLayer layer = ScoreLayer.All)
    {
        if (index < 0 || index >= Rows.Length) throw new ArgumentOutOfRangeException(nameof(index));
        var row = Rows[index];
        var page = Pages[row.Page];
        var document = Xml(page.Svg);
        var root = document.Root;
        // Page furniture is kept in every strip; every musical glyph is kept only in the
        // systems it belongs to, so neighboring rows may share vertical space safely.
        var keep = _rowElements[index];
        int child = 0;
        foreach (var element in root.Elements().ToArray())
        {
            int current = child++;
            if (element.Name.LocalName is "title" or "desc") continue;
            if (!keep.Contains(current)) element.Remove();
        }
        var background = root.Elements().FirstOrDefault(e => e.Name.LocalName == "path" &&
            e.Attribute("class")?.Value == "" && string.Equals(e.Attribute("fill")?.Value, "#ffffff", StringComparison.OrdinalIgnoreCase));
        if (removeBackground || layer == ScoreLayer.Notation) background?.Remove();
        if (layer != ScoreLayer.All)
        {
            foreach (var element in root.Elements().ToArray())
            {
                if (element == background || element.Name.LocalName is "title" or "desc") continue;
                bool grid = IsGridElement(element, index);
                if (layer == ScoreLayer.Grid ? !grid : grid) element.Remove();
            }
        }
        if (mainColor != null)
        {
            Trifle.Visuals.VisualSettings.ValidateColor(mainColor, "乐谱主颜色");
            string rgb = "#" + mainColor[..6];
            var content = root.Elements().Where(e => e != background &&
                e.Name.LocalName is not ("title" or "desc")).ToArray();
            foreach (var element in content.SelectMany(e => e.DescendantsAndSelf()))
            {
                foreach (string paint in new[] { "fill", "stroke" })
                    if (element.Attribute(paint) is { } attribute && attribute.Value != "none")
                        attribute.Value = rgb;
            }
            var group = new XElement(root.Name.Namespace + "g", new XAttribute("fill", rgb),
                new XAttribute("opacity", Format(Convert.ToByte(mainColor[6..], 16) / 255.0)));
            foreach (var element in content) { element.Remove(); group.Add(element); }
            root.Add(group);
        }
        root.SetAttributeValue("viewBox", $"0 {Format(row.CropTop)} {Format(page.Width)} {Format(row.CropHeight)}");
        root.SetAttributeValue("width", Format(page.Width));
        root.SetAttributeValue("height", Format(row.CropHeight));
        return document.ToString(SaveOptions.DisableFormatting);
    }

    // Structure that stays on screen while the notation is written out.
    private static bool IsGridClass(string name) => name is "StaffLines" or "BarLine" or "Clef" or
        "KeySig" or "TimeSig" or "Bracket" or "MeasureNumber" or "Text";

    // A clef before the first onset belongs to the system header and stays visible.
    // Later clefs are clef changes: they are written out with the nearest onset.
    private bool IsGridElement(XElement element, int row)
    {
        string name = element.Attribute("class")?.Value ?? "";
        if (!IsGridClass(name)) return false;
        if (name != "Clef") return true;
        return Bounds(element) is not { } bounds || bounds.Left <= _rowFirstOnset[row] + 0.01;
    }

    public ScoreReveal RevealForRow(int index)
    {
        if (index < 0 || index >= _reveals.Length) throw new ArgumentOutOfRangeException(nameof(index));
        return _reveals[index];
    }

    public int RevealIndex(int row, double x)
    {
        var positions = RevealForRow(row).Positions;
        int low = 0, high = positions.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (positions[middle] <= x + 1e-9) low = middle + 1; else high = middle;
        }
        return low - 1;
    }

    public double RevealX(int row, int index)
    {
        var reveal = RevealForRow(row);
        if (index < 0 || reveal.Boundaries.Length == 0) return reveal.EmptyX;
        return reveal.Boundaries[Math.Min(index, reveal.Boundaries.Length - 1)];
    }

    private ScoreReveal[] BuildReveals()
    {
        var rowPositions = Rows.Select(_ => new List<double>()).ToArray();
        foreach (var scoreEvent in Events) rowPositions[scoreEvent.Row].Add(scoreEvent.X);
        var result = new ScoreReveal[Rows.Length];
        for (int row = 0; row < Rows.Length; row++)
        {
            var positions = rowPositions[row].Distinct().OrderBy(x => x).ToArray();
            double empty = Rows[row].Left - 1;
            if (positions.Length == 0)
            {
                result[row] = new ScoreReveal(empty, positions, Array.Empty<double>());
                continue;
            }
            double[] boundaries;
            try { boundaries = ComputeBoundaries(row, positions); }
            catch (Exception error) when (error is XmlException or InvalidDataException or FormatException or
                InvalidOperationException or ArgumentException)
            {
                // A malformed row should still be readable; reveal it as a whole.
                boundaries = positions.Select(_ => Rows[row].Right + 350).ToArray();
            }
            result[row] = new ScoreReveal(empty, positions, boundaries);
        }
        return result;
    }

    // The next chord's left bearing (accidental, articulation) can sit left of its
    // onset. Split the gap between the current ink and the next ink instead of using
    // the next onset x, so a detached mark is never partially revealed.
    private double[] ComputeBoundaries(int row, double[] positions)
    {
        var edges = new double[positions.Length + 1];
        edges[0] = Rows[row].Left;
        for (int i = 1; i < positions.Length; i++) edges[i] = (positions[i - 1] + positions[i]) / 2;
        edges[^1] = Rows[row].Right;
        var inkLeft = Enumerable.Repeat(double.PositiveInfinity, positions.Length).ToArray();
        var inkRight = Enumerable.Repeat(double.NegativeInfinity, positions.Length).ToArray();
        var noteLeft = Enumerable.Repeat(double.PositiveInfinity, positions.Length).ToArray();
        var noteRight = Enumerable.Repeat(double.NegativeInfinity, positions.Length).ToArray();
        var guarded = new List<(double Left, double Right)>();
        var root = Xml(SvgForRow(row, true)).Root ?? throw new InvalidDataException("乐谱行 SVG 无根节点。");
        double staffSpace = _rowStaffSpace[row];
        foreach (var element in root.Elements())
        {
            if (element.Name.LocalName is "title" or "desc") continue;
            string className = element.Attribute("class")?.Value ?? "";
            if (className.Length == 0) continue;
            if (Bounds(element) is not { } bounds) continue;
            if (IsGridClass(className) && (className != "Clef" || bounds.Left <= _rowFirstOnset[row] + 0.01))
                continue;
            if (className == "Clef")
            {
                // Keep the clef with the closest onset, then add its full box to that
                // cell so the previous step stops before the clef and this step covers it.
                int target = 0;
                double centre = (bounds.Left + bounds.Right) / 2, distance = double.PositiveInfinity;
                for (int j = 0; j < positions.Length; j++)
                {
                    double candidate = Math.Abs(positions[j] - centre);
                    if (candidate < distance - 1e-9 ||
                        (Math.Abs(candidate - distance) <= 1e-9 && j > target))
                    {
                        distance = candidate; target = j;
                    }
                }
                inkLeft[target] = Math.Min(inkLeft[target], bounds.Left);
                inkRight[target] = Math.Max(inkRight[target], bounds.Right);
                guarded.Add((bounds.Left, bounds.Right));
                continue;
            }
            // Wide spans (beams, ties, hairpins, ottavas) and unknown markup either
            // have no onset of their own or cross several cells; leave them to the clip.
            if (bounds.Right - bounds.Left > staffSpace * 4) continue;
            int index = 0;
            double middle = (bounds.Left + bounds.Right) / 2;
            while (index + 1 < positions.Length && middle >= edges[index + 1]) index++;
            inkLeft[index] = Math.Min(inkLeft[index], bounds.Left);
            inkRight[index] = Math.Max(inkRight[index], bounds.Right);
            if (className is not ("Note" or "Rest")) continue;
            noteLeft[index] = Math.Min(noteLeft[index], bounds.Left);
            noteRight[index] = Math.Max(noteRight[index], bounds.Right);
            guarded.Add((bounds.Left, bounds.Right));
        }
        var boundaries = new double[positions.Length];
        bool extended = false, adjusted = false;
        for (int i = 0; i < positions.Length; i++)
        {
            if (i == positions.Length - 1)
            {
                boundaries[i] = Rows[row].Right + 350;
                continue;
            }
            double right = double.IsNegativeInfinity(inkRight[i]) ? positions[i] : inkRight[i];
            double left = double.IsPositiveInfinity(inkLeft[i + 1]) ? positions[i + 1] : inkLeft[i + 1];
            double currentNoteRight = noteRight[i];
            double nextNoteLeft = noteLeft[i + 1];
            double boundary;
            if (left > right + 2)
                boundary = (right + left) / 2;
            else if (!double.IsNegativeInfinity(currentNoteRight) && left > currentNoteRight + 2)
            {
                // A ledger line or tie can reach into the inter-chord gap; stop in the
                // gap after the notehead but before the next chord's left bearing.
                boundary = (currentNoteRight + left) / 2;
                adjusted = true;
            }
            else if (!double.IsNegativeInfinity(currentNoteRight) && !double.IsPositiveInfinity(nextNoteLeft) &&
                nextNoteLeft > currentNoteRight + 2)
            {
                // The "next" ink can be a stem that belongs to the current chord and its
                // box midpoint fell into the next cell. In that case use the noteheads
                // themselves: a safe gap still exists before the next chord.
                boundary = (currentNoteRight + nextNoteLeft) / 2;
                adjusted = true;
            }
            else
                boundary = left - 1;
            // A tie, beam, stem or ledger line can pull the boundary into a notehead
            // or clef. If any guarded glyph is split, extend past it. Neighbouring
            // chords in a tuplet or grace group are then revealed together instead of
            // one of their noteheads being cut.
            for (int pass = 0; pass <= guarded.Count; pass++)
            {
                double extend = double.NegativeInfinity;
                foreach (var glyph in guarded)
                {
                    double width = glyph.Right - glyph.Left;
                    if (width <= 0) continue;
                    double visible = Math.Clamp(boundary - glyph.Left, 0, width);
                    if (visible > 0.01 && width - visible > 0.01)
                        extend = Math.Max(extend, glyph.Right + 1);
                }
                if (extend <= boundary) break;
                boundary = extend;
                extended = true;
            }
            boundaries[i] = boundary;
        }
        if (extended || adjusted)
            for (int i = 1; i < boundaries.Length; i++)
                boundaries[i] = Math.Max(boundaries[i], boundaries[i - 1]);
        return boundaries;
    }

    private sealed record Element(int Id, int Page, double X, double Y, double Width, double Height);
    private sealed record SvgBox(int Index, string Class, double Left, double Top, double Right, double Bottom);
    private sealed record SemanticAnchor(int Page, int Row, string Category, double X);
    private readonly record struct PathToken(bool IsCommand, char Command, double Value);

    // Reads SVG geometry once per page so each system owns the glyphs it actually paints.
    private sealed class PageGeometry
    {
        public readonly List<SvgBox> Boxes = new();
        public readonly HashSet<int> Keep = new();
        public double StaffSpace { get; private set; } = 1;

        public static PageGeometry Read(ScorePage page)
        {
            var root = Xml(page.Svg).Root ?? throw new InvalidDataException("数据包包含无效 SVG。");
            var geometry = new PageGeometry();
            var staffCentres = new List<double>();
            int index = 0, background = -1;
            foreach (var element in root.Elements())
            {
                int current = index++;
                string name = element.Name.LocalName;
                if (name is "title" or "desc") { geometry.Keep.Add(current); continue; }
                string style = element.Attribute("class")?.Value ?? "";
                if (background < 0 && name == "path" && style.Length == 0 &&
                    string.Equals(element.Attribute("fill")?.Value, "#ffffff", StringComparison.OrdinalIgnoreCase))
                {
                    background = current;
                    geometry.Keep.Add(current);
                    continue;
                }
                if (style == "Text") continue;
                if (Bounds(element) is not { } bounds)
                {
                    // Unrecognised markup stays available to every strip instead of disappearing.
                    geometry.Keep.Add(current);
                    continue;
                }
                var box = new SvgBox(current, style, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
                geometry.Boxes.Add(box);
                if (style == "StaffLines") staffCentres.Add((box.Top + box.Bottom) / 2);
            }
            var ordered = staffCentres.Distinct().OrderBy(value => value).ToArray();
            double spacing = 0;
            for (int i = 1; i < ordered.Length; i++)
            {
                double difference = ordered[i] - ordered[i - 1];
                if (difference > 1 && (spacing == 0 || difference < spacing)) spacing = difference;
            }
            geometry.StaffSpace = spacing > 0 ? spacing : Math.Max(1, page.Width * 0.008);
            return geometry;
        }
    }

    private static HashSet<int>[] AssignRows(ScoreRow[] rows, PageGeometry[] pages, List<SemanticAnchor> anchors)
    {
        anchors ??= new List<SemanticAnchor>();
        var keep = rows.Select(_ => new HashSet<int>()).ToArray();
        var inkTop = rows.Select(row => row.Top).ToArray();
        var inkBottom = rows.Select(row => row.Top + row.Height).ToArray();
        var rowsByPage = Enumerable.Range(0, rows.Length).GroupBy(index => rows[index].Page)
            .ToDictionary(group => group.Key, group => group.ToArray());
        for (int pageIndex = 0; pageIndex < pages.Length; pageIndex++)
        {
            if (!rowsByPage.TryGetValue(pageIndex, out var pageRows)) continue;
            var geometry = pages[pageIndex];
            foreach (int row in pageRows) keep[row].UnionWith(geometry.Keep);
            var boxes = geometry.Boxes;
            if (boxes.Count == 0) continue;
            double gap = Math.Max(1, geometry.StaffSpace * 0.15);
            var order = Enumerable.Range(0, boxes.Count).OrderBy(index => boxes[index].Top).ToArray();
            var parent = Enumerable.Range(0, boxes.Count).ToArray();
            int Find(int item)
            {
                while (parent[item] != item)
                {
                    parent[item] = parent[parent[item]];
                    item = parent[item];
                }
                return item;
            }
            void Union(int a, int b)
            {
                int rootA = Find(a), rootB = Find(b);
                if (rootA != rootB) parent[rootB] = rootA;
            }
            for (int a = 0; a < order.Length; a++)
            {
                var boxA = boxes[order[a]];
                for (int b = a + 1; b < order.Length; b++)
                {
                    var boxB = boxes[order[b]];
                    if (boxB.Top > boxA.Bottom + gap) break;
                    if (boxB.Left <= boxA.Right + gap && boxA.Left <= boxB.Right + gap)
                        Union(order[a], order[b]);
                }
            }
            var components = new Dictionary<int, List<int>>();
            for (int i = 0; i < boxes.Count; i++)
            {
                int root = Find(i);
                if (!components.TryGetValue(root, out var list)) components[root] = list = new List<int>();
                list.Add(i);
            }
            foreach (var component in components.Values)
            {
                double left = double.PositiveInfinity, top = double.PositiveInfinity;
                double right = double.NegativeInfinity, bottom = double.NegativeInfinity;
                foreach (int item in component)
                {
                    var box = boxes[item];
                    left = Math.Min(left, box.Left); right = Math.Max(right, box.Right);
                    top = Math.Min(top, box.Top); bottom = Math.Max(bottom, box.Bottom);
                }
                var owners = new List<int>();
                double nearest = double.PositiveInfinity;
                // Stems can point away from their staff and pull a high/low chord towards
                // the neighbouring system. Ledger lines are always the side that steps
                // back towards the staff, so they are the reliable ownership anchor.
                var ledgers = component.Select(item => boxes[item])
                    .Where(box => box.Class == "LedgerLine").ToArray();
                // A pedal is a detached spanner below its system. When systems are close,
                // its glyphs can be nearer the next system's top edge than their own
                // staff's bottom edge, so only consider staffs above the pedal.
                bool pedal = component.All(item => IsPedalClass(boxes[item].Class));
                bool pedalBelowStaff = pedal && pageRows.Any(row =>
                    rows[row].Top + rows[row].Height <= top + 0.5);
                foreach (int row in pageRows)
                {
                    double distance;
                    if (pedalBelowStaff)
                    {
                        double staffBottom = rows[row].Top + rows[row].Height;
                        if (staffBottom > top + 0.5) continue;
                        distance = top - staffBottom;
                    }
                    else
                    {
                        distance = ledgers.Length > 0
                            ? ledgers.Min(box => RowGap(box.Top, box.Bottom, rows[row]))
                            : RowGap(top, bottom, rows[row]);
                    }
                    if (distance < nearest - 1)
                    {
                        nearest = distance;
                        owners.Clear();
                        owners.Add(row);
                    }
                    else if (distance <= nearest + 1) owners.Add(row);
                }
                // Detached markings (articulations, tempo, tuplets) have no line back to
                // their staff. MusicXML knows the measure they belong to, so use its
                // predicted x position instead of the nearest system edge.
                int semantic = SemanticOwner(component, boxes, left, top, bottom, anchors, rows,
                    pageIndex, geometry.StaffSpace);
                if (semantic >= 0)
                {
                    owners.Clear();
                    owners.Add(semantic);
                }
                foreach (int row in owners)
                {
                    foreach (int item in component) keep[row].Add(boxes[item].Index);
                    inkTop[row] = Math.Min(inkTop[row], top);
                    inkBottom[row] = Math.Max(inkBottom[row], bottom);
                }
            }
        }

        // One top margin and one staff height for every row keeps the system fixed on
        // screen while the score moves from row to row. Margins are measured in staff
        // spaces so changing MuseScore's PDF DPI does not change the crop.
        const double marginSpaces = 0.5;
        double topSpaces = marginSpaces, bottomSpaces = marginSpaces, staffSpaces = 0;
        for (int i = 0; i < rows.Length; i++)
        {
            double space = Math.Max(1, pages[rows[i].Page].StaffSpace);
            topSpaces = Math.Max(topSpaces, (rows[i].Top - inkTop[i]) / space + marginSpaces);
            bottomSpaces = Math.Max(bottomSpaces, (inkBottom[i] - rows[i].Top - rows[i].Height) / space + marginSpaces);
            staffSpaces = Math.Max(staffSpaces, rows[i].Height / space);
        }
        for (int i = 0; i < rows.Length; i++)
        {
            double space = Math.Max(1, pages[rows[i].Page].StaffSpace);
            double topMargin = topSpaces * space;
            rows[i] = rows[i] with
            {
                CropTop = rows[i].Top - topMargin,
                CropHeight = (topSpaces + staffSpaces + bottomSpaces) * space
            };
        }
        return keep;
    }

    private static double RowGap(double top, double bottom, ScoreRow row)
    {
        double staffTop = row.Top, staffBottom = row.Top + row.Height;
        if (bottom < staffTop) return staffTop - bottom;
        if (top > staffBottom) return top - staffBottom;
        return 0;
    }

    // The SVG export has no element IDs, but the bundle's MusicXML names the measure
    // and horizontal tenths position of every detached marking. Measure boxes turn
    // that into an SVG x, which is enough to keep a symbol with its own system.
    private static int SemanticOwner(List<int> component, List<SvgBox> boxes, double left,
        double top, double bottom, List<SemanticAnchor> anchors, ScoreRow[] rows, int page, double staffSpace)
    {
        string category = null;
        foreach (int item in component)
        {
            string name = boxes[item].Class;
            if (!IsFloatingClass(name)) return -1;
            string current = SvgCategory(name);
            if (current == null) return -1;
            if (category == null) category = current;
            else if (category != current) return -1;
        }
        if (category == null || anchors.Count == 0) return -1;
        // MusicXML default-x is the symbol's left edge; wide tuplets and text can
        // round differently, so they get a slightly wider matching window.
        double tolerance = category switch
        {
            "NoteSymbol" => staffSpace * 0.2,
            "Tuplet" => staffSpace * 0.5,
            _ => staffSpace * 0.75
        };
        tolerance = Math.Max(2, tolerance);
        var distances = new Dictionary<int, double>();
        foreach (var anchor in anchors)
        {
            if (anchor.Page != page || anchor.Category != category) continue;
            // The same margin x (measure numbers, repeated dynamics) can match an
            // anchor on another system. A detached mark is always vertically near its
            // own system, so reject anchors that are clearly on a different row.
            if (RowGap(top, bottom, rows[anchor.Row]) > staffSpace * 8) continue;
            double distance = Math.Abs(anchor.X - left);
            if (!distances.TryGetValue(anchor.Row, out double best) || distance < best)
                distances[anchor.Row] = distance;
        }
        if (distances.Count == 0) return -1;
        var ranked = distances.OrderBy(pair => pair.Value).ToArray();
        if (ranked[0].Value > tolerance) return -1;
        // A repeated pattern can put an equally close anchor on two systems. Keep the
        // geometric result rather than guessing in that genuinely ambiguous case.
        if (ranked.Length > 1 && ranked[1].Value <= ranked[0].Value + Math.Max(2, staffSpace * 0.1))
            return -1;
        return ranked[0].Key;
    }

    // Measure numbers are placed just above their own system and often share the
    // left margin with text on another system. They must stay with their geometric
    // row instead of matching an unrelated Text anchor.
    private static bool IsFloatingClass(string name) => name is
        "Articulation" or "Ornament" or "Fingering" or "Tempo" or "Text" or "Tuplet" or
        "Dynamic" or "RehearsalMark" or "SystemText" or "StaffText" or
        "Expression" or "Harmony" or "Lyrics" or "ChordSymbol" or "FiguredBase";

    // Pedal spanners are attached below a staff rather than floating beside it.
    private static bool IsPedalClass(string name) => name is "PedalSegment" or "Pedal";

    private static string SvgCategory(string name) => name switch
    {
        "Articulation" or "Ornament" or "Fingering" => "NoteSymbol",
        "Tuplet" => "Tuplet",
        "Tempo" or "Text" or "Dynamic" or "RehearsalMark" or "SystemText" or "StaffText" or
            "Expression" or "Harmony" or "Lyrics" or "ChordSymbol" or "FiguredBase" or "MeasureNumber" => "Text",
        _ => null
    };

    private static List<SemanticAnchor> SemanticAnchors(JsonElement data, Dictionary<int, Element> measures,
        ScoreRow[] rows, PageGeometry[] pages)
    {
        var result = new List<SemanticAnchor>();
        try
        {
            if (!data.TryGetProperty("mxml", out var encoded) || encoded.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(encoded.GetString())) return result;
            string xml = MusicXmlText(Convert.FromBase64String(encoded.GetString()));
            var root = MusicXmlDocument(xml).Root;
            if (root == null || root.Name.LocalName != "score-partwise") return result;
            var measureOrder = measures.Values.OrderBy(measure => measure.Id).ToArray();
            var rowsByPage = Enumerable.Range(0, rows.Length).GroupBy(index => rows[index].Page)
                .ToDictionary(group => group.Key, group => group.ToArray());
            foreach (var part in root.Elements().Where(element => element.Name.LocalName == "part"))
            {
                var xmlMeasures = part.Elements().Where(element => element.Name.LocalName == "measure").ToArray();
                if (xmlMeasures.Length != measureOrder.Length) continue;
                for (int index = 0; index < measureOrder.Length; index++)
                {
                    var position = measureOrder[index];
                    if (!rowsByPage.TryGetValue(position.Page, out var pageRows)) continue;
                    int row = pageRows.OrderBy(candidate => Math.Abs(rows[candidate].Top - position.Y))
                        .FirstOrDefault(-1);
                    if (row < 0 || Math.Abs(rows[row].Top - position.Y) > 2) continue;
                    double unit = pages[position.Page].StaffSpace / 10;
                    if (!double.IsFinite(unit) || unit <= 0) continue;
                    void Add(double x, string category)
                    {
                        if (category != null && double.IsFinite(x))
                            result.Add(new SemanticAnchor(position.Page, row, category, x));
                    }
                    CollectAnchors(xmlMeasures[index], position.X, unit, Add);
                }
            }
        }
        catch (Exception error) when (error is FormatException or InvalidDataException or XmlException or
            IOException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            // MusicXML is an optional refinement; old or hand-made bundles keep geometric ownership.
            result.Clear();
        }
        return result;
    }

    private static void CollectAnchors(XElement measure, double left, double unit, Action<double, string> add)
    {
        foreach (var note in measure.Descendants().Where(element => element.Name.LocalName == "note"))
        {
            double noteX = OptionalNumber(note, "default-x") ?? 0;
            foreach (var notations in note.Elements().Where(element => element.Name.LocalName == "notations"))
            {
                foreach (var group in notations.Elements())
                {
                    string name = group.Name.LocalName;
                    if (name is "articulations" or "ornaments" or "technical")
                    {
                        foreach (var mark in group.Elements())
                            add(left + (noteX + (OptionalNumber(mark, "default-x") ?? 0)) * unit, "NoteSymbol");
                    }
                    else if (name == "tuplet")
                    {
                        add(left + (noteX + (OptionalNumber(group, "default-x") ?? 0)) * unit, "Tuplet");
                    }
                    else if (name == "fermata")
                    {
                        add(left + (noteX + (OptionalNumber(group, "default-x") ?? 0)) * unit, "NoteSymbol");
                    }
                }
            }
        }
        foreach (var direction in measure.Elements().Where(element => element.Name.LocalName == "direction"))
        {
            double? directionX = OptionalNumber(direction, "default-x");
            foreach (var type in direction.Elements().Where(element => element.Name.LocalName == "direction-type"))
            {
                foreach (var mark in type.Elements())
                {
                    string category = mark.Name.LocalName switch
                    {
                        "words" or "metronome" or "dynamics" or "rehearsal" or "coda" or "segno" => "Text",
                        _ => null
                    };
                    if (category == null || (OptionalNumber(mark, "default-x") ?? directionX) is not { } x) continue;
                    add(left + x * unit, category);
                }
            }
        }
        foreach (var harmony in measure.Elements().Where(element =>
            element.Name.LocalName is "harmony" or "figured-bass"))
        {
            if (OptionalNumber(harmony, "default-x") is not { } x) continue;
            add(left + x * unit, "Text");
        }
    }

    private static double? OptionalNumber(XElement element, string name) =>
        double.TryParse(element.Attribute(name)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture,
            out double value) && double.IsFinite(value) ? value : null;

    private static string MusicXmlText(byte[] bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0x50 || bytes[1] != 0x4B || bytes[2] != 0x03 || bytes[3] != 0x04)
            return Encoding.UTF8.GetString(bytes);
        using var stream = new MemoryStream(bytes, false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.Entries.FirstOrDefault(item =>
                string.Equals(item.FullName, "score.xml", StringComparison.OrdinalIgnoreCase))
            ?? archive.Entries.FirstOrDefault(item =>
                item.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                !item.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase));
        if (entry == null) throw new InvalidDataException("MusicXML 压缩包缺少乐谱文件。");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, true);
        return reader.ReadToEnd();
    }

    private static XDocument MusicXmlDocument(string text)
    {
        // MuseScore writes a standard MusicXML DOCTYPE; ignoring it avoids any network
        // lookup while still accepting the document body.
        using var reader = XmlReader.Create(new StringReader(text),
            new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
        return XDocument.Load(reader);
    }

    private static (double Left, double Top, double Right, double Bottom)? Bounds(XElement element)
    {
        string name = element.Name.LocalName;
        double left, top, right, bottom;
        if (name == "path")
        {
            string data = element.Attribute("d")?.Value;
            if (string.IsNullOrEmpty(data) || PathBounds(data) is not { } box) return null;
            (left, top, right, bottom) = box;
        }
        else if (name is "polyline" or "polygon")
        {
            string points = element.Attribute("points")?.Value;
            if (string.IsNullOrEmpty(points) || !PointBounds(points, out left, out top, out right, out bottom)) return null;
        }
        else return null;
        if (element.Attribute("transform")?.Value is { Length: > 0 } transform)
            ApplyTransform(transform, ref left, ref top, ref right, ref bottom);
        string stroke = element.Attribute("stroke")?.Value;
        if (stroke != null && !stroke.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            double width = 1;
            if (double.TryParse(element.Attribute("stroke-width")?.Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double parsed) && parsed > 0) width = parsed;
            double half = width / 2;
            left -= half; top -= half; right += half; bottom += half;
        }
        return (left, top, right, bottom);
    }

    private static bool PointBounds(string points, out double left, out double top, out double right, out double bottom)
    {
        var values = new List<double>();
        TryNumbers(points, values);
        if (values.Count < 4) { left = top = right = bottom = 0; return false; }
        left = right = values[0]; top = bottom = values[1];
        for (int i = 2; i + 1 < values.Count; i += 2)
        {
            left = Math.Min(left, values[i]); right = Math.Max(right, values[i]);
            top = Math.Min(top, values[i + 1]); bottom = Math.Max(bottom, values[i + 1]);
        }
        return true;
    }

    private static (double Left, double Top, double Right, double Bottom)? PathBounds(string data)
    {
        double left = double.PositiveInfinity, top = double.PositiveInfinity;
        double right = double.NegativeInfinity, bottom = double.NegativeInfinity;
        bool any = false;
        double currentX = 0, currentY = 0, startX = 0, startY = 0;
        char command = '\0';
        var tokens = TokenizePath(data);
        int position = 0;
        bool Next(out double value)
        {
            if (position < tokens.Count && !tokens[position].IsCommand)
            {
                value = tokens[position].Value;
                position++;
                return true;
            }
            value = 0;
            return false;
        }
        void Add(double x, double y)
        {
            if (!double.IsFinite(x) || !double.IsFinite(y)) return;
            left = Math.Min(left, x); right = Math.Max(right, x);
            top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            any = true;
        }
        while (position < tokens.Count)
        {
            if (tokens[position].IsCommand)
            {
                command = tokens[position].Command;
                position++;
                if (command is 'Z' or 'z') { currentX = startX; currentY = startY; }
                continue;
            }
            if (command == '\0') { position++; continue; }
            bool relative = char.IsLower(command);
            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                {
                    if (!Next(out double x) || !Next(out double y)) return any ? (left, top, right, bottom) : null;
                    if (relative) { x += currentX; y += currentY; }
                    currentX = x; currentY = y; startX = x; startY = y; Add(x, y);
                    command = relative ? 'l' : 'L';
                    break;
                }
                case 'L':
                {
                    if (!Next(out double x) || !Next(out double y)) return any ? (left, top, right, bottom) : null;
                    if (relative) { x += currentX; y += currentY; }
                    currentX = x; currentY = y; Add(x, y);
                    break;
                }
                case 'H':
                {
                    if (!Next(out double x)) return any ? (left, top, right, bottom) : null;
                    if (relative) x += currentX;
                    currentX = x; Add(x, currentY);
                    break;
                }
                case 'V':
                {
                    if (!Next(out double y)) return any ? (left, top, right, bottom) : null;
                    if (relative) y += currentY;
                    currentY = y; Add(currentX, y);
                    break;
                }
                case 'C':
                {
                    if (!Next(out double x1) || !Next(out double y1) || !Next(out double x2) ||
                        !Next(out double y2) || !Next(out double x) || !Next(out double y)) return any ? (left, top, right, bottom) : null;
                    if (relative)
                    {
                        x1 += currentX; y1 += currentY; x2 += currentX;
                        y2 += currentY; x += currentX; y += currentY;
                    }
                    Add(x1, y1); Add(x2, y2); Add(x, y);
                    currentX = x; currentY = y;
                    break;
                }
                case 'S':
                case 'Q':
                {
                    if (!Next(out double x1) || !Next(out double y1) ||
                        !Next(out double x) || !Next(out double y)) return any ? (left, top, right, bottom) : null;
                    if (relative) { x1 += currentX; y1 += currentY; x += currentX; y += currentY; }
                    Add(x1, y1); Add(x, y);
                    currentX = x; currentY = y;
                    break;
                }
                case 'T':
                {
                    if (!Next(out double x) || !Next(out double y)) return any ? (left, top, right, bottom) : null;
                    if (relative) { x += currentX; y += currentY; }
                    Add(x, y);
                    currentX = x; currentY = y;
                    break;
                }
                case 'A':
                {
                    if (!Next(out _) || !Next(out _) || !Next(out _) || !Next(out _) ||
                        !Next(out _) || !Next(out double x) || !Next(out double y)) return any ? (left, top, right, bottom) : null;
                    if (relative) { x += currentX; y += currentY; }
                    Add(x, y);
                    currentX = x; currentY = y;
                    break;
                }
                default:
                    position++;
                    break;
            }
        }
        return any ? (left, top, right, bottom) : null;
    }

    private static List<PathToken> TokenizePath(string data)
    {
        var tokens = new List<PathToken>();
        int i = 0;
        while (i < data.Length)
        {
            char current = data[i];
            if (char.IsWhiteSpace(current) || current == ',') { i++; continue; }
            if (char.IsLetter(current))
            {
                tokens.Add(new PathToken(true, current, 0));
                i++;
                continue;
            }
            int start = i;
            if (current is '+' or '-') i++;
            bool digits = false;
            while (i < data.Length && char.IsDigit(data[i])) { i++; digits = true; }
            if (i < data.Length && data[i] == '.')
            {
                i++;
                while (i < data.Length && char.IsDigit(data[i])) { i++; digits = true; }
            }
            if (!digits) { i++; continue; }
            if (i < data.Length && (data[i] == 'e' || data[i] == 'E'))
            {
                int mark = i++;
                if (i < data.Length && (data[i] == '+' || data[i] == '-')) i++;
                int exponent = i;
                while (i < data.Length && char.IsDigit(data[i])) i++;
                if (i == exponent) i = mark;
            }
            if (double.TryParse(data[start..i], NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                tokens.Add(new PathToken(false, '\0', value));
        }
        return tokens;
    }

    private static void ApplyTransform(string transform, ref double left, ref double top, ref double right, ref double bottom)
    {
        foreach (Match match in Regex.Matches(transform, @"([A-Za-z]+)\s*\(([^)]*)\)"))
        {
            var values = new List<double>();
            TryNumbers(match.Groups[2].Value, values);
            switch (match.Groups[1].Value)
            {
                case "matrix" when values.Count >= 6:
                    (left, top, right, bottom) = TransformBounds(left, top, right, bottom,
                        values[0], values[1], values[2], values[3], values[4], values[5]);
                    break;
                case "translate" when values.Count >= 1:
                    (left, top, right, bottom) = TransformBounds(left, top, right, bottom,
                        1, 0, 0, 1, values[0], values.Count > 1 ? values[1] : 0);
                    break;
                case "scale" when values.Count >= 1:
                    (left, top, right, bottom) = TransformBounds(left, top, right, bottom,
                        values[0], 0, 0, values.Count > 1 ? values[1] : values[0], 0, 0);
                    break;
            }
        }
    }

    private static (double Left, double Top, double Right, double Bottom) TransformBounds(
        double left, double top, double right, double bottom,
        double a, double b, double c, double d, double e, double f)
    {
        var points = new[]
        {
            (X: a * left + c * top + e, Y: b * left + d * top + f),
            (X: a * right + c * top + e, Y: b * right + d * top + f),
            (X: a * left + c * bottom + e, Y: b * left + d * bottom + f),
            (X: a * right + c * bottom + e, Y: b * right + d * bottom + f)
        };
        return (points.Min(point => point.X), points.Min(point => point.Y),
            points.Max(point => point.X), points.Max(point => point.Y));
    }

    private static bool TryNumbers(string text, List<double> values)
    {
        foreach (Match match in Regex.Matches(text, @"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?"))
            if (double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                values.Add(value);
        return values.Count > 0;
    }
    private static Dictionary<int, Element> Elements(string xml, int pageCount, double coordinateScale)
    {
        var root = Xml(xml).Root;
        var nodes = root?.Elements().FirstOrDefault(e => e.Name.LocalName == "elements")?.Elements()
            ?? throw new InvalidDataException("乐谱位置表缺少元素。");
        var result = new Dictionary<int, Element>();
        foreach (var node in nodes)
        {
            int id = Integer(node, "id"), page = Integer(node, "page");
            double width = Attribute(node, "sx") / coordinateScale, height = Attribute(node, "sy") / coordinateScale;
            if (page < 0 || page >= pageCount || width < 0 || height <= 0 || id < 0)
                throw new InvalidDataException("乐谱位置表包含无效尺寸、页码或编号。");
            if (!result.TryAdd(id, new Element(id, page, Attribute(node, "x") / coordinateScale,
                Attribute(node, "y") / coordinateScale, width, height)))
                throw new InvalidDataException("乐谱位置表包含重复编号。");
        }
        return result;
    }

    // Positions are 12x the packaged PNG raster; its pixel width (IHDR header, big-endian)
    // anchors that space onto the SVG viewBox regardless of export preferences.
    private static double PositionScale(ScorePage page, JsonElement data)
    {
        try
        {
            if (!data.TryGetProperty("pngs", out var pngs) || pngs.ValueKind != JsonValueKind.Array
                || pngs.GetArrayLength() == 0 || pngs[0].ValueKind != JsonValueKind.String) return CoordinateScale;
            byte[] png = Convert.FromBase64String(pngs[0].GetString()
                ?? throw new InvalidDataException("乐谱数据字段不能为空。"));
            bool isPng = png.Length >= 24 && png[0] == 0x89 && png[1] == 0x50 && png[2] == 0x4E && png[3] == 0x47;
            if (!isPng || page.Width <= 0) return CoordinateScale;
            long pngWidth = ((long)png[16] << 24) | png[17] << 16 | png[18] << 8 | png[19];
            if (pngWidth <= 0) return CoordinateScale;
            double scale = CoordinateScale * pngWidth / page.Width;
            return double.IsFinite(scale) && scale is > 0 and <= 120 ? scale : CoordinateScale;
        }
        catch (Exception error) when (error is FormatException or InvalidDataException)
        {
            return CoordinateScale;
        }
    }

    private static XDocument Xml(string text)
    {
        using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }
    private static double Attribute(XElement element, string name) => Number(element.Attribute(name)?.Value
        ?? throw new InvalidDataException("乐谱位置表缺少 " + name + "。"));
    private static int Integer(XElement element, string name) => int.TryParse(element.Attribute(name)?.Value,
        NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : throw new InvalidDataException("无效的乐谱编号 " + name + "。");
    private static double Number(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value)
        ? value : throw new InvalidDataException("乐谱位置表包含无效数值。");
    private static string Format(double value) => value.ToString("0.#########", CultureInfo.InvariantCulture);
}
