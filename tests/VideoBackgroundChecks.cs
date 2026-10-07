using Trifle.Persistence;
using Trifle.Video;
using Trifle.Visuals;

internal static class VideoBackgroundChecks
{
    public static async Task RunAsync(string fixtures)
    {
        int checks = 0;
        void Check(bool pass, string name)
        { if (!pass) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS: " + name); }
        string directory = Path.Combine(Path.GetTempPath(), "trifle-video-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string file = Path.Combine(directory, "project.json");
            var background = new BackgroundSettings
            {
                Type = BackgroundType.Video, Brightness = 1.5, Opacity = 0.5, Blur = 5,
                Video = new() { Path = Path.Combine(directory, "media", "背景 视频.mp4"), PreviewHeight = 480,
                    PreviewFramesPerSecond = 15, OffsetSeconds = -0.25, Loop = false }
            };
            var visual = new VisualSettings { Background = background };
            ProjectStorage.SaveProject(file, new ProjectData { MidiPath = Path.Combine(directory, "song.mid"), Visual = visual });
            var loaded = ProjectStorage.LoadProject(file);
            Check(!Path.IsPathFullyQualified(loaded.Visual.Background.Video.Path), "Video references are relative in project files");
            Check(ProjectStorage.ResolveVisualReferences(loaded.Visual, file).Background == background, "Project restores Unicode path and all video settings");
            ProjectStorage.SavePreset(file, new VisualPreset { Visual = visual });
            Check(ProjectStorage.ResolveVisualReferences(ProjectStorage.LoadPreset(file).Visual, file).Background == background,
                "Preset preserves video independently of MIDI");
            File.WriteAllText(file, "{\"kind\":\"trifle-preset\",\"version\":1,\"visual\":{\"background\":{\"type\":\"Solid\"}}}");
            Check(ProjectStorage.LoadPreset(file).Visual.Background.Video == new VideoBackgroundSettings(), "Older files default to 720p30 without video");
            foreach (var invalid in new[] { background.Video with { PreviewHeight = 1440 }, background.Video with { OffsetSeconds = double.PositiveInfinity } })
            {
                bool rejected = false; try { invalid.Validate(); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "Invalid video parameter rejected");
            }
            foreach (double blur in new[] { double.NaN, 41 })
            {
                bool rejected = false; try { (background with { Blur = blur }).Validate(); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "Invalid shared background blur rejected");
            }
            File.WriteAllText(file, "{\"kind\":\"trifle-preset\",\"version\":1,\"visual\":{\"effectControlsVersion\":2,\"background\":{\"type\":\"Video\",\"brightness\":1.5,\"video\":{\"darkness\":0.2,\"blur\":5}}}}");
            var migrated = ProjectStorage.LoadPreset(file);
            Check(Math.Abs(migrated.Visual.Background.Brightness - 1.2) < 1e-9 && migrated.Visual.Background.Blur == 5,
                "Legacy darkness and video blur migrate to shared appearance");
            ProjectStorage.SavePreset(file, migrated);
            Check(ProjectStorage.LoadPreset(file).Visual.Background == migrated.Visual.Background &&
                !File.ReadAllText(file).Contains("darkness"), "Migration is stable across saving and loading");
            var metadata = new VideoMetadata(320, 180, 1, 30);
            Check(Math.Abs(VideoFilters.SeekTime(2.25, background.Video with { Loop = true }, metadata) - 0.25) < 1e-9,
                "Loop seek wraps to source duration");
            Check(VideoFilters.SeekTime(2, background.Video, metadata) < 1, "Non-loop end seek stays inside the last source frame");
            if (Directory.Exists(fixtures))
            {
                string path = Path.Combine(fixtures, "红 色 测试.mp4");
                var probed = await VideoMetadata.ProbeAsync("ffmpeg", path);
                Check(probed.Width == 320 && probed.Height == 180 && Math.Abs(probed.DurationSeconds - 1) < 0.01,
                    "FFprobe reads Unicode video metadata");
                background = background with { Video = background.Video with { Path = path, OffsetSeconds = 0 } };
                var spec = background.Video.PreviewSpec;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await using (var source = new VideoFrameSource("ffmpeg", path, spec, false, 0,
                    VideoFilters.Build(background, spec.Width, spec.Height, spec.FramesPerSecond), holdLastFrame: true))
                {
                    using var frame = await source.ReadAsync(timeout.Token);
                    int pixel = ((spec.Height / 2) * spec.Width + spec.Width / 2) * 4;
                    Check(frame.Pixels[pixel] > 245 && frame.Pixels[pixel + 1] < 3 && frame.Pixels[pixel + 2] < 3 && frame.Pixels[pixel + 3] == 255,
                        "Decoder preserves source pixels for shared HDR appearance");
                    for (int i = 0; i < 22; i++) { using var next = await source.ReadAsync(timeout.Token); }
                    Check(source.FramesDecoded > 15 && source.Error.Length == 0, "Non-loop decoder holds last frame after EOF");
                    await Task.Delay(150, timeout.Token);
                    Check(source.QueuedFrames <= VideoFrameSource.QueueCapacity, "Paused consumer has a bounded frame queue");
                }
                await using (var source = new VideoFrameSource("ffmpeg", Path.Combine(fixtures, "portrait.mp4"), spec, false, 0,
                    VideoFilters.Build(background, spec.Width, spec.Height, spec.FramesPerSecond)))
                {
                    using var frame = await source.ReadAsync(timeout.Token);
                    Check(frame.Pixels[3] == 0, "Video letterbox is transparent so the HDR fill remains visible");
                }
                await using (var source = new VideoFrameSource("ffmpeg", path, spec, true, 0.75))
                {
                    for (int i = 0; i < 22; i++) { using var next = await source.ReadAsync(timeout.Token); }
                    Check(source.Error.Length == 0, "Loop decoder survives source EOF after seeking");
                }
                // Different colors on either side of the loop expose bad PTS
                // rewriting that a one-color clip cannot detect.
                string loopFixture = Path.Combine(directory, "loop-phases.mp4");
                var generation = new System.Diagnostics.ProcessStartInfo("ffmpeg")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                foreach (string argument in new[] { "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i",
                    "color=red:s=160x90:r=30:d=4", "-f", "lavfi", "-i", "color=blue:s=160x90:r=30:d=4",
                    "-filter_complex", "[0:v][1:v]concat=n=2:v=1:a=0", "-c:v", "libx264", "-preset", "ultrafast", loopFixture })
                    generation.ArgumentList.Add(argument);
                using (var process = System.Diagnostics.Process.Start(generation))
                {
                    var errors = process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync(timeout.Token);
                    if (process.ExitCode != 0) throw new IOException(await errors);
                }
                await using (var source = new VideoFrameSource("ffmpeg", loopFixture, spec, true, 7.75))
                {
                    bool redAfterLoop = false;
                    for (int i = 0; i < 18; i++)
                    {
                        using var frame = await source.ReadAsync(timeout.Token);
                        int pixel = ((spec.Height / 2) * spec.Width + spec.Width / 2) * 4;
                        if (i == 17) redAfterLoop = frame.Pixels[pixel] > 240 && frame.Pixels[pixel + 2] < 3;
                    }
                    Check(redAfterLoop, "Loop after near-EOF seek restarts at the beginning with correct timestamps");
                }
            }
            Console.WriteLine($"{checks} video background checks passed.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
