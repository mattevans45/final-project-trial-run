shader_type canvas_item;

varying vec2 world_pos;

// ── Base tiling ─────────────────────────────────────────────────
uniform float tile_size = 128.0;
uniform float line_width = 0.8;
uniform float camera_zoom = 1.0; // Push this from C# to keep grid lines consistent

// ── Colors ──────────────────────────────────────────────────────
uniform vec4 color_a : source_color = vec4(0.135, 0.133, 0.130, 1.0);
uniform vec4 color_b : source_color = vec4(0.128, 0.128, 0.132, 1.0);
uniform vec4 grid_line_color : source_color = vec4(0.165, 0.163, 0.158, 1.0);

// ── Surface detail ───────────────────────────────────────────────
uniform float noise_strength = 0.022;
uniform float crack_strength = 0.08;
uniform float wetness = 1.0; 

uniform bool use_crack_texture = false;
uniform sampler2D crack_texture : repeat_enable; // Performance optimization

// ── Car Dynamics (For Wake) ──────────────────────────────────────
uniform vec2 car_pos;
uniform vec2 velocity_dir = vec2(1.0, 0.0);
uniform float car_speed = 0.0;
uniform float car_drift = 0.0;

// ================================================================

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
}

// ── Fast Noise Helpers ───────────────────────────────────────────
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
        mix(hash21(i), hash21(i + vec2(1.0, 0.0)), f.x),
        mix(hash21(i + vec2(0.0, 1.0)), hash21(i + vec2(1.0, 1.0)), f.x),
        f.y
    );
}

float fbm2(vec2 p) {
    float v = 0.5 * value_noise(p);
    v += 0.25 * value_noise(p * 2.17);
    return v;
}

float voronoi_edge(vec2 p) {
    vec2 n = floor(p);
    vec2 f = fract(p);
    float md = 8.0;
    for (int j = -1; j <= 1; j++)
    for (int i = -1; i <= 1; i++) {
        vec2 g = vec2(float(i), float(j));
        vec2 r = g + hash22(n + g) - f;
        float d = dot(r, r);
        md = min(md, d);
    }
    return md;
}

// ================================================================

void fragment() {
    vec2 pos = world_pos;
    vec2 tile_coord = pos / tile_size;
    vec2 tile_id = floor(tile_coord);
    vec2 tile_frac = fract(tile_coord);

    // ── 1. Asphalt Base ──────────────────────────────────────────
    float checker = mod(tile_id.x + tile_id.y, 2.0);
    vec4 col = mix(color_a, color_b, checker * 0.15);
    col.rgb += (hash21(tile_id) - 0.5) * 0.008;

    // Surface texture
    col.rgb += (value_noise(pos * 0.18) - 0.5) * noise_strength;
    
    // Cracks
    float crack = 0.0;
    if (use_crack_texture) {
        // Fast: Sample a pre-baked Voronoi seamless texture
        crack = 1.0 - texture(crack_texture, pos * 0.005).r;
    } else {
        // Expensive mathematical fallback
        crack = 1.0 - smoothstep(0.0, 0.012, voronoi_edge(pos * 0.05));
    }
    col.rgb -= crack * crack_strength;

    // Macro variation
    float macro = fbm2(pos * 0.0008);
    col.rgb *= 1.0 - (macro - 0.5) * 0.08;

    // ── 2. The Wake Calculation ──────────────────────────────────
    vec2 delta = pos - car_pos;
    float dist = length(delta);
    vec2 dir_to_pixel = delta / (dist + 0.0001);
    
    float dot_wake = dot(dir_to_pixel, -velocity_dir);
    float wake_cone_width = mix(0.96, 0.4, car_drift);
    float wake_shape = smoothstep(wake_cone_width, 1.0, dot_wake);
    float wake_length = 50.0 + (car_speed * 0.6);
    float wake_falloff = 1.0 - smoothstep(0.0, wake_length, dist);
    
    float speed_factor = clamp(car_speed * 0.01, 0.0, 1.0);
    float active_wake = wake_shape * wake_falloff * speed_factor;

    // ── 3. Puddles & Displacement ────────────────────────────────
    float puddle_noise = fbm2(pos * 0.003 + 100.0);
    float base_puddle = smoothstep(0.45, 0.7, puddle_noise) * wetness;
    
    float final_puddle = max(0.0, base_puddle - (active_wake * 1.5));
    col.rgb *= mix(1.0, 0.65, final_puddle);

    // ── 4. Subtle Grid ───────────────────────────────────────────
    // Divide by camera_zoom so lines don't vanish when zoomed out
    float lh = (line_width / max(camera_zoom, 0.01)) / tile_size * 0.5;
    
    vec2 grid_dist = min(tile_frac, 1.0 - tile_frac);
    float grid_mask = smoothstep(lh + 0.006, lh, min(grid_dist.x, grid_dist.y));
    col = mix(col, grid_line_color, grid_mask * 0.12);

    COLOR = col;
}