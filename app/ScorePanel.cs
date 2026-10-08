using Godot;
using System;
using Trifle.Score;

namespace Trifle.App;

public partial class ScorePanel : VBoxContainer
{
    public event Action<ScoreSettings> SettingsChanged;
    public event Action<int> RowRequested;
    public event Action<string> MuseScorePathChanged;
    public event Action DetectMuseScoreRequested;
    private CheckButton _enabled, _transparent;
    private SpinBox _width, _y, _brightness, _cursorBrightness;
    private ColorPickerButton _color, _cursorColor;
    private LineEdit _museScorePath;
    private Label _museScoreStatus;
    private Button _browseMuseScore, _detectMuseScore;
    private FileDialog _museScoreFiles;
    private Button _previous, _next;
    private Label _position;
    private ScoreSettings _settings = new();
    private bool _syncing, _busy, _available;
    private int _row, _rows;

    public override void _Ready()
    {
        SectionHeading.Bind(GetNode<Button>("Header"), GetNode<Control>("Fields"), "乐谱");
        const string fields = "Fields/Content/";
        _enabled = GetNode<CheckButton>(fields + "Enabled");
        _width = GetNode<SpinBox>(fields + "Width/Value");
        _y = GetNode<SpinBox>(fields + "Y/Value");
        _transparent = GetNode<CheckButton>(fields + "Transparent");
        _color = GetNode<ColorPickerButton>(fields + "Color/Value");
        _brightness = GetNode<SpinBox>(fields + "Brightness/Value");
        const string cursor = fields + "Cursor/Fields/Content/";
        SectionHeading.Bind(GetNode<Button>(fields + "Cursor/Header"), GetNode<Control>(fields + "Cursor/Fields"), "光标线");
        _cursorColor = GetNode<ColorPickerButton>(cursor + "Color/Value");
        _cursorBrightness = GetNode<SpinBox>(cursor + "Brightness/Value");
        const string import = fields + "Import/Fields/Content/";
        SectionHeading.Bind(GetNode<Button>(fields + "Import/Header"), GetNode<Control>(fields + "Import/Fields"), "导入配置");
        _museScorePath = GetNode<LineEdit>(import + "Program/Path");
        _museScoreStatus = GetNode<Label>(import + "Status");
        _browseMuseScore = GetNode<Button>(import + "Program/Browse");
        _detectMuseScore = GetNode<Button>(import + "Detect");
        _museScoreFiles = GetNode<FileDialog>("MuseScoreFiles");
        _browseMuseScore.Pressed += () =>
        {
            if (_busy) return;
            string path = _museScorePath.Text;
            if (System.IO.File.Exists(path)) _museScoreFiles.CurrentDir = System.IO.Path.GetDirectoryName(path);
            _museScoreFiles.PopupCenteredRatio(0.7f);
        };
        _museScoreFiles.FileSelected += path => { if (!_busy) MuseScorePathChanged?.Invoke(path); };
        _museScorePath.TextSubmitted += path => { if (!_busy && !_syncing) MuseScorePathChanged?.Invoke(path.Trim()); };
        _museScorePath.FocusExited += () => { if (!_busy && !_syncing) MuseScorePathChanged?.Invoke(_museScorePath.Text.Trim()); };
        _detectMuseScore.Pressed += () => { if (!_busy) DetectMuseScoreRequested?.Invoke(); };
        _position = GetNode<Label>(fields + "Position");
        _previous = GetNode<Button>(fields + "Rows/Previous");
        _next = GetNode<Button>(fields + "Rows/Next");
        _enabled.Toggled += _ => RequestSettings();
        _transparent.Toggled += _ => RequestSettings();
        _width.ValueChanged += _ => RequestSettings();
        _y.ValueChanged += _ => RequestSettings();
        _color.ColorChanged += _ => RequestSettings();
        _brightness.ValueChanged += _ => RequestSettings();
        _cursorBrightness.ValueChanged += _ => RequestSettings();
        _cursorColor.ColorChanged += _ => RequestSettings();
        _previous.Pressed += () => RowRequested?.Invoke(-1);
        _next.Pressed += () => RowRequested?.Invoke(1);
        Refresh(_settings, false);
    }

    private void RequestSettings()
    {
        if (_syncing || _busy) return;
        SettingsChanged?.Invoke(_settings with
        {
            Enabled = _enabled.ButtonPressed, Width = _width.Value / 100, Y = _y.Value / 100,
            RemoveBackground = _transparent.ButtonPressed, MainColor = _color.Color.ToHtml(),
            Brightness = _brightness.Value, CursorColor = _cursorColor.Color.ToHtml(), CursorBrightness = _cursorBrightness.Value
        });
    }

    public void Refresh(ScoreSettings settings, bool available)
    {
        _syncing = true; _settings = settings; _available = available;
        _enabled.SetPressedNoSignal(settings.Enabled);
        _transparent.SetPressedNoSignal(settings.RemoveBackground);
        _width.SetValueNoSignal(settings.Width * 100); _y.SetValueNoSignal(settings.Y * 100);
        _color.Color = new Color(settings.MainColor);
        _brightness.SetValueNoSignal(settings.Brightness);
        _cursorColor.Color = new Color(settings.CursorColor);
        _cursorBrightness.SetValueNoSignal(settings.CursorBrightness);
        _syncing = false;
        SetBusy(_busy);
    }

    public void RefreshImporter(string configured, string resolved)
    {
        _syncing = true;
        _museScorePath.Text = configured;
        _museScorePath.PlaceholderText = resolved.Length > 0 && configured.Length == 0 ? resolved : "选择 MuseScore4.exe";
        _museScorePath.TooltipText = configured.Length > 0 ? configured : resolved;
        bool available = System.IO.File.Exists(resolved);
        _museScoreStatus.Text = available ? (configured.Length == 0 ? "已自动检测 MuseScore" : "使用指定的 MuseScore") : "未找到 MuseScore；选择程序后可导入 .mscz。JSON 数据包仍可直接载入。";
        _museScoreStatus.TooltipText = resolved;
        _syncing = false;
    }

    public void RefreshPosition(MuseScoreBundle bundle, ScoreCursor cursor)
    {
        _row = cursor?.Row ?? 0; _rows = bundle?.Rows.Length ?? 0;
        string text = "未载入乐谱";
        if (bundle != null && cursor != null)
        {
            var row = bundle.Rows[cursor.Row];
            text = $"第 {row.Page + 1}/{bundle.Pages.Length} 页 · 第 {cursor.Row + 1}/{bundle.Rows.Length} 行 · 小节 {row.FirstMeasure}–{row.LastMeasure}";
        }
        if (_position.Text != text) _position.Text = text;
        RefreshNavigation();
    }

    private void RefreshNavigation()
    {
        bool disabled = _busy || !_available || !_settings.Enabled;
        _previous.Disabled = disabled || _row == 0;
        _next.Disabled = disabled || _row >= _rows - 1;
    }

    public bool HasOpenPopup() => _color.GetPopup().Visible || _cursorColor.GetPopup().Visible || _museScoreFiles.Visible;
    public void SetBusy(bool busy)
    {
        _busy = busy;
        _enabled.Disabled = busy || !_available;
        bool disabled = busy || !_available || !_settings.Enabled;
        _width.Editable = _y.Editable = _brightness.Editable = _cursorBrightness.Editable = !disabled;
        _transparent.Disabled = _color.Disabled = _cursorColor.Disabled = disabled;
        _museScorePath.Editable = !busy;
        _browseMuseScore.Disabled = _detectMuseScore.Disabled = busy;
        RefreshNavigation();
        ParameterRow.SyncAll(this);
    }
}
