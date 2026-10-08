shader_type canvas_item;
render_mode unshaded;

uniform sampler2D glow_level_0 : filter_linear, repeat_disable;
uniform sampler2D glow_level_1 : filter_linear, repeat_disable;
uniform sampler2D glow_level_2 : filter_linear, repeat_disable;
uniform float glow_intensity = 0.0;

// Wide keyboard light is applied AFTER HDR Glow. This soft field cannot bloom
// outside the keyboard, while contact accents and particles retain their own Glow.
uniform vec4 keyboard_rect = vec4(0.0);
uniform vec4 key_light_sources[128];
uniform vec4 key_light_colors[128];
uniform int key_light_count = 0;
uniform float white_key_width = 0.018;
uniform float key_light_radius = 2.5;
uniform float key_light_strength = 0.0;

// Shared SDR output for the preview and PNG/FFmpeg capture; verified in M0.
vec3 linear_to_srgb(vec3 value) {
    vec3 positive = max(value, vec3(0.0));
    vec3 low = positive * 12.92;
    vec3 high = 1.055 * pow(positive, vec3(1.0 / 2.4)) - 0.055;
    return mix(low, high, step(vec3(0.0031308), positive));
}

void fragment() {
    vec4 pixel = texture(TEXTURE, UV);
    if (glow_intensity > 0.0) {
        pixel.rgb += glow_intensity * (texture(glow_level_0, UV).rgb * 0.8 +
            texture(glow_level_1, UV).rgb * 0.4 + texture(glow_level_2, UV).rgb * 0.1);
    }
    vec3 rgb = clamp(linear_to_srgb(pixel.rgb), 0.0, 1.0);
    if (key_light_strength > 0.0 && key_light_count > 0 &&
        UV.x >= keyboard_rect.x && UV.x <= keyboard_rect.x + keyboard_rect.z &&
        UV.y >= keyboard_rect.y && UV.y <= keyboard_rect.y + keyboard_rect.w) {
        float down = (UV.y - keyboard_rect.y) / max(keyboard_rect.w, 0.001);
        float vertical = pow(max(1.0 - down, 0.0), 1.65);
        vec3 hue_sum = vec3(0.0);
        float weight_sum = 0.0;
        for (int i = 0; i < key_light_count; i++) {
            float dx = (UV.x - key_light_sources[i].x) / max(white_key_width * key_light_radius, 0.001);
            float lateral = exp(-dx * dx * 1.9) * (1.0 - smoothstep(0.85, 1.2, abs(dx)));
            float weight = lateral * vertical * key_light_sources[i].y * key_light_sources[i].z;
            hue_sum += key_light_colors[i].rgb * weight;
            weight_sum += weight;
        }
        if (weight_sum > 0.0001) {
            vec3 hue = hue_sum / weight_sum;
            float influence = 1.0 - exp(-weight_sum * key_light_strength);
            float surface = dot(rgb, vec3(0.2126, 0.7152, 0.0722));
            // Light warms white surfaces while leaving black keys and seams darker.
            vec3 lit_surface = hue * max(surface, 0.10) + rgb * 0.18;
            rgb = mix(rgb, lit_surface, influence * 0.65);
            rgb += hue * influence * 0.16 * sqrt(max(surface, 0.0));
        }
    }
    COLOR = vec4(clamp(rgb, 0.0, 1.0), pixel.a);
}
