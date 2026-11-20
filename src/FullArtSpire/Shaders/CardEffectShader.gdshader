shader_type canvas_item;
render_mode unshaded, blend_add;

uniform sampler2D permission_mask_texture : filter_linear, repeat_disable;
uniform int surface_type = 0;
uniform float intensity : hint_range(0.0, 1.0) = 0.2;
uniform float phase = 0.0;
uniform float motion_scale : hint_range(0.0, 1.0) = 0.05;
// Local pointer tilt, bounded to [-1,1]. Zero is also the reduced-motion pose.
uniform vec2 view_tilt = vec2(0.0);
uniform bool use_layered_uv = false;
uniform vec2 view_pointer = vec2(0.0);
uniform float depth = 1.0;
uniform float foreground_shift = 0.0;
uniform float foreground_scale = 1.0;
uniform float subject_scale = 1.0;
uniform float subject_offset_x = 0.0;
uniform float subject_offset_y = 0.0;
uniform sampler2D layer_coverage_texture : filter_linear_mipmap, repeat_disable;
uniform bool layer_coverage_use_alpha = false;
uniform float background_shift = 0.0;
// Rarity showcase extras. All default to zero, which leaves the finishes unchanged.
uniform float sparkle_strength : hint_range(0.0, 1.0) = 0.0;
uniform float backdrop_strength : hint_range(0.0, 1.0) = 0.0;
uniform float aura_strength : hint_range(0.0, 1.0) = 0.0;
uniform float aura_spectral : hint_range(0.0, 1.0) = 0.0;
uniform vec3 tier_color = vec3(1.0);
uniform float alpha_cap : hint_range(0.0, 1.0) = 0.32;

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

// Sparse star glints fixed to the print. x is brightness, y the cell seed.
// Each glint flashes when the view angle or slow time crosses its own phase.
vec2 glints(vec2 uv, vec2 cells, float density, float angle, float t, float salt) {
    vec2 grid = uv * cells;
    vec2 cell = floor(grid);
    float seed = hash21(cell + salt);
    vec2 local = fract(grid) - vec2(0.2 + 0.6 * hash21(cell + salt + 3.1), 0.2 + 0.6 * hash21(cell + salt + 7.7));
    // Keep each star at least about a pixel wide so it never aliases.
    float pixel = max(max(fwidth(grid.x), fwidth(grid.y)), 0.015);
    float core = exp(-dot(local, local) / (pixel * pixel * 2.5 + 0.0006));
    float rays = exp(-abs(local.x) / (pixel * 1.3)) * exp(-abs(local.y) * 7.0)
        + exp(-abs(local.y) / (pixel * 1.3)) * exp(-abs(local.x) * 7.0);
    float flash = pow(max(0.0, sin(seed * 18.849556 + angle * 4.0 + t * (0.7 + seed))), 10.0);
    // The large showcase stars use salt 83 and keep a visible resting core.
    if (salt == 83.0) flash = 0.25 + 0.75 * flash;
    return vec2((core + rays * 0.35) * flash * step(1.0 - density, seed), seed);
}

float filtered_line(float coordinate, float width) {
    float edge = max(fwidth(coordinate), 0.002);
    float distance = abs(fract(coordinate + 0.5) - 0.5);
    return 1.0 - smoothstep(width, width + edge, distance);
}

void fragment() {
    vec2 effect_uv = UV;
    if (use_layered_uv) {
        vec2 travel = clamp(view_pointer, vec2(-1.0), vec2(1.0)) * clamp(depth, 0.0, 1.0);
        float lift = mix(1.0, foreground_scale, depth);
        effect_uv = (UV - 0.5 - travel * foreground_shift - vec2(subject_offset_x, subject_offset_y))
            / (subject_scale * lift) + 0.5;
    }
    float inside = step(0.0, effect_uv.x) * step(effect_uv.x, 1.0)
        * step(0.0, effect_uv.y) * step(effect_uv.y, 1.0);
    vec4 source = texture(TEXTURE, effect_uv);
    float allowed = texture(permission_mask_texture, effect_uv).r * source.a * inside;
    float subject = 0.0;
    if (use_layered_uv) {
        vec4 coverage = texture(layer_coverage_texture, effect_uv);
        subject = (layer_coverage_use_alpha ? coverage.a : coverage.r) * inside;
        allowed *= subject;
    }
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
    // Showcase rares/Ancients carry a steadier finish; other modes and the
    // quiet starter/common/uncommon tiers retain their original material.
    float rare_presence = smoothstep(0.65, 0.9, sparkle_strength);
    vec3 effect_color = vec3(0.0);
    float effect_alpha = 0.0;

    if (surface_type == 1) {
        effect_color = vec3(0.79, 0.87, 0.94);
        effect_alpha = (0.035 + broad * 0.48 + catch_light * 0.14) * stock;
    } else if (surface_type == 2) {
        effect_color = mix(vec3(0.82, 0.55, 0.23), vec3(1.0, 0.91, 0.66), catch_light);
        effect_alpha = (0.04 + rare_presence * 0.11 + broad * (0.46 + rare_presence * 0.14)
            + catch_light * 0.36) * stock;
    } else if (surface_type == 3) {
        float hue = diagonal * 0.34 + 0.12 * sin(t * 0.4 + phase);
        effect_color = mix(vec3(0.82, 0.86, 0.91), spectrum(hue), 0.32);
        effect_alpha = 0.035 + broad * 0.62 + catch_light * 0.12;
    } else if (surface_type == 4) {
        float hue = diagonal * 0.75 + 0.22 * sin(t * 0.5 + phase);
        effect_color = mix(vec3(0.83), spectrum(hue), 0.68);
        effect_alpha = (0.025 + rare_presence * 0.10 + broad * 0.72 + catch_light * 0.22) * stock;
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

    float alpha = clamp(effect_alpha * allowed * intensity * transform_guard, 0.0, alpha_cap);
    // Additive light, premultiplied so the showcase layers can stack.
    vec3 light = effect_color * alpha;
    float reveal = intensity * transform_guard;

    if (sparkle_strength > 0.0) {
        // Fine stars when the card is large, a few coarse ones when it is small.
        // Rarer tiers carry more stars as well as brighter ones.
        vec2 fine = glints(effect_uv, vec2(30.0, 45.0), 0.2 + 0.45 * sparkle_strength, angle, t, 0.0);
        vec2 coarse = glints(effect_uv, vec2(11.0, 16.5), 0.25 + 0.35 * sparkle_strength, angle * 0.8, t * 0.8, 31.0);
        vec3 fine_color = mix(vec3(1.0, 0.97, 0.9), spectrum(fine.y + angle * 0.3), 0.45);
        vec3 coarse_color = mix(tier_color, spectrum(coarse.y + angle * 0.3), 0.35);
        light += (fine_color * fine.x * detail + coarse_color * coarse.x * mix(0.9, 0.5, detail))
            * allowed * sparkle_strength * reveal * 1.4;
        // A handful of larger, less intermittent glints remain legible in a
        // phone hand/grid without needing pointer movement to catch a flash.
        vec2 hero = glints(effect_uv, vec2(6.0, 9.0), 0.55, angle * 0.45, t * 0.75, 83.0);
        float resting_flash = 0.32 + 0.68 * pow(max(0.0, sin(hero.y * 18.849556 + angle * 2.0 + t)), 2.0);
        light += mix(tier_color, vec3(1.0), 0.65) * hero.x * resting_flash
            * allowed * rare_presence * reveal * 2.2;
    }

    if (use_layered_uv && backdrop_strength > 0.0) {
        // Dust and a slow sheen live on the rear plate, so they share its parallax.
        float plate_zoom = 1.0 + 2.1 * background_shift * depth;
        vec2 travel = clamp(view_pointer, vec2(-1.0), vec2(1.0)) * clamp(depth, 0.0, 1.0);
        vec2 plate_uv = (UV - 0.5) / plate_zoom + 0.5 + travel * background_shift;
        vec2 dust = glints(plate_uv, vec2(20.0, 30.0), 0.15 + 0.35 * backdrop_strength, angle * 0.6, t * 0.7, 57.0);
        float open = 1.0 - subject;
        float sheen = soft_band(diagonal, center, 0.16 + rare_presence * 0.08) * (0.22 + rare_presence * 0.12);
        vec3 dust_color = mix(tier_color, spectrum(dust.y + angle * 0.2), 0.5);
        light += (tier_color * sheen + dust_color * dust.x * mix(0.55, 1.0, detail))
            * open * backdrop_strength * reveal;
    }

    if (use_layered_uv && aura_strength > 0.0) {
        // Blurred coverage from the mask's mipmaps outlines the subject cheaply.
        vec4 near_cover = textureLod(layer_coverage_texture, effect_uv, 3.5);
        vec4 far_cover = textureLod(layer_coverage_texture, effect_uv, 5.0);
        float near_halo = layer_coverage_use_alpha ? near_cover.a : near_cover.r;
        float far_halo = layer_coverage_use_alpha ? far_cover.a : far_cover.r;
        float halo = clamp(max(near_halo, far_halo * 0.8) * inside - subject, 0.0, 1.0);
        float pulse = 0.8 + 0.2 * sin(t * 1.6 + phase);
        vec3 aura_color = mix(tier_color, spectrum(diagonal * 0.8 + angle * 0.3 + t * 0.05 + phase), aura_spectral);
        light += aura_color * halo * pulse * aura_strength * reveal * 0.5;
        // A thin steady rim makes the rare subject read before a sparkle
        // crosses it; coverage is sampled from the existing depth mask.
        float inner_rim = clamp(subject - min(near_halo, far_halo), 0.0, 1.0);
        light += aura_color * inner_rim * rare_presence * aura_strength * reveal * 0.45;
    }

    light = min(light, vec3(0.9));
    // Keep headroom on bright art; remain effect-only and respect UI fades.
    float headroom = 1.0 - smoothstep(0.65, 1.0, max(source.r, max(source.g, source.b)))
        * mix(0.65, 0.40, rare_presence);
    COLOR = vec4(light * card_modulate.rgb, card_modulate.a * headroom);
}
