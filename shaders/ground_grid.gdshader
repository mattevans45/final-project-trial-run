shader_type canvas_item;

varying vec2 world_pos;

// ── Base tiling ──────────────────────────────────────────────────────────────
uniform float tile_size   = 128.0;
uniform float line_width  = 0.8;
uniform float camera_zoom = 1.0;

// ── Colors ───────────────────────────────────────────────────────────────────
uniform vec4 color_a         : source_color = vec4(0.135, 0.133, 0.130, 1.0);
uniform vec4 color_b         : source_color = vec4(0.128, 0.128, 0.132, 1.0);
uniform vec4 grid_line_color : source_color = vec4(0.165, 0.163, 0.158, 1.0);
uniform vec4 reflection_tint : source_color = vec4(0.06,  0.18,  0.36,  1.0);

// ── Pre-baked noise textures (CPU-generated in GroundGrid.cs) ─────────────────
// puddle_noise_tex : Simplex smooth — organic blob shapes
// asphalt_noise_tex: Cellular       — crack / pore detail
uniform sampler2D puddle_noise_tex  : repeat_enable, filter_linear;
uniform sampler2D asphalt_noise_tex : repeat_enable, filter_linear;

// ── Trail splat map (SubViewport texture) ─────────────────────────────────────
// White (1.0) = car was here recently. Black (0.0) = undisturbed water.
// Managed entirely in GroundGrid.cs — no per-car shader logic needed.
uniform sampler2D trail_map      : repeat_disable, filter_linear;
uniform vec2      arena_half_size = vec2(1200.0, 900.0);

// ── Surface detail ────────────────────────────────────────────────────────────
uniform float noise_strength  = 0.022;
uniform float crack_strength  = 0.08;
uniform float wetness         = 1.0;

// ── Oil slicks ────────────────────────────────────────────────────────────────
uniform float oil_slick_strength = 0.40;

// ── Time — updated each frame by GroundGrid._Process ─────────────────────────
uniform float game_time = 0.0;

// ════════════════════════════════════════════════════════════════════════════════

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
}

// ── Minimal helpers — only what cannot be replaced by a texture lookup ────────

// Tile-level hash for per-tile randomness (drain grate placement, oil patches).
// Costs almost nothing — two fract() ops — so not worth a texture lookup.
float hash21(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

// HSV→RGB for oil slick rainbow (analytic, no good texture equivalent).
vec3 hue2rgb(float h) {
    vec3 c = abs(fract(h + vec3(0.0, 2.0/3.0, 1.0/3.0)) * 6.0 - 3.0);
    return clamp(c - 1.0, 0.0, 1.0);
}

// ════════════════════════════════════════════════════════════════════════════════

void fragment() {
    vec2 pos        = world_pos;
    vec2 tile_coord = pos / tile_size;
    vec2 tile_id    = floor(tile_coord);
    vec2 tile_frac  = fract(tile_coord);

    // ── 1. Asphalt base ───────────────────────────────────────────────────────
    // No checker — it creates hard 128-px block edges at tile boundaries.
    vec4 col = mix(color_a, color_b, hash21(tile_id) * 0.06);

    // Domain warp: read a very coarse displacement from one texture, then
    // use it to offset the UV of the finer lookup.  This breaks the ~700 px
    // repeat period so the eye can't lock onto a regular tiling grid.
    vec2 warp = (texture(asphalt_noise_tex, pos * 0.00028 + vec2(0.17, 0.83)).rg
                 - vec2(0.5)) * 2.0;

    float surf_fine  = texture(asphalt_noise_tex, pos * 0.0014 + warp * 0.30).r;
    col.rgb         += (surf_fine - 0.5) * noise_strength;

    float surf_macro = texture(asphalt_noise_tex, pos * 0.00038 + warp * 0.15 + vec2(0.33)).r;
    col.rgb         *= 1.0 - (surf_macro - 0.5) * 0.04;

    // Cracks: cellular noise at mid-scale — also warped so cracks look organic
    float crack_n = texture(asphalt_noise_tex, pos * 0.0038 + warp * 0.20 + vec2(0.5, 0.5)).r;
    float crack   = 1.0 - smoothstep(0.0, 0.15, crack_n);
    col.rgb      -= crack * crack_strength;

    // ── 2. Puddle shape — warped to break tiling ─────────────────────────────
    vec2 p_warp = (texture(puddle_noise_tex, pos * 0.00032 + vec2(0.55, 0.22)).rg
                   - vec2(0.5)) * 2.0;
    float p_n1  = texture(puddle_noise_tex, pos * 0.00170 + vec2(0.30, 0.70) + p_warp * 0.22).r;
    float p_n2  = texture(puddle_noise_tex, pos * 0.00420 + vec2(0.70, 0.20) + p_warp * 0.14).r;
    float p_v   = p_n1 * 0.65 + p_n2 * 0.35;

    // Raise the lower threshold to ~0.58 so only the top ~20 % of noise values
    // become puddles — fewer, smaller pools instead of a swamped arena.
    float base_puddle = smoothstep(0.58, 0.76, p_v) * wetness;
    float puddle_edge = smoothstep(0.54, 0.58, p_v)
                      * (1.0 - smoothstep(0.58, 0.67, p_v)) * wetness;

    // ── 3. Trail splat map — replaces the rigid wake cone ─────────────────────
    // Convert world position to trail texture UV (arena spans ±arena_half_size)
    vec2  trail_uv  = (pos + arena_half_size) / (arena_half_size * 2.0);
    float car_trail = texture(trail_map, trail_uv).r;  // 1=car drove here, 0=undisturbed

    // Trail carves a path through standing water
    float final_puddle = max(0.0, base_puddle - car_trail);

    // ── 4. Puddle rendering: reflective surface ────────────────────────────────
    // Wet asphalt darkens where water soaks in (stronger for clearly visible puddles)
    col.rgb *= 1.0 - final_puddle * wetness * 0.48;

    // Mirror reflection: vivid blue-teal water colour
    float reflectivity = final_puddle * final_puddle;
    vec3  water_col    = vec3(0.07, 0.20, 0.52);
    vec3  reflect_col  = mix(col.rgb, water_col, 0.72) * 0.88;
    col.rgb = mix(col.rgb, reflect_col, reflectivity);

    // Perimeter catch-light: bright aqua rim at the water's edge
    col.rgb = mix(col.rgb, vec3(0.25, 0.55, 0.88), puddle_edge * 0.70);

    // ── 5. Trail effects — visible on ALL surface types ───────────────────────
    // Dry tire tracks: slight darkening wherever the car drove.
    // This makes the splat map readable on asphalt, not just over puddles.
    col.rgb *= 1.0 - car_trail * 0.22;

    // Wet glint on top of the track inside puddle areas
    col.rgb = mix(col.rgb,
                  col.rgb * 1.18 + reflection_tint.rgb * 0.14,
                  car_trail * base_puddle);

    // ── 6. Oil slicks ─────────────────────────────────────────────────────────
    // Oil is DISTINCT from puddles: near-black glossy base + vivid animated rainbow.
    // The cell hash keeps slicks spatially separated from puddle blobs.
    float oil_n    = texture(puddle_noise_tex, pos * 0.0018 + vec2(0.55, 0.33)).r;
    float oil_cell = hash21(floor(pos * 0.00115 + vec2(77.3, 91.1)));
    float oil_mask = smoothstep(0.55, 0.72, oil_n) * smoothstep(0.82, 0.88, oil_cell);
    // Slow hue animation makes it shimmer (impossible to mistake for water)
    float hue      = fract(pos.x * 0.00083 + pos.y * 0.00061 + oil_n * 0.4 + game_time * 0.04);
    vec3  rainbow  = hue2rgb(hue);
    // Darken to near-black gloss first, then overlay vivid rainbow on top
    vec3  oil_col  = col.rgb * 0.28 + rainbow * 0.68;
    col.rgb        = mix(col.rgb, oil_col, oil_mask * oil_slick_strength);

    // ── 7. Drain grates ───────────────────────────────────────────────────────
    float is_drain  = step(0.94, hash21(tile_id * vec2(0.13, 0.17)));
    vec2  tc        = tile_frac - 0.5;
    float cross_h   = step(abs(tc.x), 0.022) * step(abs(tc.y), 0.095);
    float cross_v   = step(abs(tc.y), 0.022) * step(abs(tc.x), 0.095);
    float grate_rim = step(max(abs(tc.x), abs(tc.y)), 0.12)
                    * (1.0 - step(max(abs(tc.x), abs(tc.y)), 0.105));
    float grate     = clamp(cross_h + cross_v + grate_rim * 0.5, 0.0, 1.0) * is_drain;
    col.rgb         = mix(col.rgb, col.rgb * 0.42, grate * 0.88);
    col.rgb         = mix(col.rgb, col.rgb * 1.15, grate * grate_rim * wetness * 0.5);

    // ── 8. Grid lines ─────────────────────────────────────────────────────────
    float lh     = (line_width / max(camera_zoom, 0.01)) / tile_size * 0.5;
    vec2  gd     = min(tile_frac, 1.0 - tile_frac);
    float grid_m = smoothstep(lh + 0.006, lh, min(gd.x, gd.y));
    col          = mix(col, grid_line_color, grid_m * 0.12);

    COLOR = col;
}
