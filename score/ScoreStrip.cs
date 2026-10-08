using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Trifle.Score;

// Cache three rows and prepare upcoming playback rows on a CPU worker.
public partial class ScoreStrip : Node2D
{
    public MuseScoreBundle Bundle { get; private set; }
    public ScoreCursor Cursor { get; private set; }
    public int RasterCount { get; private set; }
    public int CachedRows => _cache.Count;
    public double LastRasterMilliseconds { get; private set; }
    public int PreloadCount { get; private set; }
    public double LastPreloadMilliseconds { get; private set; }
    public bool Preloading => _preloadTask != null;
    public ScoreSettings Settings { get; private set; } = new();
    public double WidthFraction => Settings.Width;
    public double YFraction => Settings.Y;
    public int RenderWidth { get; private set; } = 1920;
    public double CursorGain { get; private set; } = 1;
    public Vector2I TextureSize => _texture == null ? Vector2I.Zero : new(_texture.GetWidth(), _texture.GetHeight());
    private readonly Dictionary<int, ImageTexture> _cache = new();
    private readonly Queue<int> _order = new();
    private readonly HashSet<int> _failedPreloads = new();
    private CancellationTokenSource _preloadCancellation = new();
    private Task<ScoreRasterizer.Prepared> _preloadTask;
    private int _generation, _pendingGeneration, _pendingRow;
    private int _textureRow = -1;
    private bool _exiting;
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
        _notationMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://score/notation.gdshader") };
        _cursorMaterial = new ShaderMaterial { Shader = shader };
        _notation = new Sprite2D { Name = "Notation", Centered = false, RegionEnabled = true,
            RegionFilterClipEnabled = true, Material = _notationMaterial };
        _cursorLine = new Line2D { Name = "Cursor", Width = 3, Antialiased = true, Material = _cursorMaterial };
        AddChild(_notation); AddChild(_cursorLine);
        RefreshDrawing();
    }

    public override void _Process(double delta)
    {
        CollectPreload();
        StartPreload();
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
        CacheTexture(cursor.Row, texture); _texture = texture; _textureRow = cursor.Row;
        CursorGain = ScorePulse.Gain(-bundle.Events[cursor.Event].Seconds);
        RefreshDrawing();
        StartPreload();
    }

    public void ApplySettings(ScoreSettings settings)
    {
        settings.Validate();
        bool repaint = settings.Width != Settings.Width ||
            !string.Equals(settings.MainColor[6..], Settings.MainColor[6..], StringComparison.OrdinalIgnoreCase);
        if (repaint && Bundle != null)
        {
            var texture = settings.Enabled ? Rasterize(Bundle, Cursor.Row, settings, RenderWidth) : null;
            ClearCache();
            if (texture != null) { CacheTexture(Cursor.Row, texture); _texture = texture; _textureRow = Cursor.Row; }
        }
        if (Settings.Enabled && !settings.Enabled) CancelPreload();
        if (settings.Enabled && Bundle != null && _textureRow != Cursor.Row)
            SelectTexture(Cursor.Row, settings, RenderWidth);
        Settings = settings; Visible = settings.Enabled;
        RefreshDrawing();
        StartPreload();
    }

    public void SetLayout(double width, double y) => ApplySettings(Settings with { Width = width, Y = y });

    public void SetRenderWidth(int width)
    {
        if (width <= 0 || width > 7680) throw new ArgumentOutOfRangeException(nameof(width));
        if (width == RenderWidth) return;
        if (Bundle != null)
        {
            var texture = Settings.Enabled ? Rasterize(Bundle, Cursor.Row, Settings, width) : null;
            ClearCache();
            if (texture != null) { CacheTexture(Cursor.Row, texture); _texture = texture; _textureRow = Cursor.Row; }
        }
        RenderWidth = width;
        RefreshDrawing();
        StartPreload();
    }

    public void ClearBundle()
    {
        ClearCache(); Bundle = null; Cursor = null; CursorGain = 1;
        RefreshDrawing();
    }

    public void SetTime(double seconds)
    {
        if (Bundle == null) return;
        CollectPreload();
        var cursor = Bundle.CursorAt(seconds);
        if (Settings.Enabled && _textureRow != cursor.Row) SelectTexture(cursor.Row, Settings, RenderWidth);
        Cursor = cursor;
        CursorGain = ScorePulse.Gain(seconds - Bundle.Events[cursor.Event].Seconds);
        RefreshDrawing();
        StartPreload();
    }

    private void SelectTexture(int row, ScoreSettings settings, int renderWidth)
    {
        if (!_cache.TryGetValue(row, out var texture))
        {
            // A cold seek remains exact; ordinary playback uses the prepared cache.
            texture = Rasterize(Bundle, row, settings, renderWidth);
            CacheTexture(row, texture);
        }
        _texture = texture; _textureRow = row;
    }

    private void CacheTexture(int row, ImageTexture texture)
    {
        _cache.Add(row, texture); _order.Enqueue(row);
        while (_cache.Count > 3)
        {
            var wanted = new HashSet<int> { row };
            if (Bundle != null && Cursor != null)
            {
                wanted.Add(Cursor.Row);
                foreach (int next in UpcomingRows()) wanted.Add(next);
            }
            // Retain both prepared upcoming rows, even after a cold seek changed
            // insertion order. Otherwise one preload can evict the other and repeat work.
            int candidate = -1;
            foreach (int cached in _order)
                if (!wanted.Contains(cached)) { candidate = cached; break; }
            if (candidate >= 0)
                while (_order.Peek() != candidate) _order.Enqueue(_order.Dequeue());
            int oldest = _order.Dequeue();
            if (oldest == Cursor?.Row || oldest == row) { _order.Enqueue(oldest); continue; }
            _cache.Remove(oldest, out var expired); expired.Dispose();
        }
    }

    private IEnumerable<int> UpcomingRows()
    {
        var seen = new HashSet<int> { Cursor.Row };
        for (int i = Cursor.Event + 1; i < Bundle.Events.Length && seen.Count < 3; i++)
            if (seen.Add(Bundle.Events[i].Row)) yield return Bundle.Events[i].Row;
    }

    private void StartPreload()
    {
        if (_exiting || !Settings.Enabled || Bundle == null || _preloadTask != null) return;
        foreach (int row in UpcomingRows())
        {
            if (_cache.ContainsKey(row) || _failedPreloads.Contains(row)) continue;
            // Capture immutable inputs. The worker neither reads this node nor uploads textures.
            var bundle = Bundle; var settings = Settings; int width = RenderWidth;
            var cancellation = _preloadCancellation.Token;
            _pendingRow = row; _pendingGeneration = _generation;
            _preloadTask = Task.Run(() => ScoreRasterizer.Prepare(bundle, row, settings, width, cancellation), cancellation);
            break;
        }
    }

    private void CollectPreload()
    {
        if (_preloadTask == null || !_preloadTask.IsCompleted) return;
        var task = _preloadTask; _preloadTask = null;
        if (task.IsCanceled) return;
        if (task.IsFaulted)
        {
            var error = task.Exception.GetBaseException();
            if (_pendingGeneration == _generation)
            {
                _failedPreloads.Add(_pendingRow);
                GD.PushWarning("乐谱预加载失败：" + error.Message);
            }
            return;
        }
        if (_pendingGeneration != _generation || Bundle == null || !Settings.Enabled || _cache.ContainsKey(_pendingRow)) return;
        bool wanted = _pendingRow == Cursor.Row;
        foreach (int row in UpcomingRows()) wanted |= row == _pendingRow;
        if (!wanted) return;
        var prepared = task.Result;
        CacheTexture(_pendingRow, Upload(prepared));
        PreloadCount++;
        LastPreloadMilliseconds = prepared.Milliseconds;
    }

    private void CancelPreload()
    {
        _generation++;
        _preloadCancellation.Cancel(); _preloadCancellation.Dispose();
        _preloadCancellation = _exiting ? null : new CancellationTokenSource();
        _failedPreloads.Clear();
    }

    private ImageTexture Rasterize(MuseScoreBundle bundle, int row, ScoreSettings settings, int renderWidth)
    {
        var watch = Stopwatch.StartNew();
        var texture = Upload(ScoreRasterizer.Prepare(bundle, row, settings, renderWidth));
        RasterCount++;
        LastRasterMilliseconds = watch.Elapsed.TotalMilliseconds;
        return texture;
    }

    private static ImageTexture Upload(ScoreRasterizer.Prepared prepared)
    {
        using var image = Image.CreateFromData(prepared.Width, prepared.Height, false, Image.Format.Rgba8, prepared.Pixels);
        return ImageTexture.CreateFromImage(image);
    }

    public override void _Draw()
    {
        if (Bundle != null && _texture != null && !Settings.RemoveBackground) DrawStyleBox(_panel, _bounds);
    }

    private void RefreshDrawing()
    {
        QueueRedraw();
        if (_notation == null) return;
        _notation.Visible = _cursorLine.Visible = Bundle != null && _texture != null && _textureRow == Cursor?.Row;
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
        _notationMaterial.SetShaderParameter("notation_color", new Color(Settings.MainColor));
        float x = rect.Position.X + (float)(Cursor.X * scale);
        float top = rect.Position.Y + (float)((row.Top - row.CropTop) * scale);
        float bottom = top + (float)(row.Height * scale);
        _cursorLine.Points = new[] { new Vector2(x, top), new Vector2(x, bottom) };
        _cursorLine.DefaultColor = new Color(Settings.CursorColor);
        _cursorMaterial.SetShaderParameter("brightness", Settings.CursorBrightness * CursorGain);
    }

    private void ClearCache()
    {
        CancelPreload();
        _texture = null; _textureRow = -1;
        foreach (var texture in _cache.Values) texture.Dispose();
        _cache.Clear(); _order.Clear();
    }

    public override void _ExitTree()
    {
        _exiting = true;
        ClearCache();
        // Finish native CPU image calls before this scene (or the engine) is torn down.
        if (_preloadTask != null)
        {
            try { _preloadTask.GetAwaiter().GetResult(); }
            catch (Exception) { /* Cancellation/errors are irrelevant to a discarded scene. */ }
            _preloadTask = null;
        }
        _panel.Dispose();
        _notationMaterial?.Dispose(); _cursorMaterial?.Dispose();
    }
}
