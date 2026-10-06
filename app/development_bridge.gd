extends Node

# The editor binary has this feature even when launched by the MCP runner.
# Export templates do not. Development tooling is excluded from the release pack.
func _ready() -> void:
	if OS.has_feature("editor") and ResourceLoader.exists("res://McpInteractionServer.gd"):
		var script = load("res://McpInteractionServer.gd")
		if script:
			var server = Node.new()
			server.set_script(script)
			add_child(server)
