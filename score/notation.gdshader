shader_type canvas_item;
render_mode unshaded;

uniform vec4 notation_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform float brightness = 1.0;

void fragment() {
    // SVG group opacity and supersampled edge coverage are already in the alpha.
    // Ignore filtered transparent RGB to preserve a clean, uniform notation color.
    COLOR = vec4(notation_color.rgb * brightness, texture(TEXTURE, UV).a);
}
