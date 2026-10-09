using Godot;
using System.Linq;
using Trifle.Midi;

namespace Trifle.App;

public partial class SongInfoDialog : Window
{
    public override void _Ready()
    {
        AppLocale.BindTitle(this, "曲目信息");
        CloseRequested += Hide;
    }

    public override void _Input(InputEvent input)
    {
        if (Visible && input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            Hide();
            SetInputAsHandled();
        }
    }

    public void SetSong(MidiSong song)
    {
        GetNode<Label>("Margin/Content/SongName").Text = System.IO.Path.GetFileName(song.SourcePath);
        GetNode<Label>("Margin/Content/SongName").TooltipText = song.SourcePath;
        GetNode<Label>("Margin/Content/Summary").Text =
            string.Format(AppLocale.T("时长 {0}  ·  {1} 个音符"), TimeText.Format(song.DurationSeconds), song.Notes.Length);
        var grid = GetNode<GridContainer>("Margin/Content/Scroll/Tracks");
        foreach (Node child in grid.GetChildren()) { grid.RemoveChild(child); child.QueueFree(); }
        void Cell(string text, bool header = false) => grid.AddChild(new Label
        { Text = text, ThemeTypeVariation = header ? "MutedLabel" : "Label" });
        Cell(AppLocale.T("轨道（Track）"), true); Cell(AppLocale.T("音符数"), true); Cell(AppLocale.T("通道（Channel）"), true);
        foreach (var track in song.Tracks)
        {
            Cell($"{track.Index + 1} · {track.Name}");
            Cell(track.NoteCount.ToString());
            Cell(track.Channels.Length == 0 ? "—" : string.Join(", ", track.Channels.Select(channel => channel + 1)));
        }
        GetNode<Label>("Margin/Content/Details").Text =
            string.Format(AppLocale.T("MIDI Format {0}  ·  {1} 个轨道  ·  {2} 个 Tempo 段"), song.Format, song.Tracks.Length, song.TempoChanges.Length);
        GetNode<Label>("Margin/Content/Source").Text = song.SourcePath;
    }
}
