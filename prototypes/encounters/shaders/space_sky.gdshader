shader_type sky;

// A painted backdrop in the manner of a 1970s paperback cover: a deep wash of colour, a band of
// nebula with dust lanes, a field of stars, the sun, and (in orbit) a banded giant with rings.
// The planet is part of the sky, so it never needs a far clip plane or depth precision, and it
// stays put however far the player flies inside a zone. No TIME, so the radiance map is built once.

uniform vec3 deep_color : source_color = vec3(0.03, 0.05, 0.11);
uniform vec3 band_color : source_color = vec3(0.10, 0.09, 0.18);
uniform vec3 nebula_a : source_color = vec3(0.85, 0.35, 0.20);
uniform vec3 nebula_b : source_color = vec3(0.10, 0.45, 0.55);
uniform float nebula_strength = 0.8;
uniform vec3 nebula_axis = vec3(0.2, 0.9, 0.3);
uniform float band_width = 0.4;
uniform float star_density = 1.0;

uniform bool planet_on = true;
uniform vec3 planet_dir = vec3(0.45, -0.3, -0.85);
uniform float planet_radius = 0.42;
uniform vec3 planet_axis = vec3(0.3, 1.0, -0.45);
uniform vec3 planet_a : source_color = vec3(0.86, 0.55, 0.28);
uniform vec3 planet_b : source_color = vec3(0.95, 0.85, 0.62);
uniform vec3 planet_c : source_color = vec3(0.62, 0.25, 0.14);
uniform vec3 planet_air : source_color = vec3(1.0, 0.65, 0.4);
uniform bool rings_on = true;
uniform float ring_inner = 1.4;
uniform float ring_outer = 2.25;
uniform vec3 ring_color : source_color = vec3(0.92, 0.80, 0.62);
uniform float ring_opacity = 0.75;

uniform float sun_size = 0.02;
uniform float sun_glow = 0.35;
uniform float sun_disc_energy = 40.0;

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

float fbm(vec3 p) {
	float v = 0.0;
	float a = 0.5;
	for (int i = 0; i < 5; i++) {
		v += a * noise3(p);
		p = p * 2.07 + vec3(3.1, 1.7, 5.3);
		a *= 0.5;
	}
	return v;
}

// Stars: points scattered through a thin shell of a 3D lattice, STAR_SHELL cells out, each drawn
// where the view ray passes within its radius. Every star is placed, and kept or dropped, by itself
// (where it lies in its cell, and whether that is inside the shell), never by where the ray cuts a
// cell, so the field is equally dense in every direction. (Cutting the lattice with the sky's
// sphere left a star-free hole ringed by bands at each of the six axis directions.) A star that
// close to the ray lies within half a cell of the ray's own point each way, so the two cells
// round that point on each axis hold it.
const float STAR_SHELL = 320.0;

vec3 starfield(vec3 d) {
	vec3 p = d * STAR_SHELL;
	vec3 base = floor(p + 0.28) - 1.0;
	vec3 col = vec3(0.0);
	for (int i = 0; i < 8; i++) {
		vec3 cell = base + vec3(float(i & 1), float((i >> 1) & 1), float((i >> 2) & 1));
		if (hash13(cell) < 1.0 - 0.06 * star_density) continue;
		vec3 star = cell + vec3(hash13(cell + 11.1), hash13(cell + 23.7), hash13(cell + 37.3)) * 0.6 + 0.2;
		if (abs(length(star) - STAR_SHELL) > 0.35) continue;
		float off = length(star - d * dot(star, d));   // from the ray, in cells
		float brightness = pow(hash13(cell + 5.5), 4.0) * 7.0 + 0.5;
		vec3 tint = mix(vec3(1.0, 0.8, 0.6), vec3(0.75, 0.88, 1.0), hash13(cell + 7.7));
		col += tint * brightness * smoothstep(0.28, 0.0, off);
	}
	return col;
}

void sky() {
	vec3 d = normalize(EYEDIR);
	vec3 light = LIGHT0_DIRECTION;
	vec3 sun_light = LIGHT0_ENABLED ? LIGHT0_COLOR * LIGHT0_ENERGY : vec3(1.0);

	// The wash: deep at the poles of the galactic band, richer along it.
	float across = dot(d, normalize(nebula_axis));
	float in_band = exp(-across * across / (band_width * band_width));
	vec3 col = mix(deep_color, band_color, in_band);

	// Nebula clouds, brightest in the band, with dark dust lanes through them.
	float n1 = fbm(d * 2.1 + vec3(1.3, 0.0, 0.7));
	float n2 = fbm(d * 4.6 + vec3(n1 * 1.8));
	float cloud = smoothstep(0.38, 0.9, n1 * 0.65 + n2 * 0.55) * (0.3 + 0.9 * in_band);
	col += mix(nebula_a, nebula_b, smoothstep(0.35, 0.65, n2)) * cloud * nebula_strength;
	col *= 1.0 - 0.6 * smoothstep(0.52, 0.78, fbm(d * 6.5 + 4.0)) * in_band;

	if (!AT_CUBEMAP_PASS) {
		col += starfield(d) * (1.0 - min(cloud, 1.0) * 0.5);
	}

	// The sun: a hard disc and a soft glow.
	float sun_angle = acos(clamp(dot(d, light), -1.0, 1.0));
	vec3 sun = sun_light * (smoothstep(sun_size, sun_size * 0.85, sun_angle) * sun_disc_energy * (0.75 + 0.25 * sun_angle / sun_size)
		+ sun_glow * exp(-sun_angle * 10.0) + 0.06 * exp(-sun_angle * 2.5));

	if (planet_on) {
		// A unit sphere far enough away to have the wanted angular radius.
		vec3 centre = normalize(planet_dir) / sin(planet_radius);
		vec3 axis = normalize(planet_axis);
		float b = dot(d, centre);
		float disc = b * b - (dot(centre, centre) - 1.0);
		float planet_t = -1.0;

		if (disc > 0.0 && b > 0.0) {
			planet_t = b - sqrt(disc);
			vec3 n = normalize(d * planet_t - centre);
			float lat = dot(n, axis);
			float warp = fbm(n * 3.2 + 2.0);
			float bands = sin((lat + (warp - 0.5) * 0.16) * 28.0);
			float wide = sin((lat + (warp - 0.5) * 0.3) * 8.0 + 1.7);
			vec3 albedo = mix(planet_a, planet_b, smoothstep(-0.4, 0.7, bands));
			albedo = mix(albedo, planet_c, smoothstep(0.35, 0.95, wide) * 0.75);
			// A great storm, Foss-red.
			vec3 storm_at = normalize(vec3(0.55, -0.25, 0.8));
			float storm = smoothstep(0.17, 0.11, length(n - storm_at) + (warp - 0.5) * 0.05);
			albedo = mix(albedo, planet_c * 1.25, storm * 0.85);

			float day = smoothstep(-0.12, 0.3, dot(n, light));
			col = albedo * (sun_light * day * 0.42 + vec3(0.02, 0.025, 0.045));
			float rim = pow(1.0 - max(dot(n, -d), 0.0), 2.5);
			col += planet_air * rim * (0.1 + 0.9 * day) * 0.6;
			sun = vec3(0.0); // behind the planet
		} else if (b > 0.0) {
			// A thin halo of atmosphere just past the limb, lit on the sunward side.
			float off = length(cross(d, centre)) - 1.0;
			vec3 limb = normalize(d * b - centre);
			col += planet_air * exp(-max(off, 0.0) * 22.0) * 0.6 * (0.15 + 0.85 * max(dot(limb, light), 0.0));
		}

		if (rings_on) {
			float facing = dot(d, axis);
			float ring_t = abs(facing) > 1e-4 ? dot(centre, axis) / facing : -1.0;
			vec3 hit = d * ring_t;
			float r = length(hit - centre);
			float mask = ring_t > 0.0 ? smoothstep(ring_inner, ring_inner + 0.04, r) * smoothstep(ring_outer, ring_outer - 0.04, r) : 0.0;
			if (mask > 0.0 && (planet_t < 0.0 || ring_t < planet_t)) {
				float grooves = 0.55 + 0.45 * sin(r * 70.0) * sin(r * 23.0 + 1.0);
				float gap = smoothstep(0.015, 0.03, abs(r - (ring_inner + ring_outer) * 0.52));
				// The planet's shadow falls across the rings.
				vec3 oc = hit - centre;
				float sb = dot(oc, light);
				float shadow = (sb < 0.0 && sb * sb - (dot(oc, oc) - 1.0) > 0.0) ? 0.12 : 1.0;
				vec3 ring = ring_color * grooves * sun_light * (0.25 + 0.75 * abs(dot(light, axis))) * shadow * 0.55;
				col = mix(col, ring, mask * gap * ring_opacity);
			}
		}
	}

	COLOR = col + sun;
}
