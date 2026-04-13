shader_type canvas_item;

uniform vec4 base_tint   : source_color = vec4(0.2, 0.8, 1.0, 1.0);
uniform vec4 accent_tint : source_color = vec4(1.0, 0.1, 0.8, 1.0);

uniform float glow_strength = 1.0;
uniform float speed = 0.0;
uniform float drift = 0.0;

void fragment() {
    vec4 tex = texture(TEXTURE, UV);

    // speedFactor correctly spans 0–1 over the car's actual MaxSpeed (460 px/s).
    // The previous range was /1000 which capped at 0.46 — the tint shift
    // never reached full intensity.
    float speedFactor = clamp(speed / 460.0, 0.0, 1.0);

    // Base color blends base_tint → accent_tint top-to-bottom
    vec3 baseColor = mix(base_tint.rgb, accent_tint.rgb, UV.y);

    // Speed-based color shift toward accent
    vec3 color = mix(baseColor, accent_tint.rgb, speedFactor * 0.6);

    // Drift pulse brightens the car when sliding
    float driftPulse = sin(TIME * 12.0) * drift * 0.5;

    // Subtle inner glow that grows with speed
    vec3 finalColor = tex.rgb * color;
    finalColor += color * (speedFactor * 0.3 + driftPulse * 0.1);

    COLOR = vec4(finalColor, tex.a);
}
