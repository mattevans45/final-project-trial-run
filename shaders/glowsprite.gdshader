shader_type canvas_item;

uniform vec4 glow_color : source_color = vec4(0.0, 1.0, 1.0, 1.0);
uniform float intensity = .5;
uniform float speed = 0.0;

void fragment() {
    vec4 tex = texture(TEXTURE, UV);

    float speedBoost = clamp(speed / 600.0, 0.0, 1.0);

    vec3 emission = glow_color.rgb * (intensity + speedBoost * 2.0);

    COLOR = vec4(emission, tex.a * 0.2);
}