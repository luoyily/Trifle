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
    public ScoreTimeline Timeline { get; private set; }
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
    public double RevealX => _revealX;
    public bool CursorVisible => _cursorLine != null && _cursorLine.Visible;
    public Vector2I TextureSize
    {
        get
        {
            var texture = _texture?.Notation ?? _texture?.Combined ?? _texture?.Grid;
            return texture == null ? Vector2I.Zero : new(texture.GetWidth(), texture.GetHeight());
        }
    }
    private readonly Dictionary<int, RowTexture> _cache = new();
    private readonly Dictionary<int, int> _revealed = new();
    private readonly Queue<int> _order = new();
    private readonly HashSet<int> _failedPreloads = new();
    private CancellationTokenSource _preloadCancellation = new();
    private Task<ScoreRasterizer.PreparedSet> _preloadTask;
    private int _generation, _pendingGeneration, _pendingRow;
    private int _textureRow = -1;
    private bool _exiting;
    private double _time, _revealX;
    private RowTexture _texture;
    private Sprite2D _grid, _notation;
    private Line2D _cursorLine;
    private ShaderMaterial _notationMaterial, _cursorMaterial;
    private Rect2 _bounds;
    private readonly StyleBoxFlat _panel = new()
    {
        BgColor = Colors.White, CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
        CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8
    };

    private sealed class RowTexture
    {
        public readonly ImageTexture Combined, Grid, Notation;
        public RowTexture(ImageTexture combined, ImageTexture grid, ImageTexture notation)
        { Combined = combined; Grid = grid; Notation = notation; }
        public void Dispose() { Combined?.Dispose(); Grid?.Dispose(); Notation?.Dispose(); }
    }

    public override void _Ready()
    {
        var shader = GD.Load<Shader>("res://score/brightness.gdshader");
        _notationMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://score/notation.gdshader") };
        _cursorMaterial = new ShaderMaterial { Shader = shader };
        _grid = new Sprite2D { Name = "Grid", Centered = false, RegionEnabled = true,
            RegionFilterClipEnabled = true, Material = _notationMaterial };
        _notation = new Sprite2D { Name = "Notation", Centered = false, RegionEnabled = true,
            RegionFilterClipEnabled = true, Material = _notationMaterial };
        _cursorLine = new Line2D { Name = "Cursor", Width = 3, Antialiased = true, Material = _cursorMaterial };
        AddChild(_grid); AddChild(_notation); AddChild(_cursorLine);
        RefreshDrawing();
    }

    public override void _Process(double delta)
    {
        CollectPreload();
        StartPreload();
    }

    public void SetBundle(MuseScoreBundle bundle, ScoreSettings settings = null, ScoreTimeline timeline = null)
    {
        if (bundle == null) throw new ArgumentNullException(nameof(bundle));
        settings ??= Settings;
        settings.Validate();
        timeline ??= bundle.Timeline;
        var cursor = timeline.CursorAt(0);
        // Render before replacing the usable bundle, so a failed import leaves it intact.
        var texture = Rasterize(bundle, cursor.Row, settings, RenderWidth);
        ClearCache();
        Bundle = bundle; Timeline = timeline; _time = 0; Cursor = cursor; Settings = settings; Visible = settings.Enabled;
        _revealed.Clear();
        CacheTexture(cursor.Row, texture); _texture = texture; _textureRow = cursor.Row;
        CursorGain = ScorePulse.Gain(-timeline.Events[cursor.Event].Seconds);
        UpdateReveal();
        RefreshDrawing();
        StartPreload();
    }

    public void ApplySettings(ScoreSettings settings)
    {
        settings.Validate();
        bool effectChanged = settings.DisplayEffect != Settings.DisplayEffect;
        bool repaint = effectChanged || settings.Width != Settings.Width ||
            !string.Equals(settings.MainColor[6..], Settings.MainColor[6..], StringComparison.OrdinalIgnoreCase);
        if (repaint && Bundle != null)
        {
            var texture = settings.Enabled ? Rasterize(Bundle, Cursor.Row, settings, RenderWidth) : null;
            ClearCache();
            if (texture != null) { CacheTexture(Cursor.Row, texture); _texture = texture; _textureRow = Cursor.Row; }
        }
        if (Settings.Enabled && !settings.Enabled) CancelPreload();
        if (settings.Enabled && Bundle != null && Cursor != null && _textureRow != Cursor.Row)
            SelectTexture(Cursor.Row, settings, RenderWidth);
        Settings = settings; Visible = settings.Enabled;
        if (effectChanged)
        {
            _revealed.Clear();
            UpdateReveal();
        }
        RefreshDrawing();
        StartPreload();
    }

    public void SetLayout(double width, double y) => ApplySettings(Settings with { Width = width, Y = y });

    public void SetRenderWidth(int width)
    {
        if (width <= 0 || width > 7680) throw new ArgumentOutOfRangeException(nameof(width));
        if (width == RenderWidth) return;
        if (Bundle != null && Cursor != null)
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
        ClearCache(); Bundle = null; Timeline = null; Cursor = null; CursorGain = 1; _revealed.Clear(); _revealX = 0;
        RefreshDrawing();
    }

    public void SetTime(double seconds)
    {
        if (Bundle == null) return;
        CollectPreload();
        _time = seconds;
        var cursor = Timeline.CursorAt(seconds);
        // A repeat produces a later event at an earlier score position. Start a fresh
        // writing pass for that occurrence while normal forward playback keeps the
        // per-row high-water mark.
        if (Cursor != null && Settings.DisplayEffect == ScoreDisplayEffect.Typewriter &&
            (cursor.Row < Cursor.Row || (cursor.Row == Cursor.Row && cursor.X < Cursor.X - 0.01)))
            _revealed.Clear();
        if (Settings.Enabled && _textureRow != cursor.Row) SelectTexture(cursor.Row, Settings, RenderWidth);
        Cursor = cursor;
        CursorGain = ScorePulse.Gain(seconds - Timeline.Events[cursor.Event].Seconds);
        UpdateReveal();
        RefreshDrawing();
        StartPreload();
    }

    public void SetTimeline(ScoreTimeline timeline)
    {
        if (Bundle == null) return;
        Timeline = timeline ?? Bundle.Timeline;
        CancelPreload();
        _revealed.Clear();
        SetTime(_time);
    }

    // A manual seek, stop, or export starts a fresh writing pass. Normal playback
    // keeps the per-row high-water mark; SetTime clears it when a repeat jumps back.
    public void ResetReveal()
    {
        _revealed.Clear();
        if (Bundle != null && Cursor != null && Settings.DisplayEffect == ScoreDisplayEffect.Typewriter)
            _revealX = Bundle.RevealForRow(Cursor.Row).EmptyX;
        RefreshDrawing();
    }

    private void UpdateReveal()
    {
        _revealX = 0;
        if (Bundle == null || Timeline == null || Cursor == null ||
            Settings.DisplayEffect != ScoreDisplayEffect.Typewriter) return;
        var reveal = Bundle.RevealForRow(Cursor.Row);
        if (reveal.Positions.Length == 0 || _time < Timeline.Events[0].Seconds)
        {
            _revealX = reveal.EmptyX;
            return;
        }
        int index = Bundle.RevealIndex(Cursor.Row, Timeline.Events[Cursor.Event].X);
        if (index < 0)
        {
            _revealX = reveal.EmptyX;
            return;
        }
        if (!_revealed.TryGetValue(Cursor.Row, out int previous)) previous = index;
        if (index > previous) previous = index;
        _revealed[Cursor.Row] = previous;
        _revealX = Bundle.RevealX(Cursor.Row, previous);
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

    private void CacheTexture(int row, RowTexture texture)
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
        for (int i = Cursor.Event + 1; i < Timeline.Events.Length && seen.Count < 3; i++)
            if (seen.Add(Timeline.Events[i].Row)) yield return Timeline.Events[i].Row;
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
            _preloadTask = Task.Run(() => ScoreRasterizer.PrepareSet(bundle, row, settings, width, cancellation), cancellation);
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

    private RowTexture Rasterize(MuseScoreBundle bundle, int row, ScoreSettings settings, int renderWidth)
    {
        var watch = Stopwatch.StartNew();
        var texture = Upload(ScoreRasterizer.PrepareSet(bundle, row, settings, renderWidth));
        RasterCount++;
        LastRasterMilliseconds = watch.Elapsed.TotalMilliseconds;
        return texture;
    }

    private static RowTexture Upload(ScoreRasterizer.PreparedSet prepared) => new(
        prepared.Combined == null ? null : Upload(prepared.Combined),
        prepared.Grid == null ? null : Upload(prepared.Grid),
        prepared.Notation == null ? null : Upload(prepared.Notation));

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
        bool typewriter = Settings.DisplayEffect == ScoreDisplayEffect.Typewriter;
        bool ready = Bundle != null && _texture != null && _textureRow == Cursor?.Row;
        var notationTexture = ready ? (typewriter ? _texture.Notation : _texture.Combined) : null;
        var gridTexture = ready && typewriter ? _texture.Grid : null;
        _notation.Texture = notationTexture;
        _grid.Texture = gridTexture;
        _grid.Visible = gridTexture != null;
        _cursorLine.Visible = ready && !typewriter;
        if (!ready || Bundle == null) return;
        var row = Bundle.Rows[Cursor.Row];
        double width = 1920 * WidthFraction, scale = width / Bundle.Pages[row.Page].Width;
        var rect = new Rect2((float)((1920 - width) / 2), (float)(1080 * YFraction),
            (float)width, (float)(row.CropHeight * scale));
        _bounds = rect;
        // A short final system must not pick up marks from the previous full-width system.
        float visibleWidth = (float)Math.Min(width, (row.Right + 350) * scale);
        if (gridTexture != null)
        {
            _grid.Position = rect.Position;
            _grid.Scale = new Vector2(rect.Size.X / gridTexture.GetWidth(), rect.Size.Y / gridTexture.GetHeight());
            _grid.RegionRect = new Rect2(0, 0, gridTexture.GetWidth() * visibleWidth / rect.Size.X, gridTexture.GetHeight());
        }
        if (notationTexture != null)
        {
            _notation.Position = rect.Position;
            _notation.Scale = new Vector2(rect.Size.X / notationTexture.GetWidth(), rect.Size.Y / notationTexture.GetHeight());
            float right = typewriter ? (float)Math.Clamp(_revealX * scale, 0, visibleWidth) : visibleWidth;
            _notation.RegionRect = new Rect2(0, 0, notationTexture.GetWidth() * right / rect.Size.X, notationTexture.GetHeight());
            _notation.Visible = !typewriter || (_revealX >= row.Left && right > 0.5f);
        }
        else _notation.Visible = false;
        _notationMaterial.SetShaderParameter("brightness", Settings.Brightness);
        _notationMaterial.SetShaderParameter("notation_color", new Color(Settings.MainColor));
        if (typewriter) return;
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
