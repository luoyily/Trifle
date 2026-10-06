shader_type canvas_item;
render_mode unshaded;

uniform vec4 sources[128];
uniform vec4 source_colors[128];
uniform int source_count = 0;
uniform float song_time = 0.0;
uniform float canvas_width = 1792.0;
uniform vec2 key_surface_bounds = vec2(0.0, 1792.0);
uniform float line_emission = 0.4;
uniform float halo_emission = 0.4;
// Colors arrive in linear space from the C# settings adapter.
uniform vec4 line_color = vec4(0.18, 0.34, 0.42, 1.0);
uniform vec4 halo_color = vec4(0.18, 0.34, 0.42, 1.0);
uniform bool tint_with_notes = true;
uniform float halo_width = 12.0;
uniform float wave_amount = 1.8;
uniform float contact_boost = 0.8;
uniform float core_width = 3.5;

void fragment() {
    float x = UV.x * canvas_width;
    // The layout includes half a key gap at each outside edge. Clip to the
    // actual key faces without fading away a section of either end key.
    if (x < key_surface_bounds.x || x > key_surface_bounds.y) discard;
    float wave = wave_amount * (0.55 * sin(x * 0.022 + song_time * 1.3) +
        0.25 * sin(x * 0.049 - song_time * 0.85));
    float local = 0.0;
    vec3 tint_sum = vec3(0.0);
    for (int i = 0; i < source_count; i++) {
        float dx = (UV.x - sources[i].x) / max(sources[i].y * 1.2, 0.001);
        float weight = exp(-dx * dx * 1.5) * sources[i].z * source_colors[i].a;
        local += weight;
        tint_sum += source_colors[i].rgb * weight;
        wave += weight * wave_amount * 0.75 * sin(song_time * 4.0 + sources[i].w + dx);
    }
    wave = clamp(wave, -wave_amount * 2.0, wave_amount * 2.0);
    float d = UV.y * 96.0 - 64.0 + wave;
    float core = exp(-d * d * 2.77 / max(core_width * core_width, 0.25));
    float halo = exp(-d * d / max(halo_width * halo_width * 0.35, 1.0));
    vec3 tint = local > 0.001 ? tint_sum / local : line_color.rgb;
    float mix_amount = tint_with_notes ? clamp(local, 0.0, 0.9) : 0.0;
    vec3 line_tint = mix(line_color.rgb, tint, mix_amount);
    vec3 halo_tint = mix(halo_color.rgb, tint, mix_amount);
    float white_core = tint_with_notes ? core * 0.22 * clamp(local, 0.0, 1.0) : 0.0;
    line_tint = mix(line_tint, vec3(1.0), white_core);
    halo_tint = mix(halo_tint, vec3(1.0), white_core);
    float energy = 1.5 + min(local, 2.0) * contact_boost;
    float line_mask = line_emission > 0.0 ? core * 0.94 * line_color.a : 0.0;
    float halo_mask = halo_emission > 0.0 ? halo * 0.24 * halo_color.a : 0.0;
    float coverage = line_mask + halo_mask;
    // Weighted straight-alpha output preserves the old overlap when both
    // layers share a color and strength; a disabled layer adds no dark mask.
    vec3 radiance = (line_tint * line_emission * line_mask + halo_tint * halo_emission * halo_mask)
        * energy / max(coverage, 0.00001);
    COLOR = vec4(radiance, coverage);
}
