using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Trifle.Midi;

public enum MidiOutStatus
{
    Disabled,
    NoPort,
    Opening,
    Connected,
    PortMissing,
    Lost
}

// Owns the destination port and a dedicated send thread. The visualizer's playhead stays
// authoritative: Synchronize() re-anchors it every frame and the thread only interpolates
// between frames, so notes leave at millisecond resolution without changing playback logic.
public sealed class MidiOutService : IDisposable
{
    private const double MaxExtrapolationSeconds = 0.25;
    private const double DiscontinuitySeconds = 0.25;
    private const double RetryMilliseconds = 2000;

    private readonly object _gate = new();
    private readonly MidiOutScheduler _scheduler = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<MidiOutMessage> _pumpBuffer = new();
    private readonly List<MidiOutMessage> _flushBuffer = new();
    private readonly Thread _thread;
    private volatile bool _shutdown;

    private MidiOutSettings _settings = new();
    private IMidiOutPort _port;
    private string _portName = "";
    private MidiOutStatus _status = MidiOutStatus.Disabled;
    private bool _opening;
    private bool _playing;
    private double _anchorSongSeconds;
    private double _anchorMilliseconds;
    private double _lastSongSeconds;
    private double _nextRetryMilliseconds;

    public MidiOutService()
    {
        _thread = new Thread(PumpLoop)
        {
            IsBackground = true,
            Name = "Trifle MIDI Out",
            Priority = ThreadPriority.Highest
        };
        _thread.Start();
    }

    public MidiOutStatus Status
    {
        get { lock (_gate) return _status; }
    }

    public string PortName
    {
        get { lock (_gate) return _portName; }
    }

    public static Task<MidiOutPortInfo[]> EnumerateAsync() =>
        Task.Run(() => WinRtMidiOutPort.EnumerateAsync());

    public void Apply(MidiOutSettings settings)
    {
        settings.Validate();
        bool open = false;
        string id = "";
        lock (_gate)
        {
            bool portChanged = !string.Equals(_settings.PortId, settings.PortId, StringComparison.Ordinal);
            _settings = settings;
            if (!settings.Enabled)
            {
                SuspendLocked();
                CloseLocked();
                _status = MidiOutStatus.Disabled;
                return;
            }
            if (settings.PortId.Length == 0)
            {
                SuspendLocked();
                CloseLocked();
                _status = MidiOutStatus.NoPort;
                return;
            }
            _portName = settings.PortName;
            if (portChanged || _port == null)
            {
                if (portChanged)
                {
                    SuspendLocked();
                    CloseLocked();
                }
                _status = MidiOutStatus.Opening;
                open = true;
                id = settings.PortId;
            }
        }
        if (open) _ = OpenAsync(id);
    }

    public void SetEvents(MidiChannelEvent[] events)
    {
        lock (_gate)
        {
            SuspendLocked();
            _scheduler.SetEvents(events);
            _lastSongSeconds = 0;
        }
    }

    // Called once per frame with the exact playhead. Handles start, pause, seek and drift:
    // a jump larger than the discontinuity limit is a seek, which releases notes first.
    public void Synchronize(double songSeconds, bool playing)
    {
        if (!double.IsFinite(songSeconds)) return;
        bool open = false;
        string id = "";
        lock (_gate)
        {
            if (!playing)
            {
                if (_playing) SuspendLocked();
                if (Math.Abs(songSeconds - _lastSongSeconds) > 0.001)
                {
                    FlushLocked();
                    _scheduler.Seek(songSeconds);
                }
                _lastSongSeconds = songSeconds;
            }
            else
            {
                if (!_playing)
                {
                    _playing = true;
                    _scheduler.Seek(songSeconds);
                    _anchorSongSeconds = songSeconds;
                    _anchorMilliseconds = _clock.Elapsed.TotalMilliseconds;
                }
                else
                {
                    double expected = _anchorSongSeconds +
                        (_clock.Elapsed.TotalMilliseconds - _anchorMilliseconds) / 1000.0;
                    if (Math.Abs(songSeconds - expected) > DiscontinuitySeconds)
                    {
                        SuspendLocked();
                        _scheduler.Seek(songSeconds);
                        _playing = true;
                    }
                    _anchorSongSeconds = songSeconds;
                    _anchorMilliseconds = _clock.Elapsed.TotalMilliseconds;
                }
                // Without an open port the scheduler tracks position without emitting, so
                // opening it later starts at the current playhead instead of a burst.
                if (_port == null) _scheduler.Seek(songSeconds);
                _lastSongSeconds = songSeconds;
            }

            if (_settings.Enabled && _settings.PortId.Length > 0 && _port == null && !_opening &&
                _clock.Elapsed.TotalMilliseconds >= _nextRetryMilliseconds)
            {
                _nextRetryMilliseconds = _clock.Elapsed.TotalMilliseconds + RetryMilliseconds;
                _status = MidiOutStatus.Opening;
                open = true;
                id = _settings.PortId;
            }
        }
        if (open) _ = OpenAsync(id);
    }

    public void Dispose()
    {
        _shutdown = true;
        try { _thread.Join(500); } catch { }
        lock (_gate)
        {
            _playing = false;
            FlushLocked();
            CloseLocked();
        }
    }

    private async Task OpenAsync(string id)
    {
        string name;
        lock (_gate)
        {
            if (_opening) return;
            _opening = true;
            name = _portName;
        }
        IMidiOutPort port = null;
        try { port = await WinRtMidiOutPort.OpenAsync(id, name); }
        catch { }
        bool keep = false;
        lock (_gate)
        {
            _opening = false;
            if (port != null && _settings.Enabled &&
                string.Equals(_settings.PortId, id, StringComparison.Ordinal))
            {
                CloseLocked();
                _port = port;
                _portName = _settings.PortName.Length > 0 ? _settings.PortName : port.Name;
                _status = MidiOutStatus.Connected;
                keep = true;
            }
            else if (_settings.Enabled && string.Equals(_settings.PortId, id, StringComparison.Ordinal))
            {
                _status = MidiOutStatus.PortMissing;
            }
        }
        if (!keep && port != null) port.Dispose();
    }

    private void PumpLoop()
    {
        MidiTimerResolution.Begin();
        try
        {
            while (!_shutdown)
            {
                IMidiOutPort port = null;
                MidiOutMessage[] batch = null;
                lock (_gate)
                {
                    if (_playing && _port != null)
                    {
                        double target = _anchorSongSeconds +
                            (_clock.Elapsed.TotalMilliseconds - _anchorMilliseconds) / 1000.0;
                        if (target > _anchorSongSeconds + MaxExtrapolationSeconds)
                            target = _anchorSongSeconds + MaxExtrapolationSeconds;
                        _pumpBuffer.Clear();
                        _scheduler.Advance(target, _pumpBuffer);
                        if (_pumpBuffer.Count > 0)
                        {
                            port = _port;
                            batch = _pumpBuffer.ToArray();
                            // Sending while holding the lock keeps a pause flush strictly
                            // after the batch, so no note-on can land after its note-off.
                            SendBatch(port, batch);
                        }
                    }
                }
                Thread.Sleep(1);
            }
        }
        finally { MidiTimerResolution.End(); }
    }

    private void SendBatch(IMidiOutPort port, MidiOutMessage[] batch)
    {
        try
        {
            foreach (var message in batch) port.Send(message);
        }
        catch
        {
            lock (_gate)
            {
                if (ReferenceEquals(_port, port))
                {
                    CloseLocked();
                    _status = MidiOutStatus.Lost;
                    _nextRetryMilliseconds = _clock.Elapsed.TotalMilliseconds + RetryMilliseconds;
                }
            }
        }
    }

    private void SuspendLocked()
    {
        _playing = false;
        FlushLocked();
    }

    private void FlushLocked()
    {
        _flushBuffer.Clear();
        _scheduler.Flush(_flushBuffer);
        if (_port == null || _flushBuffer.Count == 0) return;
        try
        {
            foreach (var message in _flushBuffer) _port.Send(message);
        }
        catch
        {
            CloseLocked();
            _status = MidiOutStatus.Lost;
        }
    }

    private void CloseLocked()
    {
        var port = _port;
        _port = null;
        if (port == null) return;
        try { port.Dispose(); } catch { }
    }
}
