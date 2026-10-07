using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Trifle.Video;

// Rawvideo carries no timestamps. The fps filter establishes CFR; time is index / fps.
public sealed class VideoFrame : IDisposable
{
    public byte[] Pixels { get; }
    public long Index { get; }
    public double TimeSeconds { get; }
    private Action<byte[]> _release;

    internal VideoFrame(byte[] pixels, long index, int fps, Action<byte[]> release)
    { Pixels = pixels; Index = index; TimeSeconds = index / (double)fps; _release = release; }

    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke(Pixels);
}

public sealed class VideoFrameSource : IAsyncDisposable
{
    public const int QueueCapacity = 3;
    public const int BufferCount = QueueCapacity + 2; // queued + producer + consumer
    public VideoPreviewSpec Spec { get; }
    public Task Completion => _worker;
    public long FramesDecoded => Interlocked.Read(ref _framesDecoded);
    public double AverageReadMilliseconds => FramesDecoded == 0 ? 0 :
        Interlocked.Read(ref _readTicks) * 1000.0 / Stopwatch.Frequency / FramesDecoded;
    public int QueuedFrames => _frames.Reader.Count;
    public long BufferBytes => (long)BufferCount * Spec.FrameBytes;
    public double FirstFrameMilliseconds => Volatile.Read(ref _firstFrameMilliseconds);
    public string Error => Volatile.Read(ref _error);
    public bool IsRunning => !_worker.IsCompleted;
    private readonly Channel<VideoFrame> _frames = Channel.CreateBounded<VideoFrame>(
        new BoundedChannelOptions(QueueCapacity) { SingleReader = true, SingleWriter = true });
    private readonly Channel<byte[]> _buffers = Channel.CreateBounded<byte[]>(BufferCount);
    private readonly CancellationTokenSource _cancel = new();
    private readonly Task _worker;
    private long _framesDecoded, _readTicks;
    private double _firstFrameMilliseconds;
    private string _error = "";
    private int _disposed;

    public VideoFrameSource(string executable, string path, VideoPreviewSpec spec, bool loop = false,
        double startSeconds = 0, string filters = null, bool holdLastFrame = false)
    {
        spec.Validate();
        if (string.IsNullOrWhiteSpace(executable)) throw new ArgumentException("请填写 FFmpeg 路径。");
        if (!File.Exists(path)) throw new FileNotFoundException("视频文件不存在。", path);
        Spec = spec;
        if (!double.IsFinite(startSeconds) || startSeconds < 0) throw new ArgumentOutOfRangeException(nameof(startSeconds));
        _worker = Task.Run(() => DecodeAsync(executable, path, loop, startSeconds, filters, holdLastFrame, _cancel.Token));
    }

    public bool TryPeek(out VideoFrame frame) => _frames.Reader.TryPeek(out frame);
    public bool TryRead(out VideoFrame frame) => _frames.Reader.TryRead(out frame);
    public ValueTask<VideoFrame> ReadAsync(CancellationToken token = default) => _frames.Reader.ReadAsync(token);

    public static IReadOnlyList<string> Arguments(string path, VideoPreviewSpec spec, bool loop,
        double startSeconds = 0, string filters = null, bool holdLastFrame = false)
    {
        spec.Validate();
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin" };
        bool loopTail = loop && startSeconds > 0;
        if (loop && !loopTail) args.AddRange(new[] { "-stream_loop", "-1" });
        if (startSeconds > 0) args.AddRange(new[] { "-ss", VideoFilters.Number(startSeconds) });
        string filter = filters ?? $"fps={spec.FramesPerSecond},scale={spec.Width}:{spec.Height}:force_original_aspect_ratio=decrease:force_divisible_by=2:reset_sar=1," +
            $"pad={spec.Width}:{spec.Height}:(ow-iw)/2:(oh-ih)/2:color=black,setsar=1";
        if (holdLastFrame && !loop) filter += ",tpad=stop_mode=clone:stop=-1";
        args.AddRange(new[] { "-i", path });
        if (loopTail) args.AddRange(new[] { "-stream_loop", "-1", "-i", path });
        args.AddRange(new[] { "-filter_complex", VideoFilters.Timeline(0, loopTail ? 1 : null) + filter + "[decoded]",
            "-map", "[decoded]", "-an", "-sn", "-dn",
            "-pix_fmt", "rgba", "-fps_mode", "passthrough", "-c:v", "rawvideo", "-f", "rawvideo", "pipe:1" });
        return args;
    }

    private async Task DecodeAsync(string executable, string path, bool loop, double startSeconds,
        string filters, bool holdLastFrame, CancellationToken token)
    {
        var started = Stopwatch.StartNew();
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in Arguments(path, Spec, loop, startSeconds, filters, holdLastFrame)) info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info };
        Task<string> errors = null;
        Exception failure = null;
        try
        {
            for (int i = 0; i < BufferCount; i++) _buffers.Writer.TryWrite(new byte[Spec.FrameBytes]);
            token.ThrowIfCancellationRequested();
            if (!process.Start()) throw new IOException("无法启动 FFmpeg。");
            errors = DrainErrorsAsync(process.StandardError);
            using var killOnCancel = token.Register(() => Kill(process));
            long index = 0;
            while (true)
            {
                byte[] buffer = await _buffers.Reader.ReadAsync(token);
                VideoFrame frame = null;
                try
                {
                    long readStarted = Stopwatch.GetTimestamp();
                    int read = 0;
                    while (read < buffer.Length)
                    {
                        int count = await process.StandardOutput.BaseStream.ReadAsync(buffer.AsMemory(read), token);
                        if (count == 0) break;
                        read += count;
                    }
                    if (read == 0) break;
                    if (read != buffer.Length) throw new EndOfStreamException("FFmpeg 输出了不完整的视频帧。");
                    Interlocked.Add(ref _readTicks, Stopwatch.GetTimestamp() - readStarted);
                    Interlocked.Increment(ref _framesDecoded);
                    if (index == 0) Volatile.Write(ref _firstFrameMilliseconds, started.Elapsed.TotalMilliseconds);
                    frame = new VideoFrame(buffer, index++, Spec.FramesPerSecond, ReturnBuffer);
                    await _frames.Writer.WriteAsync(frame, token);
                    // Ownership moves to the consumer only after the bounded write succeeds.
                    buffer = null;
                    frame = null;
                }
                finally
                {
                    if (frame != null) frame.Dispose();
                    else if (buffer != null) ReturnBuffer(buffer);
                }
            }
            await process.WaitForExitAsync(token);
            string details = await errors;
            if (process.ExitCode != 0) throw new IOException("FFmpeg 解码失败：" + details.Trim());
            if (FramesDecoded == 0) throw new IOException("视频没有可解码的画面。");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!token.IsCancellationRequested)
            {
                failure = exception;
                Volatile.Write(ref _error, exception.Message);
            }
        }
        finally
        {
            Kill(process);
            try { await process.WaitForExitAsync(); } catch (InvalidOperationException) { }
            if (errors != null) await errors;
            _frames.Writer.TryComplete(failure);
        }
    }

    private void ReturnBuffer(byte[] pixels) => _buffers.Writer.TryWrite(pixels);

    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static async Task<string> DrainErrorsAsync(StreamReader reader)
    {
        var details = new StringBuilder();
        char[] buffer = new char[2048];
        int count;
        while ((count = await reader.ReadAsync(buffer)) != 0)
        {
            // Drain continuously while retaining only the diagnostic tail.
            details.Append(buffer, 0, count);
            if (details.Length > 16384) details.Remove(0, details.Length - 16384);
        }
        return details.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cancel.Cancel();
        await _worker.ConfigureAwait(false);
        while (_frames.Reader.TryRead(out var frame)) frame.Dispose();
        _cancel.Dispose();
    }
}
