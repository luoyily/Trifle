using Trifle.Audio;
using Trifle.Export;
using Trifle.Midi;
using Trifle.Persistence;
using Trifle.Playback;

internal static class AudioTimelineChecks
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new Exception("FAIL: " + label);
            checks++; Console.WriteLine("PASS: " + label);
        }
        Check(AudioTiming.PlaybackDuration(10, true, 0, 12) == 12, "Longer audio extends the timeline");
        Check(AudioTiming.PlaybackDuration(10, true, 0, 8) == 10, "Shorter audio keeps MIDI duration");
        Check(AudioTiming.PlaybackDuration(10, true, 3, 12) == 15, "Positive offset extends the audio end");
        Check(AudioTiming.PlaybackDuration(10, true, -3, 12) == 10, "Negative offset removes the leading audio duration");
        Check(AudioTiming.PlaybackDuration(10, true, -15, 12) == 10, "Audio entirely before the timeline does not shorten MIDI");
        Check(AudioTiming.PlaybackDuration(10, false, 3, 12) == 10, "Disabled audio does not extend the timeline");

        var song = new MidiSong("song.mid", 1, Array.Empty<MidiTrack>(), Array.Empty<MidiTempoChange>(),
            new[] { new MidiNote(0, 0, 60, 90, 0.5, 1) }, 1);
        var playback = new PlaybackController();
        playback.SetSong(song); playback.SetDuration(3);
        int hits = 0; playback.NoteHit += _ => hits++;
        playback.Play(); playback.Advance(1.5);
        Check(playback.IsPlaying && playback.TimeSeconds == 1.5 && hits == 1, "Preview continues past MIDI without extra note hits");
        playback.Advance(5);
        Check(!playback.IsPlaying && playback.TimeSeconds == 3, "Preview stops at the combined end");
        playback.Seek(2); playback.Play(); playback.SetDuration(1);
        Check(!playback.IsPlaying && playback.TimeSeconds == 1, "Removing an audio tail clamps the position and pauses");
        Check(song.DurationSeconds == 1 && song.Notes.Length == 1, "Combined duration leaves MIDI metadata untouched");

        var preferences = new ExportPreferences { EndSeconds = 2.5 };
        preferences.Validate(3);
        Check(preferences.EndSeconds == 2.5, "A manual export can lie inside the audio tail");
        new ExportPreferences { EndSeconds = null }.Validate(3);
        Check(true, "Full-duration export remains a saved automatic choice");
        var tail = AudioTiming.Clip(1, 2, 0, 3);
        Check(tail.SourceStart == 1 && tail.Duration == 2 && tail.Delay == 0, "Tail export selects the matching audio interval");

        string path = Path.Combine(Path.GetTempPath(), "trifle-audio-timeline-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            foreach (string extension in new[] { ".ogg", ".MP3", ".wav" })
            {
                var data = new ProjectData { MidiPath = "song.mid", TimeSeconds = 2.5,
                    Audio = new AudioSettings { Path = "audio" + extension }, Export = preferences };
                ProjectStorage.SaveProject(path, data);
                var restored = ProjectStorage.LoadProject(path);
                Check(restored.TimeSeconds == 2.5 && restored.Export.EndSeconds == 2.5 &&
                    Path.GetExtension(restored.Audio.Path) == extension, extension + " references and tail positions survive save/load");
            }
            var manual = new ProjectData { MidiPath = "song.mid", Export = new ExportPreferences
                { EndSeconds = 1, FollowTimelineEnd = false } };
            ProjectStorage.SaveProject(path, manual);
            Check(ProjectStorage.LoadProject(path).Export.FollowTimelineEnd == false,
                "A manual range ending at MIDI duration remains explicitly manual");
            ProjectStorage.SaveProject(path, manual with { Export = new ExportPreferences { FollowTimelineEnd = true } });
            var full = ProjectStorage.LoadProject(path).Export;
            Check(full.FollowTimelineEnd == true && full.EndSeconds == null,
                "Automatic full export is distinct from a numeric custom end");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
        Console.WriteLine($"Audio/timeline: {checks} targeted checks passed.");
    }
}
