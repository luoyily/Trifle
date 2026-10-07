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
        SetStatus("已移除 MIDI 和关联音频。打开 MIDI 开始新曲目。");
    }

    public void HandleFilesDropped(string[] paths)
    {
        if (_busy || paths.Length == 0) return;
        var midi = paths.Where(p => Path.GetExtension(p).ToLowerInvariant() is ".mid" or ".midi").ToArray();
        var audio = paths.Where(p => Path.GetExtension(p).ToLowerInvariant() is ".ogg" or ".mp3" or ".wav").ToArray();
        var image = paths.Where(p => Path.GetExtension(p).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp").ToArray();
        if (midi.Length > 1 || audio.Length > 1 || image.Length > 1)
        {
            SetStatus("每次请只拖入一个 MIDI、一份音频和一张背景图片。");
            return;
        }
        if (midi.Length + audio.Length + image.Length != paths.Length)
        {
            SetStatus("支持拖入 MIDI、OGG / MP3 / WAV 音频和 PNG / JPEG / WebP 背景图片。");
            return;
        }
        if (midi.Length > 0)
        {
            LoadMidiFile(midi[0]);
            // Do not attach dropped media to a previous song if MIDI loading failed.
            if (_song.SourcePath != midi[0]) return;
        }
        if (audio.Length > 0) LoadAudioFile(audio[0]);
        if (image.Length > 0) LoadBackgroundImage(image[0]);
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

    public void SetBackgroundAppearance(double opacity, double brightness)
    {
        if (_busy) return;
        _visualizer.SetBackground(_visualizer.Background with { Opacity = opacity, Brightness = brightness }, allowFallback: true);
        RefreshBackgroundSettings();
    }

    private void RefreshExportContents()
    {
        var background = _visualizer.Background;
        string kind = background.Type switch
        {
            BackgroundType.Gradient => "渐变背景",
            BackgroundType.Image => _visualizer.HasBackgroundImage ? "图片背景" : "背景色（图片不可用）",
            _ => "纯色背景"
        };
        var contents = new List<string> { "MIDI 音符", "键盘", kind };
        if (_visualizer.Particles.Enabled || _visualizer.Particles.Curves) contents.Add("粒子 / 流线");
        if (_audio.Settings.Enabled && _audio.Stream != null) contents.Add("音频");
        var warnings = new List<string>();
        if (_visualizer.BackgroundWarning.Length > 0) warnings.Add("背景图片不可用，将使用背景色。");
        else if (background.Type == BackgroundType.Image && background.ImagePath.Length > 0 && !File.Exists(background.ImagePath))
            warnings.Add("背景源文件不可用，将使用已加载的图片。");
        if (_audio.Settings.Enabled && _audio.Settings.Path.Length > 0 && !File.Exists(_audio.Settings.Path))
            warnings.Add("音频源文件不可用，请重新选择音频后导出。");
        _exportDialog.SetContents(string.Join(" · ", contents), string.Join("\n", warnings),
            _visualizer.BackgroundWarning);
        _quick.RefreshMidi(_song.SourcePath, File.Exists(_song.SourcePath));
        _quick.RefreshAudio(_audio.Settings, _audio.Duration, _audio.Stream != null && File.Exists(_audio.Settings.Path));
    }
}
