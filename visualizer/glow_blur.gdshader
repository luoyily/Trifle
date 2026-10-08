shader_type canvas_item;
render_mode unshaded, blend_disabled;

uniform vec2 direction = vec2(0.0);

void fragment() {
    // Normalized kernel at each fixed pyramid level, in reference-map pixels.
    vec3 rgb = texture(TEXTURE, UV).rgb * 0.2024;
    rgb += (texture(TEXTURE, UV + direction).rgb + texture(TEXTURE, UV - direction).rgb) * 0.179;
    rgb += (texture(TEXTURE, UV + direction * 2.0).rgb + texture(TEXTURE, UV - direction * 2.0).rgb) * 0.124;
    rgb += (texture(TEXTURE, UV + direction * 3.0).rgb + texture(TEXTURE, UV - direction * 3.0).rgb) * 0.0672;
    rgb += (texture(TEXTURE, UV + direction * 4.0).rgb + texture(TEXTURE, UV - direction * 4.0).rgb) * 0.0285;
    COLOR = vec4(rgb / 0.9998, 1.0);
}
