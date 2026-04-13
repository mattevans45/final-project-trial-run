shader_type canvas_item;

varying vec2 world_pos;
varying float wetness_mask; // Used to pass the fluid locations to the lighting engine

// ── Base tiling ──────────────────────────────────────────────────────────────
uniform float tile_size   = 128.0;
uniform float line_width  = 0.8;
uniform float camera_zoom = 1.0;

// ── Colors ───────────────────────────────────────────────────────────────────
uniform vec4 color_a         : source_color = vec4(0.135, 0.133, 0.130, 1.0);
uniform vec4 color_b         : source_color = vec4(0.128, 0.128, 0.132, 1.0);
uniform vec4 grid_line_color : source_color = vec4(0.165, 0.163, 0.158, 1.0);
uniform vec4 reflection_tint : source_color = vec4(0.06,  0.18,  0.36,  1.0);

// ── Pre-baked noise textures ─────────────────────────────────────────────────
uniform sampler2D puddle_noise_tex  : repeat_enable, filter_linear;
uniform sampler2D asphalt_noise_tex : repeat_enable, filter_linear;

// ── Maps ─────────────────────────────────────────────────────────────────────
uniform sampler2D trail_map     : repeat_disable, filter_linear;
uniform sampler2D depletion_map : repeat_disable, filter_linear;
uniform vec2      arena_half_size = vec2(1200.0, 900.0);

// ── Surface detail ───────────────────────────────────────────────────────────
uniform float noise_strength  = 0.065; 
uniform float crack_strength  = 0.18;  
uniform float wetness         = 1.0;
uniform float oil_slick_strength = 0.40;
uniform float game_time = 0.0;

// ════════════════════════════════════════════════════════════════════════════

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
}

float hash21(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

vec3 hue2rgb(float h) {
    vec3 c = abs(fract(h + vec3(0.0, 2.0/3.0, 1.0/3.0)) * 6.0 - 3.0);
    return clamp(c - 1.0, 0.0, 1.0);
}

void fragment() {
    vec2 pos = world_pos;
    
    vec2 tile_coord = pos / tile_size;
    vec2 tile_id    = floor(tile_coord);
    vec2 tile_frac  = fract(tile_coord);
    
    vec2 warp = (texture(asphalt_noise_tex, pos * 0.00028 + vec2(0.17, 0.83)).rg - vec2(0.5)) * 2.0;
    float surf_fine = texture(asphalt_noise_tex, pos * 0.0014 + warp * 0.30).r;

    vec2  map_uv  = (pos + arena_half_size) / (arena_half_size * 2.0);
    float car_trail = texture(trail_map, map_uv).r; 
    float permanent_dryness = texture(depletion_map, map_uv).r;

    vec2 p_warp = (texture(puddle_noise_tex, pos * 0.00032 + vec2(0.55, 0.22)).rg - vec2(0.5)) * 2.0;
    float p_n1  = texture(puddle_noise_tex, pos * 0.00170 + vec2(0.30, 0.70) + p_warp * 0.22).r;
    float p_n2  = texture(puddle_noise_tex, pos * 0.00420 + vec2(0.70, 0.20) + p_warp * 0.14).r;
    float p_v   = p_n1 * 0.65 + p_n2 * 0.35;

    p_v -= surf_fine * 0.12; 
    p_v -= permanent_dryness * 0.6; 

    float base_puddle = smoothstep(0.48, 0.68, p_v) * wetness;
    float puddle_edge = smoothstep(0.45, 0.48, p_v) - smoothstep(0.48, 0.55, p_v);
    puddle_edge *= wetness;

    float final_puddle = max(0.0, base_puddle - car_trail);
    float active_edge  = max(0.0, puddle_edge - car_trail);
    float spray_foam   = smoothstep(0.1, 0.8, car_trail) * base_puddle;

    vec2 refract_pos = pos + (p_warp * final_puddle * 22.0);

    vec4 col = mix(color_a, color_b, hash21(tile_id) * 0.18);
    col.rgb += (surf_fine - 0.5) * noise_strength;
    
    float surf_macro = texture(puddle_noise_tex, refract_pos * 0.00038 + warp * 0.15 + vec2(0.33)).r;
    col.rgb *= 1.0 - (surf_macro - 0.5) * 0.15; 
    
    float crack_n = texture(asphalt_noise_tex, refract_pos * 0.0038 + warp * 0.20 + vec2(0.5, 0.5)).r;
    float crack   = 1.0 - smoothstep(0.0, 0.15, crack_n);
    col.rgb -= crack * crack_strength;

    float inner_shadow = smoothstep(0.0, 0.15, final_puddle) - smoothstep(0.15, 0.50, final_puddle);
    col.rgb -= vec3(0.45) * inner_shadow; 

    col.rgb *= mix(1.0, 0.35, final_puddle); 
    col.rgb = mix(col.rgb, reflection_tint.rgb * 1.5, final_puddle * 0.75); 
    
    col.rgb += vec3(0.15) * active_edge; 
    col.rgb += vec3(0.8, 0.9, 1.0) * spray_foam * 0.8; 
    col.rgb *= 1.0 - (car_trail * 0.15); 

    float oil_n    = texture(puddle_noise_tex, pos * 0.0018 + vec2(0.55, 0.33) + p_warp * 0.3).r;
    float oil_cell = hash21(floor(pos * 0.00115 + vec2(77.3, 91.1)));
    
    float roughened_oil = oil_n - (surf_fine * 0.2);
    float oil_mask = smoothstep(0.55, 0.70, roughened_oil) * smoothstep(0.82, 0.88, oil_cell);

    float hue      = fract(pos.x * 0.00083 + pos.y * 0.00061 + oil_n * 0.4 + game_time * 0.04);
    vec3  rainbow  = hue2rgb(hue);
    vec3  oil_col  = col.rgb * 0.15 + rainbow * 0.60; 
    
    col.rgb = mix(col.rgb, oil_col, oil_mask * oil_slick_strength);

    float is_drain  = step(0.94, hash21(tile_id * vec2(0.13, 0.17)));
    vec2  tc        = tile_frac - 0.5;
    
    float cross_h   = step(abs(tc.x), 0.022) * step(abs(tc.y), 0.095);
    float cross_v   = step(abs(tc.y), 0.022) * step(abs(tc.x), 0.095);
    
    float grate_rim = step(max(abs(tc.x), abs(tc.y)), 0.12) * (1.0 - step(max(abs(tc.x), abs(tc.y)), 0.105));
    float grate     = clamp(cross_h + cross_v + grate_rim * 0.5, 0.0, 1.0) * is_drain;
    
    col.rgb = mix(col.rgb, col.rgb * 0.42, grate * 0.88);
    col.rgb = mix(col.rgb, col.rgb * 1.15, grate * grate_rim * wetness * 0.5);

    float lh     = (line_width / max(camera_zoom, 0.01)) / tile_size * 0.5;
    vec2  gd     = min(tile_frac, 1.0 - tile_frac);
    float grid_m = smoothstep(lh + 0.006, lh, min(gd.x, gd.y));
    
    float fluid_coverage = max(final_puddle, oil_mask);
    grid_m *= (1.0 - (fluid_coverage * 0.8));

    col.rgb = mix(col.rgb, grid_line_color.rgb, grid_m * 0.15);

    // STORE THE FLUID MASK SO THE LIGHT ENGINE CAN READ IT
    wetness_mask = fluid_coverage;
    COLOR = vec4(col.rgb, 1.0);

    // ── Procedural Normal Map ────────────────────────────────────────────────
    float physical_height = (surf_fine - 0.5) * noise_strength;
    physical_height -= crack * crack_strength;
    physical_height += active_edge * 0.005; 
    physical_height = mix(physical_height, -0.05, fluid_coverage);

    float dx = dFdx(physical_height);
    float dy = dFdy(physical_height);

    NORMAL = normalize(vec3(-dx * 250.0, -dy * 250.0, 1.0));
}

// ════════════════════════════════════════════════════════════════════════════
// ── CUSTOM SPECULAR LIGHTING ENGINE ─────────────────────────────────────────

void light() {
    // 1. Standard Diffuse Lighting (Preserves your 3D shadows and bevels)
    float NdotL = max(0.0, dot(NORMAL, LIGHT_DIRECTION));
    vec3 diffuse = LIGHT_COLOR.rgb * COLOR.rgb * LIGHT_ENERGY * NdotL;

    // 2. Blinn-Phong Specular Reflection Math
    vec3 view_dir = vec3(0.0, 0.0, 1.0); // Looking straight down at the 2D plane
    vec3 half_vector = normalize(LIGHT_DIRECTION + view_dir);
    float NdotH = max(0.0, dot(NORMAL, half_vector));

    // 3. Dynamic Material Properties based on our wetness mask
    // Dry asphalt is rough and barely catches light (low shininess, low intensity).
    // Fluids are mirror-smooth and catch brilliant highlights.
    float shininess = mix(5.0, 200.0, wetness_mask);
    float spec_intensity = mix(0.02, 3.5, wetness_mask);

    // 4. Calculate final glare
    float specular = pow(NdotH, shininess) * spec_intensity * LIGHT_ENERGY;

    // Add the glossy sheen on top of the base physical lighting
    LIGHT = vec4(diffuse + (LIGHT_COLOR.rgb * specular), LIGHT_COLOR.a);
}