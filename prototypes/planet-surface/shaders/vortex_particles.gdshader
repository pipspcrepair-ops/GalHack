// Particles swept round a tornado: each one orbits the axis, climbing, and is re-born at the
// bottom when it reaches the top. Used for the debris swarm and the dust skirt at the base.
// Positions are local to the emitter, which rides on the tornado's base.
shader_type particles;
render_mode disable_velocity;

uniform float radius_min = 4.0;
uniform float radius_max = 22.0;
uniform float height_max = 40.0;
uniform float spin = 1.4;          // rad/s at radius_min
uniform float rise = 0.12;         // fraction of the height climbed per second
uniform float size_min = 0.3;
uniform float size_max = 0.9;
uniform float flare = 1.0;         // how much the orbit widens with height
uniform vec4 tint : source_color = vec4(1.0);
uniform float strength = 1.0;

float hash(uint x) {
	x = (x ^ 61u) ^ (x >> 16u);
	x *= 9u;
	x = x ^ (x >> 4u);
	x *= 0x27d4eb2du;
	x = x ^ (x >> 15u);
	return float(x & 0xFFFFFFu) / 16777215.0;
}

void start() {
	CUSTOM.x = hash(INDEX * 3u + 1u);      // starting height fraction
	CUSTOM.y = hash(INDEX * 3u + 2u) * 6.2831; // starting angle
	CUSTOM.z = hash(INDEX * 3u + 3u);      // radius and size jitter
	CUSTOM.w = 0.0;
}

void process() {
	float t = TIME;
	float f = fract(CUSTOM.x + t * rise * (0.7 + 0.6 * CUSTOM.z));
	float r = mix(radius_min, radius_max, CUSTOM.z) * (1.0 + f * flare);
	float angle = CUSTOM.y + t * spin * radius_min / r * (0.8 + 0.4 * CUSTOM.z);
	vec3 p = vec3(cos(angle) * r, f * height_max, sin(angle) * r);
	float s = mix(size_min, size_max, fract(CUSTOM.z * 7.31)) * strength;
	float tumble = t * (2.0 + CUSTOM.z * 5.0);
	mat3 rot = mat3(vec3(cos(tumble), sin(tumble), 0.0), vec3(-sin(tumble), cos(tumble), 0.0), vec3(0.0, 0.0, 1.0));
	rot = rot * mat3(vec3(1.0, 0.0, 0.0), vec3(0.0, cos(tumble * 0.7), sin(tumble * 0.7)), vec3(0.0, -sin(tumble * 0.7), cos(tumble * 0.7)));
	TRANSFORM = mat4(vec4(rot[0] * s, 0.0), vec4(rot[1] * s, 0.0), vec4(rot[2] * s, 0.0), vec4(p, 1.0));
	float fade = smoothstep(0.0, 0.08, f) * (1.0 - smoothstep(0.85, 1.0, f));
	COLOR = vec4(tint.rgb, tint.a * fade * strength);
}
