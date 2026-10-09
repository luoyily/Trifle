using Godot;
using System;
using System.IO;
using Trifle.Persistence;

namespace Trifle.App;

public partial class Main
{
    private double _autosaveElapsed;
    private string _recoveryPath;
    private ConfirmationDialog _recoveryDialog;
    private RecoveryData _recovery;
    private bool _recoveryPending;

    private void InitializeRecovery()
    {
        _recoveryPath = ProjectSettings.GlobalizePath("user://recovery.trifle.json");
        _recoveryDialog = new ConfirmationDialog { Title = AppLocale.T("恢复上次会话"), OkButtonText = AppLocale.T("恢复"), CancelButtonText = AppLocale.T("忽略") };
        AddChild(_recoveryDialog);
        AppLocale.LanguageChanged += () =>
        {
            _recoveryDialog.Title = AppLocale.T("恢复上次会话");
            _recoveryDialog.OkButtonText = AppLocale.T("恢复");
            _recoveryDialog.CancelButtonText = AppLocale.T("忽略");
        };
        _recoveryDialog.Confirmed += () => RestoreRecovery();
        _recoveryDialog.Canceled += () =>
        {
            _recoveryPending = false; _recovery = null;
            try { File.Delete(_recoveryPath); }
            catch (Exception error) { SetStatus(string.Format(AppLocale.T("清理自动保存失败：{0}"), error.Message)); }
        };
        if (!File.Exists(_recoveryPath)) return;
        try
        {
            _recovery = ProjectStorage.LoadRecovery(_recoveryPath);
            _recoveryPending = true;
            _recoveryDialog.DialogText = string.Format(AppLocale.T("发现 {0} 的自动保存。\n{1}\n恢复后暂停，不会覆盖已保存项目。"),
                _recovery.SavedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), Path.GetFileName(_recovery.Project.MidiPath));
            _recoveryDialog.PopupCentered(new Vector2I(550, 210));
        }
        catch (Exception error) { SetStatus(string.Format(AppLocale.T("自动保存无法读取，请打开已有项目：{0}"), error.Message)); }
    }

    private void TickAutosave(double delta)
    {
        _autosaveElapsed += delta;
        if (_autosaveElapsed < 60 || _busy || _recoveryPending) return;
        _autosaveElapsed = 0;
        SaveRecoveryNow();
    }

    public bool SaveRecoveryNow()
    {
        if (_recoveryPath == null || _busy || _recoveryPending || _song == null || _song.SourcePath.Length == 0) return false;
        try
        {
            ProjectStorage.SaveRecovery(_recoveryPath, new RecoveryData { ProjectPath = _projectPath, Project = CaptureProject() });
            return true;
        }
        catch (Exception error) { SetStatus(string.Format(AppLocale.T("自动保存失败：{0}"), error.Message)); return false; }
    }

    public bool RestoreRecovery()
    {
        if (_recovery == null || _busy) return false;
        try
        {
            var data = _recovery.Project;
            // Reuse the normal resource replacement flow if MIDI or audio moved.
            _pendingProject = data;
            _pendingProjectPath = _recoveryPath;
            _recoveredProjectPath = _recovery.ProjectPath;
            _recoveryPending = false;
            _recoveryDialog.Hide();
            bool result = ContinueProjectLoad();
            if (result) SetStatus(AppLocale.T("已恢复自动保存（暂停）。请检查后手动保存项目。") + BackgroundNotice());
            return result;
        }
        catch (Exception error) { SetStatus(string.Format(AppLocale.T("恢复失败：{0}"), error.Message)); _recoveryPending = false; return false; }
    }

    private string _recoveredProjectPath;
}
