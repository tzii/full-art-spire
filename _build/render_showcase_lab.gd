extends SceneTree

# Renders layered cards with the rarity-showcase overlay in the game's own Godot.
# usage: -- layer_shader effect_shader plan.json output_dir

func _initialize() -> void:
	call_deferred("_run")

func _texture(path: String) -> ImageTexture:
	var image: Image
	if path.get_extension().to_lower() == "dds":
		image = Image.new()
		if image.load_dds_from_buffer(FileAccess.get_file_as_bytes(path)) != OK:
			push_error("DDS load failed: " + path)
			return null
	else:
		image = Image.load_from_file(path)
	if not image.has_mipmaps() and not image.is_compressed():
		image.generate_mipmaps()
	return ImageTexture.create_from_image(image)

func _value(value):
	if value is Array and value.size() == 2:
		return Vector2(float(value[0]), float(value[1]))
	if value is Array and value.size() == 3:
		return Color(float(value[0]), float(value[1]), float(value[2]))
	return value

func _run() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 4:
		push_error("usage: -- layer_shader effect_shader plan output")
		quit(2)
		return
	var layer_shader := Shader.new()
	layer_shader.code = FileAccess.get_file_as_string(args[0])
	var effect_shader := Shader.new()
	effect_shader.code = FileAccess.get_file_as_string(args[1])
	var jobs: Array = JSON.parse_string(FileAccess.get_file_as_string(args[2]))
	var textures := {}
	for job: Dictionary in jobs:
		for key in ["art", "background", "mask"]:
			if not textures.has(job[key]):
				textures[job[key]] = _texture(job[key])
				if textures[job[key]] == null:
					quit(2)
					return
		var art: ImageTexture = textures[job["art"]]
		var mask: ImageTexture = textures[job["mask"]]
		var width := int(job.get("width", 384))
		var viewport := SubViewport.new()
		viewport.size = Vector2i(width, width * 3 / 2)
		viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
		root.add_child(viewport)
		var portrait := TextureRect.new()
		portrait.texture = art
		portrait.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		portrait.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_COVERED
		portrait.texture_filter = CanvasItem.TEXTURE_FILTER_LINEAR_WITH_MIPMAPS
		portrait.size = Vector2(viewport.size)
		viewport.add_child(portrait)
		var layer := ShaderMaterial.new()
		layer.shader = layer_shader
		layer.set_shader_parameter("foreground_texture", art)
		layer.set_shader_parameter("background_texture", textures[job["background"]])
		layer.set_shader_parameter("foreground_mask_texture", mask)
		layer.set_shader_parameter("use_foreground_mask", true)
		layer.set_shader_parameter("use_normalized_source_uv", true)
		for name in job["layer"]:
			layer.set_shader_parameter(name, _value(job["layer"][name]))
		portrait.material = layer
		if job.has("effect"):
			var overlay := TextureRect.new()
			overlay.texture = art
			overlay.expand_mode = portrait.expand_mode
			overlay.stretch_mode = portrait.stretch_mode
			overlay.texture_filter = portrait.texture_filter
			overlay.z_index = 1
			var effect := ShaderMaterial.new()
			effect.shader = effect_shader
			effect.set_shader_parameter("permission_mask_texture", mask)
			effect.set_shader_parameter("layer_coverage_texture", mask)
			effect.set_shader_parameter("use_layered_uv", true)
			for name in ["view_pointer", "depth", "foreground_shift", "foreground_scale",
				"subject_scale", "subject_offset_x", "subject_offset_y", "background_shift"]:
				effect.set_shader_parameter(name, layer.get_shader_parameter(name))
			for name in job["effect"]:
				effect.set_shader_parameter(name, _value(job["effect"][name]))
			overlay.material = effect
			portrait.add_child(overlay)
			overlay.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
		await process_frame
		await process_frame
		RenderingServer.force_draw(false)
		if viewport.get_texture().get_image().save_png(args[3].path_join(str(job["name"]) + ".png")) != OK:
			push_error("capture failed: " + str(job["name"]))
			quit(3)
			return
		viewport.queue_free()
		await process_frame
	print("SHOWCASE_LAB_COMPLETE " + str(jobs.size()))
	quit(0)
