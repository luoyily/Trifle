using Godot;
using System;
using System.IO;
using Trifle.Audio;
using Trifle.Visuals;

namespace Trifle.App;

public partial class QuickSettingsPanel : VBoxContainer
{
    public event Action MidiRequested;
    public event Action MidiClearRequested;
    public event Action<string> AudioRequested;
    public event Action AudioClearRequested;
    public event Action<string> ImageRequested;
    public event Action ImageClearRequested;
    public event Action<int> BackgroundTypeChanged;
    public event Action<Color> ColorChanged;
    public event Action<Color> EndColorChanged;
    private OptionButton _type;
    private ColorPickerButton _color, _endColor;
    private FileDialog _audioFiles, _imageFiles;
    private bool _busy, _syncing, _hasMidi, _hasAudio, _hasImage;

    public override void _Ready()
    {
        _type = GetNode<OptionButton>("Background/Type");
        _color = GetNode<ColorPickerButton>("Background/Colors/Color");
        _endColor = GetNode<ColorPickerButton>("Background/Colors/EndColor");
        _audioFiles = GetNode<FileDialog>("AudioFiles");
        _imageFiles = GetNode<FileDialog>("ImageFiles");
        _audioFiles.CurrentDir = ProjectSettings.GlobalizePath("res://test_assets");
        _imageFiles.CurrentDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyPictures);
        foreach (string type in new[] { "纯色", "渐变", "图片", "视频（暂不可用）" }) _type.AddItem(type);
        _type.SetItemDisabled(3, true);
        _type.ItemSelected += v => { if (!_syncing && !_busy) BackgroundTypeChanged?.Invoke((int)v); };
        _color.ColorChanged += c => { if (!_syncing && !_busy) ColorChanged?.Invoke(c); };
        _endColor.ColorChanged += c => { if (!_syncing && !_busy) EndColorChanged?.Invoke(c); };
        GetNode<Button>("Midi/Load").Pressed += () => { if (!_busy) MidiRequested?.Invoke(); };
        GetNode<Button>("Midi/Clear").Pressed += () => { if (!_busy) MidiClearRequested?.Invoke(); };
        GetNode<Button>("Audio/Load").Pressed += () => { if (!_busy) _audioFiles.PopupCenteredRatio(0.7f); };
        GetNode<Button>("Audio/Clear").Pressed += () => { if (!_busy) AudioClearRequested?.Invoke(); };
        GetNode<Button>("Background/Image/Load").Pressed += () => { if (!_busy) _imageFiles.PopupCenteredRatio(0.7f); };
        GetNode<Button>("Background/Image/Clear").Pressed += () => { if (!_busy) ImageClearRequested?.Invoke(); };
        _audioFiles.FileSelected += path => { if (!_busy) AudioRequested?.Invoke(path); };
        _imageFiles.FileSelected += path => { if (!_busy) ImageRequested?.Invoke(path); };
        RefreshMidi("", false);
        RefreshAudio(new AudioSettings(), 0, false);
        RefreshBackground(new BackgroundSettings(), false);
    }

    private void FileStatus(string node, string path, bool available, string empty, string detail = "")
    {
        var label = GetNode<Label>(node);
        label.Text = path.Length == 0 ? empty : (available ? Path.GetFileName(path) + detail : "不可用 · " + Path.GetFileName(path));
        label.TooltipText = path.Length == 0 ? empty : path + (available ? "" : "\n文件不可用，请重新选择");
    }

    public void RefreshMidi(string path, bool available)
    {
        _hasMidi = path.Length > 0;
        FileStatus("Midi/File", path, available, "未选择文件");
        SetBusy(_busy);
    }

    public void RefreshAudio(AudioSettings settings, double duration, bool available)
    {
        _hasAudio = settings.Path.Length > 0;
        FileStatus("Audio/File", settings.Path, available, "未选择文件", " · " + TimeText.Format(duration));
        SetBusy(_busy);
    }

    public void RefreshBackground(BackgroundSettings settings, bool available)
    {
        _syncing = true;
        _hasImage = settings.ImagePath.Length > 0;
        _type.Select((int)settings.Type);
        _color.Color = new Color(settings.Color);
        _endColor.Color = new Color(settings.EndColor);
        GetNode<Control>("Background/Colors").Visible = settings.Type != BackgroundType.Image;
        _endColor.Visible = settings.Type == BackgroundType.Gradient;
        GetNode<Control>("Background/Image").Visible = settings.Type == BackgroundType.Image;
        FileStatus("Background/Image/File", settings.ImagePath, available, "未选择图片");
        _syncing = false;
        SetBusy(_busy);
    }

    public void SetBusy(bool busy)
    {
        _busy = busy;
        foreach (string path in new[] { "Midi/Load", "Audio/Load", "Background/Image/Load" }) GetNode<Button>(path).Disabled = busy;
        GetNode<Button>("Midi/Clear").Disabled = busy || !_hasMidi;
        GetNode<Button>("Audio/Clear").Disabled = busy || !_hasAudio;
        GetNode<Button>("Background/Image/Clear").Disabled = busy || !_hasImage;
        _type.Disabled = _color.Disabled = _endColor.Disabled = busy;
    }

    public bool HasOpenPopup() => _audioFiles.Visible || _imageFiles.Visible || _type.GetPopup().Visible ||
        _color.GetPopup().Visible || _endColor.GetPopup().Visible;
}
