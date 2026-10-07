using Godot;

namespace Trifle.Visuals;

// Both images and decoded video enter this layer before the scene's HDR Glow.
public partial class BackgroundLayer : TextureRect
{
    private SubViewport _sourceViewport, _horizontalViewport;
    private TextureRect _source, _horizontal;
    private ShaderMaterial _sourceMaterial, _horizontalMaterial, _verticalMaterial;
    private double _blur;

    public override void _Ready()
    {
        Visible = false;
        _sourceMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://visualizer/background_source.gdshader") };
        _horizontalMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://visualizer/background_horizontal.gdshader") };
        _verticalMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://visualizer/background_vertical.gdshader") };
        _horizontalViewport = new SubViewport
        {
            Name = "HorizontalBlur", TransparentBg = true, UseHdr2D = true, OwnWorld3D = true,
            Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
        };
        _sourceViewport = new SubViewport
        {
            Name = "BackgroundSource", TransparentBg = true, UseHdr2D = true, OwnWorld3D = true,
            Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
        };
        AddChild(_sourceViewport);
        _source = new TextureRect
        {
            ExpandMode = ExpandModeEnum.IgnoreSize, MouseFilter = MouseFilterEnum.Ignore,
            Material = _sourceMaterial
        };
        _sourceViewport.AddChild(_source);
        AddChild(_horizontalViewport);
        _horizontal = new TextureRect
        {
            ExpandMode = ExpandModeEnum.IgnoreSize, MouseFilter = MouseFilterEnum.Ignore,
            Material = _horizontalMaterial, Texture = _sourceViewport.GetTexture()
        };
        _horizontalViewport.AddChild(_horizontal);
        Material = _verticalMaterial;
        GetViewport().SizeChanged += Resize;
        Resize();
    }

    private void Resize()
    {
        Vector2 canvasSize = GetViewport().GetVisibleRect().Size;
        // Keep sigma <= 4 processing pixels so large blur remains smooth and cheap.
        double divisor = System.Math.Max(1, _blur * canvasSize.Y / 1080 / 4);
        var size = new Vector2I(System.Math.Max(1, (int)System.Math.Ceiling(canvasSize.X / divisor)),
            System.Math.Max(1, (int)System.Math.Ceiling(canvasSize.Y / divisor)));
        _sourceViewport.Size = size;
        _source.Size = size;
        _horizontalViewport.Size = size;
        _horizontal.Size = size;
        _sourceMaterial.SetShaderParameter("canvas_size", canvasSize);
        double sigma = _blur * size.Y / 1080;
        _horizontalMaterial.SetShaderParameter("sigma", sigma);
        _verticalMaterial.SetShaderParameter("sigma", sigma);
        Texture = _blur > 0 ? _horizontalViewport.GetTexture() : _sourceViewport.GetTexture();
    }

    public void Configure(Texture2D source, BackgroundSettings settings)
    {
        Visible = source != null && settings.Type is BackgroundType.Image or BackgroundType.Video;
        _sourceViewport.RenderTargetUpdateMode = Visible ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        _horizontalViewport.RenderTargetUpdateMode = Visible && settings.Blur > 0 ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        _source.Texture = source;
        _sourceMaterial.SetShaderParameter("source_image", source);
        _sourceMaterial.SetShaderParameter("source_size", source?.GetSize() ?? Vector2.One);
        _sourceMaterial.SetShaderParameter("cover", settings.Fit == BackgroundFit.Cover);
        if (_blur != settings.Blur || Texture == null)
        {
            _blur = settings.Blur;
            Resize();
        }
        // Match the old image Modulate conversion in HDR 2D.
        float brightness = (float)settings.Brightness;
        _verticalMaterial.SetShaderParameter("brightness", new Color(brightness, brightness, brightness).SrgbToLinear().R);
        _verticalMaterial.SetShaderParameter("opacity", settings.Opacity);
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= Resize;
    }
}
