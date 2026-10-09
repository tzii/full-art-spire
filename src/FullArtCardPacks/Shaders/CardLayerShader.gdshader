shader_type canvas_item;
render_mode unshaded, blend_mix;

// Mipmapped sampling keeps small hand/grid cards from shimmering as layers move.
uniform sampler2D foreground_texture : source_color, filter_linear_mipmap, repeat_disable;
uniform sampler2D background_texture : source_color, filter_linear_mipmap, repeat_disable;
uniform bool use_normalized_source_uv = false;
// The mask is data: no source_color hint or sRGB conversion.
uniform sampler2D foreground_mask_texture : filter_linear_mipmap, repeat_disable;
uniform bool use_foreground_mask = false;
uniform bool foreground_mask_use_alpha = false;
uniform vec2 pointer = vec2(0.0);
// Eased copy of pointer that drives motion; pointer stays the exact target.
uniform vec2 view_pointer = vec2(0.0);
// Depth cues: travelling shadow, rim light toward the pointer, receding plate.
uniform float pop_strength : hint_range(0.0, 1.0) = 0.0;
uniform float depth : hint_range(0.0, 1.0) = 1.0;
uniform float background_shift = 0.010;
uniform float foreground_shift = 0.022;
uniform float foreground_scale = 1.035;
uniform float subject_scale : hint_range(0.25, 1.5) = 1.0;
uniform float subject_offset_x : hint_range(-0.5, 0.5) = 0.0;
uniform float subject_offset_y : hint_range(-0.5, 0.5) = 0.0;
uniform float shadow_strength = 0.28;
uniform float frame_inset = 0.055;

varying vec4 card_modulate;

void vertex() {
    card_modulate = COLOR;
}

float inside_canvas(vec2 uv) {
    return step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
}

float foreground_alpha(vec2 uv) {
    // Preserve the established RGBA arithmetic exactly when no mask is enabled.
    if (!use_foreground_mask) {
        return texture(foreground_texture, clamp(uv, vec2(0.0), vec2(1.0))).a * inside_canvas(uv);
    }
    vec2 sample_uv = clamp(uv, vec2(0.0), vec2(1.0));
    vec4 mask = texture(foreground_mask_texture, sample_uv);
    float coverage = foreground_mask_use_alpha ? mask.a : mask.r;
    float alpha = texture(foreground_texture, sample_uv).a * coverage;
    return alpha * inside_canvas(uv);
}

// Coverage without the canvas cutoff, so subjects cut by the card edge get no rim there.
float edge_coverage(vec2 uv) {
    vec2 sample_uv = clamp(uv, vec2(0.0), vec2(1.0));
    float alpha = texture(foreground_texture, sample_uv).a;
    if (use_foreground_mask) {
        vec4 mask = texture(foreground_mask_texture, sample_uv);
        alpha *= foreground_mask_use_alpha ? mask.a : mask.r;
    }
    return alpha;
}

void fragment() {
    // TEXTURE is an opaque clean plate. Both planes address the same exported
    // 2:3 canvas; the parent's native crop applies consistently to both UVs.
    vec2 travel = clamp(view_pointer, vec2(-1.0), vec2(1.0)) * clamp(depth, 0.0, 1.0);
    float pop = clamp(pop_strength, 0.0, 1.0) * clamp(depth, 0.0, 1.0);
    float plate_zoom = 1.0 + 2.1 * background_shift * depth;
    vec2 plate_uv = (UV - 0.5) / plate_zoom + 0.5 + travel * background_shift;
    vec3 background = texture(TEXTURE, clamp(plate_uv, vec2(0.001), vec2(0.999))).rgb;
    if (use_normalized_source_uv) {
        // TEXTURE anchors KeepAspectCovered to the unchanged original; every
        // registered sampler addresses the same normalized source domain.
        background = texture(background_texture, clamp(plate_uv, vec2(0.001), vec2(0.999))).rgb;
    }

    vec2 edge_distance = min(UV, vec2(1.0) - UV);
    float edge = min(edge_distance.x, edge_distance.y);
    float frame_distance = abs(edge - frame_inset);
    float aa = max(fwidth(edge), 0.0005);
    float frame = 1.0 - smoothstep(0.0025, 0.0025 + aa, frame_distance);
    float outer_well = 1.0 - smoothstep(frame_inset - 0.004, frame_inset + 0.015, edge);
    background *= 1.0 - outer_well * 0.28;
    // The inset trim is beneath the foreground, so limbs and props break its
    // silhouette without crossing native cost/title/description UI or clipping.
    vec3 trim = mix(vec3(0.42, 0.49, 0.48), vec3(0.84, 0.78, 0.59), UV.x * 0.35 + (1.0 - UV.y) * 0.65);
    background = mix(background, trim, frame * 0.80);
    // Atmospheric recession: the plate loses a little saturation and light.
    float recede = 0.24 * pop;
    float plate_luma = dot(background, vec3(0.299, 0.587, 0.114));
    background = mix(background, vec3(plate_luma), recede) * (1.0 - recede * 0.6);

    float lift = mix(1.0, foreground_scale, depth);
    // Static placement is independent of pointer travel and depth, including
    // reduced motion. Positive offsets move the subject right and down.
    vec2 subject_uv = (UV - 0.5 - travel * foreground_shift - vec2(subject_offset_x, subject_offset_y)) / (subject_scale * lift) + 0.5;
    // The light sits at the pointer, so the shadow slides away from it.
    vec2 cast_offset = vec2(0.007, 0.010) * (0.3 + depth * 0.7) * (1.0 + pop) - travel * (0.005 + 0.012 * pop);
    vec2 shadow_uv = subject_uv - cast_offset;
    vec2 blur = vec2(0.0035, 0.0023) * (1.0 + pop);
    float shadow = foreground_alpha(shadow_uv) * 0.24;
    shadow += foreground_alpha(shadow_uv + vec2(blur.x, 0.0)) * 0.12;
    shadow += foreground_alpha(shadow_uv - vec2(blur.x, 0.0)) * 0.12;
    shadow += foreground_alpha(shadow_uv + vec2(0.0, blur.y)) * 0.12;
    shadow += foreground_alpha(shadow_uv - vec2(0.0, blur.y)) * 0.12;
    shadow += foreground_alpha(shadow_uv + blur) * 0.07;
    shadow += foreground_alpha(shadow_uv - blur) * 0.07;
    shadow += foreground_alpha(shadow_uv + vec2(blur.x, -blur.y)) * 0.07;
    shadow += foreground_alpha(shadow_uv + vec2(-blur.x, blur.y)) * 0.07;
    background *= 1.0 - shadow * shadow_strength * (1.0 + 0.6 * pop);

    vec4 subject = texture(foreground_texture, clamp(subject_uv, vec2(0.0), vec2(1.0)));
    if (use_foreground_mask) {
        vec4 mask = texture(foreground_mask_texture, clamp(subject_uv, vec2(0.0), vec2(1.0)));
        subject.a *= foreground_mask_use_alpha ? mask.a : mask.r;
    }
    subject.a *= inside_canvas(subject_uv);
    if (pop > 0.0) {
        // Rim light on the silhouette edge facing the light (pointer, else above).
        vec2 light = length(travel) > 0.05 ? normalize(travel) : vec2(-0.35, -0.94);
        vec2 rim_step = light * vec2(0.009, 0.006) / max(subject_scale * lift, 0.25);
        float rim = clamp(edge_coverage(subject_uv) - edge_coverage(subject_uv + rim_step), 0.0, 1.0);
        float rim_light = rim * pop * (0.50 + 0.30 * min(length(travel), 1.0));
        subject.rgb = mix(subject.rgb, vec3(1.0, 0.96, 0.86), rim_light);
    }
    vec3 composite = mix(background, subject.rgb, subject.a);
    COLOR = vec4(composite * card_modulate.rgb, card_modulate.a);
}
