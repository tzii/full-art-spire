shader_type canvas_item;
render_mode unshaded, blend_add;

uniform sampler2D permission_mask_texture : filter_linear, repeat_disable;
uniform int surface_type = 0;
uniform float intensity : hint_range(0.0, 1.0) = 0.2;
uniform float phase = 0.0;
uniform float motion_scale : hint_range(0.0, 1.0) = 0.05;

varying float presentation_scale;
varying vec4 card_modulate;

void vertex() {
    card_modulate = COLOR;
    float scale_x = length(MODEL_MATRIX[0].xy);
    float scale_y = length(MODEL_MATRIX[1].xy);
    presentation_scale = max(scale_x, scale_y);
}

vec3 spectrum(float hue) {
    return 0.5 + 0.5 * cos(6.2831853 * (hue + vec3(0.0, 0.3333333, 0.6666667)));
}

float soft_band(float coordinate, float center, float width) {
    float distance = (coordinate - center) / width;
    return exp(-distance * distance);
}

void fragment() {
    vec4 source = texture(TEXTURE, UV);
    float allowed = texture(permission_mask_texture, UV).r * source.a;
    float transform_guard = smoothstep(0.0, 0.05, presentation_scale);
    float t = TIME * motion_scale;
    // A bounded drifting light never snaps from one edge to the other.
    float center = 0.73 + 0.48 * sin(t * 0.55 + phase);
    float diagonal = UV.x * 0.85 + UV.y * 0.6;
    float broad = soft_band(diagonal, center, 0.25);
    float catch_light = soft_band(diagonal, center, 0.065);
    // Static material detail is faded out before it becomes subpixel noise.
    float footprint = max(fwidth(UV.x), fwidth(UV.y));
    float detail = 1.0 - smoothstep(0.002, 0.008, footprint);
    float grain = 0.5 + 0.5 * sin(UV.y * 700.0 + sin(UV.x * 41.0));
    float stock = mix(1.0, 0.88 + 0.12 * grain, detail);
    vec3 effect_color = vec3(0.0);
    float effect_alpha = 0.0;

    if (surface_type == 1) {
        effect_color = vec3(0.79, 0.87, 0.94);
        effect_alpha = (0.035 + broad * 0.48 + catch_light * 0.14) * stock;
    } else if (surface_type == 2) {
        effect_color = mix(vec3(0.82, 0.55, 0.23), vec3(1.0, 0.91, 0.66), catch_light);
        effect_alpha = (0.04 + broad * 0.46 + catch_light * 0.36) * stock;
    } else if (surface_type == 3) {
        float hue = diagonal * 0.34 + 0.12 * sin(t * 0.4 + phase);
        effect_color = mix(vec3(0.82, 0.86, 0.91), spectrum(hue), 0.32);
        effect_alpha = 0.035 + broad * 0.62 + catch_light * 0.12;
    } else if (surface_type == 4) {
        float hue = diagonal * 0.75 + 0.22 * sin(t * 0.5 + phase);
        effect_color = mix(vec3(0.83), spectrum(hue), 0.68);
        effect_alpha = (0.025 + broad * 0.72 + catch_light * 0.22) * stock;
    } else if (surface_type == 5) {
        float reflection = soft_band(UV.x + UV.y * 0.15, 0.55 + 0.35 * sin(t * 0.45 + phase), 0.12);
        effect_color = mix(vec3(0.48, 0.65, 0.79), vec3(0.93, 0.96, 1.0), reflection);
        effect_alpha = (0.03 + broad * 0.22 + reflection * 0.65) * stock;
    } else if (surface_type == 6) {
        float heat = 0.85 + 0.15 * sin(t * 0.8 + phase + UV.y * 4.0);
        effect_color = mix(vec3(1.0, 0.22, 0.055), vec3(1.0, 0.77, 0.28), catch_light);
        effect_alpha = (0.025 + broad * 0.62 + catch_light * 0.18) * heat;
    } else if (surface_type == 7) {
        float facet = 0.5 + 0.5 * sin(UV.x * 37.0 + UV.y * 23.0);
        effect_color = mix(vec3(0.31, 0.70, 0.91), vec3(0.87, 0.96, 1.0), catch_light);
        effect_alpha = (0.025 + broad * 0.48 + catch_light * 0.30) * mix(0.8, 1.0, facet);
    }

    float alpha = clamp(effect_alpha * allowed * intensity * transform_guard, 0.0, 0.32);
    // Keep headroom on bright art; remain effect-only and respect UI fades.
    float headroom = 1.0 - smoothstep(0.65, 1.0, max(source.r, max(source.g, source.b))) * 0.65;
    COLOR = vec4(effect_color * card_modulate.rgb, alpha * card_modulate.a * headroom);
}
