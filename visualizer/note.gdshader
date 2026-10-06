shader_type canvas_item;
render_mode unshaded;

uniform float corner_radius = 0.0;
uniform float opacity = 1.0;
uniform float brightness = 1.0;
uniform float emission = 0.0;
uniform float near_strength = 0.0;
uniform float contact_y = 0.833333;
uniform float near_distance = 0.12963;

// Width, visible height, full height, and pixels clipped from the note's top.
varying vec4 note_geometry;

void vertex() {
    note_geometry = INSTANCE_CUSTOM;
}

void fragment() {
    float mask = 1.0;
    if (corner_radius > 0.0) {
        vec2 half_size = vec2(note_geometry.x, note_geometry.z) * 0.5;
        float radius = min(corner_radius, min(half_size.x, half_size.y));
        // QuadMesh UV.y runs upward; canvas geometry runs downward.
        vec2 point = vec2((UV.x - 0.5) * note_geometry.x,
            (1.0 - UV.y) * note_geometry.y + note_geometry.w - half_size.y);
        vec2 q = abs(point) - (half_size - vec2(radius));
        float distance = length(max(q, vec2(0.0))) + min(max(q.x, q.y), 0.0) - radius;
        float aa = max(fwidth(distance), 0.001);
        mask = 1.0 - smoothstep(-aa * 0.5, aa * 0.5, distance);
    }
    float nearby = pow(clamp(1.0 - (contact_y - SCREEN_UV.y) / near_distance, 0.0, 1.0), 2.0);
    COLOR.rgb *= brightness * (1.0 + emission + near_strength * nearby);
    COLOR.a *= opacity * mask;
}
