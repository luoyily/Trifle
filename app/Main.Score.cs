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

    private void InitializeScore()
    {
        _scorePanel = _settings.GetNode<ScorePanel>("Margin/Content/Scroll/Groups/ScorePanel");
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
            SetStatus("载入乐谱失败：" + error.Message);
            GD.PushWarning(_status.Text);
            return false;
        }
    }

    private void ActivateScore(MuseScoreBundle bundle, MidiSong song, string path)
    {
        _visualizer.Score.SetBundle(bundle);
        _scorePath = path; _scoreError = "";
        SetSong(song, midiFromScore: true, scoreDuration: bundle.DurationSeconds, preserveScore: true);
        _projectPath = ""; _projectMenu.SetProjectPath(""); UpdateTitle();
        _settings.RestoreExpandedSections(new());
        SetStatus($"已载入乐谱和同源 MIDI · {bundle.Title}（{bundle.Pages.Length} 页，{bundle.Rows.Length} 行）");
    }

    private void ClearScoreState()
    {
        _scorePath = ""; _scoreError = "";
        _visualizer.Score.ClearBundle();
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
        catch (Exception error) { SetStatus("乐谱设置无效：" + error.Message); }
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

    public void JumpScoreRow(int direction)
    {
        var score = _visualizer.Score;
        if (_busy || score.Bundle == null) return;
        int row = Math.Clamp(score.Cursor.Row + Math.Sign(direction), 0, score.Bundle.Rows.Length - 1);
        var target = score.Bundle.Events.FirstOrDefault(e => e.Row == row);
        if (target != null) SetTime(target.Seconds);
    }

    private void RefreshScoreSettings()
    {
        var score = _visualizer.Score;
        _scorePanel.Refresh(score.Settings, score.Bundle != null);
        _scorePanel.RefreshPosition(score.Bundle, score.Cursor);
        _quick.RefreshScore(_scorePath, score.Bundle != null, score.Bundle?.Title ?? "");
    }

    public Godot.Collections.Dictionary GetScoreInfo()
    {
        var score = _visualizer.Score;
        return new()
        {
            ["path"] = _scorePath, ["available"] = score.Bundle != null, ["error"] = _scoreError,
            ["midi_from_score"] = _midiFromScore, ["enabled"] = score.Settings.Enabled,
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
