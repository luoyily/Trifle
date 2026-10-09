using Godot;

namespace Trifle.App;

// The existing SpinBox remains the source of truth for validation and panel events.
public partial class ParameterRow : HBoxContainer
{
    private SpinBox _number;
    private HSlider _slider;
    private bool _syncing;

    public override void _Ready()
    {
        _number = GetNode<SpinBox>("Value");
        _slider = GetNode<HSlider>("Slider");
        string label = GetNode<Label>("Label").Text;
        _number.TooltipText = string.Format(AppLocale.T("{0}：可直接输入精确数值"), AppLocale.T(label));
        _slider.TooltipText = label;
        _slider.ValueChanged += value =>
        {
            if (!_syncing && _number.Editable) _number.Value = value;
        };
        _number.ValueChanged += _ => Sync();
        Sync();
    }

    public void Sync()
    {
        if (_number == null) return;
        _syncing = true;
        _slider.MinValue = _number.MinValue;
        _slider.MaxValue = _number.MaxValue;
        _slider.Step = _number.Step;
        _slider.SetValueNoSignal(_number.Value);
        _slider.Editable = _number.Editable;
        _syncing = false;
    }

    // Refreshes use SetValueNoSignal; sync explicitly without generating edits.
    public static void SyncAll(Node root)
    {
        if (root is ParameterRow row) { row.Sync(); return; }
        foreach (Node child in root.GetChildren()) SyncAll(child);
    }
}
