shader_type canvas_item;

// ── World-anchored asphalt: tiles, grid lines, dashed lane markings, grit ──
// Attach to a large ColorRect. The varying passes true world-space position
// from vertex → fragment so the pattern stays fixed as the camera moves.

varying vec2 world_pos;

uniform float tile_size      : hint_range(32.0, 512.0) = 128.0;
uniform float line_width     : hint_range(0.5, 4.0)    = 1.2;
uniform float lane_interval  : hint_range(1.0, 16.0)   = 4.0;
uniform float lane_width     : hint_range(1.0, 6.0)    = 2.5;
uniform float dash_ratio     : hint_range(0.1, 0.9)    = 0.45;

uniform vec4 color_a         : source_color = vec4(0.165, 0.165, 0.180, 1.0);
uniform vec4 color_b         : source_color = vec4(0.137, 0.137, 0.152, 1.0);
uniform vec4 grid_line_color : source_color = vec4(0.230, 0.230, 0.250, 1.0);
uniform vec4 lane_color      : source_color = vec4(0.850, 0.780, 0.350, 0.7);

uniform float noise_strength : hint_range(0.0, 0.15)  = 0.055;
uniform float noise_scale    : hint_range(0.001, 0.05) = 0.008;

// ── Vertex: capture world-space position before the camera transform ──────
void vertex() {
    // MODEL_MATRIX is local → world (canvas) space for CanvasItem nodes.
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
}

// ── Hash / noise helpers ──────────────────────────────────────────────────
float hash21(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float value_noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(
        mix(hash21(i),               hash21(i + vec2(1.0, 0.0)), f.x),
        mix(hash21(i + vec2(0.0, 1.0)), hash21(i + vec2(1.0, 1.0)), f.x),
        f.y
    );
}

float fbm(vec2 p) {
    float v = 0.0, a = 0.5;
    for (int i = 0; i < 4; i++) { v += a * value_noise(p); p *= 2.1; a *= 0.5; }
    return v;
}

// ── Fragment ──────────────────────────────────────────────────────────────
void fragment() {
    vec2 pos = world_pos;   // true world coordinates — stationary as camera moves

    // ── Tile coordinates ──────────────────────────────────────────────────
    vec2 tile_coord = pos / tile_size;
    vec2 tile_id    = floor(tile_coord);
    vec2 tile_frac  = fract(tile_coord);

    // ── Checkerboard base ─────────────────────────────────────────────────
    float checker  = mod(tile_id.x + tile_id.y, 2.0);
    vec4 base_color = mix(color_a, color_b, checker);

    // ── Asphalt grit (multi-octave noise) ────────────────────────────────
    float noise      = fbm(pos * noise_scale);
    float fine_noise = value_noise(pos * 0.15) * 0.5 + 0.5;
    base_color.rgb  += (noise      - 0.5) * noise_strength;
    base_color.rgb  += (fine_noise - 0.5) * noise_strength * 0.3;

    // Per-tile micro colour variation
    float tile_var   = hash21(tile_id * 7.31) * 0.03 - 0.015;
    base_color.rgb  += tile_var;

    // ── Grid lines ────────────────────────────────────────────────────────
    float lh = line_width / tile_size * 0.5;
    float gx = max(
        smoothstep(lh + 0.005, lh, abs(tile_frac.x)),
        smoothstep(lh + 0.005, lh, abs(tile_frac.x - 1.0))
    );
    float gy = max(
        smoothstep(lh + 0.005, lh, abs(tile_frac.y)),
        smoothstep(lh + 0.005, lh, abs(tile_frac.y - 1.0))
    );
    base_color = mix(base_color, grid_line_color, max(gx, gy) * 0.6);

    // ── Dashed lane markings every lane_interval tiles ────────────────────
    vec2 lane_id = mod(tile_id, lane_interval);

    if (abs(lane_id.x) < 0.5) {
        float half_lw  = lane_width / tile_size * 0.5;
        float lane_mask = max(
            smoothstep(half_lw + 0.008, half_lw, abs(tile_frac.x)),
            smoothstep(half_lw + 0.008, half_lw, abs(tile_frac.x - 1.0))
        );
        lane_mask *= step(fract(tile_coord.y * 0.5), dash_ratio);
        base_color = mix(base_color, lane_color, lane_mask * lane_color.a);
    }

    if (abs(lane_id.y) < 0.5) {
        float half_lw  = lane_width / tile_size * 0.5;
        float lane_mask = max(
            smoothstep(half_lw + 0.008, half_lw, abs(tile_frac.y)),
            smoothstep(half_lw + 0.008, half_lw, abs(tile_frac.y - 1.0))
        );
        lane_mask *= step(fract(tile_coord.x * 0.5), dash_ratio);
        base_color = mix(base_color, lane_color, lane_mask * lane_color.a);
    }

    // ── Wear / stain patches ──────────────────────────────────────────────
    float stain = fbm(pos * 0.003 + 100.0);
    stain = smoothstep(0.45, 0.55, stain);
    base_color.rgb = mix(base_color.rgb, base_color.rgb * 0.88, stain * 0.15);

    COLOR = base_color;
}
