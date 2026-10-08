extends "res://tests/render_checks.gd"

# Self-contained synthetic score with two pages and a repeated first-page passage.
func fixture(path: String):
	var pages: Array[String] = []
	var positions := ""
	for page in 2:
		var paths := ""
		for local in 3:
			var row := page * 3 + local
			var top := 2000 + local * 3000
			positions += '<element id="%d" page="%d" x="18000" y="%d" sx="84000" sy="14400"/>' % [row, page, top * 12]
			paths += '<path class="Note" d="M2000,%d C1750,%d 2450,%d 2600,%d Z"/><path stroke="#000000" fill="none" stroke-width="14" d="M1800,%d L8600,%d"/>' % [top + 500, top, top, top + 350, top + 750, top + 750]
		pages.append(Marshalls.raw_to_base64(('<svg xmlns="http://www.w3.org/2000/svg" width="10000" height="12000" viewBox="0 0 10000 12000">' + paths + '</svg>').to_utf8_buffer()))
	var events := ""
	var order := [0, 1, 2, 0, 1, 2, 3, 4, 5]
	for index in order.size():
		events += '<event elid="%d" position="%d"/>' % [order[index], index * 2000]
	var metadata := {"svgs": pages,
		"mposXML": Marshalls.raw_to_base64(('<score><elements>' + positions + '</elements></score>').to_utf8_buffer()),
		"sposXML": Marshalls.raw_to_base64(('<score><elements>' + positions + '</elements><events>' + events + '</events></score>').to_utf8_buffer()),
		"midi": Marshalls.raw_to_base64("4d546864000000060000000101e04d54726b0000000400ff2f00".hex_decode()),
		"metadata": {"title": "Preload fixture", "duration": 20}}
	var file := FileAccess.open(path, FileAccess.WRITE)
	file.store_string(JSON.stringify(metadata)); file.close()

func state() -> Dictionary:
	return main.call("GetScoreInfo")

func ready():
	for _frame in 600:
		await process_frame
		if not state().preload_pending: return
	check(false, "Background row preparation timed out")

func legacy_alpha() -> PackedByteArray:
	# Same synthetic first-system crop and opaque white theme, using the old pipeline.
	var svg := '<svg xmlns="http://www.w3.org/2000/svg" width="10000" height="1900" viewBox="0 1650 10000 1900"><g fill="#ffffff">'
	for local in 3:
		var top := 2000 + local * 3000
		svg += '<path class="Note" d="M2000,%d C1750,%d 2450,%d 2600,%d Z"/><path stroke="#ffffff" fill="none" stroke-width="14" d="M1800,%d L8600,%d"/>' % [top + 500, top, top, top + 350, top + 750, top + 750]
	svg += '</g></svg>'
	var image := Image.new()
	check(image.load_svg_from_string(svg, 1920.0 / 10000 * 2) == OK, "Render legacy supersampled comparison")
	image.fix_alpha_edges()
	image.resize((image.get_width() + 1) / 2, (image.get_height() + 1) / 2, Image.INTERPOLATE_BILINEAR)
	image.fix_alpha_edges()
	return image.get_data()

func run():
	create_timer(90).timeout.connect(func(): push_error("Score preload checks timed out"); quit(1))
	root.size = Vector2i(1280, 900)
	var path := ProjectSettings.globalize_path(output + "fixture.json")
	fixture(path)
	main = load("res://app/main.tscn").instantiate()
	root.add_child(main); current_scene = main
	await process_frame
	check(main.call("LoadScoreBundleFile", path), "Load self-contained repeated two-page score")
	main.call("SetScoreAppearance", true, 100.0, 7.0, true, Color.WHITE)
	await ready()
	check(state().cached_rows == 3 and state().preload_count >= 2, "Prepare the next two playback rows while paused")
	var image: Image = main.get_node("HdrViewport/Visualizer/Score/Notation").texture.get_image()
	var current := image.get_data()
	var legacy := legacy_alpha()
	var same := current.size() == legacy.size()
	for index in range(3, current.size(), 4):
		if current[index] != legacy[index]: same = false; break
	check(same, "Coverage mask preserves every supersampled alpha value from the legacy pipeline")
	var foreground: int = state().raster_count
	for time in [2.0, 4.0, 6.0, 8.0, 10.0, 12.0, 14.0, 16.0]:
		await ready()
		main.call("SetTime", time)
		check(state().raster_count == foreground, "Playback/repeat/page transition uses a prepared texture at %.0fs" % time)
		check(state().cached_rows <= 3, "Preloading retains the three-row texture limit")
	# Rapid generation changes must not publish old dimensions or opacity.
	main.call("SetTime", 0.0)
	for width in [30.0, 80.0, 45.0]:
		main.call("SetScoreAppearance", true, width, 7.0, true, Color("55aaff80"))
	await ready()
	foreground = state().raster_count
	main.call("SetTime", 2.0)
	check(state().raster_count == foreground and abs(state().texture_width - 864) <= 1, "A width/opacity change discards stale background results")
	main.call("SetPreviewFormat", 2160, 60)
	main.call("SetTime", 0.0)
	await ready()
	foreground = state().raster_count
	main.call("SetTime", 2.0)
	check(state().raster_count == foreground and abs(state().texture_width - 1728) <= 1, "4K preparation publishes the current resolution")
	var before := state()
	main.call("SetScoreAppearance", true, 45.0, 7.0, true, Color("ff885580"))
	check(state().raster_count == before.raster_count, "RGB recoloring updates the shader without regenerating coverage")
	main.call("SetScoreAppearance", false, 45.0, 7.0, true, Color("ff885580"))
	foreground = state().raster_count
	main.call("SetTime", 16.0)
	await settled()
	check(state().row == 5 and state().raster_count == foreground and not state().preload_pending, "Hidden score advances its clock without image generation")
	main.call("SetScoreAppearance", true, 45.0, 7.0, true, Color("ff885580"))
	check(state().visible and state().row == 5 and state().texture_width == 1728, "Showing notation restores the current row and resolution")
	main.call("SetTime", 0.0)
	main.call("SetPreviewFormat", 1080, 60)
	main.call("ClearScoreFile")
	await settled()
	check(not state().available and state().cached_rows == 0 and not state().preload_pending, "Clearing score cancels preparation and prevents stale cache insertion")
	check(main.call("LoadScoreBundleFile", path), "Reimport after cancelling preparation")
	# Exercise teardown while a native CPU preparation may still be running.
	main.queue_free(); await process_frame
	print("Score preload: ", checks, " checks passed.")
	quit(0)
