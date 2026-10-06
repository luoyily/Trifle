shader_type canvas_item;
render_mode unshaded, blend_add;
uniform float particle_size = 4.8;
uniform float emission = 1.0;

uniform float glow_strength = 0.85;
uniform float glow_radius = 2.4;
varying float age_ratio;
varying float sparkle;

void vertex() {
    float seed = INSTANCE_CUSTOM.y;
    age_ratio = clamp(INSTANCE_CUSTOM.x, 0.0, 1.0);
    sparkle = seed;
    // Round grains keep their diameter while fading. The larger quad carries a soft halo.
    float extent = 1.4 + glow_radius;
    VERTEX *= particle_size * (0.65 + seed * 0.7) * extent * mix(1.0, 0.85, age_ratio);
}

void fragment() {
    float r = length((UV - vec2(0.5)) * 2.0) * (1.4 + glow_radius);
    float core = exp(-r * r * 2.3);
    float halo = exp(-r * r / max(glow_radius * glow_radius * 0.6, 0.1));
    float edge = 1.0 - smoothstep(0.82, 1.0, length((UV - vec2(0.5)) * 2.0));
    float fade = pow(1.0 - age_ratio, 0.85) * smoothstep(0.0, 0.035, age_ratio + 0.01);
    vec3 hue = mix(COLOR.rgb, vec3(1.0), core * 0.12);
    COLOR.rgb = hue * (1.0 + emission) * mix(0.85, 1.2, sparkle);
    COLOR.a *= (core + halo * glow_strength * 0.16) * fade * edge;
}
