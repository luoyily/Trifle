using Godot;
using System;
using System.Linq;
using System.IO;
using Trifle.Midi;

namespace Trifle.Visuals;

public partial class Visualizer : Node2D
{
    [Export(PropertyHint.Range, "0,127,1")] public int FirstPitch { get; set; } = 21;
    [Export(PropertyHint.Range, "0,127,1")] public int LastPitch { get; set; } = 108;
    [Export(PropertyHint.Range, "1,12,0.1")] public double LookAheadSeconds { get; set; } = 6;

    private MidiSong _song;
    private double _time;
    private KeyboardLayout _layout;
    private PianoKeyboard _keyboard;
    private NoteRenderer _notes;
    private readonly NoteColors _colors = new();
    private Rect2 _keyboardBounds;
    private KeyboardAppearance _appearance = new();
    private BackgroundSettings _background = new();
    private NoteAppearance _noteAppearance = new();
    private GlowSettings _glow = new();
    private LightEffectsSettings _lights = new();
    private HitEffects _hits;
    private ParticleEffects _particles;
    private ParticleSettings _particleSettings = new();
    private Godot.Environment _environment;
    private TextureRect _backgroundImage;
    private ShaderMaterial _outputMaterial;
    private readonly Godot.Collections.Array<Vector4> _keyLightSources = new();
    private readonly Godot.Collections.Array<Vector4> _keyLightColors = new();
    public string BackgroundWarning { get; private set; } = "";
    public bool HasBackgroundImage => _backgroundImage.Texture != null;
    public KeyboardAppearance KeyboardAppearance => _appearance;
    public BackgroundSettings Background => _background;
    public NoteAppearance NoteAppearance => _noteAppearance;
    public GlowSettings Glow => _glow;
    public LightEffectsSettings Lights => _lights;
    public ParticleSettings Particles => _particleSettings;

    public override void _Ready()
    {
        _layout = new KeyboardLayout(FirstPitch, LastPitch);
        _keyboard = GetNode<PianoKeyboard>("PianoKeyboard");
        _notes = GetNode<NoteRenderer>("Notes");
        _hits = GetNode<HitEffects>("HitEffects");
        _particles = GetNode<ParticleEffects>("ParticleEffects");
        _backgroundImage = GetNode<TextureRect>("BackgroundImage");
        _environment = GetNode<WorldEnvironment>("WorldEnvironment").Environment;
        for (int i = 0; i < 128; i++) { _keyLightSources.Add(Vector4.Zero); _keyLightColors.Add(Vector4.Zero); }
        SetGlow(_glow);
        SetNoteAppearance(_noteAppearance);
        SetKeyboardAppearance(_appearance);
    }

    public void SetSong(MidiSong song)
    {
        _song = song;
        _colors.SetSong(song);
        _notes.SetSong(song);
        _keyboard.SetSong(song);
        _particles.SetSong(song);
        SetTime(0);
    }

    public void SetTime(double seconds)
    {
        if (_song == null) return;
        if (!double.IsFinite(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
        _time = Math.Max(0, seconds);
        _keyboard.UpdateAt(_song, _time, _colors);
        _notes.SetNearLight(_lights, _keyboardBounds.Position.Y, _keyboard.ActiveKeyCount > 0);
        _notes.UpdateAt(_song, _layout, _keyboardBounds, _time, Math.Max(0.1, LookAheadSeconds), _colors);
        _hits.UpdateAt(_layout, _keyboardBounds, _time, _keyboard, _lights);
        _particles.UpdateAt(_layout, _keyboardBounds, _time, _colors);
        UpdateKeyboardAreaLight();
    }

    public void SetOutputMaterial(ShaderMaterial material) => _outputMaterial = material;

    private void UpdateKeyboardAreaLight()
    {
        if (_outputMaterial == null) return;
        float whiteWidth = 0;
        int n = 0;
        foreach (var key in _layout.Keys)
        {
            if (!key.IsBlack) whiteWidth = (float)key.Width * _keyboardBounds.Size.X / 1920;
            float strength = _keyboard.GetLightStrength(key.Pitch);
            if (strength <= 0) continue;
            var color = _keyboard.GetLightColor(key.Pitch);
            float x = (_keyboardBounds.Position.X + (float)(key.Left + key.Width / 2) * _keyboardBounds.Size.X) / 1920;
            _keyLightSources[n] = new Vector4(x, strength, color.A, 0);
            _keyLightColors[n] = new Vector4(color.R, color.G, color.B, color.A);
            n++;
        }
        if (whiteWidth == 0 && _layout.Keys.Length > 0)
            whiteWidth = (float)_layout.Keys[0].Width * _keyboardBounds.Size.X / (1920 * 0.62f);
        _outputMaterial.SetShaderParameter("keyboard_rect", new Vector4(_keyboardBounds.Position.X / 1920,
            _keyboardBounds.Position.Y / 1080, _keyboardBounds.Size.X / 1920, _keyboardBounds.Size.Y / 1080));
        _outputMaterial.SetShaderParameter("key_light_sources", _keyLightSources);
        _outputMaterial.SetShaderParameter("key_light_colors", _keyLightColors);
        _outputMaterial.SetShaderParameter("key_light_count", n);
        _outputMaterial.SetShaderParameter("white_key_width", whiteWidth);
        _outputMaterial.SetShaderParameter("key_light_radius", _lights.KeyLightRadius);
        _outputMaterial.SetShaderParameter("key_light_strength", _lights.KeyLightEnabled ? _lights.KeyLightStrength : 0);
    }

    public int GetColorMode() => (int)_colors.Mode;
    public void SetColorMode(int mode)
    {
        _colors.SetMode((NoteColorMode)mode);
        SetTime(_time);
    }

    public Color GetChannelColor(int channel) => _colors.GetChannelColor(channel);
    public Color GetTrackColor(int trackIndex) => _colors.GetTrackColor(trackIndex);

    public void SetChannelColor(int channel, Color color)
    {
        _colors.SetChannelColor(channel, color);
        SetTime(_time);
    }

    public void SetTrackColor(int trackIndex, Color color)
    {
        _colors.SetTrackColor(trackIndex, color);
        SetTime(_time);
    }

    public void SetLookAhead(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 1 || seconds > 12)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        LookAheadSeconds = seconds;
        SetTime(_time);
    }

    public void SetPitchRange(int firstPitch, int lastPitch)
    {
        var layout = new KeyboardLayout(firstPitch, lastPitch);
        FirstPitch = firstPitch;
        LastPitch = lastPitch;
        _layout = layout;
        _keyboard.SetLayout(_layout, _keyboardBounds);
        SetTime(_time);
    }

    public int GetVisibleNoteCount() => _notes.VisibleNoteCount;
    public int GetActiveKeyCount() => _keyboard.ActiveKeyCount;

    public void SetNoteAppearance(NoteAppearance appearance)
    {
        appearance.Validate();
        _noteAppearance = appearance;
        _notes.SetAppearance(appearance);
    }

    public void SetGlow(GlowSettings settings)
    {
        settings.Validate();
        _glow = settings;
        _environment.GlowEnabled = settings.Enabled;
        _environment.GlowIntensity = (float)settings.Intensity;
    }

    public void SetLightEffects(LightEffectsSettings settings)
    {
        settings.Validate();
        _lights = settings;
        _keyboard.SetLightSettings(settings);
        SetTime(_time);
    }

    public void SetKeyboardAppearance(KeyboardAppearance appearance)
    {
        appearance.Validate();
        _appearance = appearance;
        _keyboardBounds = new Rect2((float)appearance.X * 1920, (float)appearance.Y * 1080,
            (float)appearance.Width * 1920, (float)appearance.Height * 1080);
        _keyboard.SetLayout(_layout, _keyboardBounds);
        _keyboard.SetColors(new Color(appearance.WhiteColor), new Color(appearance.BlackColor));
        SetTime(_time);
    }

    public void SetParticles(ParticleSettings settings)
    {
        settings.Validate(); _particleSettings = settings;
        _particles.SetSettings(settings);
        SetTime(_time);
    }

    public void SetBackground(BackgroundSettings settings, bool allowFallback = false, bool reloadImage = false)
    {
        settings.Validate();
        Texture2D texture = _backgroundImage.Texture;
        string warning = BackgroundWarning;
        // Color and mode changes reuse the loaded GPU texture; an explicit reload can retry a missing image.
        if (reloadImage || settings.ImagePath != _background.ImagePath || (settings.ImagePath.Length > 0 && texture == null))
        {
            texture = null;
            warning = "";
            try
            {
                if (settings.ImagePath.Length > 0)
                {
                    if (!File.Exists(settings.ImagePath)) throw new FileNotFoundException("背景图片不存在。", settings.ImagePath);
                    using var image = new Image();
                    Error error = image.Load(settings.ImagePath);
                    if (error != Error.Ok || image.IsEmpty()) throw new IOException("背景图片读取失败：" + error);
                    texture = ImageTexture.CreateFromImage(image);
                }
            }
            catch (Exception error) when (allowFallback && error is IOException or UnauthorizedAccessException)
            { warning = "背景图片不可用，已使用背景色：" + settings.ImagePath; }
        }
        if (settings.ImagePath.Length == 0) { texture = null; warning = ""; }
        var previous = _backgroundImage.Texture;
        _backgroundImage.Texture = texture;
        _backgroundImage.StretchMode = settings.Fit == BackgroundFit.Contain
            ? TextureRect.StretchModeEnum.KeepAspectCentered : TextureRect.StretchModeEnum.KeepAspectCovered;
        _background = settings;
        BackgroundWarning = warning;
        if (previous != texture) previous?.Dispose();
        QueueRedraw();
    }

    public VisualSettings GetSettings() => new()
    {
        FirstPitch = FirstPitch, LastPitch = LastPitch, LookAheadSeconds = LookAheadSeconds,
        ColorMode = _colors.Mode,
        ChannelColors = Enumerable.Range(0, 16).ToDictionary(index => index, index => _colors.GetChannelColor(index).ToHtml()),
        TrackColors = Enumerable.Range(0, _song?.Tracks.Length ?? 0).ToDictionary(index => index, index => _colors.GetTrackColor(index).ToHtml()),
        Keyboard = _appearance, Background = _background, Note = _noteAppearance, Glow = _glow, Lights = _lights,
        Particles = _particleSettings
    };

    // Presets can come from another song; unmatched track indices are reported to the caller.
    public int ApplySettings(VisualSettings settings, bool ignoreMissingTracks = false)
    {
        settings.Validate(ignoreMissingTracks ? null : _song.Tracks.Length);
        SetBackground(settings.Background, allowFallback: true, reloadImage: true);
        _layout = new KeyboardLayout(settings.FirstPitch, settings.LastPitch);
        FirstPitch = settings.FirstPitch;
        LastPitch = settings.LastPitch;
        LookAheadSeconds = settings.LookAheadSeconds;
        SetNoteAppearance(settings.Note);
        SetGlow(settings.Glow);
        SetLightEffects(settings.Lights);
        SetParticles(settings.Particles);
        SetKeyboardAppearance(settings.Keyboard);
        _colors.ResetChannels();
        _colors.SetSong(_song);
        _colors.SetMode(settings.ColorMode);
        foreach (var (index, color) in settings.ChannelColors) _colors.SetChannelColor(index, Color.FromHtml(color));
        int skipped = 0;
        foreach (var (index, color) in settings.TrackColors)
        {
            if (index >= _song.Tracks.Length) { skipped++; continue; }
            _colors.SetTrackColor(index, Color.FromHtml(color));
        }
        SetTime(_time);
        return skipped;
    }

    public override void _Draw()
    {
        var first = new Color(_background.Color);
        var last = new Color(_background.EndColor);
        if (_background.Gradient == BackgroundGradient.Solid)
            DrawRect(new Rect2(0, 0, 1920, 1080), first);
        else
            DrawPolygon(new[] { Vector2.Zero, new Vector2(1920, 0), new Vector2(1920, 1080), new Vector2(0, 1080) },
                _background.Gradient == BackgroundGradient.Vertical ? new[] { first, first, last, last } : new[] { first, last, last, first });
    }
}
