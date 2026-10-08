shader_type canvas_item;
render_mode unshaded, blend_disabled;

void fragment() {
    vec2 source_size = vec2(textureSize(TEXTURE, 0));
    // The reference map is always 960x540. Threshold BEFORE area downsampling,
    // so narrow HDR strokes keep their energy instead of disappearing in a blur.
    vec2 footprint = source_size / vec2(960.0, 540.0);
    vec2 low = UV * source_size - footprint * 0.5;
    vec2 high = low + footprint;
    ivec2 first = ivec2(floor(low));
    ivec2 end = ivec2(ceil(high));
    vec3 sum = vec3(0.0);
    for (int y = first.y; y < end.y; y++) {
        float wy = max(0.0, min(high.y, float(y + 1)) - max(low.y, float(y)));
        for (int x = first.x; x < end.x; x++) {
            float wx = max(0.0, min(high.x, float(x + 1)) - max(low.x, float(x)));
            ivec2 coordinate = clamp(ivec2(x, y), ivec2(0), ivec2(source_size) - 1);
            vec3 rgb = max(texelFetch(TEXTURE, coordinate, 0).rgb, vec3(0.0));
            float peak = max(rgb.r, max(rgb.g, rgb.b));
            sum += min(rgb, vec3(12.0)) * smoothstep(1.0, 3.0, peak) * wx * wy;
        }
    }
    COLOR = vec4(sum / (footprint.x * footprint.y), 1.0);
}
