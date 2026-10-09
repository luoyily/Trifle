using Godot;

namespace Trifle.App;

public partial class AboutDialog : Window
{
    private const string LicensesPath = "res://licenses/THIRD-PARTY-LICENSES.txt";

    public override void _Ready()
    {
        CloseRequested += Hide;
        var version = ProjectSettings.GetSetting("application/config/version", "").AsString();
        GetNode<Label>("Margin/Content/Version").Text =
            (version.Length > 0 ? "版本 " + version : "开发版本") + " · MIDI 钢琴可视化";
        GetNode<RichTextLabel>("Margin/Content/GitHub").MetaClicked +=
            meta => OS.ShellOpen(meta.AsString());
        using var file = FileAccess.Open(LicensesPath, FileAccess.ModeFlags.Read);
        if (file != null) GetNode<RichTextLabel>("Margin/Content/LicensesScroll/LicensesText").Text = file.GetAsText();
        else GetNode<Button>("Margin/Content/ShowLicenses").Disabled = true;
    }

    public override void _Input(InputEvent input)
    {
        if (Visible && input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            Hide();
            SetInputAsHandled();
        }
    }

    private void ToggleLicenses(bool pressed) =>
        GetNode<ScrollContainer>("Margin/Content/LicensesScroll").Visible = pressed;
}
