extends SceneTree

# Run with a rendering device, not --headless: these checks exercise real GPU output.
var main: Control
var checks := 0
var output := "user://render_checks/"

func _initialize():
	var original := OS.get_user_data_dir()
	ProjectSettings.set_setting("application/config/use_custom_user_dir", true)
	ProjectSettings.set_setting("application/config/custom_user_dir_name", "Trifle Render Checks/" + Time.get_datetime_string_from_system().replace(":", "-"))
	assert(OS.get_user_data_dir() != original)
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(output))
	prepare_cache_directories(original + "/shader_cache", OS.get_user_data_dir() + "/shader_cache")
	call_deferred("run")

func prepare_cache_directories(source: String, destination: String):
	var directory := DirAccess.open(source)
	if directory == null: return
	DirAccess.make_dir_recursive_absolute(destination)
	for child in directory.get_directories():
		prepare_cache_directories(source + "/" + child, destination + "/" + child)

func check(value: bool, label: String):
	if not value:
		push_error("FAIL: " + label)
		quit(1)
		assert(value, label)
	checks += 1
	print("PASS: " + label)

func settled():
	for _frame in 3:
		await process_frame
		await RenderingServer.frame_post_draw

func capture() -> Image:
	await settled()
	return main.get_node("PreviewViewport").get_texture().get_image()

func screen_rect() -> Rect2:
	var preview: Control = main.get_node("Margin/Content/Body/PreviewArea/Preview")
	var rect: Rect2 = preview.call("GetDrawRect")
	return Rect2(preview.global_position + rect.position, rect.size)

func add_strip(parent: Node, center: float, width: float):
	var strip := ColorRect.new()
	strip.position = Vector2(center - width * 0.5, 300)
	strip.size = Vector2(width, 180)
	strip.color = Color.WHITE
	var material := ShaderMaterial.new()
	material.shader = load("res://score/brightness.gdshader")
	material.set_shader_parameter("brightness", 3.0)
	strip.material = material
	parent.add_child(strip)

func level_profile(center: float) -> Dictionary:
	var material: ShaderMaterial = main.get_node("PreviewViewport/Present").material
	var texture: Texture2D = material.get_shader_parameter("glow_level_0")
	var image := texture.get_image()
	var scale := image.get_width() / 1920.0
	var peak := 0.0
	var integral := 0.0
	var extent := 0.0
	for x in range(int((center - 140) * scale), int((center + 140) * scale)):
		var value := image.get_pixel(x, int(390 * scale)).r
		peak = max(peak, value)
		integral += value / scale
		if value > 0.01: extent = max(extent, abs((x + 0.5) / scale - center))
	return {"peak": peak, "integral": integral, "extent": extent}

func run():
	create_timer(90).timeout.connect(func(): push_error("Render checks timed out"); quit(1))
	root.size = Vector2i(1280, 900)
	main = load("res://app/main.tscn").instantiate()
	root.add_child(main); current_scene = main
	await process_frame
	main.get_node("Margin/Content/Body/PreviewArea/EmptyState").hide()
	var visualizer: Node2D = main.get_node("HdrViewport/Visualizer")
	for child in visualizer.get_children():
		if child is CanvasItem: child.hide()
	main.call("SetGlow", false, 1.0)
	main.get_node("PreviewViewport/Present").material.set_shader_parameter("key_light_strength", 0.0)
	var strips := Node2D.new()
	visualizer.add_child(strips)
	add_strip(strips, 600.0, 3.0)
	add_strip(strips, 1200.0, 40.0)
	var reference := await capture()
	var rect := screen_rect()
	check(rect.position == rect.position.round() and rect.size == rect.size.round(), "Window preview edges align to whole screen pixels")
	main.call("SetUiVisible", false)
	root.size = Vector2i(1920, 1080)
	await settled()
	var exact := root.get_texture().get_image()
	check(reference.get_data() == exact.get_data(), "1:1 window presentation preserves every source pixel")
	main.call("SetUiVisible", true)
	main.get_node("Margin/Content/Body/PreviewArea/EmptyState").hide()
	root.size = Vector2i(1280, 900)
	await settled()
	check(reference.get_data() == main.get_node("PreviewViewport").get_texture().get_image().get_data(), "Window resizing preserves the full-resolution capture texture")
	# A 4K one-pixel checkerboard must average to gray when shrunk into the window.
	# Bilinear-only sampling aliases this pattern at non-integer scale factors.
	strips.hide()
	var board := ColorRect.new()
	board.size = Vector2(1920, 1080)
	var shader := Shader.new()
	shader.code = "shader_type canvas_item; render_mode unshaded, blend_disabled; void fragment() { float v = mod(floor(FRAGCOORD.x) + floor(FRAGCOORD.y), 2.0); COLOR = vec4(vec3(v), 1.0); }"
	board.material = ShaderMaterial.new(); board.material.shader = shader
	visualizer.add_child(board)
	main.call("SetPreviewFormat", 2160, 60)
	await settled()
	rect = screen_rect()
	var window := root.get_texture().get_image()
	var maximum_error := 0.0
	for y in range(int(rect.position.y + rect.size.y * 0.4), int(rect.position.y + rect.size.y * 0.6)):
		for x in range(int(rect.position.x + rect.size.x * 0.4), int(rect.position.x + rect.size.x * 0.6)):
			maximum_error = max(maximum_error, abs(window.get_pixel(x, y).r - 0.5))
	check(maximum_error < 0.03, "Non-integer 4K window downsampling suppresses checkerboard aliasing")
	board.queue_free(); strips.show()
	var profiles := {}
	for height in [1080, 2160]:
		main.call("SetPreviewFormat", height, 60)
		main.call("SetGlow", false, 1.0)
		var off := await capture()
		main.call("SetGlow", true, 1.0)
		var on := await capture()
		var scale: float = height / 1080.0
		check(on.get_pixel(int(612 * scale), int(390 * scale)).r > off.get_pixel(int(612 * scale), int(390 * scale)).r + 0.02,
			"Thin 3-pixel HDR stroke produces visible global glow at %dp" % height)
		profiles[str(height)] = {"thin": level_profile(600.0), "wide": level_profile(1200.0)}
		check(on.save_png(output + "glow_%d.png" % height) == OK, "Captured real GPU glow at %dp" % height)
	for label in ["thin", "wide"]:
		var low: Dictionary = profiles["1080"][label]
		var high: Dictionary = profiles["2160"][label]
		check(abs(low.peak - high.peak) / high.peak < 0.02 and abs(low.integral - high.integral) / high.integral < 0.02 and abs(low.extent - high.extent) <= 4,
			"%s stroke keeps glow brightness and range across 1080p/4K" % label)
	# Export captures after one process_frame/frame_post_draw pair. All bloom passes
	# must already contain the new source frame at that point.
	strips.position.x = 72
	await process_frame
	await RenderingServer.frame_post_draw
	var first_frame: Image = main.get_node("PreviewViewport").get_texture().get_image()
	var stable_frame := await capture()
	check(first_frame.get_data() == stable_frame.get_data(), "A single export frame wait updates the entire glow pyramid")
	main.call("SetGlow", true, 0.0)
	var zero := await capture()
	main.call("SetGlow", false, 1.0)
	var disabled := await capture()
	check(zero.get_data() == disabled.get_data(), "Zero intensity and disabled glow both restore the source composite")
	check(main.get_node("HdrGlow/BrightParts").render_target_update_mode == SubViewport.UPDATE_DISABLED, "Disabled glow stops the GPU passes")
	print("Render checks: ", checks, " passed. Output: ", ProjectSettings.globalize_path(output), " Profiles: ", profiles)
	main.queue_free(); await process_frame
	quit(0)
