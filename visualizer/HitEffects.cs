using Godot;
using System;

namespace Trifle.Visuals;

public partial class HitEffects : Node2D
{
    private KeyboardLayout _layout;
    private Rect2 _keyboard;
    private PianoKeyboard _keys;
    private KeyboardLightSettings _lights = new();
    private TextureRect _line;
    private ShaderMaterial _lineMaterial;
    private readonly Godot.Collections.Array<Vector4> _sources = new();
    private readonly Godot.Collections.Array<Vector4> _sourceColors = new();
    public int VisibleHits { get; private set; }

    public override void _Ready()
    {
        _lineMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://visualizer/contact_line.gdshader") };
        _line = new TextureRect
        {
            Name = "ContactLine", Material = _lineMaterial,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Texture = MakeWhiteTexture()
        };
        AddChild(_line);
        for (int i = 0; i < 128; i++) { _sources.Add(Vector4.Zero); _sourceColors.Add(Vector4.Zero); }
    }

    private static Texture2D MakeWhiteTexture()
    {
        var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.Fill(Colors.White);
        var texture = ImageTexture.CreateFromImage(image);
        image.Dispose();
        return texture;
    }

    public void UpdateAt(KeyboardLayout layout, Rect2 keyboard, double time, PianoKeyboard keys, KeyboardLightSettings lights, ContactLineSettings settings)
    {
        _layout = layout; _keyboard = keyboard; _keys = keys; _lights = lights;
        _line.Position = new Vector2(keyboard.Position.X, keyboard.Position.Y - 64);
        _line.Size = new Vector2(keyboard.Size.X, 96);
        _line.Visible = (settings.LineEnabled && settings.LineEmission > 0) || (settings.HaloEnabled && settings.HaloEmission > 0);
        float surfaceLeft = keyboard.Size.X, surfaceRight = 0;
        int n = 0;
        foreach (var key in layout.Keys)
        {
            float keyLeft = (float)key.Left * keyboard.Size.X;
            float keyWidth = (float)key.Width * keyboard.Size.X;
            float inset = (keyWidth - PianoKeyboard.GetDrawnKeyWidth(keyWidth)) / 2;
            surfaceLeft = Math.Min(surfaceLeft, keyLeft + inset);
            surfaceRight = Math.Max(surfaceRight, keyLeft + keyWidth - inset);
            float strength = keys.GetLightStrength(key.Pitch);
            if (strength <= 0) continue;
            var color = keys.GetLightColor(key.Pitch).SrgbToLinear();
            _sources[n] = new Vector4((float)(key.Left + key.Width / 2), (float)key.Width, strength, key.Pitch * 0.71f);
            _sourceColors[n] = new Vector4(color.R, color.G, color.B, color.A);
            n++;
        }
        _lineMaterial.SetShaderParameter("sources", _sources);
        _lineMaterial.SetShaderParameter("source_colors", _sourceColors);
        _lineMaterial.SetShaderParameter("source_count", n);
        _lineMaterial.SetShaderParameter("song_time", time);
        _lineMaterial.SetShaderParameter("canvas_width", keyboard.Size.X);
        _lineMaterial.SetShaderParameter("key_surface_bounds", new Vector2(surfaceLeft, surfaceRight));
        _lineMaterial.SetShaderParameter("line_emission", settings.LineEnabled ? settings.LineEmission : 0);
        _lineMaterial.SetShaderParameter("halo_emission", settings.HaloEnabled ? settings.HaloEmission : 0);
        _lineMaterial.SetShaderParameter("line_color", LinearRgba(settings.LineColor));
        _lineMaterial.SetShaderParameter("halo_color", LinearRgba(settings.HaloFollowsLine ? settings.LineColor : settings.HaloColor));
        _lineMaterial.SetShaderParameter("tint_with_notes", settings.TintWithNotes);
        _lineMaterial.SetShaderParameter("halo_width", settings.LineWidth);
        _lineMaterial.SetShaderParameter("wave_amount", settings.LineWave);
        _lineMaterial.SetShaderParameter("contact_boost", settings.LineContactBoost);
        _lineMaterial.SetShaderParameter("core_width", settings.LineCoreWidth);
        QueueRedraw();
    }

    private static Vector4 LinearRgba(string hex)
    {
        var color = new Color(hex).SrgbToLinear();
        // A Color variant is converted again by the HDR canvas renderer.
        return new Vector4(color.R, color.G, color.B, color.A);
    }

    public override void _Draw()
    {
        VisibleHits = 0;
        if (_layout == null || !_lights.HitEnabled || _lights.HitEmission == 0) return;
        foreach (var key in _layout.Keys)
        {
            float strength = _keys.GetLightStrength(key.Pitch);
            if (strength <= 0) continue;
            float width = (float)key.Width * _keyboard.Size.X;
            float x = _keyboard.Position.X + (float)(key.Left + key.Width / 2) * _keyboard.Size.X;
            Color color = _keys.GetLightColor(key.Pitch);
            float energy = (float)_lights.HitEmission * strength * 1.4f;
            color = new Color(color.R * energy, color.G * energy, color.B * energy, color.A * 0.8f);
            float height = key.IsBlack ? 34 : 46;
            float spread = width * (key.IsBlack ? 1.55f : 1.9f);
            DrawRect(new Rect2(x - spread / 2, _keyboard.Position.Y - height * 0.62f, spread, height), color);
            VisibleHits++;
        }
    }
}
