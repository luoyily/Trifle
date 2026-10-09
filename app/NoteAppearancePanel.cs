using Godot;
using System;
using Trifle.Visuals;

namespace Trifle.App;

public partial class NoteAppearancePanel : VBoxContainer
{
    public event Action<NoteAppearance> AppearanceChanged;
    private NoteAppearance _appearance = new();
    private EffectSection _style, _emissionSection;
    private OptionButton _shape;
    private SpinBox _radius, _opacity, _brightness, _emission;
    private bool _syncing, _busy;

    public override void _Ready()
    {
        _style = GetNode<EffectSection>("Style");
        _emissionSection = GetNode<EffectSection>("Emission");
        _shape = GetNode<OptionButton>("Style/Fields/Content/Shape");
        PopulateShapeItems();
        AppLocale.LanguageChanged += () =>
        {
            int selected = _shape.Selected;
            PopulateShapeItems();
            _shape.Select(selected);
        };
        _radius = GetNode<SpinBox>("Style/Fields/Content/Radius/Value");
        _opacity = GetNode<SpinBox>("Style/Fields/Content/Opacity/Value");
        _brightness = GetNode<SpinBox>("Style/Fields/Content/Brightness/Value");
        _emission = GetNode<SpinBox>("Emission/Fields/Content/Strength/Value");
        _shape.ItemSelected += shape => Request(_appearance with { Shape = (NoteShape)shape });
        _radius.ValueChanged += value => Request(_appearance with { CornerRadius = value });
        _opacity.ValueChanged += value => Request(_appearance with { Opacity = value / 100 });
        _brightness.ValueChanged += value => Request(_appearance with { Brightness = value });
        _emission.ValueChanged += value => Request(_appearance with { Emission = value });
        _emissionSection.EnabledChanged += enabled => Request(_appearance with { EmissionEnabled = enabled });
        GetNode<Button>("Style/Fields/Content/Reset").Pressed += () =>
        {
            var d = new NoteAppearance();
            Request(_appearance with { Shape = d.Shape, CornerRadius = d.CornerRadius, Opacity = d.Opacity, Brightness = d.Brightness });
        };
        GetNode<Button>("Emission/Fields/Content/Reset").Pressed += () => Request(_appearance with { Emission = new NoteAppearance().Emission });
        Refresh(_appearance);
    }

    private void PopulateShapeItems()
    {
        _shape.Clear();
        _shape.AddItem(AppLocale.T("直角矩形"));
        _shape.AddItem(AppLocale.T("圆角矩形"));
    }

    private void Request(NoteAppearance appearance) { if (!_syncing && !_busy) AppearanceChanged?.Invoke(appearance); }

    public void Refresh(NoteAppearance appearance)
    {
        _syncing = true; _appearance = appearance;
        _shape.Select((int)appearance.Shape);
        _radius.SetValueNoSignal(appearance.CornerRadius);
        _opacity.SetValueNoSignal(appearance.Opacity * 100);
        _brightness.SetValueNoSignal(appearance.Brightness);
        _emission.SetValueNoSignal(appearance.Emission);
        _syncing = false;
        SetBusy(_busy);
    }

    public void SetBusy(bool busy)
    {
        _busy = busy;
        _style.Refresh(true, busy);
        _emissionSection.Refresh(_appearance.EmissionEnabled, busy);
        _radius.Editable = !busy && _appearance.Shape == NoteShape.RoundedRectangle;
        ParameterRow.SyncAll(this);
    }

    public bool HasOpenPopup() => _shape.GetPopup().Visible;
}
