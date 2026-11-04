extends "res://tests/render_checks.gd"

# Two systems, two positions per system, plus a repeated first system.
func fixture(path: String) -> void:
	var page := '<svg xmlns="http://www.w3.org/2000/svg" width="10000" height="12000" viewBox="0 0 10000 12000">'
	page += '<path class="" fill="#ffffff" d="M0,0 L10000,0 L10000,12000 L0,12000 Z"/>'
	for top in [2000, 6000]:
		for line in 5:
			page += '<polyline class="StaffLines" fill="none" stroke="#000000" stroke-width="2" points="1500,%d 8500,%d"/>' % [top + line * 20, top + line * 20]
		page += '<path class="Clef" fill="#000000" d="M1600,%d L1620,%d L1610,%d Z"/>' % [top, top + 80, top + 40]
		page += '<path class="BarLine" fill="#000000" d="M4500,%d L4502,%d L4500,%d Z"/>' % [top, top + 80, top + 80]
		page += '<path class="MeasureNumber" fill="#000000" d="M4510,%d L4530,%d L4530,%d Z"/>' % [top - 30, top - 30, top - 10]
		page += '<path class="Note" fill="#000000" d="M1950,%d L1990,%d L1990,%d L1950,%d Z"/>' % [top + 40, top + 40, top + 80, top + 80]
		page += '<path class="Note" fill="#000000" d="M4950,%d L4990,%d L4990,%d L4950,%d Z"/>' % [top + 40, top + 40, top + 80, top + 80]
		page += '<path class="Articulation" fill="#000000" d="M4960,%d L4980,%d L4960,%d Z"/>' % [top - 10, top, top + 10]
	page += '</svg>'
	var positions := ('<element id="0" page="0" x="18000" y="24000" sx="84000" sy="2400"/>' +
		'<element id="1" page="0" x="18000" y="72000" sx="84000" sy="2400"/>')
	var segments := ('<element id="10" page="0" x="24000" y="24000" sx="1200" sy="2400"/>' +
		'<element id="20" page="0" x="60000" y="24000" sx="1200" sy="2400"/>' +
		'<element id="30" page="0" x="24000" y="72000" sx="1200" sy="2400"/>' +
		'<element id="40" page="0" x="60000" y="72000" sx="1200" sy="2400"/>')
	var events := ('<event elid="10" position="1000"/><event elid="20" position="3000"/>' +
		'<event elid="30" position="5000"/><event elid="40" position="7000"/>' +
		'<event elid="10" position="9000"/>')
	var metadata := {"svgs": [Marshalls.raw_to_base64(page.to_utf8_buffer())],
		"mposXML": Marshalls.raw_to_base64(('<score><elements>' + positions + '</elements></score>').to_utf8_buffer()),
		"sposXML": Marshalls.raw_to_base64(('<score><elements>' + segments + '</elements><events>' + events + '</events></score>').to_utf8_buffer()),
		"midi": Marshalls.raw_to_base64("4d546864000000060000000101e04d54726b0000000400ff2f00".hex_decode()),
		"metadata": {"title": "Typewriter fixture", "duration": 10}}
	var file := FileAccess.open(path, FileAccess.WRITE)
	file.store_string(JSON.stringify(metadata)); file.close()

func state() -> Dictionary:
	return main.call("GetScoreInfo")

func wait_prepared() -> void:
	for _frame in 600:
		await process_frame
		if not state().preload_pending: return
	check(false, "Typewriter preparation timed out")

func run() -> void:
	create_timer(90).timeout.connect(func(): push_error("Typewriter checks timed out"); quit(1))
	root.size = Vector2i(1280, 900)
	var path := ProjectSettings.globalize_path(output + "typewriter.json")
	fixture(path)
	main = load("res://app/main.tscn").instantiate()
	root.add_child(main); current_scene = main
	await process_frame
	check(main.call("LoadScoreBundleFile", path), "Load typewriter fixture")
	main.call("SetScoreAppearance", true, 100.0, 7.0, true, Color.WHITE)
	var panel: Node = main.find_child("ScorePanel", true, false)
	var effect: OptionButton = panel.get_node("Fields/Content/Effect/Value")
	check(effect.item_count == 2 and effect.get_item_id(0) == 0 and effect.get_item_id(1) == 1,
		"Score panel offers the two mutually exclusive effects")
	effect.select(1)
	effect.item_selected.emit(1)
	await wait_prepared()
	var score: Node2D = main.get_node("HdrViewport/Visualizer/Score")
	var grid: Sprite2D = score.get_node("Grid")
	var notation: Sprite2D = score.get_node("Notation")
	var cursor: Line2D = score.get_node("Cursor")
	check(state().display_effect == "Typewriter" and not state().cursor_visible,
		"Typewriter hides the cursor line")
	check(grid.visible and not cursor.visible, "Typewriter shows the grid layer and hides the cursor node")
	main.call("SetTime", 0.0)
	await settled()
	check(state().row == 0 and not notation.visible, "Before the first onset only the empty staff is visible")
	main.call("SetTime", 1.0)
	await settled()
	var first := notation.region_rect.size.x
	check(notation.visible and first > 0, "The first onset reveals a partial system")
	main.call("SetTime", 3.0)
	await settled()
	var second := notation.region_rect.size.x
	check(second > first, "The next onset extends the revealed region")
	var high: float = state().reveal_x
	score.call("SetTime", 3.2)
	await process_frame
	check(abs(state().reveal_x - high) < 1.0, "Forward playback keeps the high-water reveal")
	score.call("SetTime", 1.5)
	await process_frame
	check(state().reveal_x < high - 1.0, "A backward jump rewinds the typewriter reveal")
	main.call("SetTime", 1.5)
	await settled()
	check(state().reveal_x < high - 1.0, "A manual seek resets the typewriter reveal")
	main.call("SetTime", 3.0)
	await settled()
	high = state().reveal_x
	score.call("SetTime", 5.0)
	await process_frame
	score.call("SetTime", 7.0)
	await process_frame
	var second_row_high: float = state().reveal_x
	score.call("SetTime", 9.0)
	await process_frame
	check(state().row == 0 and state().reveal_x < high - 1.0,
		"A repeat across systems rewinds the typewriter")
	score.call("SetTime", 5.0)
	await process_frame
	check(state().row == 1 and state().reveal_x < second_row_high - 1.0,
		"Systems after a repeat write themselves again")
	main.call("SetTime", 5.0)
	await settled()
	check(state().row == 1 and notation.visible, "The second system appears at its first onset")
	effect.select(0)
	effect.item_selected.emit(0)
	await wait_prepared()
	check(state().display_effect == "CursorLine" and state().cursor_visible,
		"Switching back restores the cursor effect")
	check(not grid.visible and cursor.visible, "Cursor mode hides the grid layer and shows the line")
	main.queue_free(); await process_frame
	print("Typewriter: ", checks, " checks passed.")
	quit(0)
