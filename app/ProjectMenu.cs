using Godot;
using System;
using System.IO;

namespace Trifle.App;

public enum ProjectFileAction { OpenProject, SaveProject, SaveProjectAs, OpenPreset, SavePreset, RelinkMidi, RelinkAudio, WithoutAudio, RelinkScore, About }

public partial class ProjectMenu : MenuButton
{
    public event Action<ProjectFileAction, string> FileRequested;
    public event Action RelinkCancelled;
    public event Action AboutRequested;
    private FileDialog _files;
    private ConfirmationDialog _missing;
    private ProjectFileAction _action;
    private string _projectPath = "";
    private Button _withoutAudio;
    private bool _missingAudio;
    private bool _missingScore;
    private bool _separateScore;

    public override void _Ready()
    {
        var popup = GetPopup();
        BuildMenu(popup);
        popup.IdPressed += id =>
        {
            var action = (ProjectFileAction)id;
            if (action == ProjectFileAction.About) { AboutRequested?.Invoke(); return; }
            if (action == ProjectFileAction.SaveProject && _projectPath.Length > 0)
                FileRequested?.Invoke(action, _projectPath);
            else ChooseFile(action);
        };
        _files = GetNode<FileDialog>("Files");
        _files.FileSelected += path => FileRequested?.Invoke(_action, path);
        _files.Canceled += () =>
        {
            if (_action is ProjectFileAction.RelinkMidi or ProjectFileAction.RelinkAudio or ProjectFileAction.RelinkScore) RelinkCancelled?.Invoke();
        };
        _missing = GetNode<ConfirmationDialog>("MissingMidi");
        _missing.GetLabel().AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _missing.Confirmed += () => ChooseFile(_missingAudio ? ProjectFileAction.RelinkAudio :
            _separateScore ? ProjectFileAction.RelinkScore : ProjectFileAction.RelinkMidi);
        _missing.Canceled += () => RelinkCancelled?.Invoke();
        _withoutAudio = _missing.AddButton(AppLocale.T("不使用音频"), true, "without_audio");
        _withoutAudio.Pressed += () => FileRequested?.Invoke(ProjectFileAction.WithoutAudio, "");
        // The popup is built once; rebuild it when the locale changes. The missing-file dialog's
        // Title / buttons are retranslated on every RequestReplacement call.
        AppLocale.LanguageChanged += RefreshMenuTexts;
    }

    private void RefreshMenuTexts()
    {
        BuildMenu(GetPopup());
        if (_withoutAudio != null) _withoutAudio.Text = AppLocale.T("不使用音频");
    }

    private void BuildMenu(PopupMenu popup)
    {
        popup.Clear();
        popup.AddItem(AppLocale.T("打开项目…"), (int)ProjectFileAction.OpenProject);
        popup.AddItem(AppLocale.T("保存项目"), (int)ProjectFileAction.SaveProject);
        popup.AddItem(AppLocale.T("项目另存为…"), (int)ProjectFileAction.SaveProjectAs);
        popup.AddSeparator();
        popup.AddItem(AppLocale.T("加载视觉预设…"), (int)ProjectFileAction.OpenPreset);
        popup.AddItem(AppLocale.T("保存视觉预设…"), (int)ProjectFileAction.SavePreset);
        popup.AddSeparator();
        popup.AddItem(AppLocale.T("关于 Trifle…"), (int)ProjectFileAction.About);
    }

    public void SetProjectPath(string path) => _projectPath = path;
    public void ClearMissingRequest() => _missing.Hide();
    public bool HasOpenDialog() => GetPopup().Visible || _files.Visible || _missing.Visible;

    public void RequestMidiReplacement(string message, bool score = false) => RequestReplacement(message, false, score);
    public void RequestAudioReplacement(string message) => RequestReplacement(message, true);
    public void RequestScoreReplacement(string message)
    {
        RequestReplacement(message, false, true);
        _separateScore = true;
    }

    private void RequestReplacement(string message, bool audio, bool score = false)
    {
        _missingAudio = audio;
        _missingScore = score;
        _separateScore = false;
        _missing.Title = audio ? AppLocale.T("项目音频无法载入") : score ? AppLocale.T("项目乐谱无法载入") : AppLocale.T("项目 MIDI 无法载入");
        _missing.OkButtonText = audio ? AppLocale.T("重新选择音频…") : score ? AppLocale.T("重新选择乐谱…") : AppLocale.T("重新选择 MIDI…");
        _withoutAudio.Visible = audio;
        _missing.DialogText = message + "\n" + AppLocale.T("重新选择后继续；取消会保留当前项目。");
        _missing.PopupCentered();
    }

    private void ChooseFile(ProjectFileAction action)
    {
        _action = action;
        bool preset = action is ProjectFileAction.OpenPreset or ProjectFileAction.SavePreset;
        bool save = action is ProjectFileAction.SaveProject or ProjectFileAction.SaveProjectAs or ProjectFileAction.SavePreset;
        _files.FileMode = save ? FileDialog.FileModeEnum.SaveFile : FileDialog.FileModeEnum.OpenFile;
        _files.Title = action switch
        {
            ProjectFileAction.OpenProject => AppLocale.T("打开 Trifle 项目"),
            ProjectFileAction.OpenPreset => AppLocale.T("加载视觉预设"),
            ProjectFileAction.SavePreset => AppLocale.T("保存视觉预设"),
            ProjectFileAction.RelinkMidi => _missingScore ? AppLocale.T("重新选择项目的乐谱数据包") : AppLocale.T("重新选择项目的 MIDI 文件"),
            ProjectFileAction.RelinkAudio => AppLocale.T("重新选择项目的音频"),
            ProjectFileAction.RelinkScore => AppLocale.T("重新选择项目的乐谱"),
            _ => AppLocale.T("保存 Trifle 项目")
        };
        _files.Filters = action == ProjectFileAction.RelinkScore ? new[] { "*.mscz,*.json ; " + AppLocale.T("MuseScore 乐谱 / 数据包") }
            : action == ProjectFileAction.RelinkMidi
            ? (_missingScore ? new[] { "*.mscz,*.json ; " + AppLocale.T("MuseScore 乐谱 / 数据包") } : new[] { "*.mid,*.midi ; " + AppLocale.T("MIDI 文件") })
            : action == ProjectFileAction.RelinkAudio ? new[] { "*.ogg,*.mp3,*.wav ; " + AppLocale.T("音频（OGG / MP3 / WAV）"), "*.ogg ; " + AppLocale.T("OGG / Vorbis 音频"), "*.mp3 ; " + AppLocale.T("MP3 音频"), "*.wav ; " + AppLocale.T("WAV 音频") }
            : preset ? new[] { "*.trifle-preset.json ; " + AppLocale.T("Trifle 视觉预设") }
            : new[] { "*.trifle.json ; " + AppLocale.T("Trifle 项目") };
        _files.CurrentFile = save ? (preset ? "visual.trifle-preset.json" : "project.trifle.json") : "";
        if (_projectPath.Length > 0)
        {
            _files.CurrentDir = Path.GetDirectoryName(_projectPath);
            if (save && !preset) _files.CurrentFile = Path.GetFileName(_projectPath);
        }
        _files.PopupCenteredRatio(0.7f);
    }
}
