using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Trifle.Midi;

namespace Trifle.Score;

public sealed record ScoreImportResult(MuseScoreBundle Bundle, bool FromCache);

// Conversion has no Godot dependencies and never overwrites the source score.
public sealed class MuseScoreImporter
{
    private readonly string _cacheDirectory;
    public MuseScoreImporter(string cacheDirectory) => _cacheDirectory = Path.GetFullPath(cacheDirectory);
    public static bool IsScoreFile(string path) => Path.GetExtension(path).Equals(".mscz", StringComparison.OrdinalIgnoreCase);

    public static string FindExecutable()
    {
        var roots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs") };
        foreach (string root in roots.Where(Directory.Exists))
        {
            string[] folders;
            try { folders = Directory.GetDirectories(root, "MuseScore*"); }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException) { continue; }
            foreach (string folder in folders)
                foreach (string file in ExecutablesIn(folder))
                    if (File.Exists(file)) return file;
        }
        if (OperatingSystem.IsWindows())
            foreach (string file in RegistryCandidates())
                if (File.Exists(file)) return file;
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            string file = Path.Combine(folder.Trim('"'), "MuseScore4.exe");
            if (File.Exists(file)) return Path.GetFullPath(file);
        }
        return "";
    }

    private static IEnumerable<string> ExecutablesIn(string directory)
    {
        yield return Path.Combine(directory, "bin", "MuseScore4.exe");
        yield return Path.Combine(directory, "MuseScore4.exe");
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> RegistryCandidates()
    {
        var candidates = new List<string>();
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var app = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\MuseScore4.exe");
                if (app?.GetValue("") is string appPath) candidates.Add(appPath.Trim('"'));
                using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall == null) continue;
                foreach (string name in uninstall.GetSubKeyNames())
                {
                    using var entry = uninstall.OpenSubKey(name);
                    if (entry?.GetValue("DisplayName") is not string title || !title.StartsWith("MuseScore", StringComparison.OrdinalIgnoreCase)) continue;
                    if (entry.GetValue("InstallLocation") is string folder && folder.Length > 0) candidates.AddRange(ExecutablesIn(folder));
                    if (entry.GetValue("DisplayIcon") is string icon)
                    {
                        int comma = icon.LastIndexOf(',');
                        candidates.Add((comma >= 0 ? icon[..comma] : icon).Trim().Trim('"'));
                    }
                }
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        return candidates;
    }

    private string CachePath(string source)
    {
        using var file = File.OpenRead(source);
        return Path.Combine(_cacheDirectory, "media-v1-" + Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant() + ".json");
    }

    private static MuseScoreBundle ReadValidated(string path)
    {
        var bundle = MuseScoreBundle.Read(path);
        if (bundle.Midi.Length == 0) throw new InvalidDataException("乐谱数据包缺少同源 MIDI。");
        using var midi = new MemoryStream(bundle.Midi);
        _ = MidiImporter.Read(midi, path);
        return bundle;
    }

    public bool TryReadCached(string source, out MuseScoreBundle bundle)
    {
        string path = CachePath(source);
        bundle = null;
        if (!File.Exists(path)) return false;
        try { bundle = ReadValidated(path); return true; }
        catch (Exception e) when (e is not OutOfMemoryException) { return false; }
    }

    public async Task<ScoreImportResult> ImportAsync(string source, string executable, CancellationToken cancellation = default)
    {
        source = Path.GetFullPath(source);
        if (!IsScoreFile(source)) throw new ArgumentException("请选择 .mscz 乐谱文件。");
        string cache = await Task.Run(() => CachePath(source), cancellation);
        if (File.Exists(cache))
        {
            try
            {
                var bundle = await Task.Run(() => ReadValidated(cache), cancellation);
                cancellation.ThrowIfCancellationRequested();
                return new(bundle, true);
            }
            catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException) { }
        }
        cancellation.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            throw new FileNotFoundException("未找到 MuseScore，请在“乐谱 → 导入配置”选择 MuseScore4.exe。");
        Directory.CreateDirectory(_cacheDirectory);
        string temporary = Path.Combine(_cacheDirectory, "convert-" + Guid.NewGuid().ToString("N") + ".json");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardError = true, RedirectStandardOutput = true
            };
            if (!OperatingSystem.IsWindows()) start.Environment["QT_QPA_PLATFORM"] = "offscreen";
            start.ArgumentList.Add(source); start.ArgumentList.Add("--score-media");
            start.ArgumentList.Add("-o"); start.ArgumentList.Add(temporary);
            using var process = Process.Start(start) ?? throw new IOException("MuseScore 无法启动。");
            Task<string> output = process.StandardOutput.ReadToEndAsync(), errors = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                await Task.WhenAll(output, errors);
                if (!cancellation.IsCancellationRequested) throw new TimeoutException("MuseScore 转换超时，请检查该谱面能否正常打开。");
                throw;
            }
            await Task.WhenAll(output, errors);
            deadline.Token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0 || !File.Exists(temporary))
            {
                string detail = (await errors).Trim();
                if (detail.Length == 0) detail = (await output).Trim();
                if (detail.Length > 600) detail = detail[^600..];
                throw new IOException($"MuseScore 转换失败（退出码 {process.ExitCode}），请检查程序路径及谱面文件。" + (detail.Length == 0 ? "" : "\n" + detail));
            }
            var bundle = await Task.Run(() => ReadValidated(temporary), deadline.Token);
            if (await Task.Run(() => CachePath(source), deadline.Token) != cache)
                throw new IOException("转换期间谱面文件发生变化，请重新导入。");
            deadline.Token.ThrowIfCancellationRequested();
            File.Move(temporary, cache, overwrite: true);
            return new(bundle, false);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new TimeoutException("MuseScore 转换超时，请检查该谱面能否正常打开。"); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
