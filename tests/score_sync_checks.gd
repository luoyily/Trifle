extends "res://tests/score_preload_checks.gd"

const SYNC := "Margin/Content/Body/SettingsPanel/Margin/Content/Scroll/Groups/AudioPanel/Fields/Content/ScoreSyncPanel/"

func score_fixture(path: String):
	super.fixture(path)
	var data: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(path))
	var positions := Marshalls.base64_to_utf8(data.mposXML)
	var events := "<events>"
	for index in 6: events += '<event elid="%d" position="%d"/>' % [index, index * 2000]
	events += "</events>"
	data.mposXML = Marshalls.utf8_to_base64(positions.replace("</score>", events + "</score>"))
	var segments := Marshalls.base64_to_utf8(data.sposXML)
	segments = segments.substr(0, segments.find("<events>")) + events + "</score>"
	data.sposXML = Marshalls.utf8_to_base64(segments)
	data.metadata.duration = 12
	var file := FileAccess.open(path, FileAccess.WRITE)
	file.store_string(JSON.stringify(data)); file.close()

func vlq(number: int) -> PackedByteArray:
	var bytes: Array[int] = [number & 127]
	number >>= 7
	while number > 0:
		bytes.push_front((number & 127) | 128); number >>= 7
	return PackedByteArray(bytes)

func midi_fixture(path: String):
	var track := "00ff51030f424000ff580404021808".hex_decode()
	track.append_array(vlq(123)); track.append_array("903c64".hex_decode())
	track.append_array(vlq(117)); track.append_array("803c00".hex_decode())
	track.append_array(vlq(11520 - 240)); track.append_array("ff2f00".hex_decode())
	var file := FileAccess.open(path, FileAccess.WRITE); file.big_endian = true
	file.store_buffer("4d546864000000060001000101e04d54726b".hex_decode())
	file.store_32(track.size()); file.store_buffer(track); file.close()

func project_variant(source: String, target: String, key: String, value):
	# Preserve C# integer fields; a Godot JSON round trip converts every number to float.
	var regex := RegEx.new(); regex.compile('"' + key + '"\\s*:\\s*"[^"]*"')
	var text := regex.sub(FileAccess.get_file_as_string(source), '"' + key + '": ' + JSON.stringify(value))
	var file := FileAccess.open(target, FileAccess.WRITE); file.store_string(text); file.close()

func same_path(a: String, b: String) -> bool:
	return a.replace("\\", "/").to_lower() == b.replace("\\", "/").to_lower()

func run():
	create_timer(120).timeout.connect(func(): push_error("Score synchronization checks timed out"); quit(1))
	root.size = Vector2i(1280, 900)
	var directory := ProjectSettings.globalize_path(output)
	var score_path := directory + "score.json"
	var midi_path := directory + "performance.mid"
	score_fixture(score_path); midi_fixture(midi_path)
	main = load("res://app/main.tscn").instantiate()
	root.add_child(main); current_scene = main; await process_frame
	check(main.call("LoadScoreBundleFile", score_path) and state().midi_from_score, "Score alone defaults to its bundled MIDI")
	var silent := AudioStreamWAV.new(); silent.format = AudioStreamWAV.FORMAT_16_BITS; silent.mix_rate = 48000
	var samples := PackedByteArray(); samples.resize(24000); silent.data = samples
	var audio_path := directory + "silence.wav"
	check(silent.save_to_wav(audio_path) == OK and main.call("LoadAudioFile", audio_path), "Attach companion audio before changing MIDI sources")
	main.call("LoadMidiFile", midi_path)
	check(state().available and not state().midi_from_score and main.call("GetSongInfo").path == midi_path, "Loading MIDI after a score keeps both sources")
	check(same_path(main.call("GetAudioStatus").path, audio_path), "Attaching external MIDI preserves the existing companion audio")
	check(abs(state().timeline_duration - 24) < .001, "External playback duration uses the MIDI bar grid")
	main.call("SetTime", 7.999); await ready()
	check(state().row == 1, "External playback stays in the current row before its mapped boundary")
	var foreground: int = state().raster_count
	main.call("SetTime", 8.0)
	check(state().row == 2 and state().raster_count == foreground, "Mapped row transition uses a preloaded texture")
	check(main.call("SetScoreMidiSource", true) and state().row == 4, "Source switch restores original MuseScore timing")
	check(same_path(main.call("GetAudioStatus").path, audio_path), "Switching back to bundled MIDI also preserves audio")
	check(main.call("SetScoreMidiSource", false) and state().row == 2, "Source switch reuses the independent external MIDI reference")
	main.call("SetScoreMeasureOffset", 1)
	check(state().measure_offset == 1 and state().row == 1 and abs(state().timeline_duration - 28) < .001, "Positive offset moves the score by one MIDI bar")
	main.call("JumpScoreRow", 1)
	check(state().row == 2 and abs(state().time - 12) < .001, "Row navigation seeks the remapped event time")
	main.call("SetScoreMeasureOffset", -1); main.call("SetTime", 0)
	check(state().row == 1, "Negative offset skips the first score bar at playback zero")
	main.call("SetTime", 4)
	check(state().row == 2 and state().cursor_gain > 2, "Cursor pulse uses the remapped external onset")
	main.call("SetTime", 4.5)
	check(abs(state().cursor_gain - 1) < .001, "Cursor pulse still decays after half a second")
	var saved := directory + "external.trifle.json"
	check(main.call("SaveProjectFile", saved), "Save independent MIDI and score as a project")
	main.call("ClearMidiFile")
	check(main.call("LoadProjectFile", saved) and not state().midi_from_score and state().measure_offset == -1 and state().row == 2, "Project restores independent sources and offset")
	check(main.call("SaveRecoveryNow"), "Save external synchronization in recovery")
	main.queue_free(); await process_frame
	main = load("res://app/main.tscn").instantiate(); root.add_child(main); current_scene = main
	await process_frame
	check(main.call("RestoreRecovery") and not state().midi_from_score and state().measure_offset == -1 and abs(state().time - 4.5) < .001, "Recovery restores synchronization and playback position")
	var missing := directory + "missing_score.trifle.json"
	project_variant(saved, missing, "scorePath", "missing.json")
	check(not main.call("LoadProjectFile", missing) and state().available, "Missing score leaves the current session intact")
	var relinked: bool = main.call("RelinkProjectScore", score_path)
	if not relinked: print("Relink score diagnostic: ", main.get_node("Margin/Content/Status").text)
	check(relinked and state().available, "Relinking the independent score restores the project")
	missing = directory + "missing_midi.trifle.json"
	project_variant(saved, missing, "midiPath", "missing.mid")
	check(not main.call("LoadProjectFile", missing) and state().available, "Missing external MIDI leaves the current session intact")
	relinked = main.call("RelinkProjectMidi", midi_path)
	if not relinked: print("Relink MIDI diagnostic: ", main.get_node("Margin/Content/Status").text)
	check(relinked and not state().midi_from_score, "Relinking external MIDI preserves the score reference")
	check(main.call("SetScoreMidiSource", true), "Switch project to bundled MIDI")
	var internal_project := directory + "internal.trifle.json"
	check(main.call("SaveProjectFile", internal_project) and main.call("LoadProjectFile", internal_project), "Bundled-source project also saves the external source choice")
	check(main.call("SetScoreMidiSource", false), "Restored bundled project can switch back to its external MIDI")
	main.call("ClearScoreFile")
	check(not state().available and same_path(main.call("GetSongInfo").path, midi_path), "Clearing score retains the external MIDI")
	check(main.call("LoadScoreBundleFile", score_path) and not state().midi_from_score, "Loading a score after MIDI keeps the existing external performance")
	main.call("ClearMidiFile")
	main.call("HandleFilesDropped", PackedStringArray([midi_path, score_path]))
	check(state().available and not state().midi_from_score, "Dropping one MIDI and one score together loads both")
	var controls := main.get_node(SYNC)
	check(controls.get_node("Fields/Content/Source/Value").selected == 1 and controls.get_node("Fields/Content/Offset/Value").editable, "Synchronization controls reflect the active external source")
	controls.get_node("Fields/Content/Offset/Value").value = 2
	check(state().measure_offset == 2, "UI offset edits update the playback mapping")
	main.call("SetScoreMeasureOffset", 1000)
	check(state().measure_offset == 2, "Invalid offset leaves the usable timeline intact")
	var repeated := directory + "repeated.json"
	var repeated_data: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(score_path))
	repeated_data.mposXML = Marshalls.utf8_to_base64(Marshalls.base64_to_utf8(repeated_data.mposXML).replace('elid="5"', 'elid="0"'))
	var file := FileAccess.open(repeated, FileAccess.WRITE); file.store_string(JSON.stringify(repeated_data)); file.close()
	check(not main.call("LoadScoreBundleFile", repeated) and same_path(state().path, score_path) and not state().midi_from_score, "Unsupported repeat import preserves the current score and external MIDI")
	controls.get_node("Fields/Content/Source/Value").emit_signal("item_selected", 0)
	check(state().midi_from_score and not controls.get_node("Fields/Content/Offset/Value").editable, "UI source switch disables the external offset for bundled MIDI")
	var settings := main.get_node("Margin/Content/Body/SettingsPanel")
	settings.get_node("Margin/Content/Scroll/Groups/AudioPanel/Header").button_pressed = true
	controls.get_node("Header").button_pressed = true
	await settled()
	check(controls.get_node("Fields/Content/Source/Label").global_position.x > settings.get_node("Margin/Content/Scroll/Groups/AudioPanel/Fields/Content/Offset/Label").global_position.x, "Synchronization fields retain the nested sidebar indentation")
	root.get_texture().get_image().save_png(directory + "synchronization_ui.png")
	print("Synchronization UI screenshot: ", directory + "synchronization_ui.png")
	main.queue_free(); await process_frame
	print("Score synchronization UI: ", checks, " checks passed.")
	quit(0)
