using Godot;
using System.Collections.Generic;

namespace Trifle.Visuals;

// A fixed reference pyramid makes the halo's extent independent of render resolution.
public partial class HdrGlow : Node
{
    private readonly List<SubViewport> _passes = new();
    private ShaderMaterial _output;
    private SubViewport _previous;

    public void Initialize(SubViewport source, SubViewport destination, ShaderMaterial output)
    {
        _output = output;
        _previous = source;
        Texture2D texture = AddPass("BrightParts", new Vector2I(960, 540), source.GetTexture(),
            "res://visualizer/glow_extract.gdshader");
        Vector2I[] sizes = { new(480, 270), new(240, 135), new(120, 68) };
        for (int i = 0; i < sizes.Length; i++)
        {
            texture = AddPass($"Horizontal{i}", sizes[i], texture,
                "res://visualizer/glow_blur.gdshader", new Vector2(1f / sizes[i].X, 0));
            texture = AddPass($"Vertical{i}", sizes[i], texture,
                "res://visualizer/glow_blur.gdshader", new Vector2(0, 1f / sizes[i].Y));
            _output.SetShaderParameter($"glow_level_{i}", texture);
        }
        RenderingServer.ViewportSetParentViewport(_previous.GetViewportRid(), destination.GetViewportRid());
    }

    private Texture2D AddPass(string name, Vector2I size, Texture2D source,
        string shaderPath, Vector2? direction = null)
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>(shaderPath) };
        if (direction.HasValue) material.SetShaderParameter("direction", direction.Value);
        var viewport = new SubViewport
        {
            Name = name, Size = size, TransparentBg = true, UseHdr2D = true,
            OwnWorld3D = true, Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
        };
        AddChild(viewport);
        viewport.AddChild(new TextureRect
        {
            Size = size, Texture = source, Material = material,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            MouseFilter = Control.MouseFilterEnum.Ignore
        });
        _passes.Add(viewport);
        // Godot renders viewport children before parents; creation/tree order alone
        // would present the old bloom texture. Declare producer -> consumer order
        // in the rendering server without changing the scene's node paths/worlds.
        RenderingServer.ViewportSetParentViewport(_previous.GetViewportRid(), viewport.GetViewportRid());
        _previous = viewport;
        return viewport.GetTexture();
    }

    public void Configure(GlowSettings settings)
    {
        bool enabled = settings.Enabled && settings.Intensity > 0;
        foreach (var pass in _passes)
            pass.RenderTargetUpdateMode = enabled ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        _output.SetShaderParameter("glow_intensity", enabled ? settings.Intensity : 0.0);
    }
}
