using Godot;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Trifle.Midi;
using Trifle.Visuals;

namespace Trifle.App;

public partial class Main
{
    private void OpenMidiDialog()
    {
        if (!_busy) _fileDialog.PopupCenteredRatio(0.75f);
    }

    public void ClearMidiFile()
    {
        if (_busy) return;
        _projectPath = "";
        _projectMenu.SetProjectPath("");
        SetSong(new MidiSong("", 1, Array.Empty<MidiTrack>(), Array.Empty<MidiTempoChange>(), Array.Empty<MidiNote>(), 0));
        SetStatus("已移除 MIDI、关联音频和乐谱。打开 MIDI 或乐谱开始新曲目。");
    }

    public void HandleFilesDropped(string[] paths)
    {
        if (_busy || paths.Length == 0) return;
        var midi = paths.Where(p => Path.GetExtension(p).ToLowerInvariant() is ".mid" or ".midi").ToArray();
        var audio = paths.Where(p => Path.GetExtension(p).ToLowerInvariant() is ".ogg" or ".mp3" or ".wav").ToArray();
        var image = paths.Where(p => Path.GetExtension(p).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp").ToArray();
        var video = paths.Where(Trifle.Video.VideoBackgroundSettings.IsVideoPath).ToArray();
        var score = paths.Where(p => Path.GetExtension(p).ToLowerInvariant() is ".json" or ".mscz").ToArray();
        if (midi.Length > 1 || score.Length > 1 || audio.Length > 1 || image.Length + video.Length > 1)
        {
            SetStatus("每次可拖入一份 MIDI、一份乐谱、一份音频和一个背景文件（图片或视频）。");
            return;
        }
        if (midi.Length + score.Length + audio.Length + image.Length + video.Length != paths.Length)
        {
            SetStatus("支持拖入 MIDI、MuseScore 乐谱（MSCZ / JSON）、音频、背景图片和视频。");
            return;
        }
        if (midi.Length > 0 && score.Length == 0)
        {
            LoadMidiFile(midi[0]);
            // Do not attach dropped media to a previous song if MIDI loading failed.
            if (_song.SourcePath != midi[0]) return;
        }
        if (score.Length > 0)
        {
            if (Trifle.Score.MuseScoreImporter.IsScoreFile(score[0]))
            {
                _ = ImportDroppedScoreAsync(score[0], audio.FirstOrDefault(), image.FirstOrDefault(), video.FirstOrDefault(), midi.FirstOrDefault());
                return;
            }
            if (midi.Length == 0) { if (!LoadScoreBundleFile(score[0])) return; }
            else
            {
                try
                {
                    string path = Path.GetFullPath(ProjectSettings.GlobalizePath(score[0]));
                    var bundle = Trifle.Score.MuseScoreBundle.Read(path);
                    ActivateScore(bundle, ReadScoreMidi(bundle, path), path, ReadMidiFile(midi[0]));
                }
                catch (Exception error) { SetStatus(string.Format(AppLocale.T("载入 MIDI 与乐谱失败：{0}"), error.Message)); return; }
            }
        }
        if (audio.Length > 0) LoadAudioFile(audio[0]);
        if (image.Length > 0) LoadBackgroundImage(image[0]);
        if (video.Length > 0) LoadBackgroundVideo(video[0]);
    }

    public void SetBackgroundType(int type)
    {
        if (_busy) return;
        var settings = _visualizer.Background;
        var next = (BackgroundType)type;
        if (!Enum.IsDefined(next)) return;
        _visualizer.SetBackground(settings with
        {
            Type = next,
            Gradient = next == BackgroundType.Gradient && settings.Gradient == BackgroundGradient.Solid
                ? BackgroundGradient.Vertical : settings.Gradient
        }, allowFallback: true);
        RefreshBackgroundSettings();
    }

    public void SetBackgroundAppearance(double opacity, double brightness, double blur = 0)
    {
        if (_busy) return;
        _visualizer.SetBackground(_visualizer.Background with { Opacity = opacity, Brightness = brightness, Blur = blur }, allowFallback: true);
        RefreshBackgroundSettings();
    }

    private void RefreshExportContents()
    {
        var background = _visualizer.Background;
        string kind = background.Type switch
        {
            BackgroundType.Gradient => AppLocale.T("渐变背景"),
            BackgroundType.Image => _visualizer.HasBackgroundImage ? AppLocale.T("图片背景") : AppLocale.T("背景色（图片不可用）"),
            BackgroundType.Video => background.Video.Path.Length > 0 ? AppLocale.T("视频背景") : AppLocale.T("底色（未选择视频）"),
            _ => AppLocale.T("纯色背景")
        };
        var contents = new List<string> { AppLocale.T("MIDI 音符"), AppLocale.T("键盘"), kind };
        if (_visualizer.Particles.Enabled || _visualizer.Particles.Curves) contents.Add(AppLocale.T("粒子 / 流线"));
        if (_audio.Settings.Enabled && _audio.Stream != null) contents.Add(AppLocale.T("音频"));
        if (_visualizer.Score.Bundle != null && _visualizer.Score.Settings.Enabled) contents.Add(AppLocale.T("同步乐谱"));
        var warnings = new List<string>();
        if (background.Type == BackgroundType.Video && background.Video.Path.Length > 0)
        {
            if (!File.Exists(background.Video.Path)) warnings.Add(AppLocale.T("背景视频源文件不可用，请重新选择视频后导出。"));
            else if (_videoBackground.Error.Length > 0) warnings.Add(AppLocale.T("背景视频预览不可用，请检查视频与 FFmpeg 路径后导出。"));
        }
        if (_visualizer.BackgroundWarning.Length > 0) warnings.Add(AppLocale.T("背景图片不可用，将使用背景色。"));
        else if (background.Type == BackgroundType.Image && background.ImagePath.Length > 0 && !File.Exists(background.ImagePath))
            warnings.Add(AppLocale.T("背景源文件不可用，将使用已加载的图片。"));
        if (_audio.Settings.Enabled && _audio.Settings.Path.Length > 0 && !File.Exists(_audio.Settings.Path))
            warnings.Add(AppLocale.T("音频源文件不可用，请重新选择音频后导出。"));
        if (_scorePath.Length > 0 && !File.Exists(_scorePath))
            warnings.Add(AppLocale.T("乐谱源文件不可用，将使用已加载的乐谱。"));
        _exportDialog.SetContents(string.Join(" · ", contents), string.Join("\n", warnings),
            background.Type == BackgroundType.Video ? _videoBackground.Error : _visualizer.BackgroundWarning);
        _quick.RefreshMidi(_song.SourcePath, File.Exists(_song.SourcePath), _midiFromScore);
        _quick.RefreshScore(_scorePath, _visualizer.Score.Bundle != null && File.Exists(_scorePath), _visualizer.Score.Bundle?.Title ?? "");
        _quick.RefreshAudio(_audio.Settings, _audio.Duration, _audio.Stream != null && File.Exists(_audio.Settings.Path));
    }
}
