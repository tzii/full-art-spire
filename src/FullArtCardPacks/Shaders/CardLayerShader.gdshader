shader_type canvas_item;
render_mode unshaded, blend_mix;

uniform sampler2D foreground_texture : source_color, filter_linear, repeat_disable;
uniform vec2 pointer = vec2(0.0);
uniform float depth : hint_range(0.0, 1.0) = 1.0;
uniform float background_shift = 0.010;
uniform float foreground_shift = 0.022;
uniform float foreground_scale = 1.035;
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
    return texture(foreground_texture, clamp(uv, vec2(0.0), vec2(1.0))).a * inside_canvas(uv);
}

void fragment() {
    // TEXTURE is an opaque clean plate. Both planes address the same exported
    // 2:3 canvas; the parent's native crop applies consistently to both UVs.
    vec2 travel = clamp(pointer, vec2(-1.0), vec2(1.0)) * clamp(depth, 0.0, 1.0);
    float plate_zoom = 1.0 + 2.1 * background_shift * depth;
    vec2 plate_uv = (UV - 0.5) / plate_zoom + 0.5 + travel * background_shift;
    vec3 background = texture(TEXTURE, clamp(plate_uv, vec2(0.001), vec2(0.999))).rgb;

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

    float lift = mix(1.0, foreground_scale, depth);
    vec2 subject_uv = (UV - 0.5 - travel * foreground_shift) / lift + 0.5;
    vec2 cast_offset = vec2(0.007, 0.010) * (0.3 + depth * 0.7) - travel * 0.005;
    vec2 shadow_uv = subject_uv - cast_offset;
    vec2 blur = vec2(0.0035, 0.0023);
    float shadow = foreground_alpha(shadow_uv) * 0.24;
    shadow += foreground_alpha(shadow_uv + vec2(blur.x, 0.0)) * 0.12;
    shadow += foreground_alpha(shadow_uv - vec2(blur.x, 0.0)) * 0.12;
    shadow += foreground_alpha(shadow_uv + vec2(0.0, blur.y)) * 0.12;
    shadow += foreground_alpha(shadow_uv - vec2(0.0, blur.y)) * 0.12;
    shadow += foreground_alpha(shadow_uv + blur) * 0.07;
    shadow += foreground_alpha(shadow_uv - blur) * 0.07;
    shadow += foreground_alpha(shadow_uv + vec2(blur.x, -blur.y)) * 0.07;
    shadow += foreground_alpha(shadow_uv + vec2(-blur.x, blur.y)) * 0.07;
    background *= 1.0 - shadow * shadow_strength;

    vec4 subject = texture(foreground_texture, clamp(subject_uv, vec2(0.0), vec2(1.0)));
    subject.a *= inside_canvas(subject_uv);
    vec3 composite = mix(background, subject.rgb, subject.a);
    COLOR = vec4(composite * card_modulate.rgb, card_modulate.a);
}
