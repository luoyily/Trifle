shader_type canvas_item;
render_mode unshaded;

uniform float brightness = 1.0;

void fragment() {
    COLOR.rgb *= brightness;
}
