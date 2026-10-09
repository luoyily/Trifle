using Godot;

namespace Trifle.App;

public partial class AboutDialog : Window
{
    public override void _Ready()
    {
        CloseRequested += Hide;
        var version = ProjectSettings.GetSetting("application/config/version", "").AsString();
        GetNode<Label>("Margin/Content/Version").Text =
            (version.Length > 0 ? "版本 " + version : "开发版本") + " · MIDI 钢琴可视化";
        GetNode<RichTextLabel>("Margin/Content/GitHub").MetaClicked +=
            meta => OS.ShellOpen(meta.AsString());
    }

    public override void _Input(InputEvent input)
    {
        if (Visible && input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            Hide();
            SetInputAsHandled();
        }
    }
}
