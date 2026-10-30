using Godot;
using System;
using Trifle.Midi;

namespace Trifle.App;

public partial class MidiOutPanel : VBoxContainer
{
    public event Action<MidiOutSettings> SettingsChanged;
    public event Action RefreshRequested;

    private CheckButton _enabled;
    private OptionButton _port;
    private Button _refresh;
    private Label _status;
    private MidiOutPortInfo[] _ports = Array.Empty<MidiOutPortInfo>();
    private MidiOutSettings _settings = new();
    private MidiOutStatus _statusValue = MidiOutStatus.Disabled;
    private string _statusPortName = "";
    private bool _busy;
    private bool _syncing;

    public override void _Ready()
    {
        SectionHeading.Bind(GetNode<Button>("Header"), GetNode<Control>("Fields"), "MIDI 输出");
        _enabled = GetNode<CheckButton>("Fields/Content/Enabled");
        _port = GetNode<OptionButton>("Fields/Content/Port/Value");
        _refresh = GetNode<Button>("Fields/Content/Refresh");
        _status = GetNode<Label>("Fields/Content/Status");
        _enabled.Toggled += on =>
        {
            if (!_syncing && !_busy) Emit(_settings with { Enabled = on });
        };
        _port.ItemSelected += index =>
        {
            if (_syncing || _busy) return;
            Emit(index <= 0
                ? _settings with { PortId = "", PortName = "" }
                : _settings with { PortId = _ports[index - 1].Id, PortName = _ports[index - 1].Name });
        };
        _refresh.Pressed += () => { if (!_busy) RefreshRequested?.Invoke(); };
        // The status text is composed with T(), so it needs an explicit rebuild on a
        // language switch; the placeholder item is translated for the same reason.
        AppLocale.LanguageChanged += () =>
        {
            RebuildPortItems();
        };
    }

    public void Refresh(MidiOutSettings settings, MidiOutStatus status, string portName)
    {
        _settings = settings;
        _syncing = true;
        _enabled.SetPressedNoSignal(settings.Enabled);
        if (_port.ItemCount > 0) _port.Select(PortIndex(settings));
        _syncing = false;
        SetStatus(status, portName);
        SetBusy(_busy);
    }

    public void SetPorts(MidiOutPortInfo[] ports, MidiOutSettings settings)
    {
        _ports = ports ?? Array.Empty<MidiOutPortInfo>();
        _settings = settings;
        RebuildPortItems();
        SetBusy(_busy);
    }

    // Rebuilding also re-renders the status, whose wording depends on whether any port exists.
    private void RebuildPortItems()
    {
        _syncing = true;
        _port.Clear();
        _port.AddItem(AppLocale.T("（未选择端口）"));
        foreach (var port in _ports) _port.AddItem(port.Name);
        _port.Select(PortIndex(_settings));
        _syncing = false;
        RenderStatus();
    }

    public void SetStatus(MidiOutStatus status, string portName)
    {
        _statusValue = status;
        _statusPortName = portName ?? "";
        RenderStatus();
    }

    private void RenderStatus()
    {
        if (_statusValue == MidiOutStatus.NoPort && _ports.Length == 0)
        {
            _status.Text = AppLocale.T("当前没有可用的 MIDI 输出端口。");
            return;
        }
        _status.Text = _statusValue switch
        {
            MidiOutStatus.Disabled => AppLocale.T("未启用"),
            MidiOutStatus.NoPort => AppLocale.T("未选择端口"),
            MidiOutStatus.Opening => AppLocale.T("正在连接…"),
            MidiOutStatus.Connected => string.Format(AppLocale.T("已连接 · {0}"), _statusPortName),
            MidiOutStatus.PortMissing => AppLocale.T("端口不存在，请刷新后重新选择"),
            _ => AppLocale.T("连接已断开；播放时会自动重试")
        };
    }

    public bool HasOpenPopup() => _port.GetPopup().Visible;

    public void SetBusy(bool busy)
    {
        _busy = busy;
        _enabled.Disabled = busy;
        _port.Disabled = busy || _ports.Length == 0;
        _refresh.Disabled = busy;
    }

    private void Emit(MidiOutSettings settings)
    {
        _settings = settings;
        SettingsChanged?.Invoke(settings);
    }

    // Index 0 is the "not selected" placeholder. Prefer the saved endpoint id, then its
    // name, so a loopback pair recreated with the same name keeps working.
    private int PortIndex(MidiOutSettings settings)
    {
        for (int i = 0; i < _ports.Length; i++)
            if (_ports[i].Id == settings.PortId) return i + 1;
        for (int i = 0; i < _ports.Length; i++)
            if (_ports[i].Name == settings.PortName) return i + 1;
        return 0;
    }
}
