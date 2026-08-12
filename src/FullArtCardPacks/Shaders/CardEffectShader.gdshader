shader_type canvas_item;
render_mode unshaded, blend_add;

uniform sampler2D permission_mask_texture : filter_linear, repeat_disable;
uniform int surface_type = 0;
uniform float intensity : hint_range(0.0, 1.0) = 0.2;
uniform float phase = 0.0;
uniform float motion_scale : hint_range(0.0, 1.0) = 0.05;

varying float presentation_scale;

void vertex() {
    float scale_x = length(MODEL_MATRIX[0].xy);
    float scale_y = length(MODEL_MATRIX[1].xy);
    presentation_scale = max(scale_x, scale_y);
}

vec3 spectrum(float hue) {
    return 0.5 + 0.5 * cos(6.2831853 * (hue + vec3(0.0, 0.3333333, 0.6666667)));
}

void fragment() {
    vec4 source = texture(TEXTURE, UV);
    float allowed = texture(permission_mask_texture, UV).r * source.a;
    float transform_guard = smoothstep(0.0, 0.05, presentation_scale);
    float t = TIME * motion_scale + phase;
    vec3 effect_color = vec3(0.0);
    float effect_alpha = 0.0;

    if (surface_type == 1) {
        float band = 1.0 - smoothstep(0.035, 0.11, abs(fract(UV.x + UV.y * 0.55 - t * 0.08) - 0.5));
        effect_color = vec3(0.72, 0.78, 0.84);
        effect_alpha = band * 0.28;
    } else if (surface_type == 2) {
        float line = pow(max(0.0, sin((UV.x * 7.0 + UV.y * 3.0) - t * 0.7)), 14.0);
        effect_color = vec3(0.76, 0.71, 0.58);
        effect_alpha = 0.08 + line * 0.30;
    } else if (surface_type == 3) {
        float hue = fract(UV.x * 0.55 + UV.y * 0.22 + t * 0.035);
        effect_color = mix(vec3(0.58), spectrum(hue), 0.38);
        effect_alpha = 0.15;
    } else if (surface_type == 4) {
        float hue = fract(UV.x * 0.80 + UV.y * 0.45 + t * 0.12);
        float sweep = 0.72 + 0.28 * sin((UV.x + UV.y) * 11.0 - t * 1.4);
        effect_color = spectrum(hue);
        effect_alpha = sweep * 0.80;
    }

    float alpha = clamp(effect_alpha * allowed * intensity * transform_guard, 0.0, 0.32);
    COLOR = vec4(effect_color, alpha);
}
