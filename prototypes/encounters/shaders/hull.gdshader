shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// Block-built hulls in flat paperback-cover colours. One mesh and one draw call per ship: the
// vertex colour is the paint (already linear) and UV2.x how strongly a block glows. Panel
// lines every few metres give a sense of scale; wrecks scorch and smoulder; Bloom growths pulse.

instance uniform float damage = 0.0;
instance uniform float corruption = 0.0;

varying vec3 local_pos;
varying vec3 local_normal;
varying float glow;

void vertex() {
	local_pos = VERTEX;
	local_normal = NORMAL;
	glow = UV2.x;
}

float hash13(vec3 p) {
	p = fract(p * 0.1031);
	p += dot(p, p.zyx + 31.32);
	return fract((p.x + p.y) * p.z);
}

float noise3(vec3 x) {
	vec3 i = floor(x);
	vec3 f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash13(i), hash13(i + vec3(1.0, 0.0, 0.0)), f.x),
	               mix(hash13(i + vec3(0.0, 1.0, 0.0)), hash13(i + vec3(1.0, 1.0, 0.0)), f.x), f.y),
	           mix(mix(hash13(i + vec3(0.0, 0.0, 1.0)), hash13(i + vec3(1.0, 0.0, 1.0)), f.x),
	               mix(hash13(i + vec3(0.0, 1.0, 1.0)), hash13(i + vec3(1.0, 1.0, 1.0)), f.x), f.y), f.z);
}

void fragment() {
	vec3 paint = COLOR.rgb;

	// Panel lines across each face every 4 m, faded out with distance so they never shimmer.
	vec3 cell = local_pos / 4.0;
	vec3 to_line = min(fract(cell), 1.0 - fract(cell)) * 4.0 + abs(local_normal) * 100.0;
	float edge = min(to_line.x, min(to_line.y, to_line.z));
	float w = fwidth(edge);
	float line = (1.0 - smoothstep(0.05, 0.05 + w * 1.5, edge)) * (1.0 - smoothstep(0.1, 0.5, w));
	paint *= 1.0 - 0.25 * line * (1.0 - glow);

	// Wrecks: greyed, scorched in patches, embers glowing in the burns.
	float n = noise3(local_pos * 0.22) * 0.6 + noise3(local_pos * 0.85) * 0.4;
	float scorch = smoothstep(0.42, 0.68, n) * damage;
	float grey = dot(paint, vec3(0.3, 0.59, 0.11));
	paint = mix(paint, vec3(grey) * 0.5, damage * 0.6);
	paint = mix(paint, vec3(0.03, 0.026, 0.025), scorch);

	ALBEDO = paint;
	ROUGHNESS = mix(0.6, 0.92, damage);
	METALLIC = 0.12;
	SPECULAR = 0.45;

	vec3 emission = COLOR.rgb * glow * 4.0 * (1.0 - damage);
	emission += vec3(1.0, 0.3, 0.05) * smoothstep(0.72, 0.78, n) * damage * 2.5;
	float pulse = 0.55 + 0.45 * sin(TIME * 1.7 + local_pos.z * 0.09 + local_pos.x * 0.05);
	emission *= mix(1.0, pulse * 1.7, corruption);
	EMISSION = emission;
}
