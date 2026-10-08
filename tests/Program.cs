using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Common;
using System.Text.Json;
using Trifle.Midi;
using Trifle.Playback;
using Trifle.Export;
using Trifle.Visuals;
using Trifle.Persistence;
using Trifle.Audio;

if (args.Contains("--effect-settings")) { EffectSettingsChecks.Run(); return; }
if (args.Contains("--transport")) { TransportChecks.Run(); return; }
if (args.Contains("--score-bundle")) { ScoreBundleChecks.Run(args.LastOrDefault()); return; }
if (args.Contains("--score-import")) { await ScoreImportChecks.RunAsync(args.LastOrDefault()); return; }
// A controlled converter used only by cancellation checks. It writes a partial file, then waits.
if (args.Contains("--score-media"))
{
    File.WriteAllText(args[Array.IndexOf(args, "-o") + 1], "{}");
    await Task.Delay(10000); return;
}
if (args.Contains("--encoding")) { EncodingChecks.Run(); return; }
if (args.Contains("--audio-timeline")) { AudioTimelineChecks.Run(); return; }
if (args.Contains("--video-background")) { await VideoBackgroundChecks.RunAsync(args.LastOrDefault()); return; }

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    checks++;
    Console.WriteLine("PASS: " + name);
}
void Near(double actual, double expected, string name) => Check(Math.Abs(actual - expected) < 0.000001, name);

MidiSong Read(MidiFile file, MidiFileFormat format = MidiFileFormat.MultiTrack)
{
    using var stream = new MemoryStream();
    file.Write(stream, format);
    stream.Position = 0;
    return MidiImporter.Read(stream, "fixture.mid");
}

MidiFile FileWith(params TrackChunk[] tracks) => new(tracks)
{
    TimeDivision = new TicksPerQuarterNoteTimeDivision(480)
};

var tempoFile = FileWith(
    new TrackChunk(new SequenceTrackNameEvent("Tempo"), new SetTempoEvent(500000),
        new SetTempoEvent(1000000) { DeltaTime = 480 }),
    new TrackChunk(new SequenceTrackNameEvent("Piano"),
        new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90) { Channel = (FourBitNumber)3, DeltaTime = 240 },
        new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)3, DeltaTime = 480 }));
var tempoSong = Read(tempoFile);
Check(tempoSong.Format == 1 && tempoSong.Tracks.Length == 2, "Format 1 and separate tempo track");
Check(tempoSong.Notes.Length == 1 && tempoSong.Notes[0].TrackIndex == 1 && tempoSong.Notes[0].Channel == 3,
    "Track and channel remain separate");
Near(tempoSong.Notes[0].StartSeconds, 0.25, "Onset before tempo change");
Near(tempoSong.Notes[0].EndSeconds, 1.0, "End after tempo change");
Near(tempoSong.Notes[0].DurationSeconds, 0.75, "Note spans two tempo segments");
Near(tempoSong.TempoChanges[1].TimeSeconds, 0.5, "Tempo tick 480 is 0.5 seconds");
Near(tempoSong.TempoChanges[1].BeatsPerMinute, 60, "Second tempo is 60 BPM");
Check(tempoSong.Notes[0].Velocity == 90, "Velocity preserved");

var overlapFile = FileWith(new TrackChunk(
    new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)80),
    new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { DeltaTime = 120 },
    new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 120 },
    new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 120 }));
var overlapSong = Read(overlapFile, MidiFileFormat.SingleTrack);
Check(overlapSong.Format == 0 && overlapSong.Notes.Length == 2, "Format 0 and overlapping same-pitch notes");
Near(overlapSong.Notes[0].EndSeconds, 0.25, "FIFO closes the first onset first");
Near(overlapSong.Notes[1].StartSeconds, 0.125, "Second overlap onset");
Near(overlapSong.Notes[1].EndSeconds, 0.375, "Second overlap remains open");
Check(overlapSong.Notes.Count(note => note.IsActiveAt(0.2)) == 2, "Both overlaps active");
Check(overlapSong.Notes.Count(note => note.IsActiveAt(0.3)) == 1, "One overlap ending leaves the other active");
Check(!overlapSong.Notes[0].IsActiveAt(0.25), "End time excluded from active interval");

var silentFile = FileWith(new TrackChunk(
    new NoteOnEvent((SevenBitNumber)64, (SevenBitNumber)70),
    new NoteOnEvent((SevenBitNumber)64, (SevenBitNumber)0) { DeltaTime = 480 }));
var silentSong = Read(silentFile);
Check(silentSong.Notes.Length == 1, "Zero-velocity Note On becomes Note Off");
Near(silentSong.Notes[0].EndSeconds, 0.5, "Default tempo is 120 BPM");

var channelFile = FileWith(new TrackChunk(
    new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)70),
    new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90) { Channel = (FourBitNumber)1 },
    new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)1, DeltaTime = 120 },
    new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 120 }));
var channelSong = Read(channelFile);
Near(channelSong.Notes.Single(note => note.Channel == 0).EndSeconds, 0.25, "Note Off matches its channel");
Near(channelSong.Notes.Single(note => note.Channel == 1).EndSeconds, 0.125, "Other channel remains independent");

bool rejectedFormat2 = false;
try { Read(overlapFile, MidiFileFormat.MultiSequence); }
catch (InvalidDataException) { rejectedFormat2 = true; }
Check(rejectedFormat2, "Format 2 rejected explicitly");
Check(Read(FileWith(new TrackChunk())).Notes.Length == 0, "Empty MIDI accepted without fake notes");

var layout = new KeyboardLayout();
Check(layout.Keys.Length == 88, "88 keys in A0-C8 range");
Check(layout.Keys.Count(key => !key.IsBlack) == 52 && layout.Keys.Count(key => key.IsBlack) == 36,
    "52 white and 36 black keys");
Check(KeyboardLayout.PitchName(21) == "A0" && KeyboardLayout.PitchName(108) == "C8", "Pitch endpoints");
layout.TryGetKey(60, out var c4);
layout.TryGetKey(61, out var cs4);
Near(cs4.Left + cs4.Width / 2, c4.Left + c4.Width, "Black key centered on white-key boundary");
Check(!layout.TryGetKey(20, out _), "Out-of-range pitch omitted");
Check(new KeyboardLayout(61, 73).Keys.All(key => key.Left >= 0 && key.Left + key.Width <= 1.000001),
    "Custom range with black-key endpoints fits viewport");
Near(new KeyboardLayout(61, 61).Keys[0].Width, 1, "Single black-key range has finite width");
bool rejectedRange = false;
try { _ = new KeyboardLayout(80, 60); }
catch (ArgumentOutOfRangeException) { rejectedRange = true; }
Check(rejectedRange, "Inverted range rejected");

// Test transport with onsets at zero, simultaneous notes, and a very short note.
var transportNotes = new[]
{
    new MidiNote(0, 0, 60, 80, 0, 0.5),
    new MidiNote(0, 1, 60, 100, 0.125, 0.375),
    new MidiNote(0, 0, 64, 90, 0.125, 0.25),
    new MidiNote(0, 0, 67, 70, 0.2, 0.21),
    new MidiNote(0, 0, 72, 90, 0.8, 0.9)
};
var transportSong = new MidiSong("transport.mid", 0, Array.Empty<MidiTrack>(),
    new[] { new MidiTempoChange(0, 120) }, transportNotes, 1);
var playback = new PlaybackController();
var hits = new List<MidiNote>();
playback.NoteHit += hits.Add;
playback.Play();
Check(!playback.IsPlaying, "Playback without a song stays stopped");
playback.SetSong(transportSong);
Check(playback.TimeSeconds == 0 && !playback.IsPlaying, "Loading resets playback");
playback.Play();
playback.Advance(0);
Check(hits.Count == 0, "Zero delta does not trigger hits");
playback.Advance(0.1);
Near(playback.TimeSeconds, 0.1, "Normal playback advances song seconds");
Check(hits.Count == 1 && hits[0].StartSeconds == 0, "Onset at zero fires once on fresh playback");
playback.Advance(0.025);
Check(hits.Count == 3 && hits[1] == transportNotes[1] && hits[2] == transportNotes[2],
    "All simultaneous onsets at an exact boundary fire");
playback.Pause();
playback.Advance(2);
Near(playback.TimeSeconds, 0.125, "Pause freezes time");
Check(hits.Count == 3, "Pause emits no hits");
playback.Play();
playback.Advance(0.005);
Check(hits.Count == 3, "Resume does not duplicate boundary hits");
playback.Advance(0.17);
Check(hits.Count == 4 && hits[^1] == transportNotes[3], "Short note crossed within one frame still hits");
playback.Advance(0.1);
Near(playback.TimeSeconds, 0.4, "Playback continues at the original rate");
playback.Seek(0.85);
Check(hits.Count == 4 && playback.IsPlaying, "Forward seek emits no history and keeps playing state");
playback.Advance(0.01);
Check(hits.Count == 4, "Continuing after seek does not replay skipped onsets");
playback.Seek(0.1);
playback.Advance(0.025);
Check(hits.Count == 6, "Backward seek allows future onsets to fire again");
playback.Seek(0.125);
playback.Advance(0.001);
Check(hits.Count == 6, "Seek exactly to onset rebuilds state without a hit");
playback.Pause();
playback.Seek(0.2);
Check(!playback.IsPlaying && transportNotes.Count(note => note.IsActiveAt(playback.TimeSeconds)) == 4,
    "Paused seek stays paused in an active interval");
playback.Seek(-1);
Near(playback.TimeSeconds, 0, "Negative seek clamps to zero");
playback.Seek(100);
Check(playback.TimeSeconds == 1 && !playback.IsPlaying, "Seek past end clamps and pauses");
hits.Clear();
playback.Play();
Check(playback.TimeSeconds == 0 && playback.IsPlaying, "Play from end restarts at zero");
playback.Advance(5);
Check(playback.TimeSeconds == 1 && !playback.IsPlaying, "Large delta reaches exact end and pauses");
Check(hits.SequenceEqual(transportNotes), "Large delta catches all onsets in order once");
playback.Advance(1);
Check(hits.Count == 5, "No hits after completion");
playback.Stop();
Check(playback.TimeSeconds == 0 && !playback.IsPlaying,
    "Stop resets time and pauses");
hits.Clear();
playback.Play();
playback.Advance(0.01);
Check(hits.Count == 1 && hits[0].StartSeconds == 0, "Stop resets onset cursor for replay");
playback.SetSong(tempoSong);
Check(playback.TimeSeconds == 0 && !playback.IsPlaying,
    "Song replacement resets and pauses transport");
playback.Play();
playback.Advance(0.25);
Check(hits[^1] == tempoSong.Notes[0], "Imported tempo-map onset is used by playback");
bool invalidDelta = false, invalidSeek = false;
try { playback.Advance(double.NaN); } catch (ArgumentOutOfRangeException) { invalidDelta = true; }
try { playback.Seek(double.PositiveInfinity); } catch (ArgumentOutOfRangeException) { invalidSeek = true; }
Check(invalidDelta && invalidSeek, "Non-finite timeline inputs rejected");
playback.SetSong(transportSong with { Notes = Array.Empty<MidiNote>(), DurationSeconds = 0 });
playback.Play();
Check(!playback.IsPlaying, "Zero-duration song does not start playback");

var trackFile = FileWith(
    new TrackChunk(new SequenceTrackNameEvent("Tempo")),
    new TrackChunk(new SequenceTrackNameEvent("Left"),
        new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)80),
        new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 480 }),
    new TrackChunk(new SequenceTrackNameEvent("Right"),
        new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { DeltaTime = 120 },
        new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 240 }));
var trackSong = Read(trackFile);
Check(trackSong.Notes.Length == 2 && trackSong.Notes[0].TrackIndex == 1 && trackSong.Notes[1].TrackIndex == 2,
    "Same-channel notes preserve distinct track indices");
Near(trackSong.Notes[0].EndSeconds, 0.5, "Note Off pairs inside its own track");
Near(trackSong.Notes[1].EndSeconds, 0.375, "Other same-channel track has independent end time");
var colors = new NoteColors();
colors.SetSong(trackSong);
Check(colors.GetColor(trackSong.Notes[0]) == colors.GetColor(trackSong.Notes[1]),
    "Channel mode gives same-channel tracks the same color");
colors.SetMode(NoteColorMode.Track);
Check(colors.GetColor(trackSong.Notes[0]) != colors.GetColor(trackSong.Notes[1]),
    "Track mode distinguishes tracks sharing a channel");
Check(colors.GetTrackColor(1) == colors.GetChannelColor(0), "Tempo-only track does not consume first note color");
var green = new Godot.Color(0, 1, 0, 1);
var red = new Godot.Color(1, 0, 0, 1);
colors.SetTrackColor(2, red);
colors.SetChannelColor(0, green);
Check(colors.GetColor(trackSong.Notes[1]) == red, "Track override used by note color lookup");
colors.SetMode(NoteColorMode.Channel);
Check(colors.GetColor(trackSong.Notes[0]) == green && colors.GetColor(trackSong.Notes[1]) == green,
    "Channel override applies independently to both tracks");
colors.SetMode(NoteColorMode.Track);
Check(colors.GetColor(trackSong.Notes[1]) == red, "Switching modes preserves track palette");
var manyTracks = trackSong with
{
    Tracks = Enumerable.Range(0, 20).Select(index => new MidiTrack(index, $"Track {index}", 1, new[] { 0 })).ToArray()
};
colors.SetSong(manyTracks);
Check(colors.GetTrackColor(19) == colors.GetTrackColor(3), "More than 16 tracks use repeatable default palette");
Check(colors.GetChannelColor(0) == green && colors.Mode == NoteColorMode.Track,
    "New song preserves channel palette and selected mode");
colors.SetSong(trackSong);
Check(colors.GetTrackColor(2) != red, "New song resets song-specific track overrides");
bool invalidMode = false, invalidTrack = false;
try { colors.SetMode((NoteColorMode)9); } catch (ArgumentOutOfRangeException) { invalidMode = true; }
try { colors.SetTrackColor(3, red); } catch (ArgumentOutOfRangeException) { invalidTrack = true; }
Check(invalidMode && invalidTrack, "Invalid color mode and track index rejected");
colors.SetSong(trackSong with { Tracks = Array.Empty<MidiTrack>(), Notes = Array.Empty<MidiNote>() });
Check(colors.GetChannelColor(0) == green, "Empty song keeps channel colors without inventing tracks");

string checkOutput = Path.Combine(Path.GetTempPath(), "trifle_check_" + Guid.NewGuid().ToString("N") + ".mp4");
var clip = new ExportSettings(checkOutput, 87.252, 90.252);
clip.Validate();
Check(clip.FrameCount == 90, "Three-second export has 90 frames at 30 FPS");
Near(clip.FrameTime(0), 87.252, "Export starts at exact selected song time");
Near(clip.FrameTime(89), 87.252 + 89 / 30.0, "Last export frame precedes interval end");
Check(new ExportSettings(checkOutput, 0.1, 0.4).FrameCount == 9,
    "Floating-point subtraction does not add a frame at exact boundary");
Check(new ExportSettings(checkOutput, 0, 0.101).FrameCount == 4, "Partial final frame rounds output upward");
Check(new ExportSettings(checkOutput, 0, 0.000001).FrameCount == 1, "Positive sub-frame interval exports one frame");
Check(new ExportSettings(checkOutput, 0, 235.299581).FrameCount == 7059, "Complete test song has 7059 frames");
bool invalidInterval = false, invalidSize = false, invalidFrame = false;
try { (clip with { EndSeconds = clip.StartSeconds }).Validate(); } catch (ArgumentException) { invalidInterval = true; }
try { (clip with { Width = 1919 }).Validate(); } catch (ArgumentException) { invalidSize = true; }
try { clip.FrameTime(90); } catch (ArgumentOutOfRangeException) { invalidFrame = true; }
Check(invalidInterval && invalidSize && invalidFrame, "Invalid export interval, odd H.264 size, and frame index rejected");

void Rejected(Action action, string name)
{
    bool rejected = false;
    try { action(); }
    catch (Exception error) when (error is ArgumentException or JsonException or IOException or UnauthorizedAccessException)
    { rejected = true; }
    Check(rejected, name);
}

string storageRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Trifle", "checks", Guid.NewGuid().ToString("N")));
Near(AudioTiming.SourceTime(5, 2), 3, "Positive audio offset delays source position");
Near(AudioTiming.SourceTime(5, -2), 7, "Negative audio offset skips source opening");
var delayedAudio = AudioTiming.Clip(0, 5, 2, 10);
Check(delayedAudio == new AudioWindow(0, 3, 2), "Delayed audio produces leading silence within video interval");
Check(AudioTiming.Clip(5, 3, 2, 10) == new AudioWindow(3, 3, 0), "Middle export trims exact source interval");
Check(AudioTiming.Clip(0, 5, -2, 4) == new AudioWindow(2, 2, 0), "Negative offset produces trimmed audio and silent tail");
Check(AudioTiming.Clip(0, 5, 0, 2) == new AudioWindow(0, 2, 0), "Short audio does not reduce video duration");
Check(AudioTiming.Clip(0, 5, 10, 2).Duration == 0, "Interval before delayed audio is entirely silent");
Check(AudioTiming.Clip(10, 5, 0, 2).Duration == 0, "Interval after audio end is entirely silent");
Check(AudioTiming.Clip(2, 3, 2, 3) == new AudioWindow(0, 3, 0), "Audio boundary matches full export interval");
Check(AudioTiming.Clip(0, 4 / 30.0, 0.1, 10).Duration > 0,
    "Audio covers rounded-up final video frame");
Rejected(() => new AudioSettings { OffsetSeconds = double.NaN }.Validate(), "Nonfinite audio offset rejected");
Rejected(() => new AudioSettings { OffsetSeconds = 3601 }.Validate(), "Out-of-range audio offset rejected");
Rejected(() => new AudioSettings { Path = "song.flac" }.Validate(), "Unsupported audio format rejected");
Rejected(() => AudioTiming.Clip(0, 0, 0, 10), "Empty audio export interval rejected");
new KeyboardAppearance().Validate();
new BackgroundSettings().Validate();
Check(new KeyboardAppearance().Y + new KeyboardAppearance().Height == 1,
    "Default keyboard reaches canvas bottom");
Rejected(() => new KeyboardAppearance { X = double.NaN }.Validate(), "Nonfinite keyboard position rejected");
Rejected(() => new KeyboardAppearance { X = 0.2, Width = 0.9 }.Validate(), "Keyboard extending beyond canvas rejected");
Rejected(() => new KeyboardAppearance { Y = 0 }.Validate(), "Keyboard leaves space for falling notes");
Rejected(() => new KeyboardAppearance { Height = 0.01 }.Validate(), "Unusable keyboard size rejected");
Rejected(() => new KeyboardAppearance { BlackColor = "invalid" }.Validate(), "Malformed idle key color rejected");
Rejected(() => new BackgroundSettings { Fit = (BackgroundFit)5 }.Validate(), "Unknown background fit rejected");
Rejected(() => new BackgroundSettings { ImagePath = "movie.mp4" }.Validate(), "Unsupported background file rejected");
Rejected(() => new BackgroundSettings { Color = "fff" }.Validate(), "Malformed background color rejected");
Rejected(() => new VisualSettings { Keyboard = null }.Validate(), "Null keyboard appearance rejected");
Rejected(() => new VisualSettings { Background = null }.Validate(), "Null background block rejected");
Rejected(() => new VisualSettings { Note = null }.Validate(), "Null note appearance rejected");
Rejected(() => new VisualSettings { Glow = null }.Validate(), "Null glow settings rejected");
Rejected(() => new NoteAppearance { Shape = (NoteShape)3 }.Validate(), "Unknown note shape rejected");
Rejected(() => new NoteAppearance { CornerRadius = double.NaN }.Validate(), "Nonfinite note radius rejected");
Rejected(() => new NoteAppearance { Opacity = 1.1 }.Validate(), "Note opacity outside unit interval rejected");
Rejected(() => new NoteAppearance { Brightness = -1 }.Validate(), "Negative note brightness rejected");
Rejected(() => new NoteAppearance { Emission = 9 }.Validate(), "Excessive note emission rejected");
Rejected(() => new GlowSettings { Intensity = double.PositiveInfinity }.Validate(), "Nonfinite glow intensity rejected");
Directory.CreateDirectory(storageRoot);
try
{
    string projectFile = Path.Combine(storageRoot, "song.trifle.json");
    string midiPath = Path.Combine(storageRoot, "素材.mid");
    trackFile.Write(midiPath, false);
    var savedProject = new ProjectData
    {
        MidiPath = midiPath, TimeSeconds = 0.2, SettingsVisible = false,
        Audio = new AudioSettings { Path = Path.Combine(storageRoot, "素材.ogg"), Enabled = false, OffsetSeconds = -0.35 },
        Visual = new VisualSettings
        {
            FirstPitch = 48, LastPitch = 84, LookAheadSeconds = 3, ColorMode = NoteColorMode.Track,
            ChannelColors = new() { [0] = "00ff00ff" }, TrackColors = new() { [2] = "ff0000ff" },
            Keyboard = new KeyboardAppearance { X = 0.1, Y = 0.7, Width = 0.8, Height = 0.2,
                WhiteColor = "aabbccff", BlackColor = "102030ff" },
            Background = new BackgroundSettings { Color = "112233ff",
                ImagePath = Path.Combine(storageRoot, "背景.PNG"), Fit = BackgroundFit.Cover },
            Note = new NoteAppearance { Shape = NoteShape.RoundedRectangle, CornerRadius = 8,
                Opacity = 0.6, Brightness = 0.8, Emission = 2 },
            Glow = new GlowSettings { Enabled = true, Intensity = 0.7 },
            KeyboardLights = new KeyboardLightSettings { KeyboardEmission = 2, HitEmission = 3, NearStrength = 1 },
            Particles = new ParticleSettings { Enabled = true, Amount = 32, Turbulence = 1, Beam = true, Curves = true }
        },
        Export = new ExportPreferences { OutputPath = Path.Combine(storageRoot, "片段.mp4"), StartSeconds = 0.1, EndSeconds = 0.4 }
    };
    ProjectStorage.SaveProject(projectFile, savedProject);
    var loadedProject = ProjectStorage.LoadProject(projectFile);
    Check(loadedProject.MidiPath == "素材.mid" && loadedProject.Export.OutputPath == "片段.mp4",
        "Files inside project directory are stored as relative references");
    Check(loadedProject.Visual.FirstPitch == 48 && loadedProject.Visual.LastPitch == 84 &&
        loadedProject.Visual.ColorMode == NoteColorMode.Track && loadedProject.Visual.ChannelColors[0] == "00ff00ff" &&
        loadedProject.Visual.TrackColors[2] == "ff0000ff", "Project restores both palettes and keyboard range");
    Check(loadedProject.TimeSeconds == 0.2 && !loadedProject.SettingsVisible &&
        loadedProject.Export.StartSeconds == 0.1 && loadedProject.Export.EndSeconds == 0.4,
        "Project preserves playback position, sidebar preference and export interval");
    Check(loadedProject.Audio.Path == "素材.ogg" && !loadedProject.Audio.Enabled && loadedProject.Audio.OffsetSeconds == -0.35,
        "Project stores relative audio reference, toggle and offset");
    Check(loadedProject.Visual.Keyboard == savedProject.Visual.Keyboard &&
        loadedProject.Visual.Background == savedProject.Visual.Background with { ImagePath = "背景.PNG" },
        "Project preserves keyboard appearance, background color, fit and relative image reference");
    Check(loadedProject.Visual.Note == savedProject.Visual.Note && loadedProject.Visual.Glow == savedProject.Visual.Glow,
        "Project preserves note shape, opacity, brightness, emission and global glow");
    Check(loadedProject.Visual.KeyboardLights == savedProject.Visual.KeyboardLights && loadedProject.Visual.Particles == savedProject.Visual.Particles,
        "Project preserves lighting, GPU particles, turbulence, beams and curves");
    string recoveryFile = Path.Combine(storageRoot, "recovery.json");
    ProjectStorage.SaveRecovery(recoveryFile, new RecoveryData { ProjectPath = projectFile, Project = savedProject });
    var recovery = ProjectStorage.LoadRecovery(recoveryFile);
    Check(recovery.ProjectPath == projectFile && recovery.Project.MidiPath == midiPath &&
        recovery.Project.Visual.Particles == savedProject.Visual.Particles, "Recovery keeps absolute resources and original project association");
    Check(ProjectStorage.LoadProject(projectFile).TimeSeconds == savedProject.TimeSeconds, "Autosave does not overwrite user project");
    Check(ProjectStorage.ResolveVisualReferences(loadedProject.Visual,
        Path.Combine(storageRoot, "moved", "song.trifle.json")).Background.ImagePath ==
        Path.Combine(storageRoot, "moved", "背景.PNG"), "Image reference follows relocated project directory");
    Check(ProjectStorage.ResolveReference(loadedProject.Audio.Path, Path.Combine(storageRoot, "moved", "song.trifle.json")) ==
        Path.Combine(storageRoot, "moved", "素材.ogg"), "Moving project resolves audio against new project directory");
    string externalMidi = Path.Combine(Path.GetTempPath(), "other.mid");
    Check(Path.IsPathFullyQualified(ProjectStorage.MakeReference(externalMidi, projectFile)),
        "External MIDI reference remains absolute");
    string moved = Path.Combine(storageRoot, "moved");
    Directory.CreateDirectory(moved);
    File.Copy(projectFile, Path.Combine(moved, Path.GetFileName(projectFile)));
    File.Copy(midiPath, Path.Combine(moved, Path.GetFileName(midiPath)));
    Check(File.Exists(ProjectStorage.ResolveReference(loadedProject.MidiPath, Path.Combine(moved, "song.trifle.json"))),
        "Moving project and local MIDI together preserves reference");
    string presetFile = Path.Combine(storageRoot, "visual.trifle-preset.json");
    ProjectStorage.SavePreset(presetFile, new VisualPreset { Visual = savedProject.Visual });
    var loadedPreset = ProjectStorage.LoadPreset(presetFile);
    Check(loadedPreset.Visual.Note == savedProject.Visual.Note && loadedPreset.Visual.Glow == savedProject.Visual.Glow,
        "Visual preset preserves note style and glow");
    Check(loadedPreset.Visual.Keyboard == savedProject.Visual.Keyboard &&
        loadedPreset.Visual.Background.ImagePath == "背景.PNG" &&
        ProjectStorage.ResolveVisualReferences(loadedPreset.Visual, presetFile).Background == savedProject.Visual.Background,
        "Preset preserves appearance and resolves image relative to its own file");
    Check(ProjectStorage.LoadPreset(presetFile).Visual.TrackColors[2] == "ff0000ff" &&
        !File.ReadAllText(presetFile).Contains("midiPath") && !File.ReadAllText(presetFile).Contains("outputPath"),
        "Visual preset has colors without song or export references");
    Rejected(() => ProjectStorage.LoadProject(presetFile), "Preset cannot be loaded as a project");
    Rejected(() => ProjectStorage.LoadPreset(projectFile), "Project cannot be loaded as a preset");
    string minimalFile = Path.Combine(storageRoot, "minimal.trifle.json");
    File.WriteAllText(minimalFile, "{\"kind\":\"trifle-project\",\"version\":1,\"midiPath\":\"素材.mid\"}");
    var minimal = ProjectStorage.LoadProject(minimalFile);
    Check(minimal.Visual.FirstPitch == 21 && minimal.Visual.LastPitch == 108 && minimal.Visual.LookAheadSeconds == 6 &&
        minimal.Export.EndSeconds == null,
        "Version 1 files with omitted optional parameters receive defaults");
    Check(minimal.Audio.Path == "" && minimal.Audio.OffsetSeconds == 0, "Older version 1 projects default to no audio");
    Check(minimal.Visual.Keyboard == new KeyboardAppearance() && minimal.Visual.Background == new BackgroundSettings(),
        "Older projects receive default keyboard layout, colors and no image");
    Check(minimal.Visual.Note == new NoteAppearance() && minimal.Visual.Glow == new GlowSettings(),
        "Older projects default to opaque rectangular notes, unit brightness, no emission and no glow");
    Check(minimal.Visual.KeyboardLights == new KeyboardLightSettings() && minimal.Visual.Particles == new ParticleSettings() &&
        minimal.Preview == new Trifle.App.PreviewSettings() && minimal.Export.Height == 1080 && minimal.Export.FramesPerSecond == 30,
        "Older projects receive lighting, particle, preview and export defaults");
    File.WriteAllText(presetFile, "{\"kind\":\"trifle-preset\",\"version\":1}");
    Check(ProjectStorage.LoadPreset(presetFile).Visual.Background == new BackgroundSettings(),
        "Older visual presets receive default background");
    Check(ProjectStorage.LoadPreset(presetFile).Visual.Note == new NoteAppearance() &&
        ProjectStorage.LoadPreset(presetFile).Visual.Glow == new GlowSettings(), "Older visual presets receive default note style and glow");
    File.WriteAllText(path: Path.Combine(storageRoot, "null_audio.json"), contents:
        "{\"kind\":\"trifle-project\",\"version\":1,\"midiPath\":\"素材.mid\",\"audio\":null}");
    Rejected(() => ProjectStorage.LoadProject(Path.Combine(storageRoot, "null_audio.json")), "Null audio block rejected");
    string invalidFile = Path.Combine(storageRoot, "invalid.json");
    File.WriteAllText(invalidFile, "{\"kind\":\"trifle-project\",\"version\":2,\"midiPath\":\"素材.mid\"}");
    Rejected(() => ProjectStorage.LoadProject(invalidFile), "Future project version rejected explicitly");
    File.WriteAllText(invalidFile, "{\"midiPath\":\"素材.mid\"}");
    Rejected(() => ProjectStorage.LoadProject(invalidFile), "Missing required document header rejected");
    File.WriteAllText(invalidFile, "{\"kind\":\"trifle-project\",\"version\":1,\"midiPath\":\"素材.mid\",\"visual\":null}");
    Rejected(() => ProjectStorage.LoadProject(invalidFile), "Null visual block rejected");
    File.WriteAllText(invalidFile, "{broken");
    Rejected(() => ProjectStorage.LoadProject(invalidFile), "Truncated JSON rejected");
    Rejected(() => (savedProject.Visual with { FirstPitch = 90, LastPitch = 60 }).Validate(), "Invalid saved keyboard range rejected");
    Rejected(() => (savedProject.Visual with { ChannelColors = new() { [16] = "00ff00ff" } }).Validate(), "Out-of-range channel color rejected");
    Rejected(() => (savedProject.Visual with { TrackColors = new() { [2] = "invalid" } }).Validate(), "Malformed saved color rejected");
    Rejected(() => savedProject.Visual.Validate(2), "Project track color must fit loaded MIDI track count");
    Rejected(() => (savedProject.Visual with { ColorMode = (NoteColorMode)99 }).Validate(), "Unknown saved color mode rejected");
    Rejected(() => savedProject.Export.Validate(0.3), "Export interval beyond replacement song rejected");
    var emptyExport = new ExportPreferences { EndSeconds = 0 };
    emptyExport.Validate(0);
    Check(emptyExport.EndSeconds == 0, "Empty MIDI can keep a zero-length export draft");
    Rejected(() => emptyExport.Validate(1), "Nonempty song rejects zero-length export draft");
    string originalJson = File.ReadAllText(projectFile);
    Rejected(() => ProjectStorage.SaveProject(projectFile, savedProject with { Visual = savedProject.Visual with { LookAheadSeconds = 0 } }),
        "Invalid save fails before replacing project");
    Check(File.ReadAllText(projectFile) == originalJson, "Failed save preserves existing project file");
    string directoryTarget = Path.Combine(storageRoot, "directory.trifle.json");
    Directory.CreateDirectory(directoryTarget);
    Rejected(() => ProjectStorage.SaveProject(directoryTarget, savedProject), "Failed final rename is reported");
    Check(Directory.GetFiles(storageRoot, ".trifle_*.tmp").Length == 0, "Temporary project files cleaned after success and failure");
}
finally
{
    string checksPrefix = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Trifle", "checks")) + Path.DirectorySeparatorChar;
    if (storageRoot.StartsWith(checksPrefix, StringComparison.OrdinalIgnoreCase)) Directory.Delete(storageRoot, true);
}

var indexedNotes = new[]
{
    new MidiNote(0,0,60,100,0,100),
    new MidiNote(0,0,61,100,1,2),
    new MidiNote(0,0,62,100,1,3),
    new MidiNote(0,0,63,100,50,51)
};
var index = new MidiNoteIndex(indexedNotes);
Check(index.FirstStartingAtOrAfter(1) == 1 && index.FirstStartingAfter(1) == 3, "Index distinguishes onset boundaries and simultaneous notes");
Check(index.FirstStillRelevant(60) == 0 && index.FirstStillRelevant(100) == 4, "Long notes survive seeking past later short notes");
Check(index.FirstStartingAfter(-1) == 0 && index.FirstStartingAtOrAfter(999) == 4, "Index handles before and after song bounds");
Check(new MidiNoteIndex(Array.Empty<MidiNote>()).FirstStillRelevant(0) == 0, "Empty note index is safe");
Rejected(() => new MidiNoteIndex(indexedNotes.Reverse().ToArray()), "Unsorted note index rejected");
Rejected(() => new KeyboardLightSettings { NearDistance = double.NaN }.Validate(), "Nonfinite light settings rejected");
Rejected(() => new ParticleSettings { Amount = 10000 }.Validate(), "Unbounded particle count rejected");
Rejected(() => new ParticleSettings { Turbulence = double.PositiveInfinity }.Validate(), "Nonfinite turbulence rejected");
Rejected(() => new VisualSettings { Particles = null }.Validate(), "Null particle block rejected");
Rejected(() => new BackgroundSettings { Gradient = (BackgroundGradient)9 }.Validate(), "Unknown gradient rejected");
Rejected(() => new BackgroundSettings { EndColor = "bad" }.Validate(), "Invalid gradient endpoint rejected");
foreach (int height in new[] { 1080, 1440, 2160 })
    foreach (int fps in new[] { 24, 30, 60 })
    {
        var preferences = new ExportPreferences { Width = height * 16 / 9, Height = height, FramesPerSecond = fps };
        preferences.Validate();
        Check(preferences.Width * 9 == height * 16, $"Export preset {height}p/{fps} accepted");
    }
Rejected(() => new ExportPreferences { Height = 720 }.Validate(), "Invalid export preset dimensions rejected");
Rejected(() => new ExportPreferences { FramesPerSecond = 25 }.Validate(), "Unsupported export draft rate rejected");
foreach (int height in new[] { 540, 720, 1080, 1440, 2160 })
    new Trifle.App.PreviewSettings { Height = height }.Validate();
Rejected(() => new Trifle.App.PreviewSettings { FramesPerSecond = 0 }.Validate(), "Invalid preview rate rejected");
Rejected(() => new ProjectData { MidiPath = "song.mid", Preview = null }.Validate(), "Null preview block rejected");

if (args.Length > 0)
{
    var sample = MidiImporter.Read(args[0]);
    Check(sample.Notes.Length > 0 && sample.Notes.All(note => note.EndSeconds >= note.StartSeconds),
        "Provided sample imports with valid note intervals");
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        sample.SourcePath, sample.Format, sample.DurationSeconds,
        Tracks = sample.Tracks, TempoSegments = sample.TempoChanges.Length,
        FirstTempo = sample.TempoChanges[0], LastTempo = sample.TempoChanges[^1],
        NoteCount = sample.Notes.Length,
        PitchMin = sample.Notes.Min(note => note.Pitch), PitchMax = sample.Notes.Max(note => note.Pitch),
        FirstNote = sample.Notes[0], LastNote = sample.Notes.OrderBy(note => note.EndSeconds).Last()
    }));
}
if (args.Length > 1)
{
    Directory.CreateDirectory(args[1]);
    tempoFile.Write(Path.Combine(args[1], "tempo_change.mid"), false);
    overlapFile.Write(Path.Combine(args[1], "overlap.mid"), false, MidiFileFormat.SingleTrack);
    silentFile.Write(Path.Combine(args[1], "velocity_zero.mid"), false);
    channelFile.Write(Path.Combine(args[1], "channels.mid"), false);
    trackFile.Write(Path.Combine(args[1], "tracks.mid"), false);
    FileWith(new TrackChunk()).Write(Path.Combine(args[1], "empty.mid"), false);
}
Console.WriteLine($"{checks} checks passed.");
