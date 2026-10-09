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
    public event Action<string> VideoRequested;
    public event Action VideoClearRequested;
    public event Action<string> ScoreRequested;
    public event Action ScoreClearRequested;
    public event Action<int> BackgroundTypeChanged;
    public event Action<Color> ColorChanged;
    public event Action<Color> EndColorChanged;
    private OptionButton _type;
    private ColorPickerButton _color, _endColor;
    private FileDialog _audioFiles, _imageFiles, _videoFiles, _scoreFiles;
    private bool _busy, _syncing, _hasMidi, _hasAudio, _hasMedia, _hasScore;

    public override void _Ready()
    {
        _type = GetNode<OptionButton>("Background/Type");
        _color = GetNode<ColorPickerButton>("Background/Colors/Color");
        _endColor = GetNode<ColorPickerButton>("Background/Colors/EndColor");
        _audioFiles = GetNode<FileDialog>("AudioFiles");
        _imageFiles = GetNode<FileDialog>("ImageFiles");
        _videoFiles = GetNode<FileDialog>("VideoFiles");
        _scoreFiles = GetNode<FileDialog>("ScoreFiles");
        _scoreFiles.CurrentDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
        AppLocale.BindTitle(_audioFiles, "选择外部音频");
        AppLocale.BindTitle(_imageFiles, "选择背景图片");
        AppLocale.BindTitle(_videoFiles, "选择背景视频");
        AppLocale.BindTitle(_scoreFiles, "选择 MuseScore 乐谱");
        _audioFiles.CurrentDir = ProjectSettings.GlobalizePath("res://test_assets");
        _imageFiles.CurrentDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyPictures);
        _videoFiles.CurrentDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyVideos);
        PopulateTypeItems();
        AppLocale.LanguageChanged += () =>
        {
            int selected = _type.Selected;
            PopulateTypeItems();
            _type.Select(selected);
        };
        _type.ItemSelected += v => { if (!_syncing && !_busy) BackgroundTypeChanged?.Invoke((int)v); };        _color.ColorChanged += c => { if (!_syncing && !_busy) ColorChanged?.Invoke(c); };
        _endColor.ColorChanged += c => { if (!_syncing && !_busy) EndColorChanged?.Invoke(c); };
        GetNode<Button>("Midi/Load").Pressed += () => { if (!_busy) MidiRequested?.Invoke(); };
        GetNode<Button>("Midi/Clear").Pressed += () => { if (!_busy) MidiClearRequested?.Invoke(); };
        GetNode<Button>("Audio/Load").Pressed += () => { if (!_busy) _audioFiles.PopupCenteredRatio(0.7f); };
        GetNode<Button>("Audio/Clear").Pressed += () => { if (!_busy) AudioClearRequested?.Invoke(); };
        GetNode<Button>("Background/Image/Load").Pressed += () =>
        { if (!_busy) (_type.Selected == (int)BackgroundType.Video ? _videoFiles : _imageFiles).PopupCenteredRatio(0.7f); };
        GetNode<Button>("Background/Image/Clear").Pressed += () =>
        { if (!_busy) { if (_type.Selected == (int)BackgroundType.Video) VideoClearRequested?.Invoke(); else ImageClearRequested?.Invoke(); } };
        _audioFiles.FileSelected += path => { if (!_busy) AudioRequested?.Invoke(path); };
        _imageFiles.FileSelected += path => { if (!_busy) ImageRequested?.Invoke(path); };
        _videoFiles.FileSelected += path => { if (!_busy) VideoRequested?.Invoke(path); };
        GetNode<Button>("Score/Load").Pressed += () => { if (!_busy) _scoreFiles.PopupCenteredRatio(0.7f); };
        GetNode<Button>("Score/Clear").Pressed += () => { if (!_busy) ScoreClearRequested?.Invoke(); };
        _scoreFiles.FileSelected += path => { if (!_busy) ScoreRequested?.Invoke(path); };
        RefreshMidi("", false);
        RefreshAudio(new AudioSettings(), 0, false);
        RefreshBackground(new BackgroundSettings(), false);
        RefreshScore("", false);
    }

    private void PopulateTypeItems()
    {
        _type.Clear();
        foreach (string type in new[] { "纯色", "渐变", "图片", "视频" }) _type.AddItem(AppLocale.T(type));
    }

    private void FileStatus(string node, string path, bool available, string empty, string detail = "")
    {
        var label = GetNode<Label>(node);
        label.Text = path.Length == 0 ? empty : (available ? Path.GetFileName(path) + detail : string.Format(AppLocale.T("不可用 · {0}"), Path.GetFileName(path)));
        label.TooltipText = path.Length == 0 ? empty : path + (available ? "" : "\n" + AppLocale.T("文件不可用，请重新选择"));
    }

    public void RefreshMidi(string path, bool available, bool fromScore = false)
    {
        _hasMidi = path.Length > 0;
        FileStatus("Midi/File", path, available, "未选择文件");
        if (fromScore && path.Length > 0) GetNode<Label>("Midi/File").Text = available ? AppLocale.T("乐谱内 MIDI") : string.Format(AppLocale.T("不可用 · {0}"), AppLocale.T("乐谱内 MIDI"));
        SetBusy(_busy);
    }

    public void RefreshScore(string path, bool available, string title = "")
    {
        _hasScore = path.Length > 0;
        FileStatus("Score/File", path, available, "未选择文件");
        if (available && title.Length > 0)
        {
            GetNode<Label>("Score/File").Text = title + " · " + Path.GetFileName(path);
            GetNode<Label>("Score/File").TooltipText = title + "\n" + path;
        }
        SetBusy(_busy);
    }

    public void RefreshAudio(AudioSettings settings, double duration, bool available)
    {
        _hasAudio = settings.Path.Length > 0;
        FileStatus("Audio/File", settings.Path, available, "未选择文件", " · " + TimeText.Format(duration));
        SetBusy(_busy);
    }

    public void RefreshBackground(BackgroundSettings settings, bool available, bool videoAvailable = false,
        bool videoLoading = false, string videoError = "")
    {
        _syncing = true;
        bool video = settings.Type == BackgroundType.Video;
        bool file = video || settings.Type == BackgroundType.Image;
        string path = video ? settings.Video.Path : settings.ImagePath;
        _hasMedia = path.Length > 0;
        _type.Select((int)settings.Type);
        _color.Color = new Color(settings.Color);
        _endColor.Color = new Color(settings.EndColor);
        GetNode<Control>("Background/Colors").Visible = !file;
        _endColor.Visible = settings.Type == BackgroundType.Gradient;
        GetNode<Control>("Background/Image").Visible = file;
        FileStatus("Background/Image/File", path, video ? videoAvailable || videoLoading : available,
            video ? AppLocale.T("未选择视频") : AppLocale.T("未选择图片"), video && videoLoading ? AppLocale.T(" · 载入中") : "");
        if (video && videoError.Length > 0) GetNode<Label>("Background/Image/File").TooltipText = path + "\n" + videoError;
        GetNode<Button>("Background/Image/Load").TooltipText = video ? AppLocale.T("选择或替换背景视频") : AppLocale.T("选择或替换背景图片");
        GetNode<Button>("Background/Image/Clear").TooltipText = video ? AppLocale.T("移除背景视频") : AppLocale.T("移除背景图片");
        _syncing = false;
        SetBusy(_busy);
    }

    public void SetBusy(bool busy)
    {
        _busy = busy;
        foreach (string path in new[] { "Midi/Load", "Audio/Load", "Background/Image/Load", "Score/Load" }) GetNode<Button>(path).Disabled = busy;
        GetNode<Button>("Midi/Clear").Disabled = busy || !_hasMidi;
        GetNode<Button>("Audio/Clear").Disabled = busy || !_hasAudio;
        GetNode<Button>("Background/Image/Clear").Disabled = busy || !_hasMedia;
        GetNode<Button>("Score/Clear").Disabled = busy || !_hasScore;
        _type.Disabled = _color.Disabled = _endColor.Disabled = busy;
    }

    public bool HasOpenPopup() => _audioFiles.Visible || _imageFiles.Visible || _videoFiles.Visible || _scoreFiles.Visible || _type.GetPopup().Visible ||
        _color.GetPopup().Visible || _endColor.GetPopup().Visible;
}
