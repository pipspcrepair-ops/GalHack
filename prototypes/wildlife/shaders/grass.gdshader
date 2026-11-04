// Grass tufts, instanced. Sways in a travelling wind; colour runs from base to tip and varies
// patch to patch; the tips can glow (alien and Bloom worlds) so a night meadow shimmers.
shader_type spatial;
render_mode cull_disabled, diffuse_lambert, specular_disabled;

uniform vec3 base_color : source_color = vec3(0.25, 0.4, 0.15);
uniform vec3 tip_color : source_color = vec3(0.8, 0.75, 0.35);
uniform vec3 glow_color : source_color = vec3(1.0, 0.3, 0.7);
uniform float glow = 0.0;
uniform float sway = 0.18;

varying float patch;

void vertex() {
	vec3 origin = MODEL_MATRIX[3].xyz;
	float h = UV.y;
	float wind = sin(TIME * 1.6 + origin.x * 0.3 + origin.z * 0.22) + 0.5 * sin(TIME * 2.7 + origin.x * 0.9 - origin.z * 0.4);
	VERTEX.x += wind * sway * h * h;
	VERTEX.z += wind * sway * 0.5 * h * h;
	NORMAL = vec3(0.0, 1.0, 0.0);
	patch = fract(sin(dot(floor(origin.xz * 0.12), vec2(12.9898, 78.233))) * 43758.5453);
}

void fragment() {
	vec3 tip = mix(tip_color, tip_color * 0.75, patch);
	ALBEDO = mix(base_color, tip, UV.y);
	EMISSION = glow_color * glow * UV.y * UV.y * UV.y * (0.6 + 0.4 * patch);
}
