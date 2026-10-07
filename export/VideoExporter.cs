using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Trifle.Audio;

namespace Trifle.Export;

public sealed record ExportProgress(string Stage, int Frames, int TotalFrames);

public static class VideoExporter
{
    public static async Task ExportAsync(ExportSettings settings, string ffmpegPath,
        Func<double, Task<Image>> captureFrame, Action<ExportProgress> report, CancellationToken token,
        AudioExportSettings audio = null)
    {
        settings.Validate();
        audio?.Validate();
        if (string.IsNullOrWhiteSpace(ffmpegPath)) throw new ArgumentException("请填写 FFmpeg 路径或名称。");
        int total = settings.FrameCount;
        var encoding = settings.EffectiveEncoding;
        report(new ExportProgress("checking", 0, total));
        await RunFfmpegAsync(ffmpegPath, new[] { "-hide_banner", "-version" }, _ => { }, token);
        string diskRoot = Path.GetPathRoot(settings.OutputPath)!;
        var disk = new DriveInfo(diskRoot);
        if (disk.IsReady && disk.AvailableFreeSpace < 64L * 1024 * 1024)
            throw new IOException("输出磁盘可用空间不足 64 MB，请清理空间后重试。");
        // Encode beside the destination so publishing is a same-volume rename.
        string encoded = Path.Combine(Path.GetDirectoryName(settings.OutputPath)!,
            $".trifle_{Guid.NewGuid():N}.mp4");
        try
        {
            var arguments = new List<string>
            {
                "-hide_banner", "-loglevel", "error", "-n", "-nostats", "-progress", "pipe:1",
                "-f", "rawvideo", "-pixel_format", "rgb24", "-video_size", $"{settings.Width}x{settings.Height}",
                "-framerate", settings.FramesPerSecond.ToString()
            };
            arguments.AddRange(new[] { "-i", "pipe:0" });
            int audioInput = 1;
            if (audio == null) arguments.AddRange(new[] { "-frames:v", total.ToString(), "-an" });
            else AddAudioArguments(arguments, settings, audio, audioInput, "0:v:0");
            arguments.AddRange(new[] { "-vf", "scale=out_color_matrix=bt709:out_range=tv" });
            arguments.AddRange(new[]
            {
                "-c:v", encoding.CodecName, "-preset", encoding.Preset,
                "-crf", encoding.Crf.ToString(CultureInfo.InvariantCulture),
                "-pix_fmt", "yuv420p",
                "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "iec61966-2-1",
                "-color_range", "tv", "-movflags", "+faststart"
            });
            if (encoding.Encoder == "h265") arguments.AddRange(new[] { "-tag:v", "hvc1" });
            arguments.Add(encoded);
            await StreamFramesAsync(ffmpegPath, arguments, settings, captureFrame, report, token);
            token.ThrowIfCancellationRequested();
            File.Move(encoded, settings.OutputPath, settings.Overwrite);
            report(new ExportProgress("completed", total, total));
        }
        finally
        {
            // Only remove this job's exact unpublished file.
            try { if (File.Exists(encoded)) File.Delete(encoded); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { GD.PushWarning("编码临时文件清理失败：" + error.Message); }
        }
    }

    private static async Task StreamFramesAsync(string executable, IEnumerable<string> arguments,
        ExportSettings settings, Func<double, Task<Image>> capture, Action<ExportProgress> report, CancellationToken token)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info };
        if (!process.Start()) throw new IOException("无法启动 FFmpeg。");
        Task<string> errors = process.StandardError.ReadToEndAsync();
        Task progress = ReadOutputAsync(process.StandardOutput, null);
        using var killOnCancel = token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        try
        {
            // One frame at a time, with OS-pipe backpressure. Memory and disk do not grow with clip length.
            for (int frame = 0; frame < settings.FrameCount; frame++)
            {
                token.ThrowIfCancellationRequested();
                using var image = await capture(settings.FrameTime(frame));
                if (image.GetWidth() != settings.Width || image.GetHeight() != settings.Height)
                    throw new InvalidOperationException("捕获视口尺寸与导出设置不一致。");
                image.Convert(Image.Format.Rgb8);
                await process.StandardInput.BaseStream.WriteAsync(image.GetData(), token);
                report(new ExportProgress("rendering", frame + 1, settings.FrameCount));
            }
            process.StandardInput.Close();
            report(new ExportProgress("encoding", settings.FrameCount, settings.FrameCount));
            await process.WaitForExitAsync(token);
            await progress;
            if (process.ExitCode != 0) throw new IOException("FFmpeg 编码失败：" + (await errors).Trim());
        }
        catch (Exception error)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await progress;
            string details = (await errors).Trim();
            token.ThrowIfCancellationRequested();
            if (error is IOException && details.Length > 0) throw new IOException("FFmpeg 编码失败：" + details, error);
            throw;
        }
    }

    private static void AddAudioArguments(List<string> arguments, ExportSettings settings, AudioExportSettings audio,
        int audioInput, string videoMap)
    {
        // Match the rendered frame duration, including the rounded-up final frame.
        double duration = settings.FrameCount / (double)settings.FramesPerSecond;
        var window = AudioTiming.Clip(settings.StartSeconds, duration, audio.OffsetSeconds, audio.DurationSeconds);
        static string Number(double value) => value.ToString("0.#########", CultureInfo.InvariantCulture);
        if (window.Duration == 0)
            arguments.AddRange(new[] { "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo:d=" + Number(duration) });
        else
        {
            arguments.AddRange(new[] { "-i", audio.Path });
            // Resample before sample-count delay; pad the tail instead of shortening video to audio length.
            string filter = $"atrim=start={Number(window.SourceStart)}:duration={Number(window.Duration)}," +
                "asetpts=PTS-STARTPTS,aresample=48000,aformat=channel_layouts=stereo," +
                $"adelay={Math.Round(window.Delay * 48000).ToString(CultureInfo.InvariantCulture)}S:all=1," +
                $"apad,atrim=duration={Number(duration)}";
            arguments.AddRange(new[] { "-af", filter });
        }
        // Limit by time, rather than -frames:v, so the audio encoder can finish its last packets.
        arguments.AddRange(new[] { "-map", videoMap, "-map", $"{audioInput}:a:0", "-t", Number(duration),
            "-c:a", "aac", "-b:a", settings.EffectiveEncoding.AudioBitrateKbps.ToString(CultureInfo.InvariantCulture) + "k",
            "-ar", "48000", "-ac", "2" });
    }

    private static async Task RunFfmpegAsync(string executable, IEnumerable<string> arguments,
        Action<string> inspectOutput, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info };
        if (!process.Start()) throw new InvalidOperationException("无法启动 FFmpeg。");
        Task<string> errors = process.StandardError.ReadToEndAsync();
        Task output = ReadOutputAsync(process.StandardOutput, inspectOutput);
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await Task.WhenAll(output, errors);
            throw;
        }
        await Task.WhenAll(output, errors);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"FFmpeg 失败（退出码 {process.ExitCode}）：\n" + (await errors).Trim());
    }

    private static async Task ReadOutputAsync(StreamReader reader, Action<string> inspect)
    {
        string line;
        while ((line = await reader.ReadLineAsync()) != null)
            inspect?.Invoke(line);
    }
}
