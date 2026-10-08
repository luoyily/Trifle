using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Trifle.Score;

// Rasterize on row, appearance or resolution changes. Ordinary playback only moves the cursor.
public partial class ScoreStrip : Node2D
{
    public MuseScoreBundle Bundle { get; private set; }
    public ScoreCursor Cursor { get; private set; }
    public int RasterCount { get; private set; }
    public int CachedRows => _cache.Count;
    public double LastRasterMilliseconds { get; private set; }
    public ScoreSettings Settings { get; private set; } = new();
    public double WidthFraction => Settings.Width;
    public double YFraction => Settings.Y;
    public int RenderWidth { get; private set; } = 1920;
    public double CursorGain { get; private set; } = 1;
    public Vector2I TextureSize => _texture == null ? Vector2I.Zero : new(_texture.GetWidth(), _texture.GetHeight());
    private readonly Dictionary<int, ImageTexture> _cache = new();
    private readonly Queue<int> _order = new();
    private ImageTexture _texture;
    private Sprite2D _notation;
    private Line2D _cursorLine;
    private ShaderMaterial _notationMaterial, _cursorMaterial;
    private Rect2 _bounds;
    private readonly StyleBoxFlat _panel = new()
    {
        BgColor = Colors.White, CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
        CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8
    };

    public override void _Ready()
    {
        var shader = GD.Load<Shader>("res://score/brightness.gdshader");
        _notationMaterial = new ShaderMaterial { Shader = shader };
        _cursorMaterial = new ShaderMaterial { Shader = shader };
        _notation = new Sprite2D { Name = "Notation", Centered = false, RegionEnabled = true,
            RegionFilterClipEnabled = true, Material = _notationMaterial };
        _cursorLine = new Line2D { Name = "Cursor", Width = 3, Antialiased = true, Material = _cursorMaterial };
        AddChild(_notation); AddChild(_cursorLine);
        RefreshDrawing();
    }

    public void SetBundle(MuseScoreBundle bundle, ScoreSettings settings = null)
    {
        if (bundle == null) throw new ArgumentNullException(nameof(bundle));
        settings ??= Settings;
        settings.Validate();
        var cursor = bundle.CursorAt(0);
        // Render before replacing the usable bundle, so a failed import leaves it intact.
        var texture = Rasterize(bundle, cursor.Row, settings, RenderWidth);
        ClearCache();
        Bundle = bundle; Cursor = cursor; Settings = settings; Visible = settings.Enabled;
        _cache.Add(cursor.Row, texture); _order.Enqueue(cursor.Row); _texture = texture;
        CursorGain = ScorePulse.Gain(-bundle.Events[cursor.Event].Seconds);
        RefreshDrawing();
    }

    public void ApplySettings(ScoreSettings settings)
    {
        settings.Validate();
        bool repaint = settings.Width != Settings.Width || settings.MainColor != Settings.MainColor;
        if (repaint && Bundle != null)
        {
            var texture = Rasterize(Bundle, Cursor.Row, settings, RenderWidth);
            ClearCache();
            _cache.Add(Cursor.Row, texture); _order.Enqueue(Cursor.Row); _texture = texture;
        }
        Settings = settings; Visible = settings.Enabled;
        RefreshDrawing();
    }

    public void SetLayout(double width, double y) => ApplySettings(Settings with { Width = width, Y = y });

    public void SetRenderWidth(int width)
    {
        if (width <= 0 || width > 7680) throw new ArgumentOutOfRangeException(nameof(width));
        if (width == RenderWidth) return;
        if (Bundle != null)
        {
            var texture = Rasterize(Bundle, Cursor.Row, Settings, width);
            ClearCache();
            _cache.Add(Cursor.Row, texture); _order.Enqueue(Cursor.Row); _texture = texture;
        }
        RenderWidth = width;
        RefreshDrawing();
    }

    public void ClearBundle()
    {
        ClearCache(); Bundle = null; Cursor = null; CursorGain = 1;
        RefreshDrawing();
    }

    public void SetTime(double seconds)
    {
        if (Bundle == null) return;
        var cursor = Bundle.CursorAt(seconds);
        if (Cursor == null || cursor.Row != Cursor.Row || _texture == null)
        {
            if (!_cache.TryGetValue(cursor.Row, out var texture))
            {
                texture = Rasterize(Bundle, cursor.Row, Settings, RenderWidth);
                _cache.Add(cursor.Row, texture); _order.Enqueue(cursor.Row);
                if (_cache.Count > 3)
                {
                    int oldest = _order.Dequeue();
                    _cache.Remove(oldest, out var expired); expired.Dispose();
                }
            }
            _texture = texture;
        }
        Cursor = cursor;
        CursorGain = ScorePulse.Gain(seconds - Bundle.Events[cursor.Event].Seconds);
        RefreshDrawing();
    }

    private ImageTexture Rasterize(MuseScoreBundle bundle, int row, ScoreSettings settings, int renderWidth)
    {
        var watch = Stopwatch.StartNew();
        using var image = new Image();
        float scale = (float)(renderWidth * settings.Width / bundle.Pages[bundle.Rows[row].Page].Width);
        // Backing is drawn independently, so notation brightness cannot brighten the paper.
        Error error = image.LoadSvgFromString(bundle.SvgForRow(row, true, settings.MainColor), scale * 2);
        if (error != Error.Ok || image.IsEmpty()) throw new InvalidDataException("乐谱 SVG 渲染失败：" + error);
        // Supersample only the notation, then cache at the output resolution. A 2:1
        // bilinear reduction averages the 2x2 coverage samples without sharpening rings.
        // Extend transparent RGB before and after filtering to keep colored edges clean.
        image.FixAlphaEdges();
        image.Resize(Math.Max(1, (image.GetWidth() + 1) / 2), Math.Max(1, (image.GetHeight() + 1) / 2),
            Image.Interpolation.Bilinear);
        image.FixAlphaEdges();
        var texture = ImageTexture.CreateFromImage(image);
        RasterCount++;
        LastRasterMilliseconds = watch.Elapsed.TotalMilliseconds;
        return texture;
    }

    public override void _Draw()
    {
        if (Bundle != null && _texture != null && !Settings.RemoveBackground) DrawStyleBox(_panel, _bounds);
    }

    private void RefreshDrawing()
    {
        QueueRedraw();
        if (_notation == null) return;
        _notation.Visible = _cursorLine.Visible = Bundle != null && _texture != null;
        _notation.Texture = _texture;
        if (Bundle == null || _texture == null) return;
        var row = Bundle.Rows[Cursor.Row];
        double width = 1920 * WidthFraction, scale = width / Bundle.Pages[row.Page].Width;
        var rect = new Rect2((float)((1920 - width) / 2), (float)(1080 * YFraction),
            (float)width, (float)(row.CropHeight * scale));
        _bounds = rect;
        // A short final system must not pick up marks from the previous full-width system.
        float visibleWidth = (float)Math.Min(width, (row.Right + 350) * scale);
        var source = new Rect2(0, 0, _texture.GetWidth() * visibleWidth / rect.Size.X, _texture.GetHeight());
        _notation.Position = rect.Position;
        _notation.Scale = new Vector2(rect.Size.X / _texture.GetWidth(), rect.Size.Y / _texture.GetHeight());
        _notation.RegionRect = source;
        _notationMaterial.SetShaderParameter("brightness", Settings.Brightness);
        float x = rect.Position.X + (float)(Cursor.X * scale);
        float top = rect.Position.Y + (float)((row.Top - row.CropTop) * scale);
        float bottom = top + (float)(row.Height * scale);
        _cursorLine.Points = new[] { new Vector2(x, top), new Vector2(x, bottom) };
        _cursorLine.DefaultColor = new Color(Settings.CursorColor);
        _cursorMaterial.SetShaderParameter("brightness", Settings.CursorBrightness * CursorGain);
    }

    private void ClearCache()
    {
        _texture = null;
        foreach (var texture in _cache.Values) texture.Dispose();
        _cache.Clear(); _order.Clear();
    }

    public override void _ExitTree()
    {
        ClearCache();
        _panel.Dispose();
        _notationMaterial?.Dispose(); _cursorMaterial?.Dispose();
    }
}
