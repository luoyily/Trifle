shader_type canvas_item;
render_mode unshaded, blend_premul_alpha;

uniform float sigma = 0.0;
uniform float brightness = 1.0;
uniform float opacity = 1.0;

void fragment() {
    vec4 result = vec4(0.0);
    float total = 0.0;
    if (sigma <= 0.0) {
        result = texture(TEXTURE, UV);
    } else {
        for (int i = -12; i <= 12; i++) {
            if (abs(float(i)) <= ceil(sigma * 3.0)) {
                float distance = float(i) / max(sigma, 0.001);
                float weight = exp(-0.5 * distance * distance);
                result += texture(TEXTURE, UV + vec2(0.0, float(i) * TEXTURE_PIXEL_SIZE.y)) * weight;
                total += weight;
            }
        }
        result /= total;
    }
    COLOR = vec4(result.rgb * brightness * opacity, result.a * opacity);
}
