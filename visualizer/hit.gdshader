shader_type canvas_item;
render_mode unshaded;

void fragment() {
    vec2 p = (UV - vec2(0.5, 0.62)) * vec2(2.0, 2.0);
    float halo = exp(-p.x * p.x * 3.0 - p.y * p.y * 3.0);
    float core = exp(-p.x * p.x * 5.0 - p.y * p.y * 60.0);
    float edges = smoothstep(0.0, 0.12, UV.x) * smoothstep(0.0, 0.12, 1.0 - UV.x) *
        smoothstep(0.0, 0.12, UV.y) * smoothstep(0.0, 0.12, 1.0 - UV.y);
    COLOR.rgb = mix(COLOR.rgb, vec3(max(max(COLOR.r, COLOR.g), COLOR.b)), core * 0.3);
    COLOR.a *= (halo * 0.38 + core * 0.55) * edges;
}
