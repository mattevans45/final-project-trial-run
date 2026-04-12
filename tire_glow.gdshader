shader_type canvas_item;
render_mode blend_add;

uniform vec4 heat_color : source_color = vec4(.4, 0.35, 0.05, 1.0);
uniform float intensity = 0.0;

void fragment() {
    float alpha = texture(TEXTURE, UV).a * intensity;
    COLOR = vec4(heat_color.rgb * alpha, alpha);
}
