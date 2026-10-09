using Godot;
using System;

namespace Trifle.App;

// Expanding a section and enabling its effect are separate actions.
public partial class EffectSection : VBoxContainer
{
    [Export] public string Title { get; set; } = "效果";
    [Export] public bool HasSwitch { get; set; } = true;
    [Export] public StringName HeadingStyle { get; set; } = "SubsectionHeader";
    public event Action<bool> EnabledChanged;
    private Button _expand;
    private CheckButton _enabled;
    private Control _fields;

    public override void _Ready()
    {
        _expand = GetNode<Button>("Header/Expand");
        _expand.ThemeTypeVariation = HeadingStyle;
        _enabled = GetNode<CheckButton>("Header/Enabled");
        _fields = GetNode<Control>("Fields");
        _enabled.Visible = HasSwitch;
        _enabled.TooltipText = string.Format(AppLocale.T("启用 / 关闭{0}；保留已设置的参数"), AppLocale.T(Title));
        _enabled.Toggled += value => EnabledChanged?.Invoke(value);
        _expand.Toggled += SetExpanded;
        SetExpanded(_expand.ButtonPressed);
    }

    private void SetExpanded(bool expanded)
    {
        SectionHeading.ShowState(_expand, Title, expanded);
        _fields.Visible = expanded;
        _expand.TooltipText = string.Format(AppLocale.T(expanded ? "折叠{0}参数" : "展开{0}参数"), AppLocale.T(Title));
    }

    public void Refresh(bool enabled, bool busy)
    {
        _enabled.SetPressedNoSignal(enabled);
        _enabled.Disabled = busy;
        _fields.Modulate = enabled ? Colors.White : new Color(0.65f, 0.65f, 0.65f);
        SetEditable(_fields, enabled && !busy);
        ParameterRow.SyncAll(_fields);
    }

    private static void SetEditable(Node node, bool editable)
    {
        if (node is SpinBox number) { number.Editable = editable; return; }
        if (node is Slider slider) { slider.Editable = editable; return; }
        if (node is BaseButton button) button.Disabled = !editable;
        foreach (Node child in node.GetChildren()) SetEditable(child, editable);
    }
}
