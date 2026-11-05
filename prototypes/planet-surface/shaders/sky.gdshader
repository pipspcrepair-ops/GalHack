// The sky, from the world's own data (see SkySystem): a clear-sky gradient from its air, the
// sun and its glow in the star's colour, sunsets toward a low sun, moons lit by the sun (so they
// show phases), a neighbouring giant with bands and maybe rings, stars and a nebula at night,
// and a layer of clouds you can see the shape of: a slab of cloud raymarched against the sun,
// with flat bases, lit tops and dark undersides, computed at half resolution.
//
// The Mobile renderer keeps the reduced-resolution sky buffers in RGB10A2, so their alpha has
// only four levels: clouds' edges banded into blocks. So the alpha never goes through ALPHA: the
// half-resolution pass stores the clouds already laid over the plain sky gradient (colour only),
// and the quarter-resolution pass marches density alone for how much of what is behind them
// (stars, moons, the giant, the sun) the clouds hide.
shader_type sky;
render_mode use_half_res_pass, use_quarter_res_pass;

global uniform vec4 weather_flash;   // direction to the last strike, and its brightness

uniform vec3 zenith_day : source_color;
uniform vec3 horizon_day : source_color;
uniform vec3 zenith_night : source_color;
uniform vec3 sunset : source_color;
uniform vec3 haze : source_color;
uniform vec3 star_color : source_color;
uniform float sun_radius = 0.01;
uniform float sun_energy = 1.3;
uniform float density = 1.0;
uniform float stars_by_day = 0.0;
uniform mat3 star_turn = mat3(1.0);
uniform float overcast = 0.0;          // weather greying the whole sky
uniform vec3 overcast_color : source_color = vec3(0.5);

uniform bool nebula = false;
uniform vec3 nebula_a : source_color = vec3(0.5, 0.2, 0.6);
uniform vec3 nebula_b : source_color = vec3(0.2, 0.4, 0.7);

uniform int moon_count = 0;
uniform vec3 moon_dir[3];
uniform float moon_radius[3];
uniform vec3 moon_color[3] : source_color;

uniform bool giant = false;
uniform vec3 giant_dir = vec3(0.0, 1.0, 0.0);
uniform float giant_radius = 0.1;
uniform vec3 giant_color : source_color = vec3(0.8, 0.7, 0.5);
uniform vec3 giant_band : source_color = vec3(0.5, 0.4, 0.3);
uniform bool rings = false;
uniform vec3 ring_color : source_color = vec3(0.9, 0.85, 0.7);
uniform vec3 ring_normal = vec3(0.0, 1.0, 0.0);

uniform sampler2D cloud_noise : filter_linear_mipmap, repeat_enable;
uniform float cloud_cover = 0.4;
uniform float cloud_dark = 0.0;
uniform float cloud_base = 1600.0;
uniform float cloud_thick = 900.0;
uniform vec2 cloud_scale = vec2(8000.0, 8000.0);   // world metres per noise tile: divides the planet both ways
uniform vec2 cloud_drift = vec2(0.0);
uniform vec3 cloud_lit : source_color = vec3(1.0);
uniform vec3 cloud_shade : source_color = vec3(0.5, 0.55, 0.62);
uniform vec3 cloud_underglow : source_color = vec3(0.0);   // light from the ground: lava under ash
uniform int debug_view = 0;   // 1: cloud coverage, 2: cloud colour

float hash31(vec3 p) {
	p = fract(p * vec3(443.897, 441.423, 437.195));
	p += dot(p, p.yzx + 19.19);
	return fract((p.x + p.y) * p.z);
}

vec3 sky_color(vec3 dir, vec3 sun) {
	float up = dir.y;
	float sun_h = sun.y;
	float day = smoothstep(-0.2, 0.15, sun_h);
	float bright = 0.25 + 0.75 * smoothstep(-0.05, 0.45, sun_h);

	vec3 zen = mix(zenith_night, zenith_day * bright, day);
	vec3 hor = mix(zenith_night * 1.6 + haze * 0.02, horizon_day * bright, day);
	float a = pow(clamp(up, 0.0, 1.0), 0.42 / density);
	vec3 col = mix(hor, zen, a);

	// Sunset and sunrise: warm light piling up toward a low sun, strongest in thick air.
	vec2 flat_dir = normalize(dir.xz + vec2(1e-5));
	vec2 flat_sun = normalize(sun.xz + vec2(1e-5));
	float toward = dot(flat_dir, flat_sun) * 0.5 + 0.5;
	float low = smoothstep(0.45, 0.0, sun_h) * smoothstep(-0.28, -0.02, sun_h);
	col += sunset * low * pow(toward, 2.5) * exp(-max(up, 0.0) * 5.0 / density) * (0.55 + 0.45 * density);
	col = mix(col, col * vec3(0.9, 0.85, 0.82) + sunset * 0.08, low * 0.5);

	// Aerosol glow round the sun.
	float sd = dot(dir, sun);
	col += star_color * (pow(max(sd, 0.0), 8.0) * 0.18 + pow(max(sd, 0.0), 90.0) * 0.5) * day * density;

	// Below the horizon: the haze, darker downward.
	if (up < 0.0) col = mix(col, haze * bright * day * 0.55 + zenith_night, clamp(-up * 6.0, 0.0, 1.0));
	return col;
}

vec3 night_sky(vec3 dir, vec3 sun) {
	vec3 d = star_turn * dir;
	vec3 col = vec3(0.0);
	if (nebula) {
		vec2 uv = vec2(atan(d.x, d.z) * 0.15915 + 0.5, d.y * 0.5 + 0.5);
		float n = texture(cloud_noise, uv * vec2(2.0, 1.0)).r;
		float m = texture(cloud_noise, uv * vec2(5.0, 2.5) + 0.37).r;
		float band = exp(-pow((d.y - 0.15 + 0.3 * (n - 0.5)) * 2.2, 2.0));
		col += mix(nebula_a, nebula_b, m) * pow(n * m, 1.5) * band * 0.9;
	}
	// Stars: points on a fine grid of directions, a few bright, most faint.
	vec3 p = d * 260.0;
	vec3 cell = floor(p);
	float h = hash31(cell);
	if (h > 0.985) {
		vec3 f = fract(p) - 0.5;
		float b = smoothstep(0.32, 0.0, length(f)) * pow((h - 0.985) / 0.015, 6.0);
		col += vec3(0.85 + 0.15 * hash31(cell + 3.1), 0.9, 1.0) * b * 4.0;
	}
	return col;
}

// Moons and the giant: discs lit from the sun's side, so they show phases; the giant has bands
// and, if the system gives it rings, a ring plane that passes behind it and in front of it.
// Colours are premultiplied by alpha so the layers stack: back ring, disc, front ring.
vec4 over(vec4 front, vec4 back) {
	return vec4(front.rgb + back.rgb * (1.0 - front.a), front.a + back.a * (1.0 - front.a));
}

vec4 lit_disc(vec3 dir, vec3 centre, float radius, vec3 sun, out vec3 normal) {
	float c = dot(dir, centre);
	float r = acos(clamp(c, -1.0, 1.0));
	normal = -centre;
	if (r >= radius) return vec4(0.0);
	vec3 helper = abs(centre.y) > 0.95 ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0);
	vec3 east = normalize(cross(helper, centre));
	vec3 north = cross(centre, east);
	vec2 q = vec2(dot(dir, east), dot(dir, north)) / sin(radius);
	float z = sqrt(max(0.0, 1.0 - dot(q, q)));
	normal = normalize(east * q.x + north * q.y - centre * z);
	float edge = smoothstep(radius, radius * 0.985, r);
	return vec4(vec3(clamp(dot(normal, sun), 0.0, 1.0)), edge);
}

vec4 bodies(vec3 dir, vec3 sun) {
	vec4 result = vec4(0.0);
	if (giant) {
		vec3 axis = normalize(ring_normal);
		vec4 back = vec4(0.0), front = vec4(0.0);
		if (rings) {
			// Where the view ray crosses the ring plane through the giant's centre, in giant radii.
			float denom = dot(dir, axis);
			float t = dot(giant_dir, axis) / (abs(denom) > 1e-4 ? denom : 1e-4);
			if (t > 0.0) {
				float rr = length(dir * t - giant_dir) / sin(giant_radius);
				float ring = smoothstep(1.45, 1.5, rr) * (1.0 - smoothstep(2.35, 2.42, rr));
				ring *= 0.6 + 0.4 * sin(rr * 41.0) * sin(rr * 13.0);
				ring *= 1.0 - (smoothstep(1.93, 1.97, rr) - smoothstep(2.03, 2.07, rr)) * 0.85; // a dark gap
				float lit = 0.6 + 0.4 * clamp(abs(dot(axis, sun)) * 1.5, 0.0, 1.0);
				vec4 rc = vec4(ring_color * lit * ring * 0.85, ring * 0.85);
				if (t < 1.0) front = rc; else back = rc;
			}
		}
		vec3 n;
		vec4 disc = lit_disc(dir, giant_dir, giant_radius, sun, n);
		if (disc.a > 0.0) {
			float lat = dot(n, axis);
			float bands = sin(lat * 19.0 + sin(lat * 7.0) * 1.6) * 0.5 + 0.5;
			vec3 surface = mix(giant_color, giant_band, bands * 0.7);
			disc.rgb = surface * (0.03 + 0.97 * disc.r) * disc.a;
		}
		result = over(front, over(disc, back));
	}
	for (int i = 0; i < moon_count; i++) {
		vec3 n;
		vec4 m = lit_disc(dir, moon_dir[i], moon_radius[i], sun, n);
		if (m.a <= 0.0) continue;
		float craters = 0.82 + 0.18 * texture(cloud_noise, n.xz * 0.7 + float(i) * 0.31).r;
		m.rgb = moon_color[i] * craters * (0.025 + 1.1 * m.r) * m.a;
		result = over(m, result);
	}
	return result;
}

uniform sampler3D cloud_detail : filter_linear_mipmap, repeat_enable;

// Cloud density at a point: a coverage map from 2D noise (where clouds are, and how tall), a
// height profile with flat bases and rounded tops that rise higher where the cloud is denser,
// and 3D noise eroding the edges into billows so nothing reads as an extruded pillar.
float cloud_density(vec3 p, bool cheap) {
	vec2 uv = (p.xz + cloud_drift) / cloud_scale;
	float n = texture(cloud_noise, uv).r * 0.65 + texture(cloud_noise, uv * 2.0 + 0.31).r * 0.35;
	float cover = mix(0.12, 1.05, cloud_cover);
	float coverage = n - (1.0 - cover);
	if (coverage <= 0.0) return 0.0;
	float h = (p.y - cloud_base) / cloud_thick;
	float top = clamp(0.2 + coverage * 2.6, 0.2, 1.0);
	float profile = smoothstep(0.0, 0.07, h) * (1.0 - smoothstep(top * 0.55, top, h));
	float d = coverage * 3.2 * profile;
	if (!cheap) {
		vec3 q = (p + vec3(cloud_drift.x, 0.0, cloud_drift.y) * 1.3) / (cloud_scale.x * 0.125);
		float detail = texture(cloud_detail, q).r * 0.65 + texture(cloud_detail, q * 3.0).r * 0.35;
		d -= detail * (0.55 - 0.25 * h) * (1.0 - cloud_cover * 0.5);
	}
	return clamp(d * 1.8, 0.0, 1.0);
}

// The clouds along a view ray: their light over the given sky colour, and their opacity. With
// lit false only the opacity is marched (no light samples): the cheap pass for occlusion.
vec4 march_clouds(vec3 ro, vec3 rd, vec3 sun, vec3 sky, bool lit) {
	if (cloud_cover < 0.02) return vec4(sky, 0.0);
	float base = cloud_base, top = cloud_base + cloud_thick;
	float t0, t1;
	if (abs(rd.y) < 1e-3) {
		if (ro.y < base || ro.y > top) return vec4(sky, 0.0);
		t0 = 0.0;
		t1 = 6000.0;
	} else {
		float ta = (base - ro.y) / rd.y, tb = (top - ro.y) / rd.y;
		t0 = max(min(ta, tb), 0.0);
		t1 = max(ta, tb);
	}
	if (t1 <= 0.0 || t0 > 40000.0) return vec4(sky, 0.0);
	t1 = min(t1, t0 + 5000.0);
	const int STEPS = 40;
	float dt = (t1 - t0) / float(STEPS);
	float t = t0 + dt * (0.35 + 0.3 * hash31(rd * 731.0));
	float trans = 1.0;
	vec3 light = vec3(0.0);
	float day = smoothstep(-0.15, 0.1, sun.y);
	vec3 sun_tint = mix(vec3(1.0), star_color, 0.4);
	vec3 sun_light = mix(sunset, sun_tint, smoothstep(0.0, 0.3, sun.y)) * day * sun_energy * 1.1;
	vec3 ambient = mix(cloud_shade, sky, 0.35) * (0.12 + 0.88 * day);
	float flash = weather_flash.w;
	for (int i = 0; i < STEPS; i++) {
		vec3 p = ro + rd * t;
		float d = cloud_density(p, false);
		if (d > 0.01) {
			float ext = d * dt * 0.016;
			if (lit) {
				// Light reaching this point from the sun, through two cheaper samples of cloud.
				float toward = cloud_density(p + sun * 80.0, true) + cloud_density(p + sun * 240.0, true) * 0.8;
				float beer = exp(-toward * 1.6);
				float powder = 1.0 - exp(-d * 3.0);
				float hgt = clamp((p.y - cloud_base) / cloud_thick, 0.0, 1.0);
				vec3 lum = ambient * (0.4 + 0.6 * hgt) + cloud_lit * sun_light * beer * mix(0.55, 1.0, powder);
				lum *= 1.0 - cloud_dark * 0.78 * (1.0 - hgt * 0.4);
				lum += cloud_underglow * (1.0 - hgt) * (1.0 - hgt);
				lum += vec3(0.75, 0.72, 1.0) * flash * 2.5 * (1.0 - hgt * 0.3);
				light += trans * lum * (1.0 - exp(-ext));
			}
			trans *= exp(-ext);
			if (trans < 0.03) break;
		}
		t += dt;
	}
	float alpha = 1.0 - trans;
	// Distant clouds thin into the haze along the horizon: they fade in opacity, so whatever is
	// behind them (a moon, a giant) shows through properly; their colour leans toward the sky's.
	float fade = exp(-t0 / (16000.0 * density));
	vec3 col = alpha > 0.001 ? light / alpha : sky;
	col = mix(mix(sky, col, 0.5), col, fade);
	alpha *= fade;
	return vec4(mix(sky, col, alpha), alpha);
}

void sky() {
	vec3 dir = EYEDIR;
	vec3 sun = LIGHT0_ENABLED ? LIGHT0_DIRECTION : normalize(vec3(0.3, 0.6, -0.4));
	vec3 col = sky_color(dir, sun);

	if (AT_QUARTER_RES_PASS) {
		// How much the clouds hide, marched without light: cheap, and in a channel with ten bits.
		COLOR = vec3(march_clouds(POSITION, dir, sun, col, false).a);
	} else if (AT_HALF_RES_PASS) {
		// The clouds laid over the plain sky, at half resolution: the costly part. Stored on a
		// square-root curve, scaled down for headroom: the buffer clamps at one and dark storm or
		// ash clouds would band without the extra precision near black.
		vec4 c = march_clouds(POSITION, dir, sun, col, true);
		COLOR = sqrt(max(c.rgb, vec3(0.0)) * 0.25);
	} else {
		float day = smoothstep(-0.2, 0.15, sun.y);
		vec3 plain = col;
		float starlight = max(1.0 - day, stars_by_day) * (1.0 - overcast);
		if (dir.y > -0.02) col += night_sky(dir, sun) * starlight;
		// Bodies show through a clear sky by day as well as by night, faded by the daylight's glare.
		vec4 b = bodies(dir, sun) * (1.0 - overcast * 0.9);
		col = col * (1.0 - b.a * mix(0.88, 1.0, 1.0 - day)) + b.rgb;

		// The sun itself, in front of everything but cloud.
		float sd = dot(dir, sun);
		float disc = smoothstep(cos(sun_radius * 1.15), cos(sun_radius), sd);
		col += star_color * disc * sun_energy * 18.0 * (1.0 - overcast) * smoothstep(-0.03, 0.0, dir.y);

		float cover;
		if (AT_CUBEMAP_PASS) {
			// Reflections and ambient light: just the overall cover, no march.
			cover = dir.y > 0.0 ? cloud_cover * 0.6 : 0.0;
			col = mix(col, mix(cloud_shade, cloud_lit, 0.5) * (0.2 + 0.8 * day), cover);
		} else {
			// The clouds over the plain sky, plus whatever else is up there where they let it through.
			vec3 clouded = HALF_RES_COLOR.rgb;
			clouded = clouded * clouded * 4.0;
			cover = clamp(QUARTER_RES_COLOR.r, 0.0, 1.0);
			col = clouded + (col - plain) * (1.0 - cover);
		}
		if (debug_view == 1) col = vec3(cover);
		if (debug_view == 2) col = HALF_RES_COLOR.rgb * HALF_RES_COLOR.rgb * 4.0;
		col = mix(col, overcast_color * (0.15 + 0.85 * day), overcast * smoothstep(-0.1, 0.25, dir.y) * 0.85);
		COLOR = col;
	}
}
