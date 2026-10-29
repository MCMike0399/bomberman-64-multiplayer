extends Node
# External diagnostic controller: original engine, PCK and managed assembly stay unchanged.
# Never use this wrapper as the original-High qualification path.
var control := "none"
var output := ""
var observations: Array = []

func _enter_tree():
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--qa-bisect-control="):
			control = arg.get_slice("=", 1)
		if arg.begins_with("--playtest-output="):
			output = arg.get_slice("=", 1)
	RenderingServer.frame_pre_draw.connect(_before_draw)

func _ready():
	if control not in ["none", "ssil", "ssao", "glow", "shadows", "msaa"] or not output.is_absolute_path():
		push_error("Invalid external effect control")
		get_tree().root.get_node("Shutdown").call("RequestQuit", 1)
		return
	var original = load("res://scenes/main.tscn").instantiate()
	add_child(original)

func _before_draw():
	for viewport in get_tree().root.find_children("FullHighFixture", "SubViewport", true, false):
		var row = {"ordinal": observations.size(), "viewport": viewport.get_instance_id(), "msaaBefore": viewport.msaa_3d}
		for world in viewport.find_children("*", "WorldEnvironment", true, false):
			var env = world.environment
			row["ssilBefore"] = env.ssil_enabled
			row["ssaoBefore"] = env.ssao_enabled
			row["glowBefore"] = env.glow_enabled
			match control:
				"ssil": env.ssil_enabled = false
				"ssao": env.ssao_enabled = false
				"glow": env.glow_enabled = false
			row["ssilAfter"] = env.ssil_enabled
			row["ssaoAfter"] = env.ssao_enabled
			row["glowAfter"] = env.glow_enabled
		var shadows_before: Array = []
		var shadows_after: Array = []
		for light in viewport.find_children("*", "Light3D", true, false):
			shadows_before.append(light.shadow_enabled)
			if control == "shadows": light.shadow_enabled = false
			shadows_after.append(light.shadow_enabled)
		if control == "msaa": viewport.msaa_3d = Viewport.MSAA_DISABLED
		row["msaaAfter"] = viewport.msaa_3d
		row["shadowsBefore"] = shadows_before
		row["shadowsAfter"] = shadows_after
		observations.append(row)

	# Finish the observer before the frozen game's save/drain/quit path begins.
	# Avoid scheduling script work during scene teardown.
	if observations.size() >= 80:
		RenderingServer.frame_pre_draw.disconnect(_before_draw)
		var file = FileAccess.open(output.path_join("effect-control.json"), FileAccess.WRITE)
		if file:
			file.store_string(JSON.stringify({"scope": "External diagnostic effect control only; not original High or release qualification", "control": control, "observations": observations}))
