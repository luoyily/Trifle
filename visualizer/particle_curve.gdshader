shader_type canvas_item;
render_mode unshaded, blend_add;

uniform float emission = 1.0;
uniform float curve_strength = 0.7;
uniform float curve_width = 6.0;
uniform float glow_strength = 0.85;
uniform sampler2D curve_points : filter_nearest, repeat_disable;
varying float ribbon_age;
varying float ribbon_peak;
varying float ribbon_seed;
varying float parcel_light;

vec4 knot(int point, int row) {
    return texelFetch(curve_points, ivec2(clamp(point, 0, 7), row), 0);
}

float ease(float value) {
    float t = clamp(value, 0.0, 1.0);
    return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
}

void vertex() {
    float seed = INSTANCE_CUSTOM.y;
    ribbon_seed = seed;
    ribbon_age = INSTANCE_CUSTOM.z;
    ribbon_peak = INSTANCE_CUSTOM.w;
    int row = int(INSTANCE_CUSTOM.x + 0.5);
    float u = UV.y;
    float at = u * 7.0;
    int segment = min(int(at), 6);
    float t = at - float(segment);
    vec4 p0 = knot(segment - 1, row);
    vec4 p1 = knot(segment, row);
    vec4 p2 = knot(segment + 1, row);
    vec4 p3 = knot(segment + 2, row);
    vec2 m1 = (p2.xy - p0.xy) * 0.42;
    vec2 m2 = (p3.xy - p1.xy) * 0.42;
    float max_tangent = length(p2.xy - p1.xy) * 1.2;
    m1 *= min(1.0, max_tangent / max(length(m1), 0.001));
    m2 *= min(1.0, max_tangent / max(length(m2), 0.001));
    float t2 = t * t;
    float t3 = t2 * t;
    vec2 pos = (2.0 * t3 - 3.0 * t2 + 1.0) * p1.xy + (t3 - 2.0 * t2 + t) * m1
        + (-2.0 * t3 + 3.0 * t2) * p2.xy + (t3 - t2) * m2;
    vec2 tangent = (6.0 * t2 - 6.0 * t) * p1.xy + (3.0 * t2 - 4.0 * t + 1.0) * m1
        + (-6.0 * t2 + 6.0 * t) * p2.xy + (3.0 * t2 - 2.0 * t) * m2;
    tangent = length(tangent) > 0.001 ? normalize(tangent) : vec2(0.0, -1.0);
    parcel_light = mix(p1.z, p2.z, smoothstep(0.0, 1.0, t));
    float dissolving = smoothstep(0.48, 1.0, ribbon_age);
    float width = curve_width * (0.78 + seed * 0.44) * mix(p1.w, p2.w, t) * (1.0 + dissolving * 0.08);
    // The mesh leaves space for spill light. The visible filament and local
    // bloom are shaped separately below, not as one thick crescent silhouette.
    VERTEX = pos + vec2(-tangent.y, tangent.x) * VERTEX.x * width * 5.0;
}

void fragment() {
    float across = (UV.x - 0.5) * 5.0;
    float u = UV.y;
    float ends = ease(u / 0.22) * ease((1.0 - u) / 0.25);
    float peak = ribbon_peak + sin(ribbon_age * 3.0 + ribbon_seed * 6.2831853) * 0.025;
    float local_distance = (u - peak) / (0.095 + ribbon_seed * 0.025);
    float local_light = exp(-local_distance * local_distance);
    float hot_fade = 1.0 - smoothstep(0.48, 0.88, ribbon_age);
    float hot = local_light * hot_fade;
    float side = ribbon_seed < 0.5 ? -1.0 : 1.0;

    // Most of the ribbon stays slender; only the illuminated patch swells a little.
    float filament_width = (0.18 + sin(u * 5.0 + ribbon_seed * 6.2831853) * 0.015 + hot * 0.035)
        * mix(0.40, 1.0, ends);
    float filament = exp(-across * across / (filament_width * filament_width));
    float body_glow = exp(-across * across / 0.20) * glow_strength * 0.075;
    float lit_across = across - side * 0.08 * hot;
    float hot_core = exp(-lit_across * lit_across / 0.10) * hot;
    float spill_across = across - side * 0.22;
    float spill = exp(-spill_across * spill_across / 0.42) * hot * glow_strength;
    float edge = 1.0 - smoothstep(1.7, 2.5, abs(across));

    vec3 hue = COLOR.rgb;
    vec3 lit_hue = mix(hue, vec3(1.0), 0.32);
    vec3 radiance = hue * (filament * 0.28 + body_glow)
        + lit_hue * hot_core * 1.75 + hue * spill * 0.42;
    COLOR.rgb = radiance * (1.0 + emission) * curve_strength;
    COLOR.a *= edge * ends * parcel_light * 0.60;
}
