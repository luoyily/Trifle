using Godot;
using System;
using System.Linq;
using Trifle.Midi;
using Trifle.Visuals;

namespace Trifle.App;

public partial class SettingsPanel : PanelContainer
{
    public event Action<double> LookAheadChanged;
    public event Action<int> ColorModeChanged;
    public event Action<int, Color> ChannelColorChanged;
    public event Action<int, Color> TrackColorChanged;
    public event Action<int, int> PitchRangeChanged;
    public event Action<double, double, double, double> KeyboardBoundsChanged;
    public event Action<bool, Color> KeyboardColorChanged;

    private Visualizer _visualizer;
    private MidiSong _song;
    private SpinBox _lookAhead;
    private OptionButton _colorMode;
    private OptionButton _colorTarget;
    private ColorPickerButton _color;
    private SpinBox _firstPitch;
    private SpinBox _lastPitch;
    private Label _rangeSummary;
    private Button _resetRange;
    private SpinBox _keyboardX, _keyboardY, _keyboardWidth, _keyboardHeight;
    private ColorPickerButton _white, _black;
    private Button _resetLayout;
    private bool _syncing;
    private bool _busy;
    private const string Groups = "Margin/Content/Scroll/Groups/";

    public override void _Ready()
    {
        foreach (string group in new[] { "Notes", "Keyboard" })
        {
            var section = GetNode<VBoxContainer>(Groups + group);
            var header = section.GetNode<Button>("Header");
            SectionHeading.Bind(header, section.GetNode<Control>("Fields"), header.Text);
        }
        const string notes = Groups + "Notes/Fields/Content/";
        _lookAhead = GetNode<SpinBox>(notes + "LookAhead/Value");
        _colorMode = GetNode<OptionButton>(notes + "ColorMode");
        _colorTarget = GetNode<OptionButton>(notes + "ColorTarget");
        _color = GetNode<ColorPickerButton>(notes + "Color/Value");
        const string keyboard = Groups + "Keyboard/Fields/Content/";
        _firstPitch = GetNode<SpinBox>(keyboard + "FirstPitch/Value");
        _lastPitch = GetNode<SpinBox>(keyboard + "LastPitch/Value");
        _rangeSummary = GetNode<Label>(keyboard + "Summary");
        _resetRange = GetNode<Button>(keyboard + "Reset");
        _keyboardX = GetNode<SpinBox>(keyboard + "X/Value");
        _keyboardY = GetNode<SpinBox>(keyboard + "Y/Value");
        _keyboardWidth = GetNode<SpinBox>(keyboard + "Width/Value");
        _keyboardHeight = GetNode<SpinBox>(keyboard + "Height/Value");
        _white = GetNode<ColorPickerButton>(keyboard + "White/Value");
        _black = GetNode<ColorPickerButton>(keyboard + "Black/Value");
        _resetLayout = GetNode<Button>(keyboard + "ResetLayout");
        PopulateColorModeItems();
        AppLocale.LanguageChanged += () =>
        {
            int selected = _colorMode.Selected;
            PopulateColorModeItems();
            _colorMode.Select(selected);
            RebuildColorTargets();
        };
        _lookAhead.ValueChanged += value => { if (!_syncing && !_busy) LookAheadChanged?.Invoke(value); };
        _colorMode.ItemSelected += index => { if (!_busy) ColorModeChanged?.Invoke((int)index); };
        _colorTarget.ItemSelected += _ => RefreshColor();
        _color.ColorChanged += color =>
        {
            if (_busy || _colorTarget.ItemCount == 0) return;
            int id = _colorTarget.GetSelectedId();
            if (_visualizer.GetColorMode() == (int)NoteColorMode.Track) TrackColorChanged?.Invoke(id, color);
            else ChannelColorChanged?.Invoke(id, color);
        };
        _firstPitch.ValueChanged += _ => RequestRange();
        _lastPitch.ValueChanged += _ => RequestRange();
        _resetRange.Pressed += () => PitchRangeChanged?.Invoke(21, 108);
        foreach (var value in new[] { _keyboardX, _keyboardY, _keyboardWidth, _keyboardHeight })
            value.ValueChanged += _ =>
            {
                if (!_syncing && !_busy) KeyboardBoundsChanged?.Invoke(
                    _keyboardX.Value, _keyboardY.Value, _keyboardWidth.Value, _keyboardHeight.Value);
            };
        _white.ColorChanged += color => { if (!_syncing && !_busy) KeyboardColorChanged?.Invoke(false, color); };
        _black.ColorChanged += color => { if (!_syncing && !_busy) KeyboardColorChanged?.Invoke(true, color); };
        _resetLayout.Pressed += () =>
        {
            if (_busy) return;
            var defaults = new KeyboardAppearance();
            KeyboardBoundsChanged?.Invoke(defaults.X * 100, defaults.Y * 100, defaults.Width * 100, defaults.Height * 100);
        };
    }

    public void Bind(Visualizer visualizer) => _visualizer = visualizer;

    private System.Collections.Generic.IEnumerable<Button> FoldButtons(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is Button button && button.ToggleMode &&
                button.ThemeTypeVariation.ToString() is "SectionHeader" or "EffectHeader" or
                    "SubsectionHeader" or "NestedEffectHeader") yield return button;
            foreach (var nested in FoldButtons(child)) yield return nested;
        }
    }

    // Layout-only inset containers do not change saved section identities.
    private string SectionKey(Button button) => GetPathTo(button).ToString()
        .Replace("/EffectsInset/Groups/", "/").Replace("/Fields/Content/", "/Fields/");

    public System.Collections.Generic.Dictionary<string, bool> CaptureExpandedSections() =>
        FoldButtons(this).ToDictionary(SectionKey, button => button.ButtonPressed);

    public void RestoreExpandedSections(System.Collections.Generic.Dictionary<string, bool> expanded)
    {
        foreach (var button in FoldButtons(this))
            button.ButtonPressed = expanded.TryGetValue(SectionKey(button), out bool open) && open;
    }

    public void CollapseSection(string name) => GetNode<Button>(Groups + name + "/Header").ButtonPressed = false;

    public void SetSong(MidiSong song)
    {
        _song = song;
        RefreshValues();
        RebuildColorTargets();
    }

    public void RefreshValues()
    {
        _syncing = true;
        _lookAhead.SetValueNoSignal(_visualizer.LookAheadSeconds);
        // Temporarily widen limits before restoring the current valid pair.
        _firstPitch.MaxValue = 127;
        _lastPitch.MinValue = 0;
        _firstPitch.SetValueNoSignal(_visualizer.FirstPitch);
        _lastPitch.SetValueNoSignal(_visualizer.LastPitch);
        _firstPitch.MaxValue = _visualizer.LastPitch;
        _lastPitch.MinValue = _visualizer.FirstPitch;
        _rangeSummary.Text = string.Format(AppLocale.T("{0} – {1} · {2} 键"), KeyboardLayout.PitchName(_visualizer.FirstPitch), KeyboardLayout.PitchName(_visualizer.LastPitch), _visualizer.LastPitch - _visualizer.FirstPitch + 1);
        var appearance = _visualizer.KeyboardAppearance;
        foreach (var value in new[] { _keyboardX, _keyboardY, _keyboardWidth, _keyboardHeight }) value.MaxValue = 100;
        _keyboardX.SetValueNoSignal(appearance.X * 100);
        _keyboardY.SetValueNoSignal(appearance.Y * 100);
        _keyboardWidth.SetValueNoSignal(appearance.Width * 100);
        _keyboardHeight.SetValueNoSignal(appearance.Height * 100);
        _keyboardX.MaxValue = 100 - appearance.Width * 100;
        _keyboardY.MaxValue = 100 - appearance.Height * 100;
        _keyboardWidth.MaxValue = 100 - appearance.X * 100;
        _keyboardHeight.MaxValue = 100 - appearance.Y * 100;
        _white.Color = new Color(appearance.WhiteColor);
        _black.Color = new Color(appearance.BlackColor);
        _syncing = false;
        ParameterRow.SyncAll(this);
    }

    private void PopulateColorModeItems()
    {
        _colorMode.Clear();
        _colorMode.AddItem(AppLocale.T("按通道（Channel）"));
        _colorMode.AddItem(AppLocale.T("按轨道（Track）"));
    }

    private void RequestRange()
    {
        if (!_syncing && !_busy) PitchRangeChanged?.Invoke((int)_firstPitch.Value, (int)_lastPitch.Value);
    }

    public void RebuildColorTargets()
    {
        _colorMode.Select(_visualizer.GetColorMode());
        _colorTarget.Clear();
        if (_song == null) return;
        if (_visualizer.GetColorMode() == (int)NoteColorMode.Track)
        {
            foreach (var track in _song.Tracks.Where(track => track.NoteCount > 0))
                _colorTarget.AddItem(string.Format(AppLocale.T("轨道 {0} · {1}"), track.Index + 1, track.Name), track.Index);
            if (_colorTarget.ItemCount == 0) _colorTarget.Text = AppLocale.T("无音符轨道");
            else _colorTarget.Select(0);
        }
        else
        {
            var used = _song.Notes.Select(note => note.Channel).Distinct().Order().ToArray();
            for (int channel = 0; channel < 16; channel++)
                _colorTarget.AddItem(string.Format(AppLocale.T("通道 {0}"), channel + 1) + (used.Contains(channel) ? AppLocale.T(" · 使用中") : ""), channel);
            _colorTarget.Select(used.FirstOrDefault());
        }
        _colorTarget.Disabled = _busy || _colorTarget.ItemCount == 0;
        RefreshColor();
    }

    public void RefreshColor()
    {
        _color.Disabled = _busy || _colorTarget.ItemCount == 0;
        if (_colorTarget.ItemCount == 0) return;
        int id = _colorTarget.GetSelectedId();
        _color.Color = _visualizer.GetColorMode() == (int)NoteColorMode.Track
            ? _visualizer.GetTrackColor(id) : _visualizer.GetChannelColor(id);
        _colorTarget.TooltipText = _colorTarget.GetItemText(_colorTarget.Selected);
    }

    public bool HasOpenPopup() => _colorMode.GetPopup().Visible || _colorTarget.GetPopup().Visible ||
        _color.GetPopup().Visible || _white.GetPopup().Visible || _black.GetPopup().Visible;

    public void SetBusy(bool busy)
    {
        _busy = busy;
        _lookAhead.Editable = _firstPitch.Editable = _lastPitch.Editable = !busy;
        _colorMode.Disabled = _resetRange.Disabled = busy;
        _colorTarget.Disabled = busy || _colorTarget.ItemCount == 0;
        _color.Disabled = busy || _colorTarget.ItemCount == 0;
        foreach (var value in new[] { _keyboardX, _keyboardY, _keyboardWidth, _keyboardHeight }) value.Editable = !busy;
        _white.Disabled = _black.Disabled = _resetLayout.Disabled = busy;
        ParameterRow.SyncAll(this);
    }
}
