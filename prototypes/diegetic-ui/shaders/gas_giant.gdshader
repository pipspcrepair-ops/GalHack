// The ringed giant outside the window: warm ochre bands, gently wavy, lit by the sun.
shader_type spatial;
render_mode cull_back, specular_disabled;

uniform vec3 light_band : source_color = vec3(0.93, 0.72, 0.45);
uniform vec3 dark_band : source_color = vec3(0.62, 0.3, 0.16);
uniform vec3 storm : source_color = vec3(0.95, 0.45, 0.2);

varying vec3 local_pos;

void vertex() {
	local_pos = normalize(VERTEX);
}

void fragment() {
	float lat = local_pos.y;
	float wave = sin(local_pos.x * 7.0 + lat * 11.0) * 0.03 + sin(local_pos.z * 13.0) * 0.012;
	float bands = sin((lat + wave) * 26.0) * 0.5 + 0.5;
	vec3 col = mix(dark_band, light_band, smoothstep(0.2, 0.8, bands));
	col = mix(col, light_band * 1.08, smoothstep(0.75, 0.95, abs(lat))); // pale poles
	float spot = smoothstep(0.16, 0.0, length(vec2(atan(local_pos.z, local_pos.x) - 0.9, (lat + 0.32) * 2.2)));
	col = mix(col, storm, spot);
	ALBEDO = col;
	ROUGHNESS = 1.0;
}
