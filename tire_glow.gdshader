shader_type canvas_item;

uniform vec4 heat_color : source_color = vec4(1.0, 0.3, 0.05, 1.0);
uniform float intensity = 0.0;

void fragment() {
    vec4 tex = texture(TEXTURE, UV);

    float glow = intensity * tex.a;

    vec3 color = tex.rgb + heat_color.rgb * glow * 2.5;

    COLOR = vec4(color, tex.a);
}