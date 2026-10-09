using Godot;
using System;
using System.IO;
using System.Linq;
using Trifle.Midi;
using Trifle.Score;

namespace Trifle.App;

public partial class Main
{
    private ScorePanel _scorePanel;
    private string _scorePath = "", _scoreError = "";
    private bool _midiFromScore;
    private double _sourceDuration;
    private string _externalMidiPath = "";
    private ScoreSyncSettings _scoreSync = new();
    private ScoreSyncPanel _scoreSyncPanel;

    private void InitializeScore()
    {
        _scorePanel = _settings.GetNode<ScorePanel>("Margin/Content/Scroll/Groups/ScorePanel");
        _scoreSyncPanel = _audioPanel.GetNode<ScoreSyncPanel>("Fields/Content/ScoreSyncPanel");
        _scoreSyncPanel.MidiSourceChanged += fromScore => SetScoreMidiSource(fromScore);
        _scoreSyncPanel.MeasureOffsetChanged += SetScoreMeasureOffset;
        _quick.ScoreRequested += path => LoadScoreFile(path);
        _quick.ScoreClearRequested += ClearScoreFile;
        _scorePanel.SettingsChanged += ApplyScoreSettings;
        _scorePanel.RowRequested += JumpScoreRow;
        InitializeScoreImporter();
        RefreshScoreSettings();
    }

    private static MidiSong ReadScoreMidi(MuseScoreBundle bundle, string path)
    {
        if (bundle.Midi.Length == 0) throw new InvalidDataException("乐谱数据包没有同源 MIDI，请重新导出完整数据包。");
        using var stream = new MemoryStream(bundle.Midi);
        return MidiImporter.Read(stream, path);
    }

    public bool LoadScoreBundleFile(string path)
    {
        if (_busy) return false;
        try
        {
            path = Path.GetFullPath(ProjectSettings.GlobalizePath(path));
            var bundle = MuseScoreBundle.Read(path);
            var song = ReadScoreMidi(bundle, path);
            ActivateScore(bundle, song, path);
            return true;
        }
        catch (Exception error)
        {
            _scoreError = error.Message;
            SetStatus(string.Format(AppLocale.T("载入乐谱失败：{0}"), error.Message));
            GD.PushWarning(_status.Text);
            return false;
        }
    }

    private void ActivateScore(MuseScoreBundle bundle, MidiSong song, string path, MidiSong externalSong = null)
    {
        bool external = externalSong != null || (!_midiFromScore && _song.SourcePath.Length > 0);
        var activeSong = externalSong ?? (external ? _song : song);
        bool preserveMedia = external && activeSong.SourcePath == _song.SourcePath;
        var timeline = external ? ScoreTimeline.ForExternalMidi(bundle, activeSong, _scoreSync) : bundle.Timeline;
        double time = preserveMedia ? _playback.TimeSeconds : 0;
        _visualizer.Score.SetBundle(bundle, timeline: timeline);
        _scorePath = path; _scoreError = "";
        if (external) _externalMidiPath = activeSong.SourcePath;
        SetSong(activeSong, midiFromScore: !external, scoreDuration: timeline.DurationSeconds,
            preserveScore: true, preserveAudio: preserveMedia);
        SetTime(time);
        _projectPath = ""; _projectMenu.SetProjectPath(""); UpdateTitle();
        _settings.RestoreExpandedSections(new());
        SetStatus(string.Format(AppLocale.T("已载入乐谱 · {0}（{1} 页，{2} 行） · {3}"), bundle.Title, bundle.Pages.Length, bundle.Rows.Length,
            external ? AppLocale.T("保留外部 MIDI，按小节网格同步") : AppLocale.T("使用乐谱内 MIDI")));
    }

    private void ClearScoreState()
    {
        _scorePath = ""; _scoreError = "";
        _visualizer.Score.ClearBundle();
        _scoreSync = new();
    }

    public void ClearScoreFile()
    {
        if (_busy) return;
        ClearScoreState(); RefreshScoreSettings();
        SetStatus("已移除乐谱，保留当前 MIDI 和播放位置。");
    }

    private void ApplyScoreSettings(ScoreSettings settings)
    {
        if (_busy) return;
        try { _visualizer.Score.ApplySettings(settings); RefreshPreview(); }
        catch (Exception error) { SetStatus(string.Format(AppLocale.T("乐谱设置无效：{0}"), error.Message)); }
        RefreshScoreSettings();
    }

    public void SetScoreAppearance(bool enabled, double widthPercent, double yPercent, bool removeBackground, Color mainColor) =>
        ApplyScoreSettings(_visualizer.Score.Settings with
        {
            Enabled = enabled, Width = widthPercent / 100, Y = yPercent / 100,
            RemoveBackground = removeBackground, MainColor = mainColor.ToHtml()
        });

    public void SetScoreLighting(double brightness, Color cursorColor, double cursorBrightness) =>
        ApplyScoreSettings(_visualizer.Score.Settings with { Brightness = brightness,
            CursorColor = cursorColor.ToHtml(), CursorBrightness = cursorBrightness });

    public bool SetScoreMidiSource(bool fromScore)
    {
        if (_busy || _visualizer.Score.Bundle == null) return false;
        try
        {
            var bundle = _visualizer.Score.Bundle;
            if (fromScore == _midiFromScore) return true;
            if (!fromScore && _externalMidiPath.Length == 0)
                throw new InvalidOperationException(AppLocale.T("请先在快速设置中选择外部 MIDI 文件。"));
            var song = fromScore ? ReadScoreMidi(bundle, _scorePath) : ReadMidiFile(_externalMidiPath);
            var timeline = fromScore ? bundle.Timeline : ScoreTimeline.ForExternalMidi(bundle, song, _scoreSync);
            ReplaceScoreMidi(song, fromScore, timeline);
            SetStatus(fromScore ? AppLocale.T("已切换为乐谱内 MIDI。") : AppLocale.T("已切换为外部 MIDI，按小节网格同步。"));
            return true;
        }
        catch (Exception error) { SetStatus(string.Format(AppLocale.T("切换 MIDI 来源失败：{0}"), error.Message)); RefreshScoreSettings(); return false; }
    }

    private void ReplaceScoreMidi(MidiSong song, bool fromScore, ScoreTimeline timeline)
    {
        double time = _playback.TimeSeconds;
        _visualizer.Score.SetTimeline(timeline);
        SetSong(song, fromScore, timeline.DurationSeconds, preserveScore: true, preserveAudio: true);
        if (!fromScore) _externalMidiPath = song.SourcePath;
        SetTime(time); RefreshScoreSettings();
    }

    public void SetScoreMeasureOffset(int offset)
    {
        if (_busy) return;
        try
        {
            var settings = _scoreSync with { MeasureOffset = offset }; settings.Validate();
            var score = _visualizer.Score;
            var timeline = score.Bundle != null && !_midiFromScore
                ? ScoreTimeline.ForExternalMidi(score.Bundle, _song, settings) : null;
            if (timeline != null)
            {
                score.SetTimeline(timeline);
                _sourceDuration = Math.Max(_song.DurationSeconds, timeline.DurationSeconds);
            }
            _scoreSync = settings;
            RefreshAudioSettings(); RefreshScoreSettings();
        }
        catch (Exception error) { SetStatus(string.Format(AppLocale.T("乐谱同步设置无效：{0}"), error.Message)); RefreshScoreSettings(); }
    }

    public void JumpScoreRow(int direction)
    {
        var score = _visualizer.Score;
        if (_busy || score.Bundle == null) return;
        int row = Math.Clamp(score.Cursor.Row + Math.Sign(direction), 0, score.Bundle.Rows.Length - 1);
        var target = score.Timeline.Events.FirstOrDefault(e => e.Row == row);
        if (target != null) SetTime(target.Seconds);
    }

    private void RefreshScoreSettings()
    {
        var score = _visualizer.Score;
        _scorePanel.Refresh(score.Settings, score.Bundle != null);
        _scorePanel.RefreshPosition(score.Bundle, score.Cursor);
        _quick.RefreshScore(_scorePath, score.Bundle != null, score.Bundle?.Title ?? "");
        _scoreSyncPanel?.Refresh(score.Bundle != null, _midiFromScore, _externalMidiPath.Length > 0, _scoreSync);
    }

    public Godot.Collections.Dictionary GetScoreInfo()
    {
        var score = _visualizer.Score;
        return new()
        {
            ["path"] = _scorePath, ["available"] = score.Bundle != null, ["error"] = _scoreError,
            ["midi_from_score"] = _midiFromScore, ["enabled"] = score.Settings.Enabled,
            ["external_midi_path"] = _externalMidiPath, ["measure_offset"] = _scoreSync.MeasureOffset,
            ["timeline_duration"] = score.Timeline?.DurationSeconds ?? 0,
            ["visible"] = score.Bundle != null && score.IsVisibleInTree(), ["title"] = score.Bundle?.Title ?? "",
            ["rows"] = score.Bundle?.Rows.Length ?? 0, ["events"] = score.Bundle?.Events.Length ?? 0,
            ["row"] = score.Cursor?.Row ?? -1, ["page"] = score.Bundle == null ? -1 : score.Bundle.Rows[score.Cursor.Row].Page,
            ["cursor_x"] = score.Cursor?.X ?? 0, ["time"] = _playback.TimeSeconds,
            ["width"] = score.Settings.Width, ["y"] = score.Settings.Y,
            ["remove_background"] = score.Settings.RemoveBackground, ["main_color"] = score.Settings.MainColor,
            ["brightness"] = score.Settings.Brightness, ["cursor_color"] = score.Settings.CursorColor,
            ["cursor_brightness"] = score.Settings.CursorBrightness, ["cursor_gain"] = score.CursorGain,
            ["raster_count"] = score.RasterCount, ["cached_rows"] = score.CachedRows, ["raster_ms"] = score.LastRasterMilliseconds,
            ["preload_count"] = score.PreloadCount, ["preload_pending"] = score.Preloading,
            ["preload_ms"] = score.LastPreloadMilliseconds,
            ["render_width"] = score.RenderWidth, ["texture_width"] = score.TextureSize.X, ["texture_height"] = score.TextureSize.Y
        };
    }
}
