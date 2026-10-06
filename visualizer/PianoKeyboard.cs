using Godot;
using System;
using Trifle.Midi;

namespace Trifle.Visuals;

public partial class PianoKeyboard : Node2D
{
    private const double ReleaseSeconds = 0.22;
    private readonly MidiNote[] _activeNotes = new MidiNote[128];
    private readonly MidiNote[] _releasedNotes = new MidiNote[128];
    private readonly MidiNote[] _lightNotes = new MidiNote[128];
    private readonly float[] _lightStrength = new float[128];
    private KeyboardLayout _layout = new();
    private Rect2 _bounds;
    private NoteColors _colors;
    private Color _white = new("e8e9e6");
    private Color _black = new("202329");
    private MidiNoteIndex _index;
    private LightEffectsSettings _settings = new();
    private MultiMeshInstance2D _whiteKeys, _blackKeys;
    private ShaderMaterial _material;
    private Node2D _labels;
    private FontFile _labelFont;

    public int ActiveKeyCount { get; private set; }

    public override void _Ready()
    {
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://visualizer/keyboard.gdshader") };
        _whiteKeys = MakeBatch("WhiteKeys");
        _blackKeys = MakeBatch("BlackKeys");
        _labels = new Node2D { Name = "PitchLabels" };
        // Keep the built-in font's metrics, but render transformed glyphs with MSDF.
        // Duplicate it so the editor UI and other text retain their normal hinting.
        _labelFont = (FontFile)ThemeDB.FallbackFont.Duplicate();
        _labelFont.MultichannelSignedDistanceField = true;
        _labelFont.MsdfPixelRange = 8;
        _labelFont.MsdfSize = 64;
        AddChild(_labels);
        _labels.Draw += DrawLabels;
        SetColors(_white, _black);
        SetLightSettings(_settings);
    }

    private MultiMeshInstance2D MakeBatch(string name)
    {
        var batch = new MultiMeshInstance2D
        {
            Name = name, Material = _material,
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
                UseColors = true, UseCustomData = true,
                Mesh = new QuadMesh { Size = Vector2.One }, InstanceCount = 128, VisibleInstanceCount = 0
            }
        };
        AddChild(batch);
        return batch;
    }

    public void SetSong(MidiSong song) => _index = new MidiNoteIndex(song.Notes);
    public void SetLightSettings(LightEffectsSettings settings)
    {
        _settings = settings;
        _material.SetShaderParameter("key_emission", settings.KeyboardEnabled ? settings.KeyboardEmission : 0);
    }

    public void SetColors(Color white, Color black)
    {
        _white = white; _black = black;
        _material.SetShaderParameter("white_color", white);
        _material.SetShaderParameter("black_color", black);
    }

    public void SetLayout(KeyboardLayout layout, Rect2 bounds) { _layout = layout; _bounds = bounds; }
    public float GetLightStrength(int pitch) => _lightStrength[pitch];
    public Color GetLightColor(int pitch) => _lightNotes[pitch] == null ? Colors.Transparent : _colors.GetColor(_lightNotes[pitch]);

    public void UpdateAt(MidiSong song, double time, NoteColors colors)
    {
        _colors = colors;
        Array.Clear(_activeNotes); Array.Clear(_releasedNotes);
        Array.Clear(_lightNotes); Array.Clear(_lightStrength);
        int end = _index.FirstStartingAfter(time);
        for (int i = _index.FirstStillRelevant(time - ReleaseSeconds); i < end; i++)
        {
            var note = song.Notes[i];
            if (!_layout.TryGetKey(note.Pitch, out _)) continue;
            if (note.IsActiveAt(time)) _activeNotes[note.Pitch] = note;
            else if (time - note.EndSeconds < ReleaseSeconds &&
                (_releasedNotes[note.Pitch] == null || note.EndSeconds >= _releasedNotes[note.Pitch].EndSeconds))
                _releasedNotes[note.Pitch] = note;
        }
        ActiveKeyCount = 0;
        foreach (var key in _layout.Keys)
        {
            var note = _activeNotes[key.Pitch] ?? _releasedNotes[key.Pitch];
            if (_activeNotes[key.Pitch] != null) ActiveKeyCount++;
            if (note == null) continue;
            _lightNotes[key.Pitch] = note;
            double age = time - note.StartSeconds;
            double attack = Math.Clamp(age / 0.025, 0, 1);
            attack = attack * attack * (3 - 2 * attack);
            double release = note.IsActiveAt(time) ? 1 : Math.Pow(Math.Clamp(1 - (time - note.EndSeconds) / ReleaseSeconds, 0, 1), 2);
            double pulse = Math.Pow(Math.Clamp(1 - age / _settings.HitDecay, 0, 1), 2);
            _lightStrength[key.Pitch] = (float)(attack * release * (1 + 0.25 * pulse));
        }
        UpdateBatch(_whiteKeys.Multimesh, false);
        UpdateBatch(_blackKeys.Multimesh, true);
        _labels.QueueRedraw();
    }

    internal static float GetDrawnKeyWidth(float keyWidth) =>
        Math.Max(0.5f, keyWidth - Math.Min(1.4f, keyWidth * 0.055f));

    private void UpdateBatch(MultiMesh batch, bool black)
    {
        int n = 0;
        foreach (var key in _layout.Keys)
        {
            if (key.IsBlack != black) continue;
            float left = _bounds.Position.X + (float)key.Left * _bounds.Size.X;
            float keyWidth = (float)key.Width * _bounds.Size.X;
            float height = _bounds.Size.Y * (black ? 0.64f : 1);
            float width = GetDrawnKeyWidth(keyWidth);
            batch.SetInstanceTransform2D(n, new Transform2D(new Vector2(width, 0), new Vector2(0, height),
                new Vector2(left + keyWidth / 2, _bounds.Position.Y + height / 2)));
            batch.SetInstanceColor(n, _lightNotes[key.Pitch] == null ? Colors.White : GetLightColor(key.Pitch).SrgbToLinear());
            batch.SetInstanceCustomData(n, new Color(width, height, black ? 1 : 0, _lightStrength[key.Pitch]));
            n++;
        }
        batch.VisibleInstanceCount = n;
    }

    private void DrawLabels()
    {
        foreach (var key in _layout.Keys)
        {
            float width = (float)key.Width * _bounds.Size.X;
            if (key.IsBlack || key.Pitch % 12 != 0 || width < 20 || _bounds.Size.Y < 30) continue;
            float left = _bounds.Position.X + (float)key.Left * _bounds.Size.X;
            _labels.DrawString(_labelFont, new Vector2(left + 4, _bounds.End.Y - 12),
                KeyboardLayout.PitchName(key.Pitch), fontSize: 18, modulate: new Color("56616b"));
        }
    }
}
