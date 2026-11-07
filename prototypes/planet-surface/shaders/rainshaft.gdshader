// A storm cell's rain shaft: a soft grey veil under its cloud, marched through in the fragment so it
// has no mesh shape at all. The column is wider and fainter near the ground, torn into uneven
// curtains with gaps, leans with the wind, drifts slowly, and fades up into the cloud base and down
// into the ground haze. The mesh is only a loose closed bound that the ray is clipped against: only its
// far side is drawn, so each view ray marches the veil once, and the depth test is the shader's own
// (a hill standing in the far half of the bound must not hide the veil in front of it).
shader_type spatial;
render_mode unshaded, cull_front, depth_draw_never, depth_test_disabled, shadows_disabled, fog_disabled;

uniform vec3 tint : source_color = vec3(0.32, 0.34, 0.38);
uniform float amount = 0.0;
uniform vec3 axis_base = vec3(0.0); // ground centre of the shaft
uniform float shaft_height = 1000.0;
uniform float reach = 300.0;        // mean radius at the cloud
uniform float bound = 600.0;        // radius of the clipping cylinder
uniform vec2 lean = vec2(0.0);      // sideways offset of the top, metres
uniform vec2 drift = vec2(0.0);     // metres per second the pattern slides
uniform float seed = 0.0;
uniform float skirt_amount = 0.0;
uniform vec3 to_sun = vec3(0.0, 1.0, 0.0);
uniform vec3 sun_glow : source_color = vec3(1.0, 0.9, 0.7);
uniform float sun_amount = 1.0;
// The sky's own cloud base (mirrors storm_at and base_sag in sky.gdshader: keep them in step).
uniform sampler2D cloud_noise : filter_linear_mipmap, repeat_enable;
uniform float cloud_base = 1600.0;
uniform vec2 cloud_scale = vec2(8000.0);
uniform vec2 cloud_drift = vec2(0.0);
uniform vec4 towers[4];
uniform float tower_wall[4];
uniform int tower_n = 0;
uniform sampler2D scene_depth : hint_depth_texture, repeat_disable, filter_nearest;
uniform float soft_metres = 12.0;   // how far in front of a surface the veil thins out
// The air between: the scene's own haze, but a quarter as thick (the engine's fog, laid over, turned a
// distant curtain the very colour of the horizon behind it, so it vanished at a few kilometres).
uniform vec3 fog_color : source_color = vec3(0.6);
uniform float fog_density = 0.0;

varying vec3 wpos;

float hash(vec3 p) {
	p = fract(p * 0.3183099 + vec3(0.1, 0.2, 0.3) + seed);
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float vnoise(vec3 x) {
	vec3 i = floor(x);
	vec3 f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash(i), hash(i + vec3(1, 0, 0)), f.x), mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), f.x), f.y),
		mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), f.x), mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

float lump(vec2 xz, float y, float k) {
	vec2 uv = (xz + cloud_drift * 0.35) / cloud_scale * k + vec2(y, -y) * 0.0009;
	return texture(cloud_noise, uv).r * 0.6 + texture(cloud_noise, uv * 2.3 + 0.2).r * 0.4;
}

// Where the sky's cloud underside is above this spot, metres: its flat base less the sag the sky gives
// a storm (storm_at at the base, where there is no anvil yet, and base_sag in sky.gdshader).
float cloud_underside(vec2 xz) {
	float lp = lump(xz, cloud_base, 3.5);
	float s = 0.0;
	for (int i = 0; i < 4; i++) {
		if (i >= tower_n) break;
		float w = towers[i].w;
		float r = towers[i].z * (1.2 + 3.0 * w) * (0.5 + 1.0 * lp);
		s = max(s, w * smoothstep(r, r * 0.2, length(xz - towers[i].xy)));
	}
	float sag = s * (40.0 + 60.0 * lp) + s * s * 110.0;
	for (int i = 0; i < 4; i++) {
		if (i >= tower_n) break;
		float rw = towers[i].z * (0.45 + 0.5 * lp);
		sag += tower_wall[i] * 130.0 * smoothstep(rw, rw * 0.15, length(xz - towers[i].xy)) * (0.6 + 0.8 * lp);
	}
	return cloud_base - sag;
}

float density(vec3 p, out float g) {
	vec2 c0 = axis_base.xz;
	float under = cloud_underside(p.xz);
	float topY = under + 60.0;                         // up inside the cloud, so there is no gap under it
	float span = max(topY - axis_base.y, 1.0);
	g = clamp((topY - p.y) / span, 0.0, 1.0);          // 0 at the cloud, 1 at the ground
	if (p.y > topY || p.y < axis_base.y) return 0.0;
	float up = p.y - axis_base.y;
	vec2 wdir = normalize(lean + vec2(0.0001, 0.0));
	vec2 slant = wdir * (0.12 + 0.2 * length(lean) / max(shaft_height, 1.0)) * 3.0;
	vec2 c = c0 + lean * (1.0 - g);
	vec2 q = p.xz - c + drift * TIME - slant * up * 0.25;
	vec2 d = p.xz - c;
	float width = reach * (1.15 + 0.35 * g);           // full under the whole base of the storm, spreading a little as it falls
	// Warp the ground position so no outline is straight: lobes, bays and ragged sides.
	vec2 warp = vec2(vnoise(vec3(q * (3.0 / reach), up * 0.0015 + 11.0)), vnoise(vec3(q * (3.0 / reach) + 7.0, up * 0.0015 + 13.0))) - 0.5;
	vec2 dw = d + warp * reach * (0.9 + 0.5 * g);
	float r = length(dw) / width;
	float field = vnoise(vec3(q * (1.6 / reach), up * 0.0012)) * 0.45
		+ vnoise(vec3(q * (4.5 / reach) + 5.0, up * 0.0025 + 3.0)) * 0.3
		+ vnoise(vec3(q * (12.0 / reach) + 9.0, up * 0.004 + 7.0)) * 0.25;
	float env = 1.0 - smoothstep(0.1, 1.3, r);
	float outer = 1.0 - smoothstep(0.5, 0.88, length(d) / bound); // nothing reaches the clip, so it never shows an edge
	float cover = env * 1.25 + (field - 0.5) * 1.15 - 0.12;
	float body = smoothstep(0.2 + 0.18 * g, 0.75, cover) * outer;
	// Fingers: each column of rain runs down to its own depth and trails off in a wisp; some reach the ground.
	float fin = vnoise(vec3(q * (7.0 / reach) + 21.0, 4.0));
	float end = 0.5 + 0.7 * fin;
	body *= 1.0 - smoothstep(end - 0.35, end, g);
	// Columns of rain of uneven width and weight, wavering as they fall; the gaps between are near clear.
	float col = vnoise(vec3((q + warp * reach * 0.5) * (5.5 / reach) + 31.0, 0.5));
	body *= mix(0.12, 1.0, smoothstep(0.3, 0.62, col)) * (0.7 + 0.6 * vnoise(vec3(q * (14.0 / reach), up * 0.003)));
	// Under it all a soft pale veil of fine rain, full under the cloud and thinning toward the ground.
	float haze = smoothstep(0.05, 0.75, env + (field - 0.5) * 0.6) * outer * (1.0 - 0.55 * g) * (0.6 + 0.8 * field);
	vec2 sq = q + slant * up;
	float s1 = vnoise(vec3(sq * (38.0 / reach), up * 0.0016 + TIME * 0.08));
	float s2 = vnoise(vec3(sq * (95.0 / reach) + 4.0, up * 0.0022 - TIME * 0.12 + 2.0));
	float streak = 0.45 + 1.0 * (s1 * 0.6 + s2 * 0.4 - 0.5);
	float vert = mix(1.0, 0.3, smoothstep(0.0, 0.9, g));
	float ragged = (vnoise(vec3(q * (6.0 / reach) + 17.0, 1.5)) - 0.5) * 110.0;
	float into_cloud = 1.0 - smoothstep(under - 30.0 + ragged, under + 50.0 + ragged, p.y);
	// The foot of heavy rain: a pale skirt of spray spreading out along the ground.
	float skirt = skirt_amount * smoothstep(0.9, 1.0, g) * (1.0 - smoothstep(0.4, 1.5, length(d + warp * reach * 0.6) / width)) * (0.5 + field);
	return (body * clamp(streak, 0.1, 1.3) * vert + haze * 0.8) * into_cloud + skirt * 1.2;
}

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec3 cam = (INV_VIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	vec3 dir = normalize(wpos - cam);
	// Clip the ray against the vertical slab and the bounding cylinder.
	float t0 = 0.0, t1 = 1e5;
	if (abs(dir.y) > 1e-5) {
		float a = (axis_base.y - cam.y) / dir.y;
		float b = (axis_base.y + shaft_height - cam.y) / dir.y;
		t0 = max(t0, min(a, b));
		t1 = min(t1, max(a, b));
	} else if (cam.y < axis_base.y || cam.y > axis_base.y + shaft_height) {
		discard;
	}
	vec2 o = cam.xz - (axis_base.xz + lean * 0.5);
	vec2 dd = dir.xz;
	float A = dot(dd, dd), B = dot(o, dd), C = dot(o, o) - bound * bound;
	if (A > 1e-9) {
		float disc = B * B - A * C;
		if (disc <= 0.0) discard;
		float s = sqrt(disc);
		t0 = max(t0, (-B - s) / A);
		t1 = min(t1, (-B + s) / A);
	} else if (C > 0.0) {
		discard;
	}
	// The veil only fills the space in front of the first surface seen through this pixel.
	float scene_t = 1e6;
	float raw = texture(scene_depth, SCREEN_UV).x;
	if (raw > 1e-7) {
		vec4 v = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, raw, 1.0);
		scene_t = length(v.xyz / v.w);
	}
	t1 = min(t1, scene_t);
	if (t1 <= t0) discard;
	t1 = min(t1, t0 + 4000.0);
	const int N = 24;
	float dt = (t1 - t0) / float(N);
	float jitter = fract(52.9829189 * fract(dot(FRAGCOORD.xy, vec2(0.06711056, 0.00583715))));
	float sum = 0.0, sum_g = 0.0, sum_t = 0.0;
	for (int i = 0; i < N; i++) {
		float t = t0 + (float(i) + jitter) * dt;
		float g;
		float d = density(cam + dir * t, g) * dt * smoothstep(0.0, soft_metres, scene_t - t);
		sum += d;
		sum_g += d * g;
		sum_t += d * t;
	}
	float optical = sum / (reach * 1.6);
	float fallen = sum_g / max(sum, 1e-5); // how far down, on the whole, the rain this ray sees has fallen
	// Lit by the sun behind it: toward the sun the veil glows (pale gold at a low sun), with the sun at
	// your back it is a plain dark grey one.
	float toward = max(dot(dir, normalize(to_sun)), 0.0);
	// The light comes through where the rain is thin: the dense cores stay dark against it, so it stays streaked.
	float glow = pow(toward, 8.0) * sun_amount * (1.0 - 0.85 * smoothstep(0.04, 0.35, optical));
	// The rain stands in the storm's shadow: a thin veil is the air's light dimmed by the cloud over it, so
	// darker than the open sky behind and paler than the cloud's base; a finger's dense core darker still,
	// and darkest up under the base where it comes from, paler as it falls and spreads.
	vec3 body_col = mix(tint * 0.8, tint * 0.48, clamp(optical * 1.2, 0.0, 1.0)) * mix(0.85, 1.0, smoothstep(0.0, 0.6, fallen));
	ALBEDO = mix(body_col, sun_glow, clamp(glow, 0.0, 0.5));
	float far_t = sum_t / max(sum, 1e-5);
	ALBEDO = mix(ALBEDO, fog_color, (1.0 - exp(-far_t * fog_density * 0.25)) * 0.85);
	ALPHA = clamp((1.0 - exp(-optical * 10.0)) * amount, 0.0, 1.0);
}
