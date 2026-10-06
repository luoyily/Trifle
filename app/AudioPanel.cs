using Godot;
using System;
using System.IO;
using Trifle.Audio;

namespace Trifle.App;

public partial class AudioPanel : VBoxContainer
{
    public event Action<string> LoadRequested;
    public event Action ClearRequested;
    public event Action<bool> EnabledChanged;
    public event Action<double> OffsetChanged;
    private Label _file;
    private CheckButton _enabled;
    private SpinBox _offset;
    private Button _load;
    private Button _clear;
    private FileDialog _files;
    private bool _busy;
    private bool _hasAudio;

    public override void _Ready()
    {
        var header = GetNode<Button>("Header");
        SectionHeading.Bind(header, GetNode<Control>("Fields"), "音频");
        _file = GetNode<Label>("Fields/File");
        _enabled = GetNode<CheckButton>("Fields/Enabled");
        _offset = GetNode<SpinBox>("Fields/Offset/Value");
        _load = GetNode<Button>("Fields/Actions/Load");
        _clear = GetNode<Button>("Fields/Actions/Clear");
        _files = GetNode<FileDialog>("Files");
        _files.CurrentDir = ProjectSettings.GlobalizePath("res://assets/test_materials");
        _load.Pressed += () => { if (!_busy) _files.PopupCenteredRatio(0.7f); };
        _clear.Pressed += () => { if (!_busy) ClearRequested?.Invoke(); };
        _files.FileSelected += path => { if (!_busy) LoadRequested?.Invoke(path); };
        _enabled.Toggled += value => { if (!_busy) EnabledChanged?.Invoke(value); };
        _offset.ValueChanged += value => { if (!_busy) OffsetChanged?.Invoke(value); };
        Refresh(new AudioSettings(), 0);
    }

    public void Refresh(AudioSettings settings, double duration)
    {
        _hasAudio = settings.Path.Length > 0;
        _file.Text = _hasAudio ? $"{Path.GetFileName(settings.Path)} · {TimeText.Format(duration)}" : "未选择音频";
        _file.TooltipText = settings.Path;
        _enabled.SetPressedNoSignal(settings.Enabled);
        _offset.SetValueNoSignal(settings.OffsetSeconds);
        SetBusy(_busy);
    }

    public bool HasOpenDialog() => _files.Visible;

    public void SetBusy(bool busy)
    {
        _busy = busy;
        _load.Disabled = busy;
        _clear.Disabled = _enabled.Disabled = busy || !_hasAudio;
        _offset.Editable = !busy && _hasAudio;
    }
}
