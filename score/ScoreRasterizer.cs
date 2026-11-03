using Godot;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Trifle.Score;

// Contains only CPU image work. Each call owns its Image; it never touches nodes or the GPU.
internal static class ScoreRasterizer
{
    internal sealed record Prepared(int Width, int Height, byte[] Pixels, double Milliseconds);
    internal sealed record PreparedSet(Prepared Combined, Prepared Grid, Prepared Notation, double Milliseconds);

    internal static PreparedSet PrepareSet(MuseScoreBundle bundle, int row, ScoreSettings settings,
        int renderWidth, CancellationToken cancellation = default)
    {
        if (settings.DisplayEffect != ScoreDisplayEffect.Typewriter)
        {
            var combined = Prepare(bundle, row, settings, renderWidth, cancellation);
            return new PreparedSet(combined, null, null, combined.Milliseconds);
        }
        var watch = Stopwatch.StartNew();
        var grid = RenderLayer(bundle, row, settings, renderWidth, ScoreLayer.Grid, cancellation);
        var notation = RenderLayer(bundle, row, settings, renderWidth, ScoreLayer.Notation, cancellation);
        return new PreparedSet(null, grid, notation, watch.Elapsed.TotalMilliseconds);
    }

    internal static Prepared Prepare(MuseScoreBundle bundle, int row, ScoreSettings settings,
        int renderWidth, CancellationToken cancellation = default) =>
        RenderLayer(bundle, row, settings, renderWidth, ScoreLayer.All, cancellation);

    private static Prepared RenderLayer(MuseScoreBundle bundle, int row, ScoreSettings settings,
        int renderWidth, ScoreLayer layer, CancellationToken cancellation)
    {
        var watch = Stopwatch.StartNew();
        cancellation.ThrowIfCancellationRequested();
        float scale = (float)(renderWidth * settings.Width / bundle.Pages[bundle.Rows[row].Page].Width);
        // Keep group opacity in the coverage mask. RGB is applied in the notation shader,
        // so transparent RGB never needs a costly neighborhood search before/after resizing.
        string svg = bundle.SvgForRow(row, true, "ffffff" + settings.MainColor[6..], layer);
        cancellation.ThrowIfCancellationRequested();
        using var image = new Image();
        Error error = image.LoadSvgFromString(svg, scale * 2);
        if (error != Error.Ok || image.IsEmpty()) throw new InvalidDataException("乐谱 SVG 渲染失败：" + error);
        cancellation.ThrowIfCancellationRequested();
        image.Resize(Math.Max(1, (image.GetWidth() + 1) / 2), Math.Max(1, (image.GetHeight() + 1) / 2),
            Image.Interpolation.Bilinear);
        cancellation.ThrowIfCancellationRequested();
        return new Prepared(image.GetWidth(), image.GetHeight(), image.GetData(), watch.Elapsed.TotalMilliseconds);
    }
}
