using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Trifle.Video;

public sealed record VideoMetadata(int Width, int Height, double DurationSeconds, double FramesPerSecond)
{
    public static string ProbeExecutable(string ffmpeg)
    {
        string directory = System.IO.Path.GetDirectoryName(ffmpeg);
        return string.IsNullOrEmpty(directory) ? "ffprobe" : System.IO.Path.Combine(directory, "ffprobe" +
            (OperatingSystem.IsWindows() ? ".exe" : ""));
    }

    public static async Task<VideoMetadata> ProbeAsync(string ffmpeg, string path, CancellationToken token = default)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("背景视频文件不存在：" + path, path);
        var info = new ProcessStartInfo(ProbeExecutable(ffmpeg))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-v", "error", "-select_streams", "v:0", "-show_entries",
            "stream=width,height,duration,avg_frame_rate:format=duration", "-of", "json", path }) info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info };
        try
        {
            if (!process.Start()) throw new IOException("无法启动 FFprobe。");
        }
        catch (System.ComponentModel.Win32Exception error)
        { throw new IOException("无法启动 FFprobe，请检查导出窗口中的 FFmpeg 路径及同目录的 ffprobe。", error); }
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var kill = timeout.Token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        });
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            await Task.WhenAll(output, errors).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            throw new IOException("背景视频信息读取超时：" + path);
        }
        await Task.WhenAll(output, errors).ConfigureAwait(false);
        if (process.ExitCode != 0) throw new IOException("无法读取背景视频：" + path + "\n" + (await errors.ConfigureAwait(false)).Trim());
        using var document = JsonDocument.Parse(await output.ConfigureAwait(false));
        var streams = document.RootElement.GetProperty("streams");
        if (streams.GetArrayLength() == 0) throw new IOException("背景文件没有视频画面：" + path);
        var stream = streams[0];
        static double Numeric(JsonElement element, string name) => element.TryGetProperty(name, out var property) &&
            double.TryParse(property.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : 0;
        double duration = Numeric(stream, "duration");
        if (duration <= 0 && document.RootElement.TryGetProperty("format", out var format)) duration = Numeric(format, "duration");
        double fps = 30;
        if (stream.TryGetProperty("avg_frame_rate", out var rate))
        {
            string[] fraction = rate.GetString().Split('/');
            if (fraction.Length == 2 && double.TryParse(fraction[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double n) &&
                double.TryParse(fraction[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d > 0 && n > 0) fps = n / d;
        }
        if (!double.IsFinite(duration) || duration <= 0) throw new IOException("背景视频没有有效时长：" + path);
        return new VideoMetadata(stream.GetProperty("width").GetInt32(), stream.GetProperty("height").GetInt32(), duration, fps);
    }
}
