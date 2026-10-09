using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    private MuseScoreBundle(string title, ScorePage[] pages, ScoreRow[] rows,
        ScoreEvent[] events, ScoreMeasureEvent[] measureEvents, byte[] midi, double duration)
    {
        Title = title; Pages = pages; Rows = rows; Events = events; Midi = midi; DurationSeconds = duration;
        MeasureEvents = measureEvents; Timeline = new ScoreTimeline(rows, events, duration);
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
        var tempos = pages.SelectMany((page, index) => Xml(page.Svg).Root.Elements()
            .Where(e => e.Attribute("class")?.Value == "Tempo")
            .Select(e => TempoBounds(e, rows, index)).Where(e => e != null)).ToArray();
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            // mpos covers the staves; tempo, ottava and measure numbers extend beyond them.
            const double padding = 350;
            double top = Math.Max(0, row.Top - padding);
            double bottom = Math.Min(pages[row.Page].Height, row.Top + row.Height + padding);
            if (i > 0 && rows[i - 1].Page == row.Page)
                top = Math.Max(top, (rows[i - 1].Top + rows[i - 1].Height + row.Top) / 2);
            if (i + 1 < rows.Length && rows[i + 1].Page == row.Page)
                bottom = Math.Min(bottom, (row.Top + row.Height + rows[i + 1].Top) / 2);
            foreach (var tempo in tempos.Where(t => t.Row == i))
                top = Math.Min(top, Math.Max(0, tempo.Top - 60));
            if (bottom <= top || row.Left < 0 || row.Right > pages[row.Page].Width + 2)
                throw new InvalidDataException("乐谱位置超出 SVG 页面，请使用默认参数导出（不要使用 -r）。");
            rows[i] = row with { CropTop = top, CropHeight = bottom - top };
        }

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
        return new MuseScoreBundle(title, pages, rows, events, measureEvents, midi, duration);
    }

    public ScoreCursor CursorAt(double seconds) => Timeline.CursorAt(seconds);

    public string SvgForRow(int index, bool removeBackground = true, string mainColor = null)
    {
        if (index < 0 || index >= Rows.Length) throw new ArgumentOutOfRangeException(nameof(index));
        var row = Rows[index];
        var page = Pages[row.Page];
        var document = Xml(page.Svg);
        var root = document.Root;
        var background = root.Elements().FirstOrDefault(e => e.Name.LocalName == "path" &&
            e.Attribute("class")?.Value == "" && string.Equals(e.Attribute("fill")?.Value, "#ffffff", StringComparison.OrdinalIgnoreCase));
        if (removeBackground) background?.Remove();
        foreach (var element in root.Elements().ToArray())
        {
            // MuseScore page-frame text (title/credits/page number) is outside the system strip.
            if (element.Attribute("class")?.Value == "Text") element.Remove();
            else if (element.Attribute("class")?.Value == "Tempo" &&
                TempoBounds(element, Rows, row.Page) is { } tempo && tempo.Row != index)
                element.Remove();
        }
        if (mainColor != null)
        {
            Trifle.Visuals.VisualSettings.ValidateColor(mainColor, "乐谱主颜色");
            string rgb = "#" + mainColor[..6];
            var content = root.Elements().Where(e => e != background).ToArray();
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

    private sealed record Element(int Id, int Page, double X, double Y, double Width, double Height);
    private sealed record Tempo(int Row, double Top);
    private static Tempo TempoBounds(XElement element, ScoreRow[] rows, int page)
    {
        string path = element.Attribute("d")?.Value ?? "";
        // MuseScore emits absolute M/L/C paths for text. Control points give conservative bounds.
        // Other path dialects keep the regular system padding instead of guessing coordinates.
        if (path.Length == 0 || Regex.IsMatch(path, @"[^MLCZ0-9.,\s+\-]")) return null;
        var values = Regex.Matches(path, @"[-+]?(?:\d+\.?\d*|\.\d+)").Select(m => Number(m.Value)).ToArray();
        if (values.Length < 2 || values.Length % 2 != 0) return null;
        var ys = values.Where((_, i) => i % 2 == 1).ToArray();
        double top = ys.Min(), middle = (top + ys.Max()) / 2;
        int row = Enumerable.Range(0, rows.Length).Where(i => rows[i].Page == page)
            .OrderBy(i => Math.Abs(rows[i].Top - middle)).FirstOrDefault(-1);
        return row < 0 ? null : new Tempo(row, top);
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
