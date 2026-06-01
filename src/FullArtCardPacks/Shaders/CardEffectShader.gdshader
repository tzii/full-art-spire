shader_type canvas_item;

uniform sampler2D mask_texture;
uniform float intensity : hint_range(0.0, 1.0) = 0.3;
uniform int rarity_type = 0; // 0=common, 1=uncommon, 2=rare
uniform float time;

void fragment() {
    vec4 mask = texture(mask_texture, UV);
    vec4 base = texture(TEXTURE, UV);
    
    // Apply effect only where mask is dark (background)
    float effect_strength = (1.0 - mask.r) * intensity;
    
    if (rarity_type == 0) {
        // Common: subtle shimmer
        float shimmer = sin(time * 2.0) * 0.5 + 0.5;
        COLOR = mix(base, base + vec3(0.1 * shimmer), effect_strength);
    } else if (rarity_type == 1) {
        // Uncommon: moderate glow
        COLOR = mix(base, base + vec3(0.15, 0.2, 0.15), effect_strength);
    } else {
        // Rare: animated foil with rainbow effect
        float foil = sin(UV.x * 10.0 + time * 3.0) * 0.5 + 0.5;
        float hue = fract(time * 0.1 + UV.x);
        vec3 foil_color = vec3(
            0.5 + 0.5 * sin(hue * 6.28 + 0.0),
            0.5 + 0.5 * sin(hue * 6.28 + 2.09),
            0.5 + 0.5 * sin(hue * 6.28 + 4.18)
        );
        COLOR = mix(base, base + foil_color * foil * 0.4, effect_strength);
    }
}
