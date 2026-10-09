using Godot;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Trifle.Score;

namespace Trifle.App;

public partial class Main
{
    private MuseScoreImporter _scoreImporter;
    private string _museScoreConfiguredPath = "", _museScoreResolvedPath = "";
    private string _scoreImportStage = "idle", _scoreImportSource = "", _scoreImportError = "";
    private bool _scoreImportCached;
    private CancellationTokenSource _scoreImportCancellation;
    private Window _scoreImportDialog;

    private void InitializeScoreImporter()
    {
        _scoreImporter = new MuseScoreImporter(ProjectSettings.GlobalizePath("user://score_cache"));
        var config = new ConfigFile();
        if (File.Exists(ProjectSettings.GlobalizePath("user://musescore.cfg")) && config.Load("user://musescore.cfg") == Error.Ok)
            _museScoreConfiguredPath = config.GetValue("musescore", "path", "").AsString();
        _museScoreResolvedPath = _museScoreConfiguredPath.Length == 0 ? MuseScoreImporter.FindExecutable() : _museScoreConfiguredPath;
        _scorePanel.RefreshImporter(_museScoreConfiguredPath, _museScoreResolvedPath);
        _scorePanel.MuseScorePathChanged += path => ConfigureMuseScore(path);
        _scorePanel.DetectMuseScoreRequested += () => ConfigureMuseScore("");

        _scoreImportDialog = new Window { Visible = false, Transient = true, Exclusive = true,
            Unresizable = true, MinSize = new Vector2I(480, 190) };
        AddChild(_scoreImportDialog);
        AppLocale.BindTitle(_scoreImportDialog, "导入乐谱");
        var body = new PanelContainer { Name = "Body" };
        _scoreImportDialog.AddChild(body);
        body.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var margin = new MarginContainer { Name = "Margin" };
        body.AddChild(margin);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 20);
        var content = new VBoxContainer { Name = "Content" }; margin.AddChild(content);
        content.AddThemeConstantOverride("separation", 14);
        content.AddChild(new Label { Name = "Source", Text = AppLocale.T("乐谱"), TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis });
        content.AddChild(new Label { Text = AppLocale.T("正在读取或转换乐谱…"), ThemeTypeVariation = "MutedLabel" });
        content.AddChild(new ProgressBar { Indeterminate = true, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 10) });
        var cancel = new Button { Text = AppLocale.T("取消"), ThemeTypeVariation = "QuietButton" }; content.AddChild(cancel);
        cancel.Pressed += CancelScoreImport;
        _scoreImportDialog.CloseRequested += CancelScoreImport;
    }

    public bool ConfigureMuseScore(string path)
    {
        if (_busy) return false;
        try
        {
            path = path.Trim().Trim('"');
            if (path.Length > 0)
            {
                path = Path.GetFullPath(ProjectSettings.GlobalizePath(path));
                if (!File.Exists(path)) throw new FileNotFoundException(AppLocale.T("所选 MuseScore 程序不存在。"));
            }
            string resolved = path.Length == 0 ? MuseScoreImporter.FindExecutable() : path;
            var config = new ConfigFile(); config.SetValue("musescore", "path", path);
            if (config.Save("user://musescore.cfg") != Error.Ok) throw new IOException(AppLocale.T("无法保存本机 MuseScore 路径。"));
            _museScoreConfiguredPath = path; _museScoreResolvedPath = resolved;
            _scorePanel.RefreshImporter(path, resolved);
            SetStatus(resolved.Length > 0 ? AppLocale.T("MuseScore 已就绪，可直接选择 .mscz 乐谱。") : AppLocale.T("未检测到 MuseScore，请手动选择程序；也可直接载入 JSON 数据包。"));
            return true;
        }
        catch (Exception error)
        {
            SetStatus(string.Format(AppLocale.T("MuseScore 配置失败：{0}"), error.Message));
            _scorePanel.RefreshImporter(_museScoreConfiguredPath, _museScoreResolvedPath);
            return false;
        }
    }

    public bool LoadScoreFile(string path)
    {
        if (_busy) return false;
        if (!MuseScoreImporter.IsScoreFile(path)) return LoadScoreBundleFile(path);
        try
        {
            path = Path.GetFullPath(ProjectSettings.GlobalizePath(path));
            _ = ImportScoreAsync(path, bundle => ActivateScore(bundle, ReadScoreMidi(bundle, path), path));
            return true;
        }
        catch (Exception error) { ScoreImportFailed(error); return false; }
    }

    private async Task<bool> ImportScoreAsync(string path, Action<MuseScoreBundle> apply)
    {
        bool wasPlaying = _playback.IsPlaying;
        PausePlayback();
        _scoreImportSource = path; _scoreImportError = ""; _scoreImportCached = false; _scoreImportStage = "converting";
        _scoreImportCancellation = new CancellationTokenSource();
        SetExportBusy(true);
        _scoreImportDialog.GetNode<Label>("Body/Margin/Content/Source").Text = Path.GetFileName(path);
        _scoreImportDialog.PopupCentered(new Vector2I(480, 190));
        SetStatus(string.Format(AppLocale.T("正在导入乐谱 · {0}"), Path.GetFileName(path)));
        ScoreImportResult result = null;
        try { result = await _scoreImporter.ImportAsync(path, _museScoreResolvedPath, _scoreImportCancellation.Token); }
        catch (OperationCanceledException)
        {
            _scoreImportStage = "cancelled";
            if (GodotObject.IsInstanceValid(this) && IsInsideTree()) SetStatus(AppLocale.T("已取消乐谱导入，保留当前曲目。"));
        }
        catch (Exception error) { ScoreImportFailed(error); }
        finally
        {
            _scoreImportCancellation.Dispose(); _scoreImportCancellation = null;
            if (GodotObject.IsInstanceValid(this) && IsInsideTree())
            {
                _scoreImportDialog.Hide(); SetExportBusy(false);
            }
        }
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree()) return false;
        if (result != null)
        {
            try
            {
                apply(result.Bundle);
                _scoreImportCached = result.FromCache; _scoreImportStage = "completed";
                if (result.FromCache) SetStatus(_status.Text + AppLocale.T(" · 已复用缓存"));
            }
            catch (Exception error) { ScoreImportFailed(error); result = null; }
        }
        if (result == null && wasPlaying) PlayPlayback();
        if (_quitAfterExport) { SaveRecoveryNow(); GetTree().Quit(); }
        return result != null;
    }

    private void ScoreImportFailed(Exception error)
    {
        _scoreImportStage = "failed"; _scoreImportError = _scoreError = error.Message;
        if (GodotObject.IsInstanceValid(this) && IsInsideTree()) SetStatus(string.Format(AppLocale.T("导入乐谱失败：{0}"), error.Message));
    }

    public void CancelScoreImport() => _scoreImportCancellation?.Cancel();

    private async Task ImportDroppedScoreAsync(string path, string audio, string image, string video, string midi = null)
    {
        path = Path.GetFullPath(ProjectSettings.GlobalizePath(path));
        Trifle.Midi.MidiSong externalSong = null;
        try { if (midi != null) externalSong = ReadMidiFile(midi); }
        catch (Exception error) { SetStatus(string.Format(AppLocale.T("载入外部 MIDI 失败：{0}"), error.Message)); return; }
        if (!await ImportScoreAsync(path, bundle => ActivateScore(bundle, ReadScoreMidi(bundle, path), path, externalSong))) return;
        if (audio != null) LoadAudioFile(audio);
        if (image != null) LoadBackgroundImage(image);
        if (video != null) LoadBackgroundVideo(video);
    }

    public Godot.Collections.Dictionary GetScoreImportStatus() => new()
    {
        ["busy"] = _scoreImportCancellation != null, ["stage"] = _scoreImportStage,
        ["source"] = _scoreImportSource, ["error"] = _scoreImportError, ["cached"] = _scoreImportCached,
        ["configured_path"] = _museScoreConfiguredPath, ["resolved_path"] = _museScoreResolvedPath
    };
}
