using Godot;
using System;

namespace Trifle.Visuals;

// A shared, time-varying air field. Curl gives rolling motion; a small gradient
// component lets the stylized plume gather and loosen as it is carried upward.
internal sealed class ParticleFlow
{
    public const float Step = 1f / 60;
    private readonly ParticleSettings _settings;
    private readonly float _speed;

    public ParticleFlow(ParticleSettings settings) { _settings = settings; _speed = (float)settings.Speed; }

    public Vector2 Velocity(Vector2 position, double time, float sourceY, float pace = 0.5f, float scatter = 0, float detailWeight = 1)
    {
        float t = (float)time;
        float height = Math.Max(0, sourceY - position.Y);
        float opening = Smooth(Math.Clamp(height / 60, 0, 1));
        var broad = Noise(position.X / 145, (position.Y + t * _speed * 0.32f) / 145, t * 0.29f);
        var detail = Noise(position.X / 63 + 7.3f, (position.Y + t * _speed * 0.23f) / 63, t * 0.51f + 4.1f);
        var wind = Noise(position.X / 470, 2.7f, t * 0.32f);
        var curl = new Vector2(broad.Z, -broad.Y) * 0.84f + new Vector2(detail.Z, -detail.Y) * (0.30f * detailWeight);
        var gather = new Vector2(detail.Y, detail.Z) * (-0.12f * detailWeight);
        float gust = (float)_settings.FlowStrength;
        float turbulence = (float)_settings.Turbulence;
        var velocity = new Vector2(wind.X * _speed * 0.32f * gust, -_speed * (0.70f + pace * 0.25f));
        velocity += (curl + gather) * (_speed * turbulence * opening);
        // Intrinsic fan spread opens with height, independently of air turbulence.
        velocity.X += scatter * _speed * (float)_settings.LateralSpread * (_settings.Beam ? 0.055f : 0.20f) * (0.25f + opening * 0.75f);
        return velocity;
    }

    public Vector2[] TraceGuide(Vector2 origin, double birth, float life, float pace, float scatter)
    {
        int steps = (int)Math.Ceiling(life / Step);
        var positions = new Vector2[steps + 1];
        positions[0] = origin;
        var velocity = new Vector2(scatter * _speed * 0.05f * (float)_settings.LateralSpread * (_settings.Beam ? 0.3f : 1), -_speed * (0.75f + pace * 0.22f));
        for (int i = 1; i <= steps; i++)
        {
            var target = Velocity(positions[i - 1], birth + (i - 0.5) * Step, origin.Y, pace, scatter, 0.30f);
            velocity = velocity.Lerp(target, 0.0952f);
            positions[i] = positions[i - 1] + velocity * Step;
            if (positions[i].Y > origin.Y) { positions[i].Y = origin.Y; velocity.Y = Math.Min(velocity.Y, 0); }
        }
        return positions;
    }

    // Follow the same air history with a gentler response. Lift is independent
    // of the fade clock, so a lingering ribbon keeps rising instead of braking.
    public Vector2[] TraceCurve(Vector2[] guide, float pace)
    {
        var positions = new Vector2[guide.Length];
        positions[0] = guide[0];
        float baseRise = _speed * (0.70f + pace * 0.25f);
        float rise = baseRise * 1.35f;
        float deformation = (float)_settings.CurveDeformation;
        float verticalWeight = 0.18f + deformation * 0.14f;
        float response = 1 - MathF.Exp(-Step / (0.42f - deformation * 0.06f));
        const float lateralWeight = 0.85f;
        var velocity = new Vector2((guide[1].X - guide[0].X) / Step * lateralWeight, -rise);
        for (int i = 1; i < positions.Length; i++)
        {
            var sharedVelocity = (guide[i] - guide[i - 1]) / Step;
            float verticalGust = (sharedVelocity.Y + baseRise) * verticalWeight;
            // Softly limit opposing gusts; retain breathing motion without a
            // hard speed clamp or the vortex pinning the whole ribbon in place.
            float variation = rise * 0.35f;
            var target = new Vector2(sharedVelocity.X * lateralWeight, -rise + variation * MathF.Tanh(verticalGust / variation));
            velocity = velocity.Lerp(target, response);
            positions[i] = positions[i - 1] + velocity * Step;
        }
        return positions;
    }

    public Vector2[] Trace(Vector2 origin, double birth, float life, float pace, PlumeGuides.Motion guide)
    {
        int steps = (int)Math.Ceiling(life / Step);
        var positions = new Vector2[steps + 1];
        positions[0] = origin;
        var offset = origin - guide.Sample(0);
        var fineVelocity = Vector2.Zero;
        for (int i = 1; i <= steps; i++)
        {
            float t = (float)(birth + (i - 0.5) * Step);
            var previous = positions[i - 1];
            float opening = Smooth(Math.Clamp((origin.Y - previous.Y) / 60, 0, 1));
            var detail = Noise(previous.X / 63 + 7.3f, (previous.Y + t * _speed * 0.23f) / 63, t * 0.51f + 4.1f);
            var fine = (new Vector2(detail.Z, -detail.Y) * 0.30f - new Vector2(detail.Y, detail.Z) * 0.12f)
                * (_speed * (float)_settings.Turbulence * opening * 0.70f);
            fine.Y -= (pace - 0.5f) * _speed * 0.14f;
            fineVelocity = fineVelocity.Lerp(fine, 0.0952f);
            // Small-scale motion relaxes toward the shared parcel; it cannot pull
            // the dot layer away from the larger rolling movement of the plume.
            offset = offset * 0.9892f + fineVelocity * Step;
            positions[i] = guide.Sample(i * Step) + offset;
            positions[i].Y = Math.Min(positions[i].Y, origin.Y);
        }
        return positions;
    }

    public static float SmoothRandom(int note, double phase, uint salt)
    {
        int cell = (int)Math.Floor(phase);
        return Lerp(Random(note, cell, 0, salt), Random(note, cell + 1, 0, salt), Smooth((float)(phase - cell)));
    }

    public static Vector2 Sample(Vector2[] positions, double age, int stride = 1, int point = 0)
    {
        double frame = Math.Max(age / Step, 0);
        int i = Math.Min((int)frame, positions.Length / stride - 2);
        return positions[i * stride + point].Lerp(positions[(i + 1) * stride + point], (float)Math.Clamp(frame - i, 0, 1));
    }

    public static float Random(int note, int packet, int item, uint salt = 0)
    {
        uint value = unchecked((uint)note * 73856093u ^ (uint)packet * 19349663u ^ (uint)item * 83492791u ^ salt);
        return Hash(value);
    }

    private static float Hash(uint value)
    {
        value ^= value >> 16; value *= 0x7feb352d; value ^= value >> 15;
        value *= 0x846ca68b; value ^= value >> 16;
        return (value & 0x00ffffff) / 16777216f;
    }

    private static float Lattice(int x, int y, int z) =>
        Hash(unchecked((uint)x * 374761393u ^ (uint)y * 668265263u ^ (uint)z * 2246822519u)) * 2 - 1;
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    private static float Smooth(float v) => v * v * v * (v * (v * 6 - 15) + 10);
    private static float Derivative(float v) => 30 * v * v * (v - 1) * (v - 1);

    // Value and analytical x/y derivatives of smooth 3D value noise.
    private static Vector3 Noise(float x, float y, float z)
    {
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y), iz = (int)MathF.Floor(z);
        float fx = x - ix, fy = y - iy, fz = z - iz;
        float u = Smooth(fx), v = Smooth(fy), w = Smooth(fz);
        float a = Lattice(ix, iy, iz), b = Lattice(ix + 1, iy, iz);
        float c = Lattice(ix, iy + 1, iz), d = Lattice(ix + 1, iy + 1, iz);
        float e = Lattice(ix, iy, iz + 1), f = Lattice(ix + 1, iy, iz + 1);
        float g = Lattice(ix, iy + 1, iz + 1), h = Lattice(ix + 1, iy + 1, iz + 1);
        float low = Lerp(Lerp(a, b, u), Lerp(c, d, u), v);
        float high = Lerp(Lerp(e, f, u), Lerp(g, h, u), v);
        float dx = Lerp(Lerp(b - a, d - c, v), Lerp(f - e, h - g, v), w) * Derivative(fx);
        float dy = Lerp(Lerp(c - a, d - b, u), Lerp(g - e, h - f, u), w) * Derivative(fy);
        return new Vector3(Lerp(low, high, w), dx, dy);
    }
}
