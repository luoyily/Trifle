shader_type canvas_item;
render_mode unshaded, blend_disabled;

uniform vec2 destination_size = vec2(1920.0, 1080.0);

void fragment() {
    vec2 source_size = vec2(textureSize(TEXTURE, 0));
    vec2 footprint = source_size / destination_size;
    if (max(footprint.x, footprint.y) <= 1.0) {
        COLOR = texture(TEXTURE, UV);
    } else {
        // Integrate every covered source pixel; ordinary bilinear sampling can skip
        // thin staff lines when the final window is smaller than the render texture.
        vec2 low = UV * source_size - footprint * 0.5;
        vec2 high = low + footprint;
        ivec2 first = ivec2(floor(low));
        ivec2 end = ivec2(ceil(high));
        vec4 sum = vec4(0.0);
        for (int y = first.y; y < end.y; y++) {
            float wy = max(0.0, min(high.y, float(y + 1)) - max(low.y, float(y)));
            for (int x = first.x; x < end.x; x++) {
                float wx = max(0.0, min(high.x, float(x + 1)) - max(low.x, float(x)));
                ivec2 coordinate = clamp(ivec2(x, y), ivec2(0), ivec2(source_size) - 1);
                sum += texelFetch(TEXTURE, coordinate, 0) * (wx * wy);
            }
        }
        COLOR = sum / (footprint.x * footprint.y);
    }
}
