using Godot;
using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Trifle.Export;
using Trifle.Audio;
using Trifle.Midi;
using Trifle.Playback;
using Trifle.Persistence;
using Trifle.Visuals;
using GodotFileAccess = Godot.FileAccess;

namespace Trifle.App;

public partial class Main : Control
{
    private MidiSong _song;
    private Visualizer _visualizer;
    private HSlider _timeline;
    private Label _title;
    private Label _timeLabel;
    private Label _status;
    private FileDialog _fileDialog;
    private readonly PlaybackController _playback = new();
    private Button _play;
    private Texture2D _playIcon, _pauseIcon;
    private SettingsPanel _settings;
    private Button _settingsToggle;
    private bool _settingsVisible = true;
    private bool _resumeAfterScrub;
    private int _hitsSinceSeek;
    private MidiNote _lastHit;
    private SongInfoDialog _infoDialog;
    private ExportDialog _exportDialog;
    private SubViewport _viewport;
    private SubViewport _hdrViewport;
    private TextureRect _present;
    private HdrGlow _hdrGlow;
    private bool _busy;
    private bool _uiVisible = true;
    private bool _quitAfterExport;
    private CancellationTokenSource _exportCancellation;
    private ExportProgress _exportProgress = new("idle", 0, 0);
    private string _exportOutput = "";
    private string _exportError = "";
    private double _exportFrameTime;
    private readonly Stopwatch _exportWatch = new();
    private ProjectMenu _projectMenu;
    private string _projectPath = "";
    private ProjectData _pendingProject;
    private string _pendingProjectPath;
    private AudioPlayback _audio;
    private AudioPanel _audioPanel;
    private BackgroundPanel _backgroundPanel;
    private NoteAppearancePanel _notePanel;
    private QuickSettingsPanel _quick;
    private KeyboardLightPanel _keyboardLightPanel;
    private ContactLinePanel _contactLinePanel;
    private GlowPanel _glowPanel;
    private ParticlePanel _particlePanel;
    private PreviewPanel _previewPanel;
    private PreviewSettings _previewSettings = new();

    public override void _Ready()
    {
        GetTree().Root.GuiEmbedSubwindows = true;
        GetTree().AutoAcceptQuit = false;
        GetWindow().MinSize = EditorWindowMinimum;
        GetWindow().CloseRequested += () =>
        {
            if (_busy) { _quitAfterExport = true; CancelExport(); CancelScoreImport(); }
            else { SaveRecoveryNow(); GetTree().Quit(); }
        };
        _visualizer = GetNode<Visualizer>("HdrViewport/Visualizer");
        _viewport = GetNode<SubViewport>("PreviewViewport");
        _hdrViewport = GetNode<SubViewport>("HdrViewport");
        _present = _viewport.GetNode<TextureRect>("Present");
        _videoBackground = GetNode<Trifle.Video.VideoBackground>("VideoBackground");
        _present.Texture = _hdrViewport.GetTexture();
        _hdrGlow = new HdrGlow { Name = "HdrGlow" };
        AddChild(_hdrGlow);
        _hdrGlow.Initialize(_hdrViewport, _viewport, (ShaderMaterial)_present.Material);
        _visualizer.SetOutputMaterial((ShaderMaterial)_present.Material, _hdrGlow);
        GetNode<PreviewSurface>("Margin/Content/Body/PreviewArea/Preview").Texture = _viewport.GetTexture();
        const string ui = "Margin/Content/";
        _timeline = GetNode<HSlider>(ui + "Transport/Time");
        _timeLabel = GetNode<Label>(ui + "Transport/TimeLabel");
        _title = GetNode<Label>(ui + "Header/Title");
        _status = GetNode<Label>(ui + "Status");
        _projectMenu = GetNode<ProjectMenu>(ui + "Header/ProjectMenu");
        _projectMenu.FileRequested += HandleProjectFile;
        _projectMenu.RelinkCancelled += () => _pendingProject = null;
        _play = GetNode<Button>(ui + "Transport/Play");
        _playIcon = GetThemeIcon("play", "Trifle");
        _pauseIcon = GetThemeIcon("pause", "Trifle");
        GetNode<Button>(ui + "Transport/Stop").Icon = GetThemeIcon("stop", "Trifle");
        GetNode<Button>(ui + "Transport/Start").Icon = GetThemeIcon("restart", "Trifle");
        GetNode<Button>(ui + "Transport/FirstNote").Icon = GetThemeIcon("first_note", "Trifle");
        _settings = GetNode<SettingsPanel>(ui + "Body/SettingsPanel");
        _audio = GetNode<AudioPlayback>("AudioPlayback");
        _audioPanel = _settings.GetNode<AudioPanel>("Margin/Content/Scroll/Groups/AudioPanel");
        _quick = _settings.GetNode<QuickSettingsPanel>("Margin/Content/QuickSettingsPanel");
        _quick.AudioRequested += path => LoadAudioFile(path);
        _quick.AudioClearRequested += ClearAudio;
        _quick.ImageRequested += path => LoadBackgroundImage(path);
        _quick.ImageClearRequested += ClearBackgroundImage;
        _quick.VideoRequested += path => LoadBackgroundVideo(path);
        _quick.VideoClearRequested += ClearBackgroundVideo;
        _quick.BackgroundTypeChanged += SetBackgroundType;
        _quick.ColorChanged += SetBackgroundColor;
        _quick.EndColorChanged += color => SetBackgroundGradient((int)_visualizer.Background.Gradient, color);
        _audioPanel.EnabledChanged += SetAudioEnabled;
        _audioPanel.OffsetChanged += SetAudioOffset;
        _settings.Bind(_visualizer);
        InitializeScore();
        _settingsToggle = GetNode<Button>(ui + "Header/Settings");
        _settingsToggle.Toggled += SetSettingsVisible;
        _settings.LookAheadChanged += SetLookAhead;
        _settings.ColorModeChanged += SetColorMode;
        _settings.ChannelColorChanged += SetChannelColor;
        _settings.TrackColorChanged += SetTrackColor;
        _settings.PitchRangeChanged += SetPitchRange;
        _settings.KeyboardBoundsChanged += SetKeyboardBounds;
        _settings.KeyboardColorChanged += SetKeyboardColor;
        _backgroundPanel = _settings.GetNode<BackgroundPanel>("Margin/Content/Scroll/Groups/BackgroundPanel");
        _backgroundPanel.ColorChanged += SetBackgroundColor;
        _backgroundPanel.FitChanged += SetBackgroundFit;
        _backgroundPanel.AppearanceChanged += SetBackgroundAppearance;
        _backgroundPanel.VideoChanged += ApplyVideoSettings;
        _videoBackground.StatusChanged += RefreshVideoStatus;
        _videoBackground.FrameChanged += _visualizer.SetVideoTexture;
        RefreshBackgroundSettings();
        _notePanel = _settings.GetNode<NoteAppearancePanel>("Margin/Content/Scroll/Groups/Notes/Fields/Content/NoteAppearancePanel");
        _notePanel.AppearanceChanged += ApplyNoteAppearance;
        RefreshNoteSettings();
        _keyboardLightPanel = _settings.GetNode<KeyboardLightPanel>("Margin/Content/Scroll/Groups/Keyboard/Fields/Content/EffectsInset/Groups/KeyboardLightPanel");
        _contactLinePanel = _settings.GetNode<ContactLinePanel>("Margin/Content/Scroll/Groups/Keyboard/Fields/Content/EffectsInset/Groups/ContactLinePanel");
        _glowPanel = _settings.GetNode<GlowPanel>("Margin/Content/Scroll/Groups/GlowPanel");
        _keyboardLightPanel.SettingsChanged += ApplyKeyboardLights;
        _contactLinePanel.SettingsChanged += ApplyContactLine;
        _glowPanel.SettingsChanged += ApplyGlow;
        RefreshLightSettings();
        _particlePanel = _settings.GetNode<ParticlePanel>("Margin/Content/Scroll/Groups/ParticlePanel");
        _particlePanel.SettingsChanged += ApplyParticles;
        _particlePanel.Refresh(_visualizer.Particles);
        _backgroundPanel.GradientChanged += (mode, color) => SetBackgroundGradient(mode, color);
        _previewPanel = _settings.GetNode<PreviewPanel>("Margin/Content/Scroll/Groups/PreviewPanel");
        _previewPanel.SettingsChanged += ApplyPreviewSettings;
        ApplyPreviewSettings(_previewSettings);
        _play.Pressed += TogglePlayback;
        GetNode<Button>(ui + "Transport/Stop").Pressed += StopPlayback;
        // This event is the future effects hook. Seeking only rebuilds state and clears diagnostics.
        _playback.NoteHit += note => { _hitsSinceSeek++; _lastHit = note; };
        _fileDialog = GetNode<FileDialog>("MidiDialog");
        _fileDialog.CurrentDir = ProjectSettings.GlobalizePath("res://test_assets");
        _fileDialog.FileSelected += LoadMidiFile;
        _quick.MidiRequested += OpenMidiDialog;
        _quick.MidiClearRequested += ClearMidiFile;
        GetNode<Button>(ui + "Body/PreviewArea/EmptyState/Content/OpenMidi").Pressed += OpenMidiDialog;
        GetWindow().FilesDropped += HandleFilesDropped;
        _infoDialog = GetNode<SongInfoDialog>("SongInfoDialog");
        _exportDialog = GetNode<ExportDialog>("ExportDialog");
        _exportDialog.FfmpegPathChanged += RefreshBackgroundSettings;
        GetNode<Button>(ui + "Header/Info").Pressed += () => _infoDialog.PopupCentered();
        GetNode<Button>(ui + "Header/Export").Pressed += () =>
        {
            if (_playback.DurationSeconds > 0) { RefreshExportContents(); _exportDialog.Open(); }
            else SetStatus("当前曲目没有可导出的时间区间。");
        };
        GetNode<Button>(ui + "Header/HideUi").Pressed += () => SetUiVisible(!_uiVisible);
        _exportDialog.ExportRequested += (settings, ffmpeg) => _ = ExportAsync(settings, ffmpeg);
        _exportDialog.CancelRequested += CancelExport;
        GetNode<Button>(ui + "Transport/Start").Pressed += () => SetTime(0);
        GetNode<Button>(ui + "Transport/FirstNote").Pressed += () =>
        {
            if (_song?.Notes.Length > 0) SetTime(_song.Notes[0].StartSeconds);
        };
        _timeline.ValueChanged += SetTime;
        _timeline.DragStarted += () =>
        {
            _resumeAfterScrub = _playback.IsPlaying;
            PausePlayback();
        };
        _timeline.DragEnded += _ =>
        {
            if (_resumeAfterScrub && _playback.TimeSeconds < _playback.DurationSeconds) PlayPlayback();
            _resumeAfterScrub = false;
        };
        SetSong(new MidiSong("", 1, Array.Empty<MidiTrack>(), Array.Empty<MidiTempoChange>(), Array.Empty<MidiNote>(), 0));
        SetStatus("打开 MIDI 或乐谱开始；音频、背景和视觉参数在右侧设置。Space 播放 / 暂停，Ctrl+H 隐藏界面。");
        InitializeRecovery();
    }

    public override void _Process(double delta)
    {
        TickAutosave(delta);
        if (_busy || !_playback.IsPlaying) return;
        _playback.Advance(delta);
        _audio.Synchronize(_playback.TimeSeconds, _playback.IsPlaying, delta);
        RefreshPreview();
    }

    public void LoadMidiFile(string path)
    {
        if (_busy) return;
        try
        {
            var song = ReadMidiFile(path);
            var score = _visualizer.Score;
            if (score.Bundle != null)
            {
                var timeline = Trifle.Score.ScoreTimeline.ForExternalMidi(score.Bundle, song, _scoreSync);
                ReplaceScoreMidi(song, false, timeline);
                SetStatus("已载入外部 MIDI；保留乐谱并按小节网格同步。");
                return;
            }
            _projectPath = "";
            _projectMenu.SetProjectPath("");
            SetSong(song);
            _settings.RestoreExpandedSections(new());
            SetStatus("已打开 MIDI；可在设置栏选择音频。Space 播放 / 暂停，Ctrl+H 显示 / 隐藏界面。");
            GD.Print($"[M4] Imported {path}: {song.Tracks.Length} tracks, {song.Notes.Length} notes, {song.DurationSeconds:F6}s.");
        }
        catch (Exception exception)
        {
            SetStatus("导入失败：" + exception.Message);
            GD.PushWarning("[M4] " + exception.Message);
        }
    }

    private static MidiSong ReadMidiFile(string path)
    {
        using var file = GodotFileAccess.Open(path, GodotFileAccess.ModeFlags.Read);
        if (file == null) throw new IOException($"无法读取 MIDI：{GodotFileAccess.GetOpenError()}");
        using var stream = new MemoryStream(file.GetBuffer(checked((long)file.GetLength())));
        return MidiImporter.Read(stream, path);
    }

    private void SetSong(MidiSong song, bool midiFromScore = false, double scoreDuration = 0,
        bool preserveScore = false, bool preserveAudio = false)
    {
        var audioSettings = _audio.Settings; var audioStream = _audio.Stream;
        if (!preserveScore)
        {
            ClearScoreState();
            _externalMidiPath = midiFromScore ? "" : song.SourcePath;
        }
        _midiFromScore = midiFromScore;
        _sourceDuration = Math.Max(song.DurationSeconds, scoreDuration);
        _song = song;
        _quick.RefreshMidi(song.SourcePath, song.SourcePath.Length > 0, midiFromScore);
        GetNode<Control>("Margin/Content/Body/PreviewArea/EmptyState").Visible = _uiVisible && song.SourcePath.Length == 0;
        // A newly imported MIDI must not accidentally play the previous song's audio.
        _audio.Configure(preserveAudio ? audioSettings : new AudioSettings(), preserveAudio ? audioStream : null);
        _playback.SetSong(song);
        _resumeAfterScrub = false;
        _visualizer.SetSong(song);
        _infoDialog.SetSong(song);
        _settings.SetSong(song);
        _exportDialog.ResetForSong(song);
        RefreshAudioSettings();
        RefreshScoreSettings();
        UpdateTitle();
        ClearHits();
        RefreshPreview();
    }

    private void SetStatus(string text, string details = null)
    {
        _status.Text = text;
        _status.TooltipText = details ?? text;
    }

    private void UpdateTitle()
    {
        _title.Text = _song.SourcePath.Length == 0 ? "未打开 MIDI" : Path.GetFileName(_song.SourcePath);
        _title.TooltipText = _projectPath.Length == 0 ? _song.SourcePath : _projectPath + "\n" + _song.SourcePath;
    }

    private void HandleProjectFile(ProjectFileAction action, string path)
    {
        switch (action)
        {
            case ProjectFileAction.OpenProject: LoadProjectFile(path); break;
            case ProjectFileAction.SaveProject:
            case ProjectFileAction.SaveProjectAs: SaveProjectFile(path); break;
            case ProjectFileAction.OpenPreset: LoadVisualPreset(path); break;
            case ProjectFileAction.SavePreset: SaveVisualPreset(path); break;
            case ProjectFileAction.RelinkMidi: RelinkProjectMidi(path); break;
            case ProjectFileAction.RelinkAudio: RelinkProjectAudio(path); break;
            case ProjectFileAction.RelinkScore: RelinkProjectScore(path); break;
            case ProjectFileAction.WithoutAudio: LoadProjectWithoutAudio(); break;
        }
    }

    private ProjectData CaptureProject() => new()
    {
        MidiPath = ProjectSettings.GlobalizePath(_song.SourcePath),
        MidiFromScore = _midiFromScore, ScorePath = _scorePath,
        ExternalMidiPath = _externalMidiPath.Length == 0 ? "" : ProjectSettings.GlobalizePath(_externalMidiPath), ScoreSync = _scoreSync,
        Visual = _visualizer.GetSettings(), Export = _exportDialog.GetPreferences(),
        Audio = _audio.Settings,
        TimeSeconds = _playback.TimeSeconds,
        SettingsVisible = _settingsVisible,
        ExpandedSections = _settings.CaptureExpandedSections(),
        Preview = _previewSettings
    };

    public bool SaveProjectFile(string path)
    {
        if (_busy || _song == null || _song.SourcePath.Length == 0)
        { SetStatus("请先打开 MIDI 再保存项目。"); return false; }
        try
        {
            var data = CaptureProject();
            data.Export.Validate(_playback.DurationSeconds);
            if (data.Export.OutputPath.Length > 0 && !Path.IsPathFullyQualified(data.Export.OutputPath))
                throw new ArgumentException("视频输出需要使用绝对路径。");
            ProjectStorage.SaveProject(path, data);
            _projectPath = Path.GetFullPath(path);
            _projectMenu.SetProjectPath(_projectPath);
            UpdateTitle();
            SetStatus("已保存 · " + Path.GetFileName(path), path);
            return true;
        }
        catch (Exception error) { SetStatus("保存项目失败：" + error.Message); return false; }
    }

    public bool LoadProjectFile(string path)
    {
        if (_busy) return false;
        _pendingProject = null;
        _projectMenu.ClearMissingRequest();
        _recoveredProjectPath = null;
        try
        {
            var data = ProjectStorage.LoadProject(path);
            _pendingProject = data;
            _pendingProjectPath = path;
            return ContinueProjectLoad();
        }
        catch (Exception error)
        {
            _pendingProject = null;
            SetStatus("加载项目失败：" + error.Message);
            return false;
        }
    }

    private bool ContinueProjectLoad(Trifle.Score.MuseScoreBundle importedScore = null)
    {
        string midi = ProjectStorage.ResolveReference(_pendingProject.MidiPath, _pendingProjectPath);
        if (!File.Exists(midi))
        {
            _projectMenu.RequestMidiReplacement((_pendingProject.MidiFromScore ? "项目引用的乐谱文件不存在：\n" : "项目引用的 MIDI 文件不存在：\n") + midi,
                _pendingProject.MidiFromScore);
            SetStatus("项目等待重新选择音乐源文件；当前曲目保持原样。");
            return false;
        }
        string scorePath = ProjectStorage.ResolveReference(_pendingProject.ScorePath, _pendingProjectPath);
        if (scorePath.Length > 0 && !File.Exists(scorePath))
        {
            _projectMenu.RequestScoreReplacement("项目引用的乐谱文件不存在：\n" + scorePath);
            SetStatus("项目等待重新选择乐谱；当前曲目保持原样。");
            return false;
        }
        string audio = ProjectStorage.ResolveReference(_pendingProject.Audio.Path, _pendingProjectPath);
        if (audio.Length > 0 && !File.Exists(audio))
        {
            _projectMenu.RequestAudioReplacement("项目引用的音频文件不存在：\n" + audio);
            SetStatus("项目等待重新选择音频；也可不使用音频加载。当前曲目保持原样。");
            return false;
        }
        string bundlePath = _pendingProject.MidiFromScore ? midi : scorePath;
        if (bundlePath.Length > 0 && Trifle.Score.MuseScoreImporter.IsScoreFile(bundlePath) && importedScore == null &&
            !_scoreImporter.TryReadCached(bundlePath, out importedScore))
        {
            _projectMenu.ClearMissingRequest();
            _ = ImportScoreAsync(bundlePath, bundle => ContinueProjectLoad(bundle));
            return false;
        }
        ApplyProject(_pendingProject, _pendingProjectPath, midi, audio, importedScore);
        if (_recoveredProjectPath != null)
        {
            _projectPath = _recoveredProjectPath;
            _recoveredProjectPath = null;
            _projectMenu.SetProjectPath(_projectPath);
            UpdateTitle();
        }
        _pendingProject = null;
        return true;
    }

    public bool RelinkProjectMidi(string midiPath)
    {
        if (_busy || _pendingProject == null) return false;
        try
        {
            bool linkedScore = _pendingProject.MidiFromScore && _pendingProject.ScorePath.Length > 0 &&
                string.Equals(ProjectStorage.ResolveReference(_pendingProject.ScorePath, _pendingProjectPath),
                    ProjectStorage.ResolveReference(_pendingProject.MidiPath, _pendingProjectPath), StringComparison.OrdinalIgnoreCase);
            _pendingProject = _pendingProject with
            {
                MidiPath = Path.GetFullPath(midiPath),
                ExternalMidiPath = _pendingProject.MidiFromScore ? _pendingProject.ExternalMidiPath : Path.GetFullPath(midiPath),
                ScorePath = linkedScore ? Path.GetFullPath(midiPath) : _pendingProject.ScorePath
            };
            return ContinueProjectLoad();
        }
        catch (Exception error)
        {
            SetStatus("重新关联失败：" + error.Message);
            _projectMenu.RequestMidiReplacement(_status.Text, _pendingProject.MidiFromScore);
            return false;
        }
    }

    public bool RelinkProjectScore(string scorePath)
    {
        if (_busy || _pendingProject == null) return false;
        try
        {
            _pendingProject = _pendingProject with { ScorePath = Path.GetFullPath(scorePath) };
            return ContinueProjectLoad();
        }
        catch (Exception error)
        {
            SetStatus("重新关联乐谱失败：" + error.Message);
            _projectMenu.RequestScoreReplacement(_status.Text); return false;
        }
    }

    public bool RelinkProjectAudio(string audioPath)
    {
        if (_busy || _pendingProject == null) return false;
        try
        {
            _pendingProject = _pendingProject with { Audio = _pendingProject.Audio with { Path = Path.GetFullPath(audioPath) } };
            return ContinueProjectLoad();
        }
        catch (Exception error)
        {
            SetStatus("重新关联失败：" + error.Message);
            _projectMenu.RequestAudioReplacement(_status.Text);
            return false;
        }
    }

    public bool LoadProjectWithoutAudio()
    {
        if (_busy || _pendingProject == null) return false;
        _pendingProject = _pendingProject with { Audio = new AudioSettings() };
        try { return ContinueProjectLoad(); }
        catch (Exception error)
        {
            _pendingProject = null;
            _projectMenu.ClearMissingRequest();
            SetStatus("加载项目失败：" + error.Message);
            return false;
        }
    }

    private void ApplyProject(ProjectData data, string projectPath, string midiPath, string audioPath, Trifle.Score.MuseScoreBundle importedScore = null)
    {
        // All file reads and data checks finish before changing the active scene.
        string scorePath = ProjectStorage.ResolveReference(data.ScorePath, projectPath);
        string bundlePath = data.MidiFromScore ? midiPath : scorePath;
        var sourceBundle = bundlePath.Length > 0 ? importedScore ?? Trifle.Score.MuseScoreBundle.Read(bundlePath) : null;
        var song = data.MidiFromScore ? ReadScoreMidi(sourceBundle, midiPath) : ReadMidiFile(midiPath);
        if (data.MidiFromScore && scorePath.Length > 0 && !string.Equals(scorePath, midiPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("乐谱内 MIDI 的来源需与乐谱引用一致。");
        var scoreTimeline = scorePath.Length == 0 ? null : data.MidiFromScore ? sourceBundle.Timeline
            : Trifle.Score.ScoreTimeline.ForExternalMidi(sourceBundle, song, data.ScoreSync);
        data.Visual.Validate(song.Tracks.Length);
        var audioStream = AudioPlayback.ReadStream(audioPath);
        double sourceDuration = Math.Max(song.DurationSeconds, scoreTimeline?.DurationSeconds ??
            (data.MidiFromScore ? sourceBundle.DurationSeconds : 0));
        double duration = AudioTiming.PlaybackDuration(sourceDuration,
            data.Audio.Enabled && audioStream != null, data.Audio.OffsetSeconds, audioStream?.GetLength() ?? 0);
        data.Export.Validate(duration);
        if (data.TimeSeconds > duration) throw new ArgumentException("保存的播放位置超出播放时长。");
        var export = data.Export with { OutputPath = ProjectStorage.ResolveReference(data.Export.OutputPath, projectPath) };
        var visual = ProjectStorage.ResolveVisualReferences(data.Visual, projectPath);
        if (scorePath.Length > 0) _visualizer.Score.SetBundle(sourceBundle, visual.Score, scoreTimeline);
        _scorePath = scorePath; _scoreError = "";
        SetSong(song, data.MidiFromScore, sourceDuration, preserveScore: scorePath.Length > 0);
        _scoreSync = data.ScoreSync;
        _externalMidiPath = !data.MidiFromScore ? midiPath :
            ProjectStorage.ResolveReference(data.ExternalMidiPath, projectPath);
        _audio.Configure(data.Audio with { Path = audioPath }, audioStream);
        RefreshAudioSettings();
        _visualizer.ApplySettings(visual);
        RefreshScoreSettings();
        RefreshBackgroundSettings();
        RefreshNoteSettings();
        RefreshLightSettings();
        _settings.SetSong(song);
        _particlePanel.Refresh(_visualizer.Particles);
        _exportDialog.ApplyPreferences(export, song.DurationSeconds);
        SetTime(data.TimeSeconds);
        SetSettingsVisible(data.SettingsVisible);
        ApplyPreviewSettings(data.Preview);
        _settings.RestoreExpandedSections(data.ExpandedSections);
        SetUiVisible(true);
        _projectPath = Path.GetFullPath(projectPath);
        _projectMenu.ClearMissingRequest();
        _projectMenu.SetProjectPath(_projectPath);
        UpdateTitle();
        SetStatus("已加载（暂停）· " + Path.GetFileName(projectPath) + BackgroundNotice(), projectPath + BackgroundNotice());
    }

    public bool SaveVisualPreset(string path)
    {
        if (_busy || _song == null) return false;
        try
        {
            ProjectStorage.SavePreset(path, new VisualPreset { Visual = _visualizer.GetSettings() });
            SetStatus("已保存视觉预设 · " + Path.GetFileName(path), path);
            return true;
        }
        catch (Exception error) { SetStatus("保存预设失败：" + error.Message); return false; }
    }

    public bool LoadVisualPreset(string path)
    {
        if (_busy || _song == null) return false;
        try
        {
            var preset = ProjectStorage.LoadPreset(path);
            int skipped = _visualizer.ApplySettings(ProjectStorage.ResolveVisualReferences(preset.Visual, path), ignoreMissingTracks: true);
            RefreshScoreSettings();
            RefreshBackgroundSettings();
            RefreshNoteSettings();
            RefreshLightSettings();
            _particlePanel.Refresh(_visualizer.Particles);
            _settings.SetSong(_song);
            RefreshPreview();
            SetStatus("已加载视觉预设。" + (skipped > 0 ? $"跳过 {skipped} 个不存在的轨道颜色。" : "") + BackgroundNotice());
            return true;
        }
        catch (Exception error) { SetStatus("加载预设失败：" + error.Message); return false; }
    }

    public void SetTime(double seconds)
    {
        if (_busy || _song == null) return;
        _playback.Seek(seconds);
        SynchronizeAudio(force: true);
        ClearHits();
        RefreshPreview();
        _videoBackground.SetTime(_playback.TimeSeconds, force: true);
    }

    public void PlayPlayback()
    {
        if (_busy || _song == null) return;
        if (_playback.TimeSeconds >= _playback.DurationSeconds) ClearHits();
        _playback.Play();
        SynchronizeAudio(force: true);
        RefreshPreview();
    }

    public void PausePlayback()
    {
        if (_busy) return;
        _playback.Pause();
        SynchronizeAudio();
        RefreshPreview();
    }

    public void StopPlayback()
    {
        if (_busy) return;
        _playback.Stop();
        _audio.Stop();
        SynchronizeAudio();
        _resumeAfterScrub = false;
        ClearHits();
        RefreshPreview();
    }

    private void TogglePlayback()
    {
        if (_playback.IsPlaying) PausePlayback();
        else PlayPlayback();
    }

    public bool LoadAudioFile(string path)
    {
        if (_busy) return false;
        try
        {
            var stream = AudioPlayback.ReadStream(path);
            if (stream == null) throw new ArgumentException("请选择 OGG / Vorbis、MP3 或 WAV 文件。");
            _audio.Configure(new AudioSettings { Path = ProjectSettings.GlobalizePath(path) }, stream);
            RefreshAudioSettings();
            SynchronizeAudio(force: true);
            _settings.CollapseSection("AudioPanel");
            SetStatus("已打开音频：" + Path.GetFileName(path));
            return true;
        }
        catch (Exception error) { SetStatus("打开音频失败：" + error.Message); return false; }
    }

    public void ClearAudio()
    {
        if (_busy) return;
        _audio.Configure(new AudioSettings(), null);
        RefreshAudioSettings();
        SetStatus("已移除音频。");
    }

    public void SetAudioEnabled(bool enabled)
    {
        if (_busy) return;
        _audio.SetSettings(_audio.Settings with { Enabled = enabled });
        RefreshAudioSettings();
        SynchronizeAudio(force: true);
    }

    public void SetAudioOffset(double seconds)
    {
        if (_busy) return;
        _audio.SetSettings(_audio.Settings with { OffsetSeconds = seconds });
        RefreshAudioSettings();
        SynchronizeAudio(force: true);
    }

    private void RefreshAudioSettings()
    {
        _audioPanel.Refresh(_audio.Settings, _audio.Duration);
        _quick.RefreshAudio(_audio.Settings, _audio.Duration, _audio.Stream != null);
        _exportDialog?.SetAudio(_audio.Settings);
        if (_song == null) return;
        double duration = AudioTiming.PlaybackDuration(_sourceDuration,
            _audio.Settings.Enabled && _audio.Stream != null, _audio.Settings.OffsetSeconds, _audio.Duration);
        _playback.SetDuration(duration);
        _timeline.MaxValue = Math.Max(duration, 0.001);
        _timeline.Editable = duration > 0;
        _timeLabel.CustomMinimumSize = new Vector2(duration >= 3600 ? 160 : 110, 32);
        _exportDialog.SetDuration(duration);
        RefreshPreview();
    }

    private void SynchronizeAudio(bool force = false) =>
        _audio.Synchronize(_playback.TimeSeconds, _playback.IsPlaying, force: force);

    public Godot.Collections.Dictionary GetAudioStatus() => new()
    {
        ["path"] = _audio.Settings.Path, ["enabled"] = _audio.Settings.Enabled,
        ["offset_seconds"] = _audio.Settings.OffsetSeconds, ["duration_seconds"] = _audio.Duration,
        ["playing"] = _audio.Playing && !_audio.StreamPaused, ["paused"] = _audio.StreamPaused,
        ["source_time"] = AudioTiming.SourceTime(_playback.TimeSeconds, _audio.Settings.OffsetSeconds),
        ["position"] = _audio.AudiblePosition, ["corrections"] = _audio.Corrections,
        ["pitch_scale"] = _audio.PitchScale
    };

    public void SetLookAhead(double seconds)
    {
        if (_busy) return;
        _visualizer.SetLookAhead(seconds);
        _settings.RefreshValues();
        RefreshPreview();
    }

    private void ApplyNoteAppearance(NoteAppearance appearance)
    {
        if (_busy) return;
        _visualizer.SetNoteAppearance(appearance);
        RefreshNoteSettings();
    }

    private void ApplyGlow(GlowSettings settings)
    {
        if (_busy) return;
        _visualizer.SetGlow(settings);
        RefreshLightSettings();
    }

    public void SetNoteAppearance(int shape, double radius, double opacity, double brightness, double emission) =>
        ApplyNoteAppearance(_visualizer.NoteAppearance with { Shape = (NoteShape)shape, CornerRadius = radius,
            Opacity = opacity, Brightness = brightness, Emission = emission });

    public void SetGlow(bool enabled, double intensity) => ApplyGlow(new GlowSettings { Enabled = enabled, Intensity = intensity });

    private void ApplyKeyboardLights(KeyboardLightSettings settings)
    {
        if (_busy) return;
        _visualizer.SetKeyboardLights(settings);
        RefreshLightSettings();
    }

    public void SetLightEffects(double keyboard, double hit, double decay, double near, double distance) =>
        ApplyKeyboardLights(_visualizer.KeyboardLights with { KeyboardEmission = keyboard, HitEmission = hit,
            HitDecay = decay, NearStrength = near, NearDistance = distance });

    public void SetContactLine(double emission, double width, double wave) =>
        ApplyContactLine(_visualizer.ContactLine with { LineEmission = emission, LineWidth = width, LineWave = wave });

    public void SetKeyboardAreaLight(double strength, double radius) =>
        ApplyKeyboardLights(_visualizer.KeyboardLights with { KeyLightStrength = strength, KeyLightRadius = radius });

    private void ApplyContactLine(ContactLineSettings settings)
    {
        if (_busy) return;
        _visualizer.SetContactLine(settings);
        RefreshLightSettings();
    }

    private void RefreshLightSettings()
    {
        _keyboardLightPanel.Refresh(_visualizer.KeyboardLights);
        _contactLinePanel.Refresh(_visualizer.ContactLine);
        _glowPanel.Refresh(_visualizer.Glow);
    }

    private void ApplyParticles(ParticleSettings settings)
    {
        if (_busy) return;
        _visualizer.SetParticles(settings);
        _particlePanel.Refresh(settings);
    }

    public void SetParticles(bool enabled, int amount, double lifetime, double speed, double size, double emission, double turbulence, bool beam, bool curves) =>
        ApplyParticles(_visualizer.Particles with { Enabled = enabled, Amount = amount, Lifetime = lifetime,
            Speed = speed, Size = size, Emission = emission, Turbulence = turbulence, Beam = beam, Curves = curves });

    public void SetBackgroundGradient(int mode, Color endColor)
    {
        if (_busy) return;
        _visualizer.SetBackground(_visualizer.Background with
        {
            Type = mode == (int)BackgroundGradient.Solid ? BackgroundType.Solid : BackgroundType.Gradient,
            Gradient = (BackgroundGradient)mode, EndColor = endColor.ToHtml()
        }, allowFallback: true);
        RefreshBackgroundSettings();
    }

    public Godot.Collections.Dictionary GetLightEffectsInfo() => new()
    {
        ["keyboard_emission"] = _visualizer.KeyboardLights.KeyboardEmission, ["hit_emission"] = _visualizer.KeyboardLights.HitEmission,
        ["hit_decay"] = _visualizer.KeyboardLights.HitDecay, ["near_strength"] = _visualizer.KeyboardLights.NearStrength,
        ["near_distance"] = _visualizer.KeyboardLights.NearDistance
    };

    private void RefreshNoteSettings() => _notePanel.Refresh(_visualizer.NoteAppearance);

    public Godot.Collections.Dictionary GetNoteAppearanceInfo() => new()
    {
        ["shape"] = _visualizer.NoteAppearance.Shape.ToString(), ["radius"] = _visualizer.NoteAppearance.CornerRadius,
        ["opacity"] = _visualizer.NoteAppearance.Opacity, ["brightness"] = _visualizer.NoteAppearance.Brightness,
        ["emission"] = _visualizer.NoteAppearance.Emission,
        ["glow_enabled"] = _visualizer.Glow.Enabled, ["glow_intensity"] = _visualizer.Glow.Intensity
    };

    public void SetPitchRange(int firstPitch, int lastPitch)
    {
        if (_busy) return;
        _visualizer.SetPitchRange(firstPitch, lastPitch);
        _settings.RefreshValues();
        RefreshPreview();
    }

    public void SetKeyboardBounds(double x, double y, double width, double height)
    {
        if (_busy) return;
        _visualizer.SetKeyboardAppearance(_visualizer.KeyboardAppearance with
            { X = x / 100, Y = y / 100, Width = width / 100, Height = height / 100 });
        _settings.RefreshValues();
        RefreshPreview();
    }

    public void SetKeyboardColor(bool black, Color color)
    {
        if (_busy) return;
        var appearance = _visualizer.KeyboardAppearance;
        _visualizer.SetKeyboardAppearance(black ? appearance with { BlackColor = color.ToHtml() }
            : appearance with { WhiteColor = color.ToHtml() });
        _settings.RefreshValues();
    }

    public bool LoadBackgroundImage(string path)
    {
        if (_busy) return false;
        try
        {
            _visualizer.SetBackground(_visualizer.Background with { Type = BackgroundType.Image, ImagePath = ProjectSettings.GlobalizePath(path) }, reloadImage: true);
            RefreshBackgroundSettings();
            _settings.CollapseSection("BackgroundPanel");
            SetStatus("已打开背景图片：" + Path.GetFileName(path));
            return true;
        }
        catch (Exception error) { SetStatus("打开背景图片失败：" + error.Message); return false; }
    }

    public void ClearBackgroundImage()
    {
        if (_busy) return;
        _visualizer.SetBackground(_visualizer.Background with { ImagePath = "" });
        RefreshBackgroundSettings();
        SetStatus("已移除背景图片。");
    }

    public void SetBackgroundColor(Color color)
    {
        if (_busy) return;
        _visualizer.SetBackground(_visualizer.Background with { Color = color.ToHtml() }, allowFallback: true);
        RefreshBackgroundSettings();
    }

    public void SetBackgroundFit(int mode)
    {
        if (_busy) return;
        _visualizer.SetBackground(_visualizer.Background with { Fit = (BackgroundFit)mode }, allowFallback: true);
        RefreshBackgroundSettings();
    }

    private void RefreshBackgroundSettings()
    {
        _videoBackground.Configure(_visualizer.Background, _exportDialog?.FfmpegPath ?? "ffmpeg", _playback.TimeSeconds);
        RefreshVideoStatus();
    }
    private string BackgroundNotice() => _visualizer.Background.Type == BackgroundType.Video &&
        _visualizer.Background.Video.Path.Length > 0 &&
        (_videoBackground.Error.Length > 0 || !File.Exists(_visualizer.Background.Video.Path))
        ? " · 背景视频不可用，已使用底色。" :
        _visualizer.BackgroundWarning.Length == 0 ? "" : " · 背景图片不可用，已使用背景色。";

    public Godot.Collections.Dictionary GetAppearanceInfo() => new()
    {
        ["keyboard_x"] = _visualizer.KeyboardAppearance.X, ["keyboard_y"] = _visualizer.KeyboardAppearance.Y,
        ["keyboard_width"] = _visualizer.KeyboardAppearance.Width, ["keyboard_height"] = _visualizer.KeyboardAppearance.Height,
        ["white_color"] = _visualizer.KeyboardAppearance.WhiteColor, ["black_color"] = _visualizer.KeyboardAppearance.BlackColor,
        ["background_color"] = _visualizer.Background.Color, ["image_path"] = _visualizer.Background.ImagePath,
        ["background_fit"] = _visualizer.Background.Fit.ToString(), ["image_available"] = _visualizer.HasBackgroundImage,
        ["background_warning"] = _visualizer.BackgroundWarning
    };

    public void SetChannelColor(int channel, Color color)
    {
        if (_busy) return;
        _visualizer.SetChannelColor(channel, color);
        _settings.RefreshColor();
        RefreshPreview();
    }

    public void SetTrackColor(int trackIndex, Color color)
    {
        if (_busy) return;
        _visualizer.SetTrackColor(trackIndex, color);
        _settings.RefreshColor();
        RefreshPreview();
    }

    public void SetColorMode(int mode)
    {
        if (_busy) return;
        _visualizer.SetColorMode(mode);
        _settings.RebuildColorTargets();
        RefreshPreview();
    }

    private void ClearHits()
    {
        _hitsSinceSeek = 0;
        _lastHit = null;
    }

    private void RefreshPreview()
    {
        if (_song == null) return;
        double time = _playback.TimeSeconds;
        _visualizer.SetTime(time);
        _videoBackground.SetTime(time);
        _scorePanel?.RefreshPosition(_visualizer.Score.Bundle, _visualizer.Score.Cursor);
        _timeline.SetValueNoSignal(time);
        bool hours = _playback.DurationSeconds >= 3600;
        _timeLabel.Text = $"{TimeText.Format(time, hours)} / {TimeText.Format(_playback.DurationSeconds, hours)}";
        _play.Icon = _playback.IsPlaying ? _pauseIcon : _playIcon;
        _play.TooltipText = (_playback.IsPlaying ? "暂停" : "播放") + "（Space）";
    }

    public void SetUiVisible(bool visible)
    {
        _uiVisible = visible;
        GetNode<Control>("Margin/Content/Body/PreviewArea/EmptyState").Visible = visible && (_song?.SourcePath.Length ?? 0) == 0;
        GetWindow().MinSize = visible ? EditorWindowMinimum : PreviewWindowMinimum;
        var content = GetNode<VBoxContainer>("Margin/Content");
        foreach (string name in new[] { "Header", "HeaderDivider", "TransportDivider", "Transport", "Status" })
            content.GetNode<Control>(name).Visible = visible;
        _settings.Visible = visible && _settingsVisible;
        var margin = GetNode<MarginContainer>("Margin");
        margin.AddThemeConstantOverride("margin_left", visible ? 18 : 0);
        margin.AddThemeConstantOverride("margin_right", 0);
        margin.AddThemeConstantOverride("margin_top", 0);
        margin.AddThemeConstantOverride("margin_bottom", visible ? 4 : 0);
    }

    public void SetSettingsVisible(bool visible)
    {
        _settingsVisible = visible;
        _settingsToggle.SetPressedNoSignal(visible);
        _settings.Visible = _uiVisible && visible;
    }

    public override void _Input(InputEvent input)
    {
        if (input is not InputEventKey key || !key.Pressed || key.Echo) return;
        if (_infoDialog.Visible || _exportDialog.Visible || _fileDialog.Visible || _projectMenu.HasOpenDialog() ||
            _quick.HasOpenPopup() || _backgroundPanel.HasOpenPopup() || _notePanel.HasOpenPopup() || _contactLinePanel.HasOpenPopup() ||
            _previewPanel.HasOpenPopup() || _scorePanel.HasOpenPopup() || _scoreSyncPanel.HasOpenPopup() || (_scoreImportDialog?.Visible ?? false) || (_recoveryDialog?.Visible ?? false) ||
            _settings.HasOpenPopup()) return;
        Control focus = GetViewport().GuiGetFocusOwner();
        if (focus is LineEdit or TextEdit && key.Keycode != Key.F10) return;
        bool handled = true;
        if (key.CtrlPressed && key.Keycode == Key.H) SetUiVisible(!_uiVisible);
        else if (key.Keycode == Key.Escape && !_uiVisible) SetUiVisible(true);
        else if (!_busy && key.Keycode == Key.F10) FitWindowToWidescreen();
        else if (key.Keycode == Key.F11)
            GetWindow().Mode = GetWindow().Mode == Window.ModeEnum.Fullscreen ? Window.ModeEnum.Windowed : Window.ModeEnum.Fullscreen;
        else if (!_busy && !key.CtrlPressed && !key.AltPressed && key.Keycode == Key.Space) TogglePlayback();
        else if (!_busy && !key.CtrlPressed && !key.AltPressed &&
            key.Keycode is Key.Left or Key.Right && focus is not Godot.Range && focus is not OptionButton)
            SetTime(_playback.TimeSeconds + (key.Keycode == Key.Left ? -1 : 1));
        else handled = false;
        if (handled) GetViewport().SetInputAsHandled();
    }

    public void StartExport(string outputPath, double start, double end, string ffmpegPath) =>
        _ = ExportAsync(new ExportSettings(outputPath, start, end), ffmpegPath);

    public void StartExportWithFormat(string outputPath, double start, double end, string ffmpegPath, int height, int fps) =>
        _ = ExportAsync(new ExportSettings(outputPath, start, end, height * 16 / 9, height, fps), ffmpegPath);

    private void ApplyPreviewSettings(PreviewSettings settings)
    {
        if (_busy) return;
        settings.Validate(); _previewSettings = settings;
        SetRenderSize(new Vector2I(settings.Width, settings.Height));
        _visualizer.Scale = new Vector2(settings.Width / 1920f, settings.Height / 1080f);
        Engine.MaxFps = settings.FramesPerSecond;
        _previewPanel.Refresh(settings);
        RefreshPreview();
    }

    public void SetPreviewFormat(int height, int fps) => ApplyPreviewSettings(new PreviewSettings { Height = height, FramesPerSecond = fps });

    public void CancelExport() => _exportCancellation?.Cancel();

    private async Task ExportAsync(ExportSettings settings, string ffmpegPath)
    {
        if (_busy || _song == null) return;
        bool wasPlaying = _playback.IsPlaying;
        Vector2I oldSize = _viewport.Size;
        Vector2 oldScale = _visualizer.Scale;
        int oldFps = Engine.MaxFps;
        var oldVsync = DisplayServer.WindowGetVsyncMode();
        var video = _visualizer.Background.Type == BackgroundType.Video ? _visualizer.Background : null;
        _playback.Pause();
        SynchronizeAudio();
        _exportCancellation = new CancellationTokenSource();
        _exportOutput = settings.OutputPath;
        _exportError = "";
        _exportProgress = new("checking", 0, 0);
        _exportWatch.Restart();
        SetExportBusy(true);
        SetStatus("正在导出视频…");
        try
        {
            settings.Validate();
            if (settings.EndSeconds > _playback.DurationSeconds)
                throw new ArgumentException("导出结束时间不能超过当前播放时长。");
            _exportFrameTime = settings.StartSeconds;
            _exportDialog.Begin(settings, ffmpegPath);
            if (video != null)
            {
                await _videoBackground.BeginExportAsync(settings, ffmpegPath, _exportCancellation.Token);
            }
            Engine.MaxFps = 0;
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            SetRenderSize(new Vector2I(settings.Width, settings.Height));
            _visualizer.Scale = new Vector2(settings.Width / 1920f, settings.Height / 1080f);
            await VideoExporter.ExportAsync(settings, ffmpegPath, CaptureFrameAsync,
                progress => { _exportProgress = progress; _exportDialog.Report(progress); }, _exportCancellation.Token,
                _audio.GetExportSettings());
            SetStatus($"已导出 {settings.FrameCount} 帧（{TimeText.Format(settings.FrameCount / (double)settings.FramesPerSecond)}）：{settings.OutputPath}");
            _exportDialog.Finish("导出完成：" + settings.OutputPath);
            GD.Print("[M3] Export completed: " + settings.OutputPath);
        }
        catch (OperationCanceledException)
        {
            _exportProgress = _exportProgress with { Stage = "cancelled" };
            SetStatus("导出已取消。");
            _exportDialog.Finish(_status.Text);
        }
        catch (Exception error)
        {
            _exportError = error.Message;
            _exportProgress = _exportProgress with { Stage = "failed" };
            SetStatus("导出失败：" + error.Message);
            _exportDialog.Finish(_status.Text);
            GD.PushWarning("[M3] " + error.Message);
        }
        finally
        {
            _exportWatch.Stop();
            if (video != null) await _videoBackground.EndExportAsync();
            _exportCancellation.Dispose();
            _exportCancellation = null;
            if (GodotObject.IsInstanceValid(this) && IsInsideTree())
            {
                SetRenderSize(oldSize);
                if (video != null) _videoBackground.Resume(_playback.TimeSeconds);
                _visualizer.Scale = oldScale;
                Engine.MaxFps = oldFps;
                DisplayServer.WindowSetVsyncMode(oldVsync);
                SetExportBusy(false);
                if (wasPlaying) _playback.Play();
                SynchronizeAudio(force: true);
                RefreshPreview();
                if (_quitAfterExport) { SaveRecoveryNow(); GetTree().Quit(); }
            }
        }
    }

    private async Task<Image> CaptureFrameAsync(double seconds)
    {
        _exportFrameTime = seconds;
        if (_visualizer.Background.Type == BackgroundType.Video)
            await _videoBackground.PresentExportFrameAsync(seconds, _exportCancellation.Token);
        _visualizer.SetTime(seconds);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        return _viewport.GetTexture().GetImage();
    }

    private void SetRenderSize(Vector2I size)
    {
        _hdrViewport.Size = _viewport.Size = size;
        _present.Size = size;
        _visualizer.Score.SetRenderWidth(size.X);
    }

    private void SetExportBusy(bool busy)
    {
        _busy = busy;
        _projectMenu.Disabled = busy;
        _timeline.Editable = !busy && _playback.DurationSeconds > 0;
        _settings.SetBusy(busy);
        _audioPanel.SetBusy(busy);
        _backgroundPanel.SetBusy(busy);
        _notePanel.SetBusy(busy);
        _quick.SetBusy(busy);
        _keyboardLightPanel.SetBusy(busy);
        _contactLinePanel.SetBusy(busy);
        _glowPanel.SetBusy(busy);
        GetNode<Button>("Margin/Content/Body/PreviewArea/EmptyState/Content/OpenMidi").Disabled = busy;
        _particlePanel.SetBusy(busy);
        _previewPanel.SetBusy(busy);
        _scorePanel.SetBusy(busy);
        _scoreSyncPanel.SetBusy(busy);
        foreach (string path in new[] { "Header/Export", "Transport/Play", "Transport/Stop",
            "Transport/Start", "Transport/FirstNote" })
            GetNode<Button>("Margin/Content/" + path).Disabled = busy;
    }

    public Godot.Collections.Dictionary GetExportStatus() => new()
    {
        ["busy"] = _busy, ["stage"] = _exportProgress.Stage,
        ["frames"] = _exportProgress.Frames, ["total_frames"] = _exportProgress.TotalFrames,
        ["frame_time"] = _exportFrameTime, ["output"] = _exportOutput,
        ["error"] = _exportError, ["elapsed_seconds"] = _exportWatch.Elapsed.TotalSeconds
    };

    public override void _ExitTree()
    {
        CancelScoreImport();
        _exportCancellation?.Cancel();
        GetWindow().FilesDropped -= HandleFilesDropped;
    }

    public Godot.Collections.Dictionary GetSongInfo() => _song == null ? new() : new()
    {
        ["path"] = _song.SourcePath,
        ["midi_from_score"] = _midiFromScore,
        ["project_path"] = _projectPath,
        ["format"] = _song.Format,
        ["tracks"] = _song.Tracks.Length,
        ["notes"] = _song.Notes.Length,
        ["duration_seconds"] = _song.DurationSeconds,
        ["playback_duration_seconds"] = _playback.DurationSeconds,
        ["tempo_segments"] = _song.TempoChanges.Length,
        ["time"] = _playback.TimeSeconds,
        ["ui_visible"] = _uiVisible,
        ["settings_visible"] = _settings.Visible,
        ["first_pitch"] = _visualizer.FirstPitch,
        ["last_pitch"] = _visualizer.LastPitch,
        ["is_playing"] = _playback.IsPlaying,
        ["look_ahead_seconds"] = _visualizer.LookAheadSeconds,
        ["color_mode"] = _visualizer.GetColorMode() == (int)NoteColorMode.Track ? "track" : "channel",
        ["hits_since_seek"] = _hitsSinceSeek,
        ["last_hit_pitch"] = _lastHit?.Pitch ?? -1,
        ["last_hit_time"] = _lastHit?.StartSeconds ?? -1,
        ["visible_notes"] = _visualizer.GetVisibleNoteCount(),
        ["active_keys"] = _visualizer.GetActiveKeyCount()
    };
}
