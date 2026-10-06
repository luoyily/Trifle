using Godot;
using System;
using System.Collections.Generic;

namespace Trifle.Visuals;

// Continuous parcels of air shared by dots and ribbons. Interpolation in both
// emission time and lateral position keeps a ribbon inside the same moving plume.
internal sealed class PlumeGuides
{
    private const double BirthStep = 0.24;
    private readonly ParticleFlow _flow;
    private readonly float _life;
    private readonly Dictionary<(int Note, int Birth, int Lane), Vector2[]> _paths = new();
    private readonly Dictionary<(int Note, int Birth, int Lane), Vector2[]> _curvePaths = new();
    private readonly Dictionary<(int Note, int Birth, int Lane), double> _births = new();
    private readonly List<(int Note, int Birth, int Lane)> _expired = new();

    public PlumeGuides(ParticleFlow flow, double lifetime) { _flow = flow; _life = (float)lifetime; }

    public readonly struct Motion
    {
        private readonly Vector2[] _a, _b, _c, _d;
        private readonly float _birthBlend, _laneBlend;

        public Motion(Vector2[] a, Vector2[] b, Vector2[] c, Vector2[] d, float birthBlend, float laneBlend)
        { _a = a; _b = b; _c = c; _d = d; _birthBlend = birthBlend; _laneBlend = laneBlend; }

        public Vector2 Sample(double age)
        {
            var before = ParticleFlow.Sample(_a, age).Lerp(ParticleFlow.Sample(_b, age), _laneBlend);
            var after = ParticleFlow.Sample(_c, age).Lerp(ParticleFlow.Sample(_d, age), _laneBlend);
            return before.Lerp(after, _birthBlend);
        }
    }

    public Motion GetMotion(int note, double noteStart, double birth, float x, float width, float y, float lane, bool forCurve = false)
    {
        double at = Math.Max(0, (birth - noteStart) / BirthStep);
        int before = (int)Math.Floor(at), side = lane < 0 ? -1 : 1;
        return new Motion(GetPath(note, before, 0, noteStart, x, width, y, forCurve),
            GetPath(note, before, side, noteStart, x, width, y, forCurve),
            GetPath(note, before + 1, 0, noteStart, x, width, y, forCurve),
            GetPath(note, before + 1, side, noteStart, x, width, y, forCurve),
            (float)(at - before), Math.Clamp(Math.Abs(lane), 0, 1));
    }

    private Vector2[] GetPath(int note, int packet, int lane, double noteStart, float x, float width, float y, bool forCurve = false)
    {
        var key = (note, packet, lane);
        var cache = forCurve ? _curvePaths : _paths;
        if (cache.TryGetValue(key, out var path)) return path;
        double birth = noteStart + packet * BirthStep;
        float pace = 0.2f + Density(note, birth - noteStart) * 0.6f;
        if (forCurve)
        {
            // Derive this from the shared parcel, not a separate random field.
            path = _flow.TraceCurve(GetPath(note, packet, lane, noteStart, x, width, y), pace);
            _curvePaths.Add(key, path);
            return path;
        }
        float center = SourceX(note, birth - noteStart, x, width);
        path = _flow.TraceGuide(new Vector2(center + lane * width * 0.10f, y), birth, _life, pace, lane);
        _paths.Add(key, path); _births.Add(key, birth);
        return path;
    }

    public static float SourceX(int note, double emissionAge, float x, float width) =>
        x + (ParticleFlow.SmoothRandom(note, emissionAge * 1.1, 0x02e5be93u) - 0.5f) * width * 0.22f;

    public static float Density(int note, double emissionAge) =>
        ParticleFlow.SmoothRandom(note, emissionAge * 1.4, 0x68bc21ebu);

    public void Clear() { _paths.Clear(); _curvePaths.Clear(); _births.Clear(); }

    public void Prune(double time)
    {
        _expired.Clear();
        foreach (var (key, birth) in _births)
            if (birth < time - _life - BirthStep * 2 || birth > time + BirthStep * 2) _expired.Add(key);
        foreach (var key in _expired) { _paths.Remove(key); _curvePaths.Remove(key); _births.Remove(key); }
    }
}
