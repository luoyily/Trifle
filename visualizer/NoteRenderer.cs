using Godot;
using Trifle.Midi;

namespace Trifle.Visuals;

public partial class NoteRenderer : MultiMeshInstance2D
{
    public int VisibleNoteCount { get; private set; }
    private MidiNoteIndex _index;

    public void SetNearLight(KeyboardLightSettings settings, double keyboardY, bool active)
    {
        var material = (ShaderMaterial)Material;
        material.SetShaderParameter("near_strength", active && settings.NearEnabled ? settings.NearStrength : 0);
        material.SetShaderParameter("contact_y", keyboardY / 1080);
        material.SetShaderParameter("near_distance", settings.NearDistance / 1080);
    }

    public void SetAppearance(NoteAppearance appearance)
    {
        var material = (ShaderMaterial)Material;
        material.SetShaderParameter("corner_radius", appearance.Shape == NoteShape.RoundedRectangle ? appearance.CornerRadius : 0);
        material.SetShaderParameter("opacity", appearance.Opacity);
        material.SetShaderParameter("brightness", appearance.Brightness);
        material.SetShaderParameter("emission", appearance.EmissionEnabled ? appearance.Emission : 0);
    }

    public void SetSong(MidiSong song)
    {
        _index = new MidiNoteIndex(song.Notes);
        Multimesh?.Dispose();
        Multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            UseColors = true,
            UseCustomData = true,
            Mesh = new QuadMesh { Size = Vector2.One },
            InstanceCount = song.Notes.Length,
            VisibleInstanceCount = 0
        };
    }

    public void UpdateAt(MidiSong song, KeyboardLayout layout, Rect2 keyboard, double time, double lookAhead,
        NoteColors colors)
    {
        int count = 0;
        bool hdr = GetViewport().UseHdr2D;
        double speed = keyboard.Position.Y / lookAhead;
        int end = _index.FirstStartingAfter(time + lookAhead);
        for (int i = _index.FirstStillRelevant(time); i < end; i++)
        {
            var note = song.Notes[i];
            if (note.EndSeconds <= time || !layout.TryGetKey(note.Pitch, out var key)) continue;
            double fullTop = keyboard.Position.Y - (note.EndSeconds - time) * speed;
            double fullHeight = note.DurationSeconds * speed;
            float top = (float)System.Math.Max(0, fullTop);
            float bottom = (float)System.Math.Min(keyboard.Position.Y, fullTop + fullHeight);
            if (bottom <= top) continue;

            float width = (float)key.Width * keyboard.Size.X * 0.9f;
            float x = keyboard.Position.X + (float)(key.Left + key.Width * 0.5) * keyboard.Size.X;
            var transform = new Transform2D(new Vector2(width, 0), new Vector2(0, bottom - top),
                new Vector2(x, (top + bottom) * 0.5f));
            Multimesh.SetInstanceTransform2D(count, transform);
            // MultiMesh instance colors are raw shader values, unlike ordinary HDR canvas draw colors.
            Color color = colors.GetColor(note);
            Multimesh.SetInstanceColor(count, hdr ? color.SrgbToLinear() : color);
            // Round the original shape, then clip it. The keyboard cut must not become a rounded endpoint.
            Multimesh.SetInstanceCustomData(count, new Color(width, bottom - top, (float)fullHeight, (float)(top - fullTop)));
            count++;
        }
        Multimesh.VisibleInstanceCount = count;
        VisibleNoteCount = count;
    }
}
