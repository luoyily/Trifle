using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Trifle.Export;

// Path is what goes into the text box; Resolved is the absolute location when known.
public sealed record FfmpegProbe(string Path, string Resolved, string Version, bool Found);

// Finds a usable ffmpeg without blocking the UI: a candidate only counts once it answers
// -version within a few seconds, so a stale path or a half-installed binary is skipped.
public static class FfmpegLocator
{
    public static readonly string BundledPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    private static readonly string[] InstallRoots =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Microsoft\WinGet\Links\ffmpeg.exe"),
        @"C:\Program Files\FFmpeg\bin\ffmpeg.exe",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"scoop\shims\ffmpeg.exe"),
        @"C:\ProgramData\chocolatey\bin\ffmpeg.exe",
        @"C:\ffmpeg\bin\ffmpeg.exe",
    };

    // The configured value is tried first and kept when it works — a bare "ffmpeg" resolved
    // through PATH must not be rewritten. Bundled and common install roots follow as fallbacks.
    public static async Task<FfmpegProbe> DetectAsync(string current, CancellationToken token = default)
    {
        string requested = current?.Trim() ?? "";
        string requestedResolved = requested.Length > 0 && Path.GetDirectoryName(requested).Length == 0
            ? await WhereAsync(requested, token).ConfigureAwait(false) : "";
        IEnumerable<string> candidates = new[] { requested }.Where(path => path.Length > 0)
            .Concat(new[] { BundledPath }).Concat(InstallRoots)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (string candidate in candidates)
        {
            string resolved = string.Equals(candidate, requested, StringComparison.OrdinalIgnoreCase)
                ? requestedResolved : "";
            var probe = await ProbeAsync(candidate, resolved, token).ConfigureAwait(false);
            if (probe.Found) return probe;
        }
        return new FfmpegProbe(requested, "", "", false);
    }

    public static async Task<FfmpegProbe> ProbeAsync(string candidate, string resolved = "", CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return new FfmpegProbe("", "", "", false);
        // A bare name goes through CreateProcess PATH resolution, so existence is only checkable with a directory part.
        if (Path.GetDirectoryName(candidate).Length > 0 && !File.Exists(candidate))
            return new FfmpegProbe(candidate, resolved, "", false);
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = candidate,
                    Arguments = "-hide_banner -version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };
            if (!process.Start()) return new FfmpegProbe(candidate, resolved, "", false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(Timeout);
            string first;
            try
            {
                first = await process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false) ?? "";
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                KillQuietly(process);
                return new FfmpegProbe(candidate, resolved, "", false);
            }
            bool found = process.ExitCode == 0;
            return new FfmpegProbe(candidate, resolved.Length > 0 ? resolved : candidate,
                found ? VersionLine(first) : "", found);
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception)
        {
            return new FfmpegProbe(candidate, resolved, "", false);
        }
    }

    // Turns a bare name into the absolute path PATH resolution would pick, for display only.
    private static async Task<string> WhereAsync(string name, CancellationToken token)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "where.exe"),
                    Arguments = name,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };
            if (!process.Start()) return "";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(Timeout);
            string first;
            try
            {
                first = await process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false) ?? "";
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                KillQuietly(process);
                return "";
            }
            first = first?.Trim() ?? "";
            return process.ExitCode == 0 && first.Length > 0 && File.Exists(first) ? first : "";
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception)
        {
            return "";
        }
    }

    private static void KillQuietly(Process process)
    {
        try { if (!process.HasExited) process.Kill(); }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception) { }
    }

    // "ffmpeg version 7.1.1-essentials_build-www.gyan.dev Copyright (c) ..." -> up to the copyright notice.
    private static string VersionLine(string line)
    {
        line = line.Trim();
        int cut = line.IndexOf(" Copyright", StringComparison.Ordinal);
        return cut > 0 ? line[..cut] : line;
    }
}
