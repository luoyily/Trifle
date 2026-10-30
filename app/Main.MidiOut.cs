using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Trifle.Midi;

namespace Trifle.App;

// MIDI output wiring. Playback, audio and visuals stay unaware of the destination; the
// service mirrors the playhead and the panel only changes settings.
public partial class Main
{
    private MidiOutService _midiOut;
    private MidiOutPanel _midiOutPanel;
    private MidiOutSettings _midiOutSettings = new();
    private MidiOutStatus _midiOutStatus = MidiOutStatus.Disabled;
    private string _midiOutPortName = "";
    private bool _midiOutRefreshing;

    private void InitializeMidiOut()
    {
        _midiOut = new MidiOutService();
        _midiOutPanel = _settings.GetNode<MidiOutPanel>("Margin/Content/Scroll/Groups/MidiOutPanel");
        _midiOutPanel.SettingsChanged += ApplyMidiOutSettings;
        _midiOutPanel.RefreshRequested += () => _ = RefreshMidiOutPortsAsync();
        ApplyMidiOutSettings(_midiOutSettings);
        _ = RefreshMidiOutPortsAsync();
    }

    private void ApplyMidiOutSettings(MidiOutSettings settings)
    {
        if (_midiOut == null) return;
        try
        {
            settings.Validate();
            _midiOutSettings = settings;
            _midiOut.Apply(settings);
        }
        catch (Exception error)
        {
            SetStatus(string.Format(AppLocale.T("MIDI 输出失败：{0}"), error.Message));
        }
        RefreshMidiOutStatus();
    }

    private async Task RefreshMidiOutPortsAsync()
    {
        if (_midiOut == null || _midiOutRefreshing) return;
        _midiOutRefreshing = true;
        try
        {
            var ports = await MidiOutService.EnumerateAsync();
            if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || _midiOutPanel == null) return;
            var settings = _midiOutSettings;
            if (settings.PortId.Length > 0 &&
                ports.All(port => port.Id != settings.PortId))
            {
                // A loopback pair recreated by the system keeps its name but changes its id.
                var match = ports.FirstOrDefault(port => port.Name == settings.PortName);
                if (match.Id != null) settings = settings with { PortId = match.Id };
            }
            _midiOutPanel.SetPorts(ports, settings);
            if (settings != _midiOutSettings) ApplyMidiOutSettings(settings);
            else RefreshMidiOutStatus();
        }
        catch (Exception error)
        {
            if (GodotObject.IsInstanceValid(this) && IsInsideTree())
                SetStatus(string.Format(AppLocale.T("枚举 MIDI 输出端口失败：{0}"), error.Message));
        }
        finally { _midiOutRefreshing = false; }
    }

    private void SynchronizeMidiOut(bool playing)
    {
        if (_midiOut == null) return;
        _midiOut.Synchronize(_playback.TimeSeconds, playing);
        RefreshMidiOutStatus();
    }

    private void RefreshMidiOutStatus()
    {
        if (_midiOut == null || _midiOutPanel == null) return;
        MidiOutStatus status = _midiOut.Status;
        string name = _midiOut.PortName;
        if (status == _midiOutStatus && name == _midiOutPortName) return;
        _midiOutStatus = status;
        _midiOutPortName = name;
        _midiOutPanel.SetStatus(status, name);
    }
}
