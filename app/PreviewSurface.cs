using Godot;
using System;

namespace Trifle.App;

// Window presentation is independent of the full-resolution preview/export texture.
public partial class PreviewSurface : Control
{
    private Texture2D _texture;
    private ShaderMaterial _filter;
    private Rect2 _lastRect;
    private Vector2 _lastSourceSize;

    public Texture2D Texture
    {
        get => _texture;
        set { _texture = value; QueueRedraw(); }
    }

    public override void _Ready()
    {
        _filter = new ShaderMaterial { Shader = GD.Load<Shader>("res://app/preview_downsample.gdshader") };
        Material = _filter;
    }

    public Rect2 GetDrawRect()
    {
        if (_texture == null || Size.X <= 0 || Size.Y <= 0) return new Rect2();
        Vector2 source = _texture.GetSize();
        float scale = Math.Min(Size.X / source.X, Size.Y / source.Y);
        var drawn = new Vector2(Math.Max(1, Mathf.Floor(source.X * scale)),
            Math.Max(1, Mathf.Floor(source.Y * scale)));
        // Align the destination edges in window pixels, including fractional container offsets.
        Vector2 origin = (GlobalPosition + (Size - drawn) * 0.5f).Round() - GlobalPosition;
        return new Rect2(origin, drawn);
    }

    public override void _Process(double delta)
    {
        Rect2 rect = GetDrawRect();
        Vector2 sourceSize = _texture?.GetSize() ?? Vector2.Zero;
        if (rect != _lastRect || sourceSize != _lastSourceSize)
        {
            _lastRect = rect;
            _lastSourceSize = sourceSize;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (_texture == null || _filter == null) return;
        Rect2 rect = GetDrawRect();
        if (!rect.HasArea()) return;
        _filter.SetShaderParameter("destination_size", rect.Size);
        DrawTextureRect(_texture, rect, false);
    }
}
