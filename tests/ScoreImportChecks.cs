using Trifle.Score;

internal static class ScoreImportChecks
{
    public static async Task RunAsync(string sample)
    {
        int checks = 0;
        void Check(bool value, string label)
        {
            if (!value) throw new Exception("FAIL: " + label);
            checks++; Console.WriteLine("PASS: " + label);
        }
        string executable = MuseScoreImporter.FindExecutable();
        Check(File.Exists(executable), "Detect the local MuseScore installation");
        string directory = Path.Combine(Path.GetTempPath(), "trifle-score-import-" + Guid.NewGuid().ToString("N"));
        string cacheDirectory = Path.Combine(directory, "cache");
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "谱面 (with spaces).mscz");
            File.Copy(sample, source);
            var importer = new MuseScoreImporter(cacheDirectory);
            var first = await importer.ImportAsync(source, executable);
            Check(!first.FromCache && first.Bundle.Rows.Length == 12 && first.Bundle.Midi.Length > 0,
                "Real MuseScore conversion preserves arguments with spaces and Unicode");
            var reused = await importer.ImportAsync(source, "");
            Check(reused.FromCache && reused.Bundle.Events.Length == 238, "Valid cache works without launching MuseScore");
            string relocated = Path.Combine(directory, "moved.mscz"); File.Copy(source, relocated);
            Check((await importer.ImportAsync(relocated, "")).FromCache, "Relocated identical score reuses content cache");
            string cache = Directory.GetFiles(cacheDirectory).Single(); File.WriteAllText(cache, "{");
            Check(!(await importer.ImportAsync(source, executable)).FromCache, "Corrupt cache is rebuilt from the original score");
            using (var append = new FileStream(relocated, FileMode.Append)) append.WriteByte(0);
            Check(!importer.TryReadCached(relocated, out _), "Changed source cannot reuse stale score data");
            bool cancelled = false;
            using (var cancellation = new CancellationTokenSource(250))
            {
                try { await importer.ImportAsync(relocated, Path.Combine(AppContext.BaseDirectory, "Trifle.Checks.exe"), cancellation.Token); }
                catch (OperationCanceledException) { cancelled = true; }
            }
            Check(cancelled && !Directory.GetFiles(cacheDirectory, "convert-*").Any(), "Cancellation terminates the owned converter and removes its partial output");
            Check(importer.TryReadCached(source, out _), "Cancelled conversion leaves the earlier cache usable");
            string broken = Path.Combine(directory, "broken.mscz"); File.WriteAllText(broken, "invalid score");
            bool rejected = false;
            try { await importer.ImportAsync(broken, executable); }
            catch (IOException) { rejected = true; }
            Check(rejected && !Directory.GetFiles(cacheDirectory, "convert-*").Any(), "Conversion failure is reported and does not leave partial output");
            Check(new FileInfo(source).Length == new FileInfo(sample).Length, "Conversion never modifies the source score");
        }
        finally
        {
            if (Directory.Exists(cacheDirectory))
            {
                foreach (string file in Directory.GetFiles(cacheDirectory)) File.Delete(file);
                Directory.Delete(cacheDirectory);
            }
            foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
        Console.WriteLine($"Score import: {checks} checks passed.");
    }
}
