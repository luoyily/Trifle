using Godot;
using System;
using Trifle.Score;

namespace Trifle.App;

public partial class ScoreSyncPanel : VBoxContainer
{
    public event Action<bool> MidiSourceChanged;
    public event Action<int> MeasureOffsetChanged;
    private OptionButton _source;
    private SpinBox _offset;
    private Label _status;
    private bool _syncing, _busy, _available, _fromScore = true, _hasExternal;

    public override void _Ready()
    {
        SectionHeading.Bind(GetNode<Button>("Header"), GetNode<Control>("Fields"), "乐谱同步");
        _source = GetNode<OptionButton>("Fields/Content/Source/Value");
        _offset = GetNode<SpinBox>("Fields/Content/Offset/Value");
        _status = GetNode<Label>("Fields/Content/Status");
        _source.AddItem("乐谱内 MIDI"); _source.AddItem("外部 MIDI");
        _source.ItemSelected += index => { if (!_syncing && !_busy) MidiSourceChanged?.Invoke(index == 0); };
        _offset.ValueChanged += value => { if (!_syncing && !_busy) MeasureOffsetChanged?.Invoke((int)value); };
        Refresh(false, true, false, new ScoreSyncSettings());
    }

    public void Refresh(bool available, bool fromScore, bool hasExternal, ScoreSyncSettings settings)
    {
        _syncing = true;
        _available = available; _fromScore = fromScore; _hasExternal = hasExternal;
        _source.Select(fromScore ? 0 : 1);
        _source.SetItemDisabled(1, !hasExternal);
        _offset.SetValueNoSignal(settings.MeasureOffset);
        _status.Text = !available ? "载入乐谱后可选择同步来源。" : fromScore
            ? "使用乐谱原始发声时间。选择 MIDI 文件可改用外部 MIDI。"
            : "按外部 MIDI 的拍号与速度表逐小节对齐。";
        _syncing = false; SetBusy(_busy);
    }

    public bool HasOpenPopup() => _source.GetPopup().Visible;
    public void SetBusy(bool busy)
    {
        _busy = busy;
        _source.Disabled = busy || !_available;
        _offset.Editable = !busy && _available && !_fromScore && _hasExternal;
        ParameterRow.SyncAll(this);
    }
}
