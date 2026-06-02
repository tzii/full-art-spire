shader_type canvas_item;

uniform sampler2D mask_texture;
uniform float intensity : hint_range(0.0, 1.0) = 0.3;
uniform int rarity_type = 0; // 0=common, 1=uncommon, 2=rare

void fragment() {
    vec4 mask = texture(mask_texture, UV);
    vec4 base = texture(TEXTURE, UV);
    float animation_time = TIME;
    
    // Apply effect only where mask is dark (background)
    float effect_strength = (1.0 - mask.r) * intensity;
    
    if (rarity_type == 0) {
        // Common: readable silver shimmer
        float shimmer = sin((UV.x + UV.y) * 18.0 - animation_time * 2.8) * 0.5 + 0.5;
        float band = smoothstep(0.65, 1.0, shimmer);
        vec4 target = base;
        target.rgb += vec3(0.07, 0.09, 0.12) + vec3(0.20, 0.22, 0.28) * band;
        COLOR = mix(base, target, effect_strength);
    } else if (rarity_type == 1) {
        // Uncommon: pulsing green-gold glow
        float pulse = sin(animation_time * 2.0) * 0.5 + 0.5;
        vec4 target = base;
        target.rgb += vec3(0.08, 0.18, 0.10) + vec3(0.10, 0.18, 0.08) * pulse;
        COLOR = mix(base, target, effect_strength);
    } else {
        // Rare: animated foil with rainbow effect
        float foil = sin(UV.x * 12.0 + UV.y * 5.0 + animation_time * 3.0) * 0.5 + 0.5;
        float hue = fract(animation_time * 0.12 + UV.x + UV.y * 0.35);
        vec3 foil_color = vec3(
            0.5 + 0.5 * sin(hue * 6.28 + 0.0),
            0.5 + 0.5 * sin(hue * 6.28 + 2.09),
            0.5 + 0.5 * sin(hue * 6.28 + 4.18)
        );
        vec4 target = base;
        target.rgb += foil_color * (0.18 + foil * 0.35);
        COLOR = mix(base, target, effect_strength);
    }
}
