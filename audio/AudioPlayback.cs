using Godot;
using System;
using System.IO;
using Trifle.Export;

namespace Trifle.Audio;

public partial class AudioPlayback : AudioStreamPlayer
{
    public AudioSettings Settings { get; private set; } = new();
    public double Duration => Stream?.GetLength() ?? 0;
    public int Corrections { get; private set; }
    private double _checkElapsed;
    private double _settleElapsed;
    private double _pausedPosition;
    private const double CheckInterval = 0.25;
    private const double DriftLimit = 0.08;

    public static AudioStream ReadStream(string path)
    {
        if (path.Length == 0) return null;
        new AudioSettings { Path = path }.Validate();
        string absolute = ProjectSettings.GlobalizePath(path);
        if (!File.Exists(absolute)) throw new FileNotFoundException("音频文件不存在。", absolute);
        AudioStream stream = Path.GetExtension(absolute).ToLowerInvariant() switch
        {
            ".ogg" => AudioStreamOggVorbis.LoadFromFile(absolute),
            ".mp3" => AudioStreamMP3.LoadFromFile(absolute),
            ".wav" => AudioStreamWav.LoadFromFile(absolute),
            _ => throw new ArgumentException("请选择 OGG / Vorbis、MP3 或 WAV 文件。")
        };
        if (stream == null || stream.GetLength() <= 0)
            throw new IOException("无法读取有效音频，请检查文件格式与内容（OGG / Vorbis、MP3、WAV）。");
        switch (stream)
        {
            case AudioStreamOggVorbis ogg: ogg.Loop = false; break;
            case AudioStreamMP3 mp3: mp3.Loop = false; break;
            case AudioStreamWav wav: wav.LoopMode = AudioStreamWav.LoopModeEnum.Disabled; break;
        }
        return stream;
    }

    public void Configure(AudioSettings settings, AudioStream stream)
    {
        settings.Validate();
        Stop();
        StreamPaused = false;
        Stream = stream;
        Settings = settings;
        Corrections = 0;
        _pausedPosition = 0;
        _checkElapsed = _settleElapsed = 0;
    }

    public void SetSettings(AudioSettings settings)
    {
        settings.Validate();
        Settings = settings;
    }

    public AudioExportSettings GetExportSettings() => Settings.Enabled && Stream != null
        ? new AudioExportSettings(Settings.Path, Settings.OffsetSeconds, Duration) : null;

    public double AudiblePosition => !Playing || StreamPaused ? _pausedPosition : Math.Max(0,
        GetPlaybackPosition() + AudioServer.GetTimeSinceLastMix() - AudioServer.GetOutputLatency());

    public void Synchronize(double songTime, bool playing, double elapsed = 0, bool force = false)
    {
        double target = AudioTiming.SourceTime(songTime, Settings.OffsetSeconds);
        if (!Settings.Enabled || Stream == null || target < 0 || target >= Duration)
        {
            Stop();
            _pausedPosition = Math.Clamp(target, 0, Duration);
            return;
        }
        if (!playing)
        {
            // Paused seeks do not start the audio device. Resume starts at the selected time.
            StreamPaused = true;
            _pausedPosition = target;
            return;
        }
        if (force || !Playing || StreamPaused)
        {
            StreamPaused = false;
            Play((float)target);
            _checkElapsed = 0;
            _settleElapsed = CheckInterval;
            return;
        }
        _settleElapsed = Math.Max(0, _settleElapsed - elapsed);
        _checkElapsed += elapsed;
        if (_checkElapsed < CheckInterval || _settleElapsed > 0) return;
        _checkElapsed = 0;
        if (Math.Abs(AudiblePosition - target) > DriftLimit)
        {
            Seek((float)target);
            Corrections++;
            _settleElapsed = CheckInterval;
        }
    }
}
