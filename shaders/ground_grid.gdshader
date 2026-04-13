shader_type canvas_item;

varying vec2 world_pos;

// ── Base tiling ──────────────────────────────────────────────────────────────
uniform float tile_size = 128.0;
uniform float line_width = 0.8;
uniform float camera_zoom = 1.0;

// ── Colors ───────────────────────────────────────────────────────────────────
uniform vec4 color_a         : source_color = vec4(0.135, 0.133, 0.130, 1.0);
uniform vec4 color_b         : source_color = vec4(0.128, 0.128, 0.132, 1.0);
uniform vec4 grid_line_color : source_color = vec4(0.165, 0.163, 0.158, 1.0);

// ── Puddle reflection tint — ambient sky colour reflected in standing water ──
uniform vec4 reflection_tint : source_color = vec4(0.04, 0.14, 0.28, 1.0);

// ── Surface detail ───────────────────────────────────────────────────────────
uniform float noise_strength  = 0.022;
uniform float crack_strength  = 0.08;
uniform float wetness         = 1.0;

uniform bool      use_crack_texture = false;
uniform sampler2D crack_texture : repeat_enable;

// ── Oil slicks ───────────────────────────────────────────────────────────────
uniform float oil_slick_strength = 0.40;

// ── Car dynamics (wake / ripples) ────────────────────────────────────────────
uniform vec2  car_pos;
uniform vec2  velocity_dir = vec2(1.0, 0.0);
uniform float car_speed    = 0.0;
uniform float car_drift    = 0.0;

// ── Time — pass via GroundGrid._Process for ripple animation ─────────────────
uniform float game_time = 0.0;

// ════════════════════════════════════════════════════════════════════════════════

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
}

// ── Hash / noise helpers ──────────────────────────────────────────────────────

float hash21(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

vec2 hash22(vec2 p) {
    p = vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)));
    return fract(sin(p) * 43758.5453);
}

float value_noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(
        mix(hash21(i),              hash21(i + vec2(1.0, 0.0)), f.x),
        mix(hash21(i + vec2(0.0, 1.0)), hash21(i + vec2(1.0, 1.0)), f.x),
        f.y
    );
}

float fbm2(vec2 p) {
    return 0.500 * value_noise(p)
         + 0.250 * value_noise(p * 2.17);
}

// Three-octave FBM for richer, more detailed puddle shapes
float fbm3(vec2 p) {
    return 0.500 * value_noise(p)
         + 0.250 * value_noise(p * 2.17)
         + 0.125 * value_noise(p * 4.73);
}

float voronoi_edge(vec2 p) {
    vec2 n = floor(p);
    vec2 f = fract(p);
    float md = 8.0;
    for (int j = -1; j <= 1; j++)
    for (int i = -1; i <= 1; i++) {
        vec2  g = vec2(float(i), float(j));
        vec2  r = g + hash22(n + g) - f;
        float d = dot(r, r);
        md = min(md, d);
    }
    return md;
}

// Hue → RGB (s=1, v=1 rainbow)
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

    // ── 1. Asphalt base ──────────────────────────────────────────────────────
    float checker = mod(tile_id.x + tile_id.y, 2.0);
    vec4  col     = mix(color_a, color_b, checker * 0.15);
    col.rgb += (hash21(tile_id) - 0.5) * 0.008;
    col.rgb += (value_noise(pos * 0.18) - 0.5) * noise_strength;

    // Cracks
    float crack = 0.0;
    if (use_crack_texture) {
        crack = 1.0 - texture(crack_texture, pos * 0.005).r;
    } else {
        crack = 1.0 - smoothstep(0.0, 0.012, voronoi_edge(pos * 0.05));
    }
    col.rgb -= crack * crack_strength;

    // Macro tonal variation
    float macro = fbm2(pos * 0.0008);
    col.rgb *= 1.0 - (macro - 0.5) * 0.08;

    // ── 2. Wake calculation ───────────────────────────────────────────────────
    vec2  delta        = pos - car_pos;
    float dist         = length(delta);
    vec2  dir_to_pixel = delta / (dist + 0.0001);

    float dot_wake       = dot(dir_to_pixel, -velocity_dir);
    float wake_cone      = mix(0.96, 0.40, car_drift);
    float wake_shape     = smoothstep(wake_cone, 1.0, dot_wake);
    float wake_length    = 50.0 + car_speed * 0.6;
    float wake_falloff   = 1.0 - smoothstep(0.0, wake_length, dist);
    float speed_factor   = clamp(car_speed * 0.01, 0.0, 1.0);
    float active_wake    = wake_shape * wake_falloff * speed_factor;

    // ── 3. Puddle shape ───────────────────────────────────────────────────────
    // Blend 3-octave FBM (smooth blobs) with Voronoi cells (organic boundaries)
    float p_fbm     = fbm3(pos * 0.0028 + vec2(17.4, 83.1));
    float p_voronoi = 1.0 - sqrt(clamp(voronoi_edge(pos * 0.005 + vec2(54.1, 21.7)), 0.0, 1.0));
    float p_noise   = p_fbm * 0.65 + p_voronoi * 0.35;

    float base_puddle = smoothstep(0.42, 0.65, p_noise) * wetness;
    // Thin bright perimeter ring — where puddle meets dry asphalt
    float puddle_edge = smoothstep(0.36, 0.42, p_noise)
                      * (1.0 - smoothstep(0.42, 0.52, p_noise)) * wetness;

    // Car displaces standing water as it passes through
    float final_puddle = max(0.0, base_puddle - active_wake * 1.5);

    // ── 4. Puddle rendering: reflective, not dark ─────────────────────────────
    // Wet asphalt: first darkens slightly as water wets the surface
    float wetness_darken = final_puddle * wetness * 0.30;
    col.rgb *= 1.0 - wetness_darken;

    // Reflection brightens the puddle centre (squared for falloff toward edges)
    float reflectivity = final_puddle * final_puddle;
    vec3  reflect_col  = reflection_tint.rgb * 0.50 + col.rgb * 0.20;
    col.rgb = mix(col.rgb, reflect_col, reflectivity);

    // Perimeter catch-light: thin bright rim at the water's edge
    col.rgb = mix(col.rgb, vec3(0.30, 0.42, 0.62), puddle_edge * 0.55);

    // ── 5. Animated ripples from the car ─────────────────────────────────────
    // Concentric sine rings on the water surface, fading with distance and speed
    float rip_speed  = clamp(car_speed * 0.006, 0.0, 1.0);
    float rip_wave   = sin(dist * 0.072 - game_time * 5.5);
    rip_wave         = pow(max(rip_wave, 0.0), 6.0);   // sharp, narrow rings
    float rip_fall   = (1.0 - smoothstep(0.0, wake_length * 2.0, dist)) * base_puddle;
    float ripple     = rip_wave * rip_fall * rip_speed * 0.45;
    col.rgb         += ripple * (reflection_tint.rgb * 0.7 + vec3(0.08));

    // Wet wake streak: shiny trail left in puddle water
    float wake_streak = active_wake * base_puddle;
    col.rgb = mix(col.rgb, col.rgb * 1.18 + reflection_tint.rgb * 0.12, wake_streak);

    // ── 6. Oil slicks ─────────────────────────────────────────────────────────
    // Deterministic patches (~8 % of area) with rainbow iridescence
    float oil_cell = hash21(floor(pos * 0.00115 + vec2(77.3, 91.1)));
    float is_oil   = smoothstep(0.82, 0.88, oil_cell);

    float oil_fbm  = fbm2(pos * 0.006 + vec2(33.0, 55.0));
    float oil_mask = smoothstep(0.48, 0.68, oil_fbm) * is_oil;

    // Hue shifts continuously across the slick surface
    float hue       = fract(pos.x * 0.00083 + pos.y * 0.00061 + oil_fbm * 0.4);
    vec3  oil_color = hue2rgb(hue);
    col.rgb = mix(col.rgb,
                  col.rgb + oil_color * 0.28,
                  oil_mask * oil_slick_strength * wetness);

    // ── 7. Drain grates ───────────────────────────────────────────────────────
    // About 6 % of tiles get a small metal grate drawn at the tile centre
    float drain_chance = hash21(tile_id * vec2(0.13, 0.17));
    float is_drain     = step(0.94, drain_chance);

    vec2  tc         = tile_frac - 0.5;       // centre of tile = (0,0)
    float cross_h    = step(abs(tc.x), 0.022) * step(abs(tc.y), 0.095);
    float cross_v    = step(abs(tc.y), 0.022) * step(abs(tc.x), 0.095);
    float grate_rim  = step(max(abs(tc.x), abs(tc.y)), 0.12)
                     * (1.0 - step(max(abs(tc.x), abs(tc.y)), 0.105));
    float grate      = clamp(cross_h + cross_v + grate_rim * 0.5, 0.0, 1.0) * is_drain;
    col.rgb          = mix(col.rgb, col.rgb * 0.42, grate * 0.88);
    // Slight wet glint on the grate when wet
    col.rgb          = mix(col.rgb, col.rgb * 1.15, grate * grate_rim * wetness * 0.5);

    // ── 8. Subtle grid lines ──────────────────────────────────────────────────
    float lh       = (line_width / max(camera_zoom, 0.01)) / tile_size * 0.5;
    vec2  gd       = min(tile_frac, 1.0 - tile_frac);
    float grid_m   = smoothstep(lh + 0.006, lh, min(gd.x, gd.y));
    col            = mix(col, grid_line_color, grid_m * 0.12);

    COLOR = col;
}
