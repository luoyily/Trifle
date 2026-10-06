using Godot;
using System;

namespace Trifle.App;

public partial class Main
{
    private static readonly Vector2I EditorWindowMinimum = new(980, 600);
    private static readonly Vector2I PreviewWindowMinimum = new(640, 360);
    private bool _fittingWindow;

    public async void FitWindowToWidescreen()
    {
        if (_busy || _fittingWindow) return;
        _fittingWindow = true;
        try
        {
            var window = GetWindow();
            var original = window.Size;
            var center = window.Position + original / 2;
            var work = DisplayServer.ScreenGetUsableRect(window.CurrentScreen);
            if (window.Mode != Window.ModeEnum.Windowed)
            {
                window.Mode = Window.ModeEnum.Windowed;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            // Integer multiples avoid independent rounding or minimum-size clamping
            // breaking the aspect ratio. The long edge becomes the landscape width.
            var decorations = window.GetSizeWithDecorations() - window.Size;
            var minimum = _uiVisible ? EditorWindowMinimum : PreviewWindowMinimum;
            int minUnits = Math.Max((minimum.X + 15) / 16, (minimum.Y + 8) / 9);
            int maxUnits = Math.Min((work.Size.X - decorations.X) / 16,
                (work.Size.Y - decorations.Y) / 9);
            if (maxUnits < minUnits)
            {
                SetStatus("当前屏幕空间不足；隐藏界面后按 F10 调整为 16:9。");
                return;
            }
            int units = Math.Clamp((int)Math.Round(Math.Max(original.X, original.Y) / 16.0), minUnits, maxUnits);
            var target = new Vector2I(units * 16, units * 9);
            window.Size = target;
            var position = center - target / 2;
            var borderOffset = window.Position - window.GetPositionWithDecorations();
            window.Position = new Vector2I(
                Math.Clamp(position.X, work.Position.X + borderOffset.X,
                    work.End.X - target.X - decorations.X + borderOffset.X),
                Math.Clamp(position.Y, work.Position.Y + borderOffset.Y,
                    work.End.Y - target.Y - decorations.Y + borderOffset.Y));
            SetStatus($"窗口已调整为 16:9 · {target.X} × {target.Y}");
        }
        finally { _fittingWindow = false; }
    }
}
