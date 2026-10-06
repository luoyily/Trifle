using Godot;
using System;
using System.Collections.Generic;
using Trifle.Midi;

namespace Trifle.Visuals;

// Finite birth-time trajectories are cached at 60 Hz, independent of playback
// frames. Dots and soft ribbons share persistent air parcels, including on seeks.
public partial class ParticleEffects : MultiMeshInstance2D
{
    public const int BurstLimit = 64;
    private const int ParticleLimit = 16384;
    private const int CurveLimit = BurstLimit * 3;
    private const int CurveSegments = 64;
    private const int CurvePoints = 8;
    private const double CurveMaxLifeScale = 1.15;
    private const double EmissionWindow = 0.34;
    private MidiNoteIndex _index;
    private ParticleSettings _settings = new();
    private ParticleFlow _flow;
    private PlumeGuides _guides;
    private ShaderMaterial _material, _curveMaterial;
    private MultiMeshInstance2D _curves;
    private readonly Dictionary<ulong, MotionPath> _paths = new();
    private Image _curveImage;
    private ImageTexture _curveTexture;
    private readonly Vector2[] _knots = new Vector2[CurvePoints];
    private readonly Vector2[] _smoothKnots = new Vector2[CurvePoints];
    private readonly float[] _knotDistance = new float[CurvePoints];
    private readonly float[] _knotLight = new float[CurvePoints];
    private readonly List<ulong> _expired = new();
    private KeyboardLayout _cachedLayout;
    private Rect2 _cachedKeyboard;
    private long _frame;
    public int VisibleCurves { get; private set; }
    public int DescendingParticles { get; private set; }
    public int ActiveBursts { get; private set; }
    public int DroppedBursts { get; private set; }
    public int VisibleParticles { get; private set; }

    private sealed class MotionPath
    {
        public Vector2[] Positions;
        public double Birth;
        public float Life;
        public long Seen;
    }

    public override void _Ready()
    {
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://visualizer/particle.gdshader") };
        Material = _material;
        Multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D, UseColors = true, UseCustomData = true,
            Mesh = new QuadMesh { Size = Vector2.One }, InstanceCount = ParticleLimit, VisibleInstanceCount = 0,
            CustomAabb = new Aabb(new Vector3(-1000, -2000, -1), new Vector3(4000, 4000, 2))
        };
        _curveMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://visualizer/particle_curve.gdshader") };
        _curveImage = Image.CreateEmpty(CurvePoints, CurveLimit, false, Image.Format.Rgbaf);
        _curveTexture = ImageTexture.CreateFromImage(_curveImage);
        _curveMaterial.SetShaderParameter("curve_points", _curveTexture);
        _curves = new MultiMeshInstance2D
        {
            Name = "FlowCurves", Material = _curveMaterial, ShowBehindParent = true,
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform2D, UseColors = true, UseCustomData = true,
                Mesh = MakeCurveMesh(), InstanceCount = CurveLimit, VisibleInstanceCount = 0,
                CustomAabb = Multimesh.CustomAabb
            }
        };
        AddChild(_curves);
        SetSettings(_settings);
    }

    private static Mesh MakeCurveMesh()
    {
        var vertices = new Vector3[(CurveSegments + 1) * 2];
        var uv = new Vector2[vertices.Length];
        var indices = new int[CurveSegments * 6];
        for (int i = 0; i <= CurveSegments; i++)
        {
            float u = i / (float)CurveSegments;
            vertices[i * 2] = new Vector3(-0.5f, u, 0);
            vertices[i * 2 + 1] = new Vector3(0.5f, u, 0);
            uv[i * 2] = new Vector2(0, u); uv[i * 2 + 1] = new Vector2(1, u);
            if (i == CurveSegments) continue;
            int n = i * 6, v = i * 2;
            indices[n] = v; indices[n + 1] = v + 1; indices[n + 2] = v + 2;
            indices[n + 3] = v + 1; indices[n + 4] = v + 3; indices[n + 5] = v + 2;
        }
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.TexUV] = uv;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    public void SetSong(MidiSong song) { _index = new MidiNoteIndex(song.Notes); ClearMotion(); }

    private void ClearMotion() { _paths.Clear(); _guides?.Clear(); }

    public void SetSettings(ParticleSettings settings)
    {
        settings.Validate();
        if (settings.Amount != _settings.Amount || settings.Lifetime != _settings.Lifetime || settings.Speed != _settings.Speed ||
            settings.Turbulence != _settings.Turbulence || settings.Beam != _settings.Beam || settings.FlowStrength != _settings.FlowStrength ||
            settings.DensityVariation != _settings.DensityVariation || settings.LateralSpread != _settings.LateralSpread) ClearMotion();
        _settings = settings; _flow = new ParticleFlow(settings);
        // Ribbons keep moving after their neighboring dots have faded.
        _guides = new PlumeGuides(_flow, settings.Lifetime * CurveMaxLifeScale);
        _material.SetShaderParameter("particle_size", settings.Size);
        _material.SetShaderParameter("emission", settings.Emission);
        _material.SetShaderParameter("glow_strength", settings.Glow);
        _material.SetShaderParameter("glow_radius", settings.GlowRadius);
        _curveMaterial.SetShaderParameter("emission", settings.CurveEmission);
        _curveMaterial.SetShaderParameter("curve_strength", settings.CurveStrength);
        _curveMaterial.SetShaderParameter("curve_width", settings.CurveWidth);
        _curveMaterial.SetShaderParameter("glow_strength", settings.CurveGlow);
    }

    public void UpdateAt(KeyboardLayout layout, Rect2 keyboard, double time, NoteColors colors)
    {
        ActiveBursts = DroppedBursts = VisibleParticles = VisibleCurves = DescendingParticles = 0;
        _frame++;
        if (_cachedLayout != layout || _cachedKeyboard != keyboard)
        { ClearMotion(); _cachedLayout = layout; _cachedKeyboard = keyboard; }
        if (_index == null || (!_settings.Enabled && !_settings.Curves))
        { Multimesh.VisibleInstanceCount = _curves.Multimesh.VisibleInstanceCount = 0; ClearMotion(); return; }
        int end = _index.FirstStartingAfter(time);
        bool showCurves = _settings.Curves && _settings.CurveStrength > 0 && _settings.CurveChance > 0;
        double history = _settings.Lifetime * (showCurves ? CurveMaxLifeScale : 1);
        int begin = _index.FirstStillRelevant(time - history);
        for (int i = end - 1; i >= begin; i--)
        {
            var note = _index.Notes[i];
            if (note.EndSeconds <= time - history || !layout.TryGetKey(note.Pitch, out var key)) continue;
            if (ActiveBursts == BurstLimit) { DroppedBursts++; continue; }
            ActiveBursts++;
            float x = keyboard.Position.X + (float)(key.Left + key.Width / 2) * keyboard.Size.X;
            float width = (float)key.Width * keyboard.Size.X;
            Color color = colors.GetColor(note).SrgbToLinear();
            if (showCurves) AddCurves(i, note, time, x, width, keyboard.Position.Y, color);
            if (!_settings.Enabled) continue;
            int first = Math.Max(0, (int)Math.Floor((time - _settings.Lifetime - note.StartSeconds) / EmissionWindow));
            int last = (int)Math.Floor((Math.Min(time, note.EndSeconds) - note.StartSeconds) / EmissionWindow);
            for (int packet = last; packet >= first; packet--)
            {
                double start = note.StartSeconds + packet * EmissionWindow;
                float puff = PlumeGuides.Density(i, (packet + 0.5) * EmissionWindow);
                float contrast = (float)_settings.DensityVariation;
                float density = 1 + contrast * 0.45f * (puff * 2 - 1);
                int count = Math.Max(1, (int)Math.Round(_settings.Amount * EmissionWindow * density));
                for (int particle = count - 1; particle >= 0; particle--)
                {
                    float random = ParticleFlow.Random(i, packet, particle, 0x9e3779b9u);
                    // Cover the full window with stratified births. Density can
                    // breathe, but the emitter never switches off between groups.
                    double birth = start + (particle + 0.2 + random * 0.6) * EmissionWindow / count;
                    if (birth > time || birth >= note.EndSeconds) continue;
                    AddParticle(i, packet, particle, note.StartSeconds, birth, time, x, width, keyboard.Position.Y, color, puff);
                }
            }
            if (time - note.StartSeconds < _settings.Lifetime)
                for (int accent = 0; accent < 6; accent++)
                    AddParticle(i, 0, 200 + accent, note.StartSeconds, note.StartSeconds, time, x, width, keyboard.Position.Y, color, 0.5f);
        }
        Multimesh.VisibleInstanceCount = VisibleParticles;
        _curves.Multimesh.VisibleInstanceCount = VisibleCurves;
        if (VisibleCurves > 0) _curveTexture.Update(_curveImage);
        Prune(_paths);
        _guides.Prune(time);
    }

    private static ulong Key(int note, int packet, int particle = 0) => ((ulong)(uint)note << 32) | (uint)(packet * 256 + particle);

    private void AddParticle(int noteIndex, int packet, int particle, double noteStart, double birth, double time,
        float x, float width, float y, Color color, float puff)
    {
        if (VisibleParticles >= ParticleLimit) return;
        float random = ParticleFlow.Random(noteIndex, packet, particle);
        float life = (float)_settings.Lifetime * (0.72f + random * 0.28f);
        double age = time - birth;
        if (age < 0 || age >= life) return;
        ulong key = Key(noteIndex, packet, particle);
        if (!_paths.TryGetValue(key, out var path))
        {
            float spread = _settings.Beam ? 0.10f : 0.24f;
            float spawn = (random - 0.5f) * width * spread;
            float pace = Math.Clamp(puff * 0.6f + ParticleFlow.Random(noteIndex, packet, particle, 0xa511e9b3u) * 0.4f, 0, 1);
            float scatter = ParticleFlow.Random(noteIndex, packet, particle, 0x63d83595u) * 2 - 1;
            var guide = _guides.GetMotion(noteIndex, noteStart, birth, x, width, y - 1, scatter);
            float center = PlumeGuides.SourceX(noteIndex, birth - noteStart, x, width);
            path = new MotionPath { Birth = birth, Life = life,
                Positions = _flow.Trace(new Vector2(center + spawn, y - 1), birth, life, pace, guide) };
            _paths.Add(key, path);
        }
        path.Seen = _frame;
        var position = ParticleFlow.Sample(path.Positions, age);
        if (ParticleFlow.Sample(path.Positions, age + ParticleFlow.Step).Y > position.Y + 0.05f) DescendingParticles++;
        int n = VisibleParticles++;
        Multimesh.SetInstanceTransform2D(n, new Transform2D(0, position));
        Multimesh.SetInstanceColor(n, color);
        Multimesh.SetInstanceCustomData(n, new Color((float)(age / life), random, 0, 0));
    }

    private void AddCurves(int noteIndex, MidiNote note, double time, float x, float width, float y, Color color)
    {
        double interval = Math.Max(0.18, _settings.Lifetime * 0.85);
        int first = Math.Max(0, (int)Math.Floor((time - _settings.Lifetime * CurveMaxLifeScale - note.StartSeconds) / interval));
        int last = (int)Math.Floor((Math.Min(time, note.EndSeconds) - note.StartSeconds) / interval);
        int count = 0;
        for (int slot = last; slot >= first && count < 3 && VisibleCurves < CurveLimit; slot--)
        {
            // Every onset gets the same independent roll, even a staccato note.
            // Later opportunities require the note to remain held, but a ribbon
            // already triggered has its own length and lifetime after release.
            if (ParticleFlow.Random(noteIndex, slot, 0, 0xc2b2ae35u) >= _settings.CurveChance) continue;
            float random = ParticleFlow.Random(noteIndex, slot, 0, 0x7f4a7c15u);
            double birth = note.StartSeconds + slot * interval + (slot == 0 ? 0 : random * interval * 0.12);
            float life = (float)_settings.Lifetime * (1.00f + random * 0.15f);
            double age = time - birth;
            if ((slot > 0 && birth >= note.EndSeconds) || age < 0 || age >= life) continue;
            float minLength = Math.Max(48, (float)_settings.CurveWidth * 12);
            float span = Math.Max((float)((0.10 + _settings.CurveLength * 0.32) * _settings.Lifetime) * (0.90f + random * 0.20f),
                minLength / ((float)_settings.Speed * 0.78f));
            span = Math.Min(span, (float)_settings.Lifetime * 0.50f);
            double ready = span + _settings.Lifetime * 0.10;
            // Preform an air segment independently of how long the key was held.
            if (age < ready) continue;
            float side = ParticleFlow.Random(noteIndex, slot, 0, 0x94d049bbu) < 0.5f ? -1 : 1;
            float lane = side * (0.84f + random * 0.12f);
            float deformation = (float)_settings.CurveDeformation;
            float slip = ParticleFlow.SmoothRandom(noteIndex, (time - note.StartSeconds) * 0.24 + slot * 1.7, 0x43b0d7e5u) * 2 - 1;
            float slant = (ParticleFlow.Random(noteIndex, slot, 0, 0x12a9c7b3u) * 2 - 1) * 0.05f;
            float ratio = (float)(age / life);
            float angle = slant + slip * (0.035f + deformation * 0.055f);
            var center = Vector2.Zero;
            for (int point = 0; point < CurvePoints; point++)
            {
                float u = point / (float)(CurvePoints - 1);
                double parcelBirth = birth + span * (1 - u);
                // Sample real elapsed time. The filtered shared path supplies
                // gentle lift; the separate lifetime ratio only controls fading.
                double parcelAge = time - parcelBirth;
                float localLane = Math.Clamp(lane + side * slip * deformation * 0.015f * (u - 0.5f), -0.99f, 0.99f);
                _knots[point] = _guides.GetMotion(noteIndex, note.StartSeconds, parcelBirth, x, width, y - 1, localLane, forCurve: true).Sample(parcelAge);
                center += _knots[point] / CurvePoints;
                float density = PlumeGuides.Density(noteIndex, parcelBirth - note.StartSeconds);
                _knotLight[point] = 0.82f + (density - 0.5f) * (float)_settings.DensityVariation * 0.30f;
            }
            var axis = _knots[^1] - _knots[0];
            var normal = axis.LengthSquared() > 0.01f ? new Vector2(-axis.Y, axis.X).Normalized() : Vector2.Right;
            float clearance = Math.Max(3, width * 0.16f) + Math.Abs(slip) * width * 0.10f * deformation;
            // Keep a short-note ribbon beside that note's released particle cloud,
            // without pretending the note kept emitting dots during preformation.
            double cloudBirth = birth + Math.Min(span, Math.Max(0, note.EndSeconds - birth)) * 0.5;
            double cloudAge = time - cloudBirth;
            var anchor = _guides.GetMotion(noteIndex, note.StartSeconds, cloudBirth, x, width, y - 1, lane, forCurve: true).Sample(cloudAge);
            for (int point = 0; point < CurvePoints; point++)
            {
                var position = _knots[point];
                if (point > 0 && point < CurvePoints - 1)
                    position = position * 0.64f + (_knots[point - 1] + _knots[point + 1]) * 0.18f;
                _smoothKnots[point] = anchor + (position - center).Rotated(angle) + normal * side * clearance;
                _knotDistance[point] = point == 0 ? 0 : _knotDistance[point - 1] + _smoothKnots[point].DistanceTo(_smoothKnots[point - 1]);
            }
            float length = _knotDistance[^1];
            float lengthFade = Ease((length - minLength * 0.65f) / (minLength * 0.35f));
            // Fit the envelope to the shorter visible interval, preserving a
            // smooth appearance and exit instead of cutting off the old fade.
            double visibleLife = life - ready;
            float fade = Ease((float)((age - ready) / (visibleLife * 0.28)))
                * MathF.Pow(Ease((float)((life - age) / (visibleLife * 0.80))), 1.25f) * lengthFade;
            int n = VisibleCurves++;
            // Uniform arc-distance samples make both gradients spread over
            // physical length, even when the air stretches individual parcels.
            float aspect = Math.Min(1, length / ((float)_settings.CurveWidth * 20));
            int segment = 0;
            for (int point = 0; point < CurvePoints; point++)
            {
                float distance = length * point / (CurvePoints - 1);
                while (segment < CurvePoints - 2 && _knotDistance[segment + 1] < distance) segment++;
                float blend = Math.Clamp((distance - _knotDistance[segment]) / Math.Max(0.001f, _knotDistance[segment + 1] - _knotDistance[segment]), 0, 1);
                var position = _smoothKnots[segment].Lerp(_smoothKnots[segment + 1], blend);
                float light = Mathf.Lerp(_knotLight[segment], _knotLight[segment + 1], blend);
                _curveImage.SetPixel(point, n, new Color(position.X, position.Y, light, aspect));
            }
            float peak = 0.38f + ParticleFlow.Random(noteIndex, slot, 0, 0xa511e9b3u) * 0.26f;
            _curves.Multimesh.SetInstanceTransform2D(n, Transform2D.Identity);
            _curves.Multimesh.SetInstanceColor(n, new Color(color.R, color.G, color.B, color.A * fade * (0.70f + random * 0.30f)));
            _curves.Multimesh.SetInstanceCustomData(n, new Color(n, random, ratio, peak));
            count++;
        }
    }

    private static float Ease(float value)
    {
        float t = Math.Clamp(value, 0, 1);
        return t * t * t * (t * (t * 6 - 15) + 10);
    }

    private void Prune(Dictionary<ulong, MotionPath> paths)
    {
        _expired.Clear();
        foreach (var (key, path) in paths) if (path.Seen != _frame) _expired.Add(key);
        foreach (var key in _expired) paths.Remove(key);
    }

    public override void _ExitTree()
    {
        _curveImage?.Dispose();
        _curveTexture?.Dispose();
    }

}
