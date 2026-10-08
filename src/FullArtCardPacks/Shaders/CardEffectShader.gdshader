shader_type canvas_item;
render_mode unshaded, blend_add;

uniform sampler2D permission_mask_texture : filter_linear, repeat_disable;
uniform int surface_type = 0;
uniform float intensity : hint_range(0.0, 1.0) = 0.2;
uniform float phase = 0.0;
uniform float motion_scale : hint_range(0.0, 1.0) = 0.05;
// Local pointer tilt, bounded to [-1,1]. Zero is also the reduced-motion pose.
uniform vec2 view_tilt = vec2(0.0);

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

float hash21(vec2 cell) {
    return fract(sin(dot(cell, vec2(127.1, 311.7))) * 43758.5453);
}

// RGB samples of thin-film optical phase, not a physically complete spectral renderer.
vec3 film_color(float thickness_nm, float cosine_angle) {
    vec3 wavelength_nm = vec3(650.0, 530.0, 460.0);
    return 0.5 + 0.5 * cos(6.2831853 * (2.0 * 1.45 * thickness_nm * cosine_angle) / wavelength_nm);
}

float filtered_line(float coordinate, float width) {
    float edge = max(fwidth(coordinate), 0.002);
    float distance = abs(fract(coordinate + 0.5) - 0.5);
    return 1.0 - smoothstep(width, width + edge, distance);
}

void fragment() {
    vec4 source = texture(TEXTURE, UV);
    float allowed = texture(permission_mask_texture, UV).r * source.a;
    float transform_guard = smoothstep(0.0, 0.05, presentation_scale);
    float t = TIME * motion_scale;
    // A bounded drifting light never snaps from one edge to the other.
    vec2 tilt = clamp(view_tilt, vec2(-1.0), vec2(1.0));
    float angle = tilt.x * 0.65 + tilt.y * 0.35;
    float center = 0.73 + 0.48 * sin(t * 0.55 + phase) + angle * 0.42;
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
    } else if (surface_type == 8) {
        // Embossed grating orientation stays on the card; viewing angle selects wavelengths.
        vec2 cell = floor(UV * vec2(8.0, 12.0));
        float orientation = hash21(cell) * 6.2831853;
        vec2 groove = vec2(cos(orientation), sin(orientation));
        float dispersion = dot(UV, groove) * 1.7 + dot(tilt, groove) * 0.65
            + 0.08 * sin(t * 0.4 + phase);
        float order = soft_band(diagonal, center, 0.32);
        effect_color = mix(vec3(0.8), spectrum(dispersion), 0.88);
        effect_alpha = 0.07 + order * 0.72 + catch_light * 0.28;
    } else if (surface_type == 9) {
        // Etched foil: diffuse metal plus stationary fine hatch, less mirror-like.
        float hatch = filtered_line(UV.x * 65.0 + UV.y * 42.0, 0.12);
        float etched = mix(0.75, 0.55 + 0.45 * hatch, detail);
        effect_color = mix(vec3(0.40, 0.59, 0.54), vec3(0.90, 0.83, 0.56), broad);
        effect_alpha = (0.12 + broad * 0.55 + catch_light * 0.10) * etched;
    } else if (surface_type == 10) {
        // Discrete flake normals. Flakes never drift across the print.
        vec2 grid = UV * vec2(42.0, 63.0);
        vec2 cell = floor(grid);
        vec2 local = fract(grid) - 0.5;
        float flake = 1.0 - smoothstep(0.13, 0.13 + max(fwidth(grid.x), 0.02), length(local));
        float normal = hash21(cell);
        float flash = pow(max(0.0, cos(normal * 6.2831853 + angle * 3.0 + t * 0.3 + phase)), 18.0);
        effect_color = mix(vec3(0.9, 0.87, 0.71), spectrum(normal + angle * 0.3), 0.42);
        effect_alpha = 0.05 + broad * 0.2 + flake * flash * detail * 2.7;
    } else if (surface_type == 11) {
        // Piecewise faceted foil, with a different groove orientation per triangle.
        vec2 grid = UV * vec2(6.0, 9.0);
        vec2 local = fract(grid);
        vec2 cell = floor(grid);
        float triangle = step(local.x, local.y);
        float seed = hash21(cell + triangle * 17.0);
        float facet_catch = pow(max(0.0, cos(seed * 6.2831853 + angle * 2.4 + t * 0.3 + phase)), 4.0);
        effect_color = mix(vec3(0.43, 0.70, 0.91), spectrum(seed + angle * 0.35), 0.68);
        effect_alpha = 0.08 + broad * 0.2 + facet_catch * 0.72;
    } else if (surface_type == 12) {
        float thickness = 340.0 + 210.0 * sin(UV.x * 7.0 + sin(UV.y * 8.0))
            + 75.0 * sin(UV.y * 15.0);
        float cosine_angle = clamp(0.84 + angle * 0.13 + 0.04 * sin(t * 0.35 + phase), 0.45, 1.0);
        effect_color = film_color(thickness, cosine_angle);
        effect_alpha = 0.10 + broad * 0.67 + catch_light * 0.15;
    } else if (surface_type == 13) {
        // Narrower film-thickness variation makes a soft pearl instead of an oil slick.
        vec3 pearl = film_color(290.0 + 22.0 * sin(UV.x * 5.0 + UV.y * 3.0), 0.82 + angle * 0.12);
        effect_color = mix(vec3(0.96, 0.92, 0.87), pearl, 0.34);
        effect_alpha = 0.08 + soft_band(diagonal, center, 0.42) * 0.53 + catch_light * 0.15;
    } else if (surface_type == 14) {
        // Luminance-derived relief suggests raised varnish on printed detail.
        vec2 delta = max(TEXTURE_PIXEL_SIZE * 3.0, vec2(footprint));
        vec3 luma = vec3(0.2126, 0.7152, 0.0722);
        float dx = dot(texture(TEXTURE, UV + vec2(delta.x, 0.0)).rgb
            - texture(TEXTURE, UV - vec2(delta.x, 0.0)).rgb, luma);
        float dy = dot(texture(TEXTURE, UV + vec2(0.0, delta.y)).rgb
            - texture(TEXTURE, UV - vec2(0.0, delta.y)).rgb, luma);
        vec3 normal = normalize(vec3(-dx * 3.0, -dy * 3.0, 1.0));
        vec3 light = normalize(vec3(tilt * 0.8 + vec2(0.35, -0.4), 0.8));
        float relief = pow(max(dot(normal, light), 0.0), 16.0);
        effect_color = vec3(0.93, 0.91, 0.84);
        effect_alpha = 0.03 + broad * 0.16 + relief * 0.78;
    } else if (surface_type == 15) {
        // Angular ink-state flip. True multi-image lenticular animation needs extra art.
        float view = sin(angle * 3.8 + 0.3 * sin(t * 0.3 + phase));
        float flip = smoothstep(-0.16, 0.16, view);
        float ribs = mix(1.0, 0.82 + 0.18 * filtered_line(UV.x * 110.0, 0.18), detail);
        effect_color = mix(vec3(0.13, 0.85, 1.0), vec3(1.0, 0.31, 0.61), flip);
        effect_alpha = (0.13 + broad * 0.63) * ribs;
    } else if (surface_type == 16) {
        // Original finish: stationary aurora ribbons illuminated by viewing direction.
        float ribbons = UV.x * 4.0 + sin(UV.y * 7.0) * 0.55 + sin(UV.y * 15.0) * 0.10;
        float light = 0.5 + 0.5 * cos(ribbons * 6.2831853 + angle * 3.0 + t * 0.35 + phase);
        effect_color = mix(vec3(0.11, 0.95, 0.67), vec3(0.54, 0.29, 1.0), 0.5 + 0.5 * sin(ribbons * 2.0));
        effect_alpha = 0.035 + pow(light, 5.0) * (0.30 + broad * 0.65);
    } else if (surface_type == 17) {
        // Original finish: fixed stars with angle-dependent catch, no moving particles.
        vec2 grid = UV * vec2(16.0, 24.0);
        vec2 cell = floor(grid);
        float seed = hash21(cell);
        vec2 local = fract(grid) - vec2(0.2 + seed * 0.6, 0.2 + hash21(cell + 7.0) * 0.6);
        float size = max(fwidth(grid.x), 0.025);
        float core = exp(-dot(local, local) / (0.012 + size * 0.02));
        float cross = exp(-abs(local.x) / size) * exp(-abs(local.y) * 10.0)
            + exp(-abs(local.y) / size) * exp(-abs(local.x) * 10.0);
        float flash = pow(0.5 + 0.5 * sin(seed * 6.2831853 + angle * 3.2 + t * 0.4 + phase), 6.0);
        effect_color = mix(vec3(0.55, 0.71, 1.0), vec3(1.0, 0.87, 0.55), seed);
        effect_alpha = 0.035 + broad * 0.20 + (core + cross * 0.28) * flash * step(0.62, seed) * 2.7;
    } else if (surface_type == 18) {
        // Original finish: fixed circuit traces; light sweeps through junctions.
        vec2 grid = UV * vec2(9.0, 14.0);
        vec2 cell = floor(grid);
        float seed = hash21(cell);
        float trace = mix(filtered_line(grid.x, 0.025), filtered_line(grid.y, 0.025), step(0.5, seed));
        float junction = 1.0 - smoothstep(0.08, 0.08 + max(fwidth(grid.x), 0.01), length(fract(grid) - 0.5));
        float charge = 0.25 + 0.75 * soft_band(diagonal, center, 0.18);
        effect_color = mix(vec3(0.15, 0.87, 0.96), vec3(0.73, 1.0, 0.24), charge);
        effect_alpha = 0.025 + broad * 0.1 + (trace * 0.85 + junction * 1.5) * charge;
    }

    float alpha = clamp(effect_alpha * allowed * intensity * transform_guard, 0.0, 0.32);
    // Keep headroom on bright art; remain effect-only and respect UI fades.
    float headroom = 1.0 - smoothstep(0.65, 1.0, max(source.r, max(source.g, source.b))) * 0.65;
    COLOR = vec4(effect_color * card_modulate.rgb, alpha * card_modulate.a * headroom);
}
