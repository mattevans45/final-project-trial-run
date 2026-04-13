shader_type canvas_item;

uniform float time_sec : hint_range(0.0, 1000.0) = 0.0;

vec3 hue2rgb(float h) {
    vec3 c = abs(fract(h + vec3(0.0, 0.667, 0.333)) * 6.0 - 3.0) - 1.0;
    return clamp(c, 0.0, 1.0);
}

void fragment() {
    vec2  uv   = UV - 0.5;
    float dist = length(uv) * 2.0;

    // Soft disc that fades from full at centre to transparent at edge.
    // Use standard edge0 < edge1 form — inverted smoothstep is undefined in GLSL.
    float alpha = (1.0 - smoothstep(0.35, 1.0, dist)) * TEXTURE.a;

    // Hue sweeps outward from centre and drifts over time — unmistakable as oil.
    float hue = fract(time_sec * 0.22 + dist * 0.70 + uv.x * 0.25);
    vec3  col = hue2rgb(hue) * 0.85 + 0.15;

    COLOR = vec4(col, alpha * 0.55);
}
