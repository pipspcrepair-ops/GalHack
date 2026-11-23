// A body of something the light passes through (a dust storm, later a fog bank or a close cloud), drawn
// on any renderer by stepping through moving 3D noise inside it. The mesh is only a box round it, drawn
// from inside (its back faces), so it is the same whether the eye is out of it or in it. The shape is a
// rounded footprint with a top that can stand taller on its leading side (a wall), its outline pushed
// in and out by the noise so it heaps and rolls. It is depth-aware: it fills only the space in front of
// the first surface seen through a pixel, so what stands in front of it stays in front and what is in
// it fades by how deep in it is. What the environment fog already draws at the eye is taken off its
// density, so from inside, where the fog has thickened to its murk, it only adds its billows. It is lit
// by the same lights and sky as everything else (the sun, the night light, the sky's own ambient), so
// it never stands darker or brighter than the land at that hour: the march only works out how much of
// it the sky and the light reach.
shader_type spatial;
render_mode cull_front, depth_draw_never, depth_test_disabled, shadows_disabled, specular_disabled;

#include "res://shaders/noise.gdshaderinc"

uniform vec2 centre = vec2(0.0);        // the middle of its footprint (x, z)
uniform vec2 axis = vec2(1.0, 0.0);     // the way its leading side faces (x, z), unit
uniform vec2 radii = vec2(1000.0);      // half its footprint along the axis and across it, metres
uniform float base_y = 0.0;             // its floor (under the ground is fine: the ground hides it)
uniform float height = 500.0;           // its top over the floor, at its tallest
uniform float front_rise = 0.0;         // 0..1: how much lower its trailing side stands than its leading one
uniform float edge = 300.0;             // how far in from its outline it thickens to all of it, metres
uniform float density = 0.05;           // extinction per metre deep inside
uniform float top_fade = 1.0;           // the share of that density left at its top
uniform float billow = 200.0;           // how far the noise pushes its outline in and out, metres
uniform float grain = 400.0;            // the size of its heaps, metres
uniform vec3 flow = vec3(0.0);          // how fast its heaps drift, m/s
uniform float rise = 0.0;               // how fast they climb, m/s
uniform float strength = 1.0;
uniform float fog_floor = 0.0;          // the environment fog's density at the eye (already drawn)
uniform vec3 fog_color : source_color = vec3(0.7);
uniform vec3 albedo : source_color = vec3(0.6, 0.45, 0.3);
uniform vec3 to_sun = vec3(0.0, 1.0, 0.0); // the way to the light that casts its shade (the sun, or the night light)
uniform int steps = 32;
uniform sampler3D heaps : filter_linear, repeat_enable;
uniform sampler2D scene_depth : hint_depth_texture, repeat_disable, filter_nearest;

varying vec3 wpos;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

vec2 perp() { return vec2(-axis.y, axis.x); }

// A point in its own frame: x along its axis, y over its floor, z across.
vec3 local(vec3 p) {
	vec2 d = p.xz - centre;
	return vec3(dot(d, axis), p.y - base_y, dot(d, perp()));
}

// How far inside it a point is, metres (negative outside), before the heaps: in from its outline on
// the ground (along the line from its middle), or down from its top, whichever is nearer. The outline
// is a squared-off oval (a superellipse, as DustStorm.Inside has it), so its leading side runs long and
// straight and only its ends round off; its top is nearly flat, a little lower toward its back.
float inside(vec3 l) {
	vec2 e = vec2(l.x / radii.x, l.z / radii.y);
	e *= e;
	float r = sqrt(sqrt(dot(e, e)));
	float flat_in = r > 1e-4 ? length(l.xz) * (1.0 - r) / r : min(radii.x, radii.y);
	float lead = clamp(l.x / radii.x, -1.0, 1.0);
	float top = height * (1.0 - front_rise * 0.5 * (1.0 - lead)) * (0.82 + 0.18 * smoothstep(0.0, 3.0 * edge, flat_in));
	// Over its top the heaps may heap no higher than a little: no wisp comes loose and floats free above it.
	return min(flat_in, top - l.y) - 2.0 * max(l.y - top, 0.0);
}

// Its heaps at a point, 0 to 1: great round lobes, a few smaller ones on them, and close to the eye
// (`near`, 0 to 1) the wisps that stream past. All drift and climb with it.
float heap(vec3 p, float near) {
	vec3 q = p - flow * TIME - vec3(0.0, rise * TIME, 0.0);
	float h = texture(heaps, q / grain).r * 0.8 + texture(heaps, (q + vec3(31.0, 17.0, 53.0)) / (grain * 0.45)).r * 0.2;
	if (near > 0.0) h += near * 0.9 * (texture(heaps, (q - flow * TIME * 0.6 + vec3(7.0, 3.0, 11.0)) / (grain * 0.12)).r - 0.5);
	return h;
}

// How far the first thing seen through a point of the screen is, metres (sky: very far).
float scene_distance(vec2 uv, mat4 inv_projection) {
	float raw = textureLod(scene_depth, uv, 0.0).x;
	if (raw <= 1e-7) return 1e7;
	vec4 v = inv_projection * vec4(uv * 2.0 - 1.0, raw, 1.0);
	return length(v.xyz / v.w);
}

void fragment() {
	vec3 cam = (INV_VIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	vec3 dir = normalize(wpos - cam);
	// Clip the ray to its box, in its own frame.
	vec3 o = local(cam), d = vec3(dot(dir.xz, axis), dir.y, dot(dir.xz, perp()));
	vec3 lo = vec3(-radii.x - billow, 0.0, -radii.y - billow), hi = vec3(radii.x + billow, height + billow, radii.y + billow);
	vec3 inv = 1.0 / mix(d, vec3(1e-6), vec3(lessThan(abs(d), vec3(1e-6))));
	vec3 ta = (lo - o) * inv, tb = (hi - o) * inv;
	vec3 tn = min(ta, tb), tf = max(ta, tb);
	float t0 = max(max(tn.x, tn.y), max(tn.z, 0.0)), t1 = min(min(tf.x, tf.y), tf.z);
	// What stands in front of it: the picture it is laid over is already smoothed, so a pixel at a silhouette holds
	// part near thing, part the bare sky behind it, with one depth for both. So it is drawn to the farthest depth of the
	// pixel and its eight neighbours, over the share of them that lies behind where it starts.
	vec2 px = 1.0 / VIEWPORT_SIZE;
	float scene_t = 0.0, behind = 0.0;
	for (int k = 0; k < 9; k++) {
		vec2 uv = SCREEN_UV + px * vec2(float(k % 3) - 1.0, float(k / 3) - 1.0);
		float st = scene_distance(uv, INV_PROJECTION_MATRIX);
		scene_t = max(scene_t, st);
		behind += st > t0 ? 1.0 / 9.0 : 0.0;
	}
	t1 = min(t1, scene_t);
	if (t1 <= t0 || behind <= 0.0) discard;

	// Steps crowd near the eye, where its heaps are seen largest, and spread out beyond.
	// Jitter enough to break the steps' slices into a fine grain rather than streaks, not so much it speckles.
	float jitter = 0.5 + 0.7 * (fract(52.9829189 * fract(dot(FRAGCOORD.xy, vec2(0.06711056, 0.00583715)))) - 0.5);
	float span = t1 - t0, trans = 1.0, depth_sum = 0.0;
	float sky = 0.0, lit = 0.0; // how much of it the sky's light reaches, and the light's, along the ray
	float n = float(steps);
	for (int i = 0; i < steps; i++) {
		float a = float(i) / n, b = float(i + 1) / n, m = (float(i) + jitter) / n;
		float t = t0 + span * m * m, dt = span * (b * b - a * a);
		vec3 p = cam + dir * t;
		p.y += round_sink(p.xz - cam.xz); // it sinks past the near land as the ground does
		vec3 l = local(p);
		float h = heap(p, 1.0 - smoothstep(40.0, 300.0, t));
		float s = inside(l) + billow * (h * 2.0 - 1.0); // its outline pushed in and out by its heaps
		if (s <= 0.0) continue;
		float up = clamp(l.y / height, 0.0, 1.0);
		// Inside it the murk is uneven too: thick in its heaps, thinner between them, as they stream past.
		float rho = density * smoothstep(0.0, edge, s) * mix(1.0, top_fade, up) * (0.4 + 1.2 * h) * strength;
		float sigma = max(rho - fog_floor, 0.0);
		if (sigma <= 0.0) continue;
		// Lit by the sun as far as little of it lies between: its own heaps shade their undersides and
		// the far side of each, its bulk the side away from the sun. The sky lights it from above, so
		// it darkens down to its foot and deeper in.
		vec3 ps = p + to_sun * (0.3 * grain);
		float near_shade = smoothstep(-0.1 * grain, 0.2 * grain, inside(local(ps)) + billow * (heap(ps, 0.0) * 2.0 - 1.0));
		float far_shade = smoothstep(-edge, 2.0 * edge, inside(local(p + to_sun * (2.5 * edge))));
		float sunlit = (1.0 - 0.75 * near_shade) * (1.0 - 0.7 * far_shade);
		float deep = smoothstep(0.0, 2.0 * edge, s);
		float alpha = 1.0 - exp(-sigma * dt);
		sky += trans * alpha * (0.5 + 0.5 * up) * (1.0 - 0.4 * deep);
		lit += trans * alpha * sunlit * (0.55 + 0.45 * up);
		depth_sum += trans * alpha * t;
		trans *= 1.0 - alpha;
		if (trans < 0.01) break;
	}
	float cover = 1.0 - trans;
	if (cover < 0.002) discard;
	// Lit as a surface facing the sky would be: the engine adds the sky's ambient by ALBEDO (as much of
	// it as the sky reaches) and light() the sun's and night light's, scaled by ROUGHNESS (how much of
	// it the light reaches, against the sky's, a quarter of it).
	sky = max(sky / cover, 1e-3);
	ALBEDO = albedo * sky;
	ROUGHNESS = clamp(lit / cover / sky * 0.25, 0.0, 1.0);
	METALLIC = 0.0;
	SPECULAR = 0.0;
	NORMAL = (VIEW_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz;
	// The environment's fog lies between the eye and it as far as most of it is, not as far as its box.
	FOG = vec4(fog_color, 1.0 - exp(-fog_floor * depth_sum / cover));
	// Thin wisps let the sky through rather than darken it; at a silhouette it covers the share of the pixel's
	// neighbourhood that lies behind where it starts, so a wire half over the sky keeps half of itself.
	ALPHA = cover * smoothstep(0.0, 0.3, cover) * behind;
}

// The light on it: brighter looking toward the light, through its thin rim (fine dust scatters forward).
void light() {
	float toward = max(dot(LIGHT, -VIEW), 0.0);
	DIFFUSE_LIGHT += LIGHT_COLOR / PI * ROUGHNESS * 4.0 * 0.6 * (0.75 + 0.6 * toward * toward * toward);
}
