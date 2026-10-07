shader_type canvas_item;
render_mode unshaded, blend_disabled;

uniform sampler2D source_image : source_color, filter_linear, repeat_disable;
uniform vec2 source_size = vec2(1.0);
uniform vec2 canvas_size = vec2(1920.0, 1080.0);
uniform bool cover = false;

void fragment() {
    vec2 ratios = canvas_size / source_size;
    float fit = cover ? max(ratios.x, ratios.y) : min(ratios.x, ratios.y);
    vec2 uv = (UV - vec2(0.5)) * canvas_size / (source_size * fit) + vec2(0.5);
    vec4 result = vec4(0.0);
    if (all(greaterThanEqual(uv, vec2(0.0))) && all(lessThanEqual(uv, vec2(1.0)))) {
        result = texture(source_image, uv);
    }
    COLOR = vec4(result.rgb * result.a, result.a);
}
