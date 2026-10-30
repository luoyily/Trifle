using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Trifle.Video;

namespace Trifle.Export;

// Path is what goes into the text box; Resolved is the absolute location when known.
// Ffprobe is only usable when it sits next to Resolved: the background video probes its
// metadata there, so a lone ffmpeg.exe counts as usable for export but incomplete overall.
public sealed record FfmpegProbe(string Path, string Resolved, string Version, bool Found,
    string Ffprobe = "", bool FfprobeFound = false);

// Finds a usable ffmpeg without blocking the UI: PATH and an explicitly chosen file are the
// only sources, and a candidate only counts once it answers -version within a few seconds.
// A bare name is resolved to the absolute location PATH would pick, so ffprobe is looked up
// next to the exact binary that will run — never through a second, independent PATH search.
public static class FfmpegLocator
{
    public const string DefaultName = "ffmpeg";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    // The configured value is tried as-is — a bare "ffmpeg" resolved through PATH must not be
    // rewritten. Resolved stays empty for an explicit path, which is already absolute.
    public static async Task<FfmpegProbe> DetectAsync(string current, CancellationToken token = default)
    {
        string requested = string.IsNullOrWhiteSpace(current) ? DefaultName : current.Trim();
        bool bare = Path.GetDirectoryName(requested).Length == 0;
        string resolved = bare ? await WhereAsync(requested, token).ConfigureAwait(false) : "";
        var probe = await ProbeAsync(requested, resolved, token).ConfigureAwait(false);
        if (!probe.Found) return probe;
        string ffprobe = VideoMetadata.ProbeExecutable(probe.Resolved.Length > 0 ? probe.Resolved : requested);
        var ffprobeProbe = await ProbeAsync(ffprobe, ffprobe, token).ConfigureAwait(false);
        return probe with { Ffprobe = ffprobe, FfprobeFound = ffprobeProbe.Found };
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
