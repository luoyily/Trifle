using Godot;
using System;
using Trifle.Audio;

namespace Trifle.App;

// Asset selection lives in QuickSettingsPanel; this section owns timing.
public partial class AudioPanel : VBoxContainer
{
    public event Action<bool> EnabledChanged;
    public event Action<double> OffsetChanged;
    private CheckButton _enabled;
    private SpinBox _offset;
    private bool _busy, _hasAudio;

    public override void _Ready()
    {
        SectionHeading.Bind(GetNode<Button>("Header"), GetNode<Control>("Fields"), "时间与同步");
        _enabled = GetNode<CheckButton>("Fields/Content/Enabled");
        _offset = GetNode<SpinBox>("Fields/Content/Offset/Value");
        _enabled.Toggled += v => { if (!_busy) EnabledChanged?.Invoke(v); };
        _offset.ValueChanged += v => { if (!_busy) OffsetChanged?.Invoke(v); };
        Refresh(new AudioSettings(), 0);
    }

    public void Refresh(AudioSettings settings, double duration)
    {
        _hasAudio = settings.Path.Length > 0;
        _enabled.SetPressedNoSignal(settings.Enabled);
        _offset.SetValueNoSignal(settings.OffsetSeconds);
        SetBusy(_busy);
    }

    public void SetBusy(bool busy)
    {
        _busy = busy;
        _enabled.Disabled = busy || !_hasAudio;
        _offset.Editable = !busy && _hasAudio;
    }
}
