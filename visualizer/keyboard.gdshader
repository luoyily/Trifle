shader_type canvas_item;
render_mode unshaded;

uniform vec4 white_color : source_color = vec4(0.91, 0.914, 0.90, 1.0);
uniform vec4 black_color : source_color = vec4(0.125, 0.137, 0.16, 1.0);
uniform float key_emission = 0.0;
varying vec4 key_shape;

void vertex() { key_shape = INSTANCE_CUSTOM; }

void fragment() {
    vec2 size = max(key_shape.xy, vec2(0.5));
    vec2 p = vec2(UV.x, 1.0 - UV.y);
    bool black = key_shape.z > 0.5;
    float radius = min(black ? 2.2 : 3.2, size.x * 0.18);
    if (!black && p.y < 0.5) radius = 0.0;
    vec2 q = abs((p - 0.5) * size) - (size * 0.5 - vec2(radius));
    float dist = length(max(q, vec2(0.0))) + min(max(q.x, q.y), 0.0) - radius;
    float aa = max(fwidth(dist), 0.5);
    float mask = 1.0 - smoothstep(-aa * 0.5, aa * 0.5, dist);
    float light = key_shape.w * COLOR.a;
    vec4 base = black ? black_color : white_color;
    // The key's own highlight is a solid note color. Wide reflected light is
    // composited separately after Glow, clipped to the keyboard region.
    vec3 body = mix(base.rgb, COLOR.rgb, clamp(light, 0.0, 1.0));
    body += COLOR.rgb * key_emission * light;
    if (black) {
        float front = smoothstep(1.0 - min(7.0 / size.y, 0.1), 1.0 - 1.0 / size.y, p.y);
        body *= mix(1.0, 0.56, front);
        float edge = exp(-pow((p.y - (1.0 - min(7.0 / size.y, 0.1))) * size.y, 2.0));
        body += vec3(0.028) * edge;
        body += vec3(0.014) * exp(-pow((p.x - 0.07) * 15.0, 2.0)) * (1.0 - front);
    } else {
        body *= 1.0 - 0.045 * smoothstep(1.0 - 3.0 / size.y, 1.0, p.y);
    }
    COLOR = vec4(body, base.a * mask);
}
