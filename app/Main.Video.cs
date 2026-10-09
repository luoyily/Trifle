using Godot;
using System;
using System.IO;
using Trifle.Video;
using Trifle.Visuals;

namespace Trifle.App;

public partial class Main
{
    private VideoBackground _videoBackground;

    private void RefreshVideoStatus()
    {
        if (_backgroundPanel == null || _quick == null) return;
        _backgroundPanel.Refresh(_visualizer.Background, _visualizer.HasBackgroundImage,
            _videoBackground.Status, _videoBackground.Error);
        _quick.RefreshBackground(_visualizer.Background, _visualizer.HasBackgroundImage,
            _videoBackground.Available, _videoBackground.Loading, _videoBackground.Error);
        if (_visualizer.Background.Type == BackgroundType.Video && _videoBackground.Error.Length > 0 && !_busy)
            SetStatus(AppLocale.T("背景视频不可用，已使用底色。请检查视频和 FFmpeg 路径。"), _videoBackground.Error);
    }

    public bool LoadBackgroundVideo(string path)
    {
        if (_busy) return false;
        try
        {
            path = ProjectSettings.GlobalizePath(path);
            if (!File.Exists(path)) throw new FileNotFoundException(AppLocale.T("背景视频不存在。"), path);
            ApplyVideoSettings(_visualizer.Background.Video with { Path = path });
            _visualizer.SetBackground(_visualizer.Background with { Type = BackgroundType.Video }, allowFallback: true);
            RefreshBackgroundSettings();
            _videoBackground.Configure(_visualizer.Background, _exportDialog.FfmpegPath, _playback.TimeSeconds, reload: true);
            _settings.CollapseSection("BackgroundPanel");
            SetStatus(string.Format(AppLocale.T("已选择背景视频：{0}"), Path.GetFileName(path)));
            return true;
        }
        catch (Exception error) { SetStatus(string.Format(AppLocale.T("打开背景视频失败：{0}"), error.Message)); return false; }
    }

    public void ClearBackgroundVideo()
    {
        if (_busy) return;
        ApplyVideoSettings(_visualizer.Background.Video with { Path = "" });
        SetStatus(AppLocale.T("已移除背景视频。"));
    }

    private void ApplyVideoSettings(VideoBackgroundSettings settings)
    {
        if (_busy) return;
        settings.Validate();
        _visualizer.SetBackground(_visualizer.Background with { Video = settings }, allowFallback: true);
        RefreshBackgroundSettings();
    }

    public void SetVideoFormat(int height, int fps, bool loop, double offset) =>
        ApplyVideoSettings(_visualizer.Background.Video with
        {
            PreviewHeight = height, PreviewFramesPerSecond = fps, Loop = loop,
            OffsetSeconds = offset
        });

    public void SetFfmpegPath(string path) { if (!_busy) _exportDialog.SetFfmpegPath(path); }

    public Godot.Collections.Dictionary GetVideoInfo() => new()
    {
        ["path"] = _visualizer.Background.Video.Path, ["available"] = _videoBackground.Available,
        ["loading"] = _videoBackground.Loading, ["error"] = _videoBackground.Error, ["status"] = _videoBackground.Status,
        ["frame"] = _videoBackground.PresentedFrame, ["presented_time"] = _videoBackground.PresentedTime,
        ["timeline_time"] = _videoBackground.TimelineTime, ["restarts"] = _videoBackground.RestartCount,
        ["queued_frames"] = _videoBackground.QueuedFrames,
        ["height"] = _visualizer.Background.Video.PreviewHeight,
        ["fps"] = _visualizer.Background.Video.PreviewFramesPerSecond
    };
}
