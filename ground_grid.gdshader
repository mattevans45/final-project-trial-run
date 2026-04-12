shader_type canvas_item;

// ── Asphalt ground with procedural grid, noise, and lane markings ──
// Attach to a large ColorRect or Sprite2D covering the play area.

uniform float tile_size : hint_range(32.0, 512.0) = 128.0;
uniform float line_width : hint_range(0.5, 4.0) = 1.2;
uniform float lane_interval : hint_range(1.0, 16.0) = 4.0;
uniform float lane_width : hint_range(1.0, 6.0) = 2.5;
uniform float dash_ratio : hint_range(0.1, 0.9) = 0.45;

uniform vec4 color_a : source_color = vec4(0.165, 0.165, 0.18, 1.0);
uniform vec4 color_b : source_color = vec4(0.137, 0.137, 0.152, 1.0);
uniform vec4 grid_line_color : source_color = vec4(0.23, 0.23, 0.25, 1.0);
uniform vec4 lane_color : source_color = vec4(0.85, 0.78, 0.35, 0.7);

uniform float noise_strength : hint_range(0.0, 0.15) = 0.06;
uniform float noise_scale : hint_range(0.001, 0.05) = 0.008;
uniform float vignette_strength : hint_range(0.0, 1.0) = 0.3;

// Simple hash-based noise (no texture needed)
float hash21(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float value_noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f); // smoothstep

    float a = hash21(i);
    float b = hash21(i + vec2(1.0, 0.0));
    float c = hash21(i + vec2(0.0, 1.0));
    float d = hash21(i + vec2(1.0, 1.0));

    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm(vec2 p) {
    float val = 0.0;
    float amp = 0.5;
    for (int i = 0; i < 4; i++) {
        val += amp * value_noise(p);
        p *= 2.1;
        amp *= 0.5;
    }
    return val;
}

void fragment() {
    // World-space UV (assumes the rect is placed correctly)
    vec2 world_pos = (UV - 0.5) * vec2(textureSize(TEXTURE, 0));
    // For a ColorRect, use VERTEX or pass world coords via varying.
    // Simpler: use FRAGCOORD offset by scroll. We'll use UV * rect_size.
    // Actually for a CanvasItem we can use VERTEX which gives local coords.

    vec2 pos = VERTEX;

    // ── Tile coordinates ──
    vec2 tile_coord = pos / tile_size;
    vec2 tile_id = floor(tile_coord);
    vec2 tile_frac = fract(tile_coord);

    // ── Checkerboard ──
    float checker = mod(tile_id.x + tile_id.y, 2.0);
    vec4 base_color = mix(color_a, color_b, checker);

    // ── Asphalt noise (grit texture) ──
    float noise = fbm(pos * noise_scale);
    float fine_noise = value_noise(pos * 0.15) * 0.5 + 0.5;
    base_color.rgb += (noise - 0.5) * noise_strength;
    base_color.rgb += (fine_noise - 0.5) * noise_strength * 0.3;

    // ── Per-tile color variation ──
    float tile_var = hash21(tile_id * 7.31) * 0.03 - 0.015;
    base_color.rgb += tile_var;

    // ── Grid lines ──
    float line_half = line_width / tile_size * 0.5;
    float gx = smoothstep(line_half + 0.005, line_half, abs(tile_frac.x));
    float gy = smoothstep(line_half + 0.005, line_half, abs(tile_frac.y));
    // Also check from the other side (frac near 1.0)
    gx = max(gx, smoothstep(line_half + 0.005, line_half, abs(tile_frac.x - 1.0)));
    gy = max(gy, smoothstep(line_half + 0.005, line_half, abs(tile_frac.y - 1.0)));
    float grid = max(gx, gy);
    base_color = mix(base_color, grid_line_color, grid * 0.6);

    // ── Lane markings (dashed yellow lines at intervals) ──
    vec2 lane_coord = mod(tile_id, lane_interval);

    // Vertical lane lines
    if (abs(lane_coord.x) < 0.5) {
        float lx = abs(tile_frac.x);
        float lane_half = lane_width / tile_size * 0.5;
        float lane_mask = smoothstep(lane_half + 0.008, lane_half, lx);
        lane_mask = max(lane_mask, smoothstep(lane_half + 0.008, lane_half, abs(tile_frac.x - 1.0)));

        // Dashing
        float dash = step(fract(tile_coord.y * 0.5), dash_ratio);
        lane_mask *= dash;

        base_color = mix(base_color, lane_color, lane_mask * lane_color.a);
    }

    // Horizontal lane lines
    if (abs(lane_coord.y) < 0.5) {
        float ly = abs(tile_frac.y);
        float lane_half = lane_width / tile_size * 0.5;
        float lane_mask = smoothstep(lane_half + 0.008, lane_half, ly);
        lane_mask = max(lane_mask, smoothstep(lane_half + 0.008, lane_half, abs(tile_frac.y - 1.0)));

        float dash = step(fract(tile_coord.x * 0.5), dash_ratio);
        lane_mask *= dash;

        base_color = mix(base_color, lane_color, lane_mask * lane_color.a);
    }

    // ── Subtle wear/stain patches ──
    float stain = fbm(pos * 0.003 + 100.0);
    stain = smoothstep(0.45, 0.55, stain);
    base_color.rgb = mix(base_color.rgb, base_color.rgb * 0.88, stain * 0.15);

    COLOR = base_color;
}
