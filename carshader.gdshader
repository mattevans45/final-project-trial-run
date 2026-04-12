shader_type canvas_item;

uniform vec4 base_tint : source_color = vec4(0.2, 0.8, 1.0, 1.0);
uniform vec4 accent_tint : source_color = vec4(1.0, 0.1, 0.8, 1.0);

uniform float glow_strength = 2.0;
uniform float edge_width = 1;

uniform float speed = 0.0;
uniform float drift = 0.0;

void fragment() {
    vec4 tex = texture(TEXTURE, UV);

    // --- EDGE DETECTION (based on alpha differences) ---
    float px = TEXTURE_PIXEL_SIZE.x * edge_width;
    float py = TEXTURE_PIXEL_SIZE.y * edge_width;

    float a = tex.a;
    float a_left  = texture(TEXTURE, UV + vec2(-px, 0.0)).a;
    float a_right = texture(TEXTURE, UV + vec2(px, 0.0)).a;
    float a_up    = texture(TEXTURE, UV + vec2(0.0, -py)).a;
    float a_down  = texture(TEXTURE, UV + vec2(0.0, py)).a;

    float edge = abs(a - a_left)
               + abs(a - a_right)
               + abs(a - a_up)
               + abs(a - a_down);

    edge = clamp(edge * 5.0, 0.0, 1.0);

    // --- COLOR BASE ---
    float speedFactor = clamp(speed / 600.0, 0.0, 1.0);
    vec3 baseColor = mix(base_tint.rgb, accent_tint.rgb, UV.y);

    // Speed-based color shift
    vec3 color = mix(baseColor, accent_tint.rgb, speedFactor * 0.6);

    // --- DRIFT PULSE ---
    float driftPulse = sin(TIME * 12.0) * drift * 0.5;

    // --- EDGE GLOW ONLY ---
    float emission = edge * (1.0 + speedFactor * 1.5 + driftPulse);

    vec3 finalColor = tex.rgb * color;

    // Apply glow ONLY to edges
    //finalColor += color * emission * glow_strength;


// subtle inner glow
	finalColor += color * speedFactor * 0.3;

    COLOR = vec4(finalColor, tex.a);
}