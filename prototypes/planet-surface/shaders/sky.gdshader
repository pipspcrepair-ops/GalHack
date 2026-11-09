// The sky, from the world's own data (see SkySystem): a clear-sky gradient from its air, the
// sun and its glow in the star's colour, sunsets toward a low sun, moons lit by the sun (so they
// show phases), a neighbouring giant with bands and maybe rings, the galaxy's real stars and
// nebulae at night (from its catalogue, as seen from this star: GalaxyShader),
// and a layer of clouds you can see the shape of: a slab of cloud raymarched against the sun,
// with flat bases, lit tops and dark undersides, computed at half resolution.
//
// The Mobile renderer keeps the reduced-resolution sky buffers in RGB10A2, so their alpha has
// only four levels: clouds' edges banded into blocks. So the alpha never goes through ALPHA: the
// half-resolution pass stores the clouds already laid over the plain sky gradient (colour only),
// and the quarter-resolution pass marches density alone for how much of what is behind them
// (stars, the galaxy, the black hole, moons, the giant and rings, the sun and its corona) the
// clouds hide: one transmittance, the cloud's own, multiplied into everything behind it. The haze
// that fades far clouds into the sky's colour fades their look, never how much they hide: a deck
// at the horizon is still a deck, and no star shows through it.
shader_type sky;
render_mode use_half_res_pass, use_quarter_res_pass;

global uniform vec4 weather_flash;   // where in the world the last strike lights the cloud from, and its brightness

uniform vec3 zenith_day : source_color;
uniform vec3 horizon_day : source_color;
uniform vec3 zenith_night : source_color;
uniform vec3 sunset : source_color;
uniform vec3 haze : source_color;
uniform vec3 star_color : source_color;
uniform float sun_energy = 1.3;
uniform float density = 1.0;
uniform float stars_by_day = 0.0;
uniform mat3 star_turn = mat3(1.0);   // world directions into the galaxy catalogue's
uniform float overcast = 0.0;          // weather greying the whole sky
uniform vec3 overcast_color : source_color = vec3(0.5);

uniform int moon_count = 0;
uniform vec3 moon_dir[10];
uniform float moon_radius[10];
uniform vec3 moon_color[10] : source_color;
uniform vec3 moon_light[10];
uniform int moon_layer[10];
uniform float moon_shade[10];          // the share of the sun each moon sees (another body's shadow hides the rest)
uniform float moon_red[10];            // how deep it stands in a shadow reddened by a world's air
uniform float sun_lit = 1.0;           // the share of the sun's disc in sight from here: an eclipse darkens the day            // which layer of surfaces shows it; -1 while it is plain
uniform vec3 moon_axis[10];
uniform vec3 moon_zero[10];            // where its map's zero longitude faces this moment
uniform sampler2DArray surfaces : source_color, filter_linear_mipmap, repeat_enable;

uniform bool giant = false;
uniform vec3 giant_dir = vec3(0.0, 1.0, 0.0);
uniform float giant_radius = 0.1;
uniform int giant_layer = -1;
uniform vec3 giant_zero = vec3(1.0, 0.0, 0.0);
uniform int giant_at = 0;              // how many of the moon slots, farthest first, stand behind the giant
uniform vec3 giant_light = vec3(0.0, 1.0, 0.0);
uniform bool home_ring = false;
uniform vec3 home_ring_color : source_color = vec3(0.8);
uniform mat3 sky_to_globe = mat3(1.0);
uniform vec3 home_ring_eye = vec3(0.0, 1.0, 0.0); // the eye in the globe's frame, in globe radii
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
uniform float stratus = 0.0;     // cumulus heaps (0) to flat layers of grey (1)
uniform float cirrus = 0.0;      // thin high streaks
uniform int tower_count = 0;      // storm towers: their deck is thick and dark, whatever the sky around them
uniform vec4 tower[4];           // x, z, radius, strength
uniform vec2 storm_wind = vec2(1.0, 0.0);  // which way the anvils spread
// The night's own light on the clouds (SkyRig: the light the ground has at night): the brightest moon's
// way, or the giant's, or the stars' from overhead, and how much it gives.
uniform vec4 night_light = vec4(0.0, 1.0, 0.0, 0.0);
uniform vec3 night_color : source_color = vec3(0.62, 0.7, 0.95);
// Lit places under the clouds: their lamps light the cloud base over them (x, z, reach, strength) in their colour.
uniform int glow_count = 0;
uniform vec4 ground_glow[4];
uniform vec3 ground_glow_color[4] : source_color;
uniform int debug_view = 0;   // 1: cloud coverage, 2: cloud colour, 3: the night backdrop alone, brightened

// How much of the sun's light gets through the air, by its height: all of it from about 17° up,
// under half at the horizon (SkyRig.AirMass, the same curve for the sunlight on the ground).
float sun_air(float sun_h) {
	return 0.45 + 0.55 * smoothstep(0.02, 0.3, sun_h);
}

vec3 sky_color(vec3 dir, vec3 sun) {
	float up = dir.y;
	float sun_h = sun.y;
	float day = smoothstep(-0.2, 0.15, sun_h) * (0.04 + 0.96 * sun_lit);
	float bright = 0.25 + 0.75 * smoothstep(-0.05, 0.45, sun_h);

	vec3 zen = mix(zenith_night, zenith_day * bright, day);
	vec3 hor = mix(zenith_night * 1.6 + haze * 0.02, horizon_day * bright, day);
	float a = pow(clamp(up, 0.0, 1.0), 0.42 / density);
	vec3 col = mix(hor, zen, a);

	// Sunset and sunrise: warm light piling up toward a low sun, strongest in thick air.
	vec2 flat_dir = normalize(dir.xz + vec2(1e-5));
	vec2 flat_sun = normalize(sun.xz + vec2(1e-5));
	// Clamped: straight away from the sun the dot can round past -1, and pow of a negative is NaN,
	// which drew a dotted dark line down the sky on the anti-solar azimuth at dawn and dusk.
	float toward = clamp(dot(flat_dir, flat_sun) * 0.5 + 0.5, 0.0, 1.0);
	float low = smoothstep(0.45, 0.0, sun_h) * smoothstep(-0.28, -0.02, sun_h);
	col += sunset * low * pow(toward, 2.5) * exp(-max(up, 0.0) * 5.0 / density) * (0.45 + 0.35 * density);
	col = mix(col, col * vec3(0.9, 0.85, 0.82) + sunset * 0.08, low * 0.5);

	// Aerosol glow round the sun. Near the horizon its light has come a long way through the air
	// (sun_through): the wide halo dims with it, so a low sun is a contained glow, not a white sky.
	float sd = dot(dir, sun);
	float sun_through = sun_air(sun_h);
	// Low down both halos thin with the same path and take the sunset's colour, so a setting sun
	// sits in a warm contained glow you can look at, never a white blaze.
	vec3 halo = mix(sunset, star_color, smoothstep(0.03, 0.38, sun_h));
	col += halo * (pow(max(sd, 0.0), 8.0) * 0.18 * sun_through + pow(max(sd, 0.0), 90.0) * 0.5 * sun_through * sun_through) * day * density;

	// Below the horizon: the haze, darker downward.
	if (up < 0.0) col = mix(col, haze * bright * day * 0.55 + zenith_night, clamp(-up * 6.0, 0.0, 1.0));
	return col;
}

#include "res://galaxy_sky.gdshaderinc"

// The night backdrop: the galaxy, in true directions. Looked up along the direction itself, so it
// has no seams, poles or corners. The stars themselves need the full picture, not a reflection.
vec3 night_sky(vec3 dir, bool points) {
	return galaxy_sky(star_turn * dir, points);
}

// Moons and the giant: discs lit from the sun's side, so they show phases; the giant has bands
// and, if the system gives it rings, a ring plane that passes behind it and in front of it.
// Colours are premultiplied by alpha so the layers stack: back ring, disc, front ring.
vec4 over(vec4 front, vec4 back) {
	return vec4(front.rgb + back.rgb * (1.0 - front.a), front.a + back.a * (1.0 - front.a));
}

// A body's own map at a point of its surface, at the detail a disc of this angular radius can show.
vec4 surface_at(int layer, vec3 n, vec3 axis, vec3 zero, float radius) {
	float u = atan(dot(n, cross(axis, zero)), dot(n, zero)) / 6.2831853;
	float v = acos(clamp(dot(n, axis), -1.0, 1.0)) / 3.1415927;
	return textureLod(surfaces, vec3(u, v, float(layer)), log2(max(40.7 / (radius * 700.0), 1.0)));
}

vec4 lit_disc(vec3 dir, vec3 centre, float radius, vec3 sun, out vec3 normal) {
	float c = dot(dir, centre);
	normal = -centre;
	if (c <= cos(radius)) return vec4(0.0);
	float r = acos(clamp(c, -1.0, 1.0));
	vec3 helper = abs(centre.y) > 0.95 ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0);
	vec3 east = normalize(cross(helper, centre));
	vec3 north = cross(centre, east);
	vec2 q = vec2(dot(dir, east), dot(dir, north)) / sin(radius);
	float z = sqrt(max(0.0, 1.0 - dot(q, q)));
	normal = normalize(east * q.x + north * q.y - centre * z);
	float edge = smoothstep(radius, radius * 0.985, r);
	return vec4(vec3(clamp(dot(normal, sun), 0.0, 1.0)), edge);
}

// This world's own ring, a real thing in its equator's plane, 1.45 to 2.3 globe radii from its centre:
// the same bands the ring shows from space.
vec4 own_ring(vec3 dir, vec3 sun) {
	if (!home_ring) return vec4(0.0);
	vec3 d = sky_to_globe * dir;
	if (abs(d.y) < 1e-5) return vec4(0.0);
	float t = -home_ring_eye.y / d.y;
	if (t <= 0.0) return vec4(0.0);
	float b = dot(home_ring_eye, d), past = b * b - dot(home_ring_eye, home_ring_eye) + 1.0;
	if (past > 0.0 && -b - sqrt(past) > 0.0 && -b - sqrt(past) < t) return vec4(0.0); // the globe is in the way
	float r = (length(home_ring_eye.xz + d.xz * t) - 1.45) / 0.85;
	if (r <= 0.0 || r >= 1.0) return vec4(0.0);
	float bands = 0.55 + 0.25 * sin(r * 61.0) + 0.2 * sin(r * 23.0 + 1.0);
	float gap = smoothstep(0.0, 0.02, abs(r - 0.62)) * smoothstep(0.0, 0.05, r) * smoothstep(1.0, 0.94, r);
	float a = gap * mix(0.95, 1.0, clamp(bands, 0.0, 1.0));
	vec3 lit = home_ring_color * (0.45 + 0.55 * clamp(bands, 0.0, 1.0)) * (0.8 + 0.3 * bands) * (0.35 + 0.65 * clamp(abs((sky_to_globe * sun).y) * 1.5, 0.0, 1.0));
	return vec4(lit * a, a);
}

vec4 bodies(vec3 dir, vec3 sun) {
	vec4 result = vec4(0.0), layer = vec4(0.0);
	if (giant) {
		vec3 axis = normalize(ring_normal);
		vec4 back = vec4(0.0), front = vec4(0.0);
		if (rings) {
			// Where the view ray crosses the ring plane through the giant's centre, in giant radii.
			float denom = dot(dir, axis);
			float t = dot(giant_dir, axis) / (abs(denom) > 1e-4 ? denom : 1e-4);
			if (t > 0.0) {
				float rr = length(dir * t - giant_dir) / sin(giant_radius);
				// The bands are rock in lighter and darker tones: only the gap lets through what is behind.
				float ring = smoothstep(1.45, 1.5, rr) * (1.0 - smoothstep(2.35, 2.42, rr));
				ring *= 1.0 - (smoothstep(1.93, 1.97, rr) - smoothstep(2.03, 2.07, rr)) * 0.85;
				float tone = 0.6 + 0.4 * sin(rr * 41.0) * sin(rr * 13.0);
				float lit = 0.6 + 0.4 * clamp(abs(dot(axis, giant_light)) * 1.5, 0.0, 1.0);
				// The giant's own shadow across its ring, soft at the edge.
				vec3 p = (dir * t - giant_dir) / sin(giant_radius);
				float along = dot(p, giant_light);
				lit *= along < 0.0 ? mix(0.06, 1.0, smoothstep(0.96, 1.04, length(p - giant_light * along))) : 1.0;
				vec4 rc = vec4(ring_color * lit * tone * ring, ring);
				if (t < 1.0) front = rc; else back = rc;
			}
		}
		vec3 n;
		vec4 disc = lit_disc(dir, giant_dir, giant_radius, giant_light, n);
		if (disc.a > 0.0) {
			float lat = dot(n, axis);
			float bands = sin(lat * 19.0 + sin(lat * 7.0) * 1.6) * 0.5 + 0.5;
			vec3 surface = mix(giant_color, giant_band, bands * 0.7);
			if (giant_layer >= 0) surface = surface_at(giant_layer, n, axis, giant_zero, giant_radius).rgb;
			// Never quite black on its night side, its limb faintly lit all round by its air: a whole
			// sphere even dark against the night (the same numbers as space's own: SpaceSky).
			float limb = pow(1.0 - clamp(dot(n, -giant_dir), 0.0, 1.0), 3.0) * (0.35 + 0.65 * smoothstep(-0.3, 0.6, dot(n, giant_light)));
			disc.rgb = surface * (0.07 + 0.93 * disc.r + 0.22 * limb) * disc.a;
		}
		layer = over(front, over(disc, back));
	}
	// Farthest first, whatever each one is, so a far world never crosses in front of a near moon or the giant.
	for (int i = 0; i < moon_count; i++) {
		if (i == giant_at) result = over(layer, result);
		vec3 n;
		vec4 m = lit_disc(dir, moon_dir[i], moon_radius[i], moon_light[i], n);
		if (m.a <= 0.0) continue;
		vec3 ground = moon_color[i] * (0.82 + 0.18 * textureLod(cloud_noise, n.xz * 0.7 + float(i) * 0.31, 0.0).r);
		float glows = 0.0;
		if (moon_layer[i] >= 0) {
			vec4 own = surface_at(moon_layer[i], n, moon_axis[i], moon_zero[i], moon_radius[i]);
			ground = own.rgb;
			glows = own.a;
		}
		m.rgb = (ground * (0.05 + 1.1 * m.r * moon_shade[i] + glows * 0.8) + vec3(0.32, 0.06, 0.025) * moon_red[i]) * m.a;
		result = over(m, result);
	}
	if (giant_at >= moon_count) result = over(layer, result);
	// This world's own ring is nearer than anything else up there.
	return over(own_ring(dir, sun), result);
}

uniform sampler3D cloud_detail : filter_linear_mipmap, repeat_enable;

// Cloud density at a point: a coverage map from 2D noise (where clouds are, and how tall), a
// height profile with flat bases and rounded tops that rise higher where the cloud is denser,
// and 3D noise eroding the edges into billows so nothing reads as an extruded pillar.
// One storm, built of the same cloud noise as the deck so it is part of it and never a thing on its
// own: a huge lumpy mass, cauliflower at the edge and the top, growing out of a deck that thickens
// toward it for many kilometres. A weak cell is a modest swelling; a strong one towers and spreads
// into a broad flat anvil downwind, over a dark ragged base that sags across a wide area.
// x: how much storm is here (0..1), y: how tall it stands here (layer thicknesses), z: lump (0..1),
// w: the deck's thickening toward it.
uniform float tower_wall[4];     // 0..1: a funnel hangs from this cell's base (a lumpy lowered bulge of it)
float lump(vec3 p, float k) {
	vec2 uv = (p.xz + cloud_drift * 0.35) / cloud_scale * k + vec2(p.y, -p.y) * 0.0009;
	return textureLod(cloud_noise, uv, 0.0).r * 0.6 + textureLod(cloud_noise, uv * 2.3 + 0.2, 0.0).r * 0.4;
}

vec4 storm_at(vec3 p) {
	float h = (p.y - cloud_base) / cloud_thick;
	vec4 best = vec4(0.0);
	float lp = lump(p, 3.5);
	float lq = lump(p, 8.0);
	for (int i = 0; i < tower_count; i++) {
		float w = tower[i].w;
		float reach = tower[i].z * (1.2 + 3.0 * w);
		float topH = 0.7 + 3.2 * w * w;
		float ah = smoothstep(topH * 0.5, topH * 0.85, h);          // the anvil: up near the top it spreads
		vec2 centre = tower[i].xy + storm_wind * ah * reach * 1.1;
		float r = reach * (1.0 + 0.9 * ah) * (0.5 + 1.0 * lp);      // a cauliflower edge, in lobes
		float d = length(p.xz - centre);
		float s = w * smoothstep(r, r * 0.2, d);
		float halo = w * smoothstep(reach * 1.9, reach * 0.4, length(p.xz - tower[i].xy));
		best.w = max(best.w, halo);
		if (s > best.x) best = vec4(s, topH * (0.7 + 0.6 * lq), lp, best.w);
	}
	return best;
}

// How far the base sags here, metres: ragged over the whole storm, and a lumpy bulge where a funnel hangs.
float base_sag(vec3 p, vec4 st) {
	float sag = st.x * (40.0 + 60.0 * st.z) + st.x * st.x * 110.0; // deeper the stronger
	for (int i = 0; i < tower_count; i++) {
		float rw = tower[i].z * (0.45 + 0.5 * st.z);
		sag += tower_wall[i] * 130.0 * smoothstep(rw, rw * 0.15, length(p.xz - tower[i].xy)) * (0.6 + 0.8 * st.z);
	}
	return sag;
}

// ---- The front ------------------------------------------------------------------------------
// A storm front (WeatherSim.Front): a long line advancing on the wind, its edge bowed and wandering in
// bays and lobes. Behind the edge the storm's deck trails for kilometres, dark and thick; ahead the air is
// open but for the anvil's thin sheet, spread ahead up high. A thunderstorm's carries a shelf cloud along
// its edge. The world is a torus: the line runs along one of its lattice directions and closes on itself,
// and comes round again every so far across it; each point measures itself against the nearest copy, and
// every wave along the edge is a whole number of waves to the period, so there is no seam anywhere.
uniform vec4 front_line = vec4(0.0);    // xy: a point of its edge's straight line (the copy nearest the eye), zw: the way it advances
uniform vec4 front_shape = vec4(0.0);   // x: strength 0..1, y: its period along the line, z: the spacing of its copies, w: how deep the storm behind it reaches
uniform vec4 front_wave = vec4(0.0);    // x: how far along the next copy is shifted, y: how far its bows hold the edge back, z: bows, w: bays (per period)
uniform vec4 front_kind = vec4(0.0);    // x: 1 a shelf cloud along its edge, y: 1 a roll cloud ahead of it, z: its seed, w: its severity

// How far the edge lags behind its straight line at a distance along it (Front.Lag).
float front_lag(float u) {
	float f = u / front_shape.y, s = front_kind.z;
	return front_wave.y * (0.5 - 0.5 * cos(6.2831853 * (front_wave.z * f + s)))
		+ 220.0 * sin(6.2831853 * (front_wave.w * f + fract(s * 7.13)))
		+ 90.0 * sin(6.2831853 * (floor(front_shape.y / 1600.0 + 0.5) * f + fract(s * 3.71)))
		+ 120.0 * sin(6.2831853 * (floor(front_shape.y / 700.0 + 0.5) * f + fract(s * 5.31)))
		+ 55.0 * sin(6.2831853 * (floor(front_shape.y / 330.0 + 0.5) * f + fract(s * 9.17)));
}

// Where a point stands against the nearest copy of the front: x along it, y ahead of its edge (negative behind), z its strength.
vec3 front_at(vec2 xz) {
	vec2 d = xz - front_line.xy;
	float u = dot(d, vec2(-front_line.w, front_line.z));
	float s = dot(d, front_line.zw);
	float j = floor(s / front_shape.z + 0.5);
	u -= j * front_wave.x;
	return vec3(u, s - j * front_shape.z + front_lag(u), front_shape.x);
}

// A wave along the edge with a whole number of crests to its period, near the given wavelength (no seam where the line closes).
float along_wave(float u, float metres) {
	return u / front_shape.y * max(1.0, floor(front_shape.y / metres + 0.5));
}

// How far ahead of an edge its open air reaches before the next copy's storm takes over.
float front_reach() {
	return front_shape.z * 0.5 - front_wave.y;
}

// The deck trailing behind the edge (x) and the open air everywhere its storm is not (y), 0..1 (Front.Cover, Front.Clear).
vec2 front_sides(vec3 fr) {
	return vec2(fr.z * smoothstep(150.0, -1200.0, fr.y) * smoothstep(-front_shape.w, -front_shape.w * 0.6, fr.y),
		fr.z * (1.0 - smoothstep(700.0, -100.0, fr.y) * smoothstep(-front_shape.w * 1.05, -front_shape.w * 0.75, fr.y)));
}

// The shelf cloud along the leading edge: a long low wedge hanging under the base. Its face climbs from a
// thin lip up and back into the storm in smooth tiers; its underside rises behind the lip in flat tiers with
// smooth risers, finely striated along the edge, until it meets the storm's own base.
float shelf_cloud(vec3 p, vec3 fr, bool cheap) {
	float W = min(2600.0, front_reach() * 0.8);
	float a = fr.y;
	if (front_kind.x < 0.5 || fr.z < 0.01 || a > 260.0 || a < -W) return 0.0;
	float x = clamp(-a / W, 0.0, 1.0);
	float wob = textureLod(cloud_noise, vec2(along_wave(fr.x, 3000.0), 0.61 + front_kind.z), 0.0).r - 0.5;
	float drop = (150.0 + 140.0 * front_kind.w) * smoothstep(0.0, 0.6, fr.z);
	// Heaps of the cloud's own noise, a few hundred metres across: they bend its tiers so no step runs
	// ruled, and decide where it buds first as it grows (the strongest heaps fill in, then the rest).
	float heap = textureLod(cloud_noise, vec2(along_wave(fr.x, 650.0), a / 650.0) + 0.41, 0.0).r * 0.6
		+ textureLod(cloud_noise, vec2(along_wave(fr.x, 260.0), a / 260.0) + 0.77, 0.0).r * 0.4;
	float lip = cloud_base - drop + wob * 60.0 + (heap - 0.5) * 90.0;
	float k = x * 4.0 + (heap - 0.5) * 1.4;
	float tiers = (floor(k) + smoothstep(0.45, 1.0, fract(k))) * 0.25;
	float under = lip + (drop - 10.0) * mix(x, tiers, 0.75);
	float s = sqrt(x) * 3.0 + (heap - 0.5) * 1.1;
	float face = (floor(s) + smoothstep(0.3, 1.0, fract(s))) / 3.0;
	float over = lip + 20.0 + (drop + cloud_thick * 1.3) * mix(sqrt(x), face, 0.55);
	float d = smoothstep(under, under + 28.0, p.y) * smoothstep(over, over - 40.0, p.y) * smoothstep(80.0, -30.0, a + (heap - 0.5) * 300.0); // its front face heaped, not a wall
	if (!cheap && d > 0.0) {
		// Fine striations running along the edge under it, and a little tearing so it is not machined.
		float stri = sin(a / 36.0 + wob * 11.0 + sin(6.2831853 * along_wave(fr.x, 4400.0)) * 1.5) * 0.5 + 0.5;
		d *= 1.0 - 0.4 * stri * smoothstep(under + 90.0, under, p.y);
		d -= (textureLod(cloud_noise, vec2(along_wave(fr.x, 900.0), a / 900.0) + 0.13, 0.0).r - 0.45) * 0.5 * smoothstep(over - 200.0, over, p.y);
	}
	// Growing, it buds in rounded heaps along a ragged line and spreads, never appearing whole.
	float grown = smoothstep(0.0, 1.0, fr.z);
	return clamp(d, 0.0, 1.0) * smoothstep(1.0 - grown * 1.3, 1.15 - grown * 1.3, 1.0 - heap);
}
// Growing cumulus over a fair-weather waterspout's base (WeatherSim.SpawnSpouts): a swelling of the deck,
// tall and with a flat darker base, where the spouts hang.
uniform int cumulus_count = 0;
uniform vec4 cumulus[2];   // x, z, radius, how far grown 0..1

float cumulus_at(vec2 xz, float n) {
	float s = 0.0;
	for (int i = 0; i < cumulus_count; i++)
		s = max(s, cumulus[i].w * smoothstep(cumulus[i].z, cumulus[i].z * 0.2, length(xz - cumulus[i].xy) * (0.35 + 1.3 * n)));
	return s;
}

float cloud_density(vec3 p, bool cheap) {
	vec2 uv = (p.xz + cloud_drift) / cloud_scale;
	// Two octaves of the same noise, as CloudCoverage.Coverage and the globe read them, so the clouds you see
	// from the ground are the ones on the globe (a turned second octave matched neither, nor wrapped with the planet).
	float n = textureLod(cloud_noise, uv, 0.0).r * 0.65 + textureLod(cloud_noise, uv * 2.0 + 0.31, 0.0).r * 0.35;
	bool stormy = tower_count > 0 || front_shape.x > 0.001;
	// Stratus: the same noise pulled out flat along one way, an even sheet with long seams in it.
	// Flat layers come in broad patches that drift across the map: some places heaped, some layered.
	float st = (stratus > 0.001 && !stormy) ? stratus * smoothstep(0.3, 0.55, textureLod(cloud_noise, uv * 0.06 + 0.77, 0.0).r + stratus * 0.25) : 0.0;
	if (st > 0.001) n = mix(n, 0.32 + 0.42 * textureLod(cloud_noise, uv * vec2(0.3, 1.3) + 0.47, 0.0).r, st);
	vec4 st_ = tower_count > 0 ? storm_at(p) : vec4(0.0);
	float storm = st_.x;
	float cover = mix(0.12, 1.05, cloud_cover);
	float halo = st_.w;
	float coverage = n - (1.0 - cover) + storm * 0.55; // the deck thickens toward the storm (below)
	// A front: its deck trailing behind the edge, the open air ahead with the anvil's thin sheet high over it, and its shelf.
	float extra = 0.0, behind = 0.0;
	if (front_shape.x > 0.001) {
		vec3 fr = front_at(p.xz);
		// Where its deck begins and ends, the cloud's own small heaps decide: it buds and spreads in rounded
		// masses along a ragged line, never along a ruled one.
		vec3 heaped = fr;
		heaped.y += (textureLod(cloud_noise, uv * 1.9 + 0.37, 0.0).r - 0.5) * 900.0 + (n - 0.5) * 500.0;
		vec2 sides = front_sides(heaped);
		behind = sides.x;
		halo *= 1.0 - sides.y; // clear sky right beside the front: its storms' deck only spreads behind the edge
		float hh = (p.y - cloud_base) / cloud_thick;
		// Its cells' anvils thin out as they spread ahead of the edge: a sheet up high, not a second deck.
		float thin = 0.6 * sides.y * smoothstep(1.0, 1.8, hh);
		coverage += sides.x * 0.75 - sides.y * 0.55 - storm * 0.55 * thin;
		storm *= 1.0 - thin;
		// The anvils' sheet: lenses, thick where the noise is and thinning to nothing at their edges, so
		// from below they are soft rounded patches, not slabs cut out with sheer sides.
		float lens = smoothstep(0.25, 0.75, textureLod(cloud_noise, uv * 0.7 + 0.21, 0.0).r);
		float half_thick = 0.42 * lens;
		float sheet = fr.z * smoothstep(-1500.0, 0.0, fr.y) * smoothstep(min(9000.0 + 5000.0 * front_kind.w, front_reach() - 300.0), 1000.0, fr.y)
			* smoothstep(half_thick, half_thick * 0.3, abs(hh - 2.47)) * front_kind.x;
		// Seen from below it is looked through slantwise for kilometres, and as a lens cut from the ground's field
		// alone that drew it out into fans of streaks: the detail breaks it up with height as well.
		float veil = sheet * 0.45 * lens;
		if (!cheap && veil > 0.001) veil *= smoothstep(0.25, 0.65, textureLod(cloud_detail, (p + vec3(cloud_drift.x, 0.0, cloud_drift.y) * 1.3) / (cloud_scale.x * 0.125), 0.0).r);
		extra = max(shelf_cloud(p, fr, cheap), veil);
	}
	coverage += halo * 0.4;
	float swell = cumulus_count > 0 ? cumulus_at(p.xz, n) : 0.0;
	coverage += swell * 0.6;
	if (coverage <= 0.0) return clamp(extra * 1.8, 0.0, 1.0);
	float h = (p.y - cloud_base + (tower_count > 0 ? base_sag(p, st_) : 0.0)) / cloud_thick; // a dark ragged base under the storm
	float top = clamp(0.2 + coverage * 2.6, 0.2, 1.0);
	top = mix(top, 0.4, st);
	top = mix(top, 1.3 + 1.3 * n, behind * (1.0 - storm)); // behind a front the deck stands thick
	top = mix(top, st_.y, storm); // the storm stands up through the deck
	top = mix(top, 1.5 + 0.8 * n, swell); // a cumulus growing tall over the spouts
	// A heap, not a prism. The coverage is a field over the ground: a cloud cut from it alone stood on its
	// outline with sheer walls as tall as its middle (square-sided blocks from the side), and every wrinkle
	// in the field ran straight up the wall as a column (streaks hanging under every cloud). Its top is
	// domed instead, falling to nothing at its edge: a new cloud buds as a low round heap and swells, its
	// outline at each height its own, and the base stays flat.
	top *= sqrt(clamp(coverage * 6.0, 0.0, 1.0));
	float up = clamp(h / top, 0.0, 1.0);
	float profile = smoothstep(0.0, min(mix(0.07, 0.2, max(storm, behind)), top * 0.4), h) * (1.0 - smoothstep(mix(0.55, 0.82, max(storm, behind)), 1.0, up));
	float d = coverage * 3.2 * profile;
	if (!cheap) {
		vec3 q = (p + vec3(cloud_drift.x, 0.0, cloud_drift.y) * 1.3) / (cloud_scale.x * 0.125);
		float detail = textureLod(cloud_detail, q, 0.0).r * 0.65 + textureLod(cloud_detail, q * 3.0, 0.0).r * 0.35;
		// Bases flat-ish, tops billowing: the detail bites deeper the higher up the heap.
		d -= detail * mix(0.3, 0.75, up) * (1.0 - cloud_cover * 0.5);
		if (storm > 0.01) d -= detail * 0.5 * (1.0 - smoothstep(0.15, 0.7, storm)); // billows and curling lobes where it meets the open sky
	}
	return clamp(max(d, extra) * 1.8, 0.0, 1.0);
}

// Where along its first step each pixel's march starts, 0..1: interleaved gradient noise on the
// pixel grid, so neighbouring pixels sample the slab at well-spread depths. (A hash of the view
// direction, over only a third of a step, left the steps showing as combs and streaks along
// cloud edges.)
float march_jitter(vec2 pixel) {
	return fract(52.9829189 * fract(dot(pixel, vec2(0.06711056, 0.00583715))));
}

// How much the lit places under it light the cloud here (their lamps, in their colour).
vec3 place_glow(vec2 xz) {
	vec3 g = vec3(0.0);
	for (int i = 0; i < glow_count; i++) {
		float f = smoothstep(ground_glow[i].z, 0.0, length(xz - ground_glow[i].xy));
		g += ground_glow_color[i] * ground_glow[i].w * f * f;
	}
	return g;
}

// The round world's sink of far clouds (the march's own, below): 1 / 300 km past 2,000 m.
const float CLOUD_SINK = 1.667e-6;

// Past the march's reach the layer goes on to the horizon: on a round world every ray climbing from
// under it meets it within about 20 km. Its look has long faded into the haze there, but it still hides
// what is behind it: seen edge-on a broken layer is cloud nearly all the way along, and a storm's or a
// front's deck is as solid there as overhead. Large-scale only, never the far clouds' own grain (which,
// squeezed against the horizon, drew steps along it).
float far_cover(vec3 ro, vec3 rd) {
	if (ro.y >= cloud_base) return 0.0;
	float h = cloud_base + cloud_thick * 0.2;
	float rise = h - ro.y;
	// Where the ray, rising over a ground that falls away, meets that height: t * y + (t - 2000)^2 * sink = rise.
	float a = CLOUD_SINK, b = rd.y - 4000.0 * CLOUD_SINK, c = 4.0e6 * CLOUD_SINK - rise;
	float t = rd.y > 0.0 && rise < rd.y * 2000.0 ? rise / rd.y // within its first 2 km the ground is flat
		: (-b + sqrt(max(b * b - 4.0 * a * c, 0.0))) / (2.0 * a);
	vec3 p = ro + rd * t;
	p.y = h;
	float cover = smoothstep(0.05, 0.4, cloud_cover);
	if (tower_count > 0) cover = max(cover, storm_at(p).x);
	if (front_shape.x > 0.001) cover = max(cover, front_sides(front_at(p.xz)).x);
	return cover;
}

// The clouds along a view ray: their light over the given sky colour, and their opacity as laid over
// that colour (faded into the haze far off). `hide` is how much of what is behind them they hide: the
// march's own transmittance, all of it, and the deck past its reach. With lit false only the opacity is
// marched (no light samples): the cheap pass for occlusion.
vec4 march_clouds(vec3 ro, vec3 rd, vec3 sun, vec3 sky, bool lit, float jitter, out float hide) {
	hide = 0.0;
	bool fronted = front_shape.x > 0.001;
	bool stormy = tower_count > 0 || fronted;
	if (cloud_cover < 0.02 && !fronted) return vec4(sky, 0.0);
	float base = cloud_base - (fronted ? 330.0 : tower_count > 0 ? 280.0 : 0.0), top = cloud_base + cloud_thick * (stormy ? 4.0 : 1.0);
	float t0, t1;
	if (abs(rd.y) < 1e-3) {
		if (ro.y < base || ro.y > top) {
			if (!lit) hide = far_cover(ro, rd);
			return vec4(sky, 0.0);
		}
		t0 = 0.0;
		t1 = 2400.0;
	} else {
		float ta = (base - ro.y) / rd.y, tb = (top - ro.y) / rd.y;
		t0 = max(min(ta, tb), 0.0);
		t1 = max(ta, tb);
	}
	// The layer sits low, so its far reach is scaled to its height: about 16 km and 2 km of path.
	if (t1 <= 0.0 || t0 > 16000.0) {
		if (!lit) hide = far_cover(ro, rd);
		return vec4(sky, 0.0);
	}
	// A storm is long to look through at a low angle: cutting its path short drew a hard straight underside.
	t1 = min(t1, t0 + (stormy ? 9000.0 : 2000.0));
	// Steps by the length of the path through the slab: a few dozen straight up, more toward the
	// horizon, where the path is long and too few would slice the clouds into layers.
	int steps = int(clamp((t1 - t0) / 28.0, 24.0, stormy ? 112.0 : 64.0));
	float dt = (t1 - t0) / float(steps);
	float t = t0 + dt * jitter;
	float trans = 1.0;
	vec3 light = vec3(0.0);
	float day = smoothstep(-0.15, 0.1, sun.y) * (0.04 + 0.96 * sun_lit);
	vec3 sun_tint = mix(vec3(1.0), star_color, 0.4);
	vec3 sun_light = mix(sunset, sun_tint, smoothstep(0.0, 0.3, sun.y)) * day * sun_energy * 1.1;
	// By night the clouds are lit as the ground is: by the brightest moon (or the giant, or the stars
	// from overhead), in its colour, brighter toward it (the cloud round a moon is silvered); the light
	// is sampled toward whichever lights them, eased over from the sun through the twilight.
	float by_night = smoothstep(-0.06, -0.14, sun.y);
	vec3 key = sun;
	if (by_night > 0.0) {
		vec3 k = mix(sun, night_light.xyz, by_night);
		key = dot(k, k) > 0.04 ? normalize(k) : night_light.xyz;
	}
	vec3 night_lit = night_color * night_light.w * by_night * (0.75 + 1.4 * pow(max(dot(rd, night_light.xyz), 0.0), 6.0));
	// The sky's own light on them, by night the moonlit (or starlit) sky's: dark, never black.
	vec3 ambient = mix(cloud_shade, sky, 0.35) * (0.12 + 0.88 * day) + cloud_shade * night_color * night_light.w * by_night * 0.55;
	// A strike lights only the cloud round it, falling off with distance from it: a far one its own
	// cell, a near one (seen from under it) the deck for a good way round. Never the whole sky.
	float flash = weather_flash.w;
	float flash_reach = mix(1800.0, 650.0, smoothstep(700.0, 2500.0, length(weather_flash.xz - ro.xz)));
	float fronts = 0.0; // how much of what this ray met was the front's: its shelf and deck stand out low over the horizon
	// The shelf is a thin crisp wedge: through its band the march takes short steps (and may take more of them).
	// Its short steps come out of their own budget: once it is spent the march goes on at the plain step,
	// so the far side of the ray is never cut off (a cut along the band's edges drew square masses).
	int most = fronted ? steps + 64 : steps;
	float short_left = 64.0;
	for (int i = 0; i < most && t < t1; i++) {
		vec3 p = ro + rd * t;
		// The world is round: far clouds follow the land over the horizon (the same slow curve from the
		// same distance as the far land, round_sink in noise.gdshaderinc: 1 / 300 km past 2,000 m), so
		// distant bases sink out of sight and only the upper bulk shows. Overhead it is flat.
		float away = max(length(p.xz - ro.xz) - 2000.0, 0.0);
		p.y += away * away * 1.667e-6;
		float step_t = dt;
		vec3 fr = vec3(0.0);
		if (fronted) {
			fr = front_at(p.xz);
			// Eased in and out across the band's edges, so no seam follows them.
			float band = front_kind.x * smoothstep(500.0, 300.0, fr.y) * smoothstep(-3200.0, -2800.0, fr.y)
				* smoothstep(cloud_base + cloud_thick * 1.9, cloud_base + cloud_thick * 1.6, p.y);
			if (band > 0.0 && short_left > 0.0) {
				step_t = mix(dt, min(dt, 22.0 + t * 0.004), band);
				short_left -= 1.0 - step_t / dt;
			}
		}
		float d = cloud_density(p, false);
		if (d > 0.01) {
			// A thinner slab packs the same cloud into less path: as solid, seen through.
			float ext = d * step_t * 0.016 * clamp(700.0 / cloud_thick, 1.0, 2.0);
			if (fronted && trans > 0.5) fronts = max(fronts, max(front_sides(fr).x, step(p.y, cloud_base) * fr.z));
			if (lit) {
				// Light reaching this point from the sun (or the moon), through two cheaper samples of cloud.
				float reach = clamp(cloud_thick / 900.0, 0.3, 1.0);
				float toward = cloud_density(p + key * 80.0 * reach, true) + cloud_density(p + key * 240.0 * reach, true) * 0.8;
				float beer = exp(-toward * 1.6);
				// Under a front's deck the sun (or the moon) is shut out from above: the shelf and the low cloud there are
				// in its shadow, unless a low sun reaches in under it from the open side.
				if (fronted && p.y < cloud_base + cloud_thick * 1.5) {
					vec3 q = p + key * ((cloud_base + cloud_thick * 1.8 - p.y) / max(key.y, 0.08));
					beer *= 1.0 - 0.85 * front_sides(front_at(q.xz)).x;
				}
				float powder = 1.0 - exp(-d * 3.0);
				float hgt = clamp((p.y - cloud_base) / cloud_thick, 0.0, 1.0);
				vec3 lum = ambient * (0.4 + 0.6 * hgt) + cloud_lit * (sun_light + night_lit) * beer * mix(0.55, 1.0, powder);
				float dark = cloud_dark;
				float flank = 0.0;
				if (tower_count > 0) { vec4 sd = storm_at(p); flank = (1.0 - smoothstep(0.12, 0.65, sd.x)) * smoothstep(0.0, 0.12, sd.x); dark = max(dark, max(mix(0.4, 0.92, sd.x) * smoothstep(0.0, 0.3, sd.x), sd.w * 0.35)) * (1.0 - 0.5 * flank); }
				float rim = 0.0;
				if (fronted) {
					// Behind the edge the deck is slate, heavy with rain, and takes little of the sun; the shelf is
					// blue-grey; ahead the cloud is fair weather's. Where the shelf's lip meets the clear air it is lit.
					vec2 sides = front_sides(fr);
					float shelf = step(p.y, cloud_base) * fr.z;
					dark = max(dark * (1.0 - 0.6 * sides.y), sides.x * 0.95);
					if (shelf > 0.0) dark = max(dark * 0.7, 0.62 * fr.z);
					lum -= cloud_lit * (sun_light + night_lit) * beer * mix(0.55, 1.0, powder) * 0.6 * max(sides.x, shelf);
					rim = shelf * smoothstep(-260.0, -20.0, fr.y) * beer * front_kind.x;
				}
				if (cumulus_count > 0) dark = max(dark, 0.55 * cumulus_at(p.xz, 0.5) * (1.0 - hgt)); // its flat base, dark over the water
				lum *= 1.0 - dark * 0.78 * (1.0 - hgt * 0.4);
				lum += cloud_lit * sun_light * rim * 0.9;
				lum += cloud_lit * sun_light * flank * (0.25 + 0.4 * hgt); // lit flanks: lighter toward the edges
				// Light from below: lava under ash, the lamps of a town, and by night the moonlit ground's own,
				// so a storm's base is dark cloud, never a hole in the sky.
				vec3 under = cloud_underglow + night_color * night_light.w * by_night * 0.12 * (1.0 - 0.5 * cloud_dark);
				if (glow_count > 0) under += place_glow(p.xz);
				lum += under * (1.0 - hgt) * (1.0 - hgt);
				if (flash > 0.001) {
					vec2 off = (p.xz - weather_flash.xz) / flash_reach;
					lum += vec3(0.75, 0.72, 1.0) * flash * 2.5 * (1.0 - hgt * 0.3) * exp(-dot(off, off));
				}
				light += trans * lum * (1.0 - exp(-ext));
			}
			trans *= exp(-ext);
			if (trans < 0.03) break;
		}
		t += step_t;
	}
	// The march stops once all but 3% of the light is gone: that last little is gone too, or a star many
	// times the sky's brightness still shone through the thickest storm.
	float alpha = clamp((1.0 - trans) / 0.97, 0.0, 1.0);
	// What is behind them, they hide by all of their opacity; toward the march's reach the layer's far
	// cover takes over from what it found, so nothing steps where its reach ends.
	if (!lit) {
		float past = smoothstep(8000.0, 16000.0, t0);
		hide = 1.0 - (1.0 - alpha * (1.0 - past)) * (1.0 - far_cover(ro, rd) * past);
	}
	// Distant clouds thin into the haze along the horizon: their look fades into the sky's colour (an
	// opacity, laid over the sky gradient, and their colour leaning toward it).
	float fade = exp(-t0 / (6500.0 * density)) * (1.0 - smoothstep(9000.0, 16000.0, t0));
	// The low layer's far clouds would close to thin streaks along the horizon: they thin out first.
	// A storm stands out through the haze: its far parts keep their body.
	if (stormy) fade = max(fade, exp(-t0 / 14000.0) * (1.0 - smoothstep(9000.0, 16000.0, t0)));
	// A front's shelf and deck are what you watch coming: they stay in sight down to the horizon, sinking behind it.
	if (ro.y < base) fade *= mix(smoothstep(0.015, 0.09, rd.y), smoothstep(-0.01, 0.035, rd.y), fronts);
	else fade *= 0.3 + 0.7 * smoothstep(0.0, 0.07, abs(rd.y)); // in or over the layer, its edge on the horizon stays soft
	vec3 col = alpha > 0.001 ? light / alpha : sky;
	col = mix(mix(sky, col, 0.5), col, fade);
	alpha *= fade;
	return vec4(mix(sky, col, alpha), alpha);
}

// ---- The way out ---------------------------------------------------------------------------
// As the air thins with height the sky fades into space's own backdrop: the same galaxy and glare
// the orbit's sky draws (planet-generator's space shader), so when it takes over nothing changes.

uniform float space_fade = 0.0;        // 0 the planet's sky, 1 space
uniform mat3 space_turn = mat3(1.0);   // the world's directions into the backdrop's
uniform vec3 space_sun = vec3(0.0, 1.0, 0.0);

// Space behind the sky. The stars show once the sky has gone dark enough, or if it already was.
// The sun is the orbit's own (the include's sun_in_space): the same disc at the same size and
// light, so nothing about it changes as one sky gives way to the other.
vec3 space_backdrop(vec3 d, vec3 catalogue, float shown, bool points) {
	return vec3(0.003, 0.003, 0.006) + galaxy_sky(catalogue, points) * shown + sun_in_space(d, space_sun);
}

// Across the sun's face, level: the way its shimmer sways.
vec3 sun_east(vec3 sun) {
	vec3 e = cross(vec3(0.0, 1.0, 0.0), sun);
	return dot(e, e) > 1e-6 ? normalize(e) : vec3(1.0, 0.0, 0.0);
}

void sky() {
	vec3 dir = EYEDIR;
	vec3 sun = LIGHT0_ENABLED ? LIGHT0_DIRECTION : normalize(vec3(0.3, 0.6, -0.4));
	vec3 col = sky_color(dir, sun);

	if (AT_QUARTER_RES_PASS) {
		// How much the clouds hide of what is behind them, marched without light: cheap, and in a
		// channel with ten bits; and in the next, their opacity as laid over the sky (the way out reads it).
		float hide;
		float laid = march_clouds(POSITION, dir, sun, col, false, march_jitter(FRAGCOORD.xy), hide).a;
		COLOR = vec3(hide, laid, 0.0);
	} else if (AT_HALF_RES_PASS) {
		// The clouds laid over the plain sky, at half resolution: the costly part. Stored on a
		// square-root curve, scaled down for headroom: the buffer clamps at one and dark storm or
		// ash clouds would band without the extra precision near black.
		float hide;
		vec4 c = march_clouds(POSITION, dir, sun, col, true, march_jitter(FRAGCOORD.xy), hide);
		COLOR = sqrt(max(c.rgb, vec3(0.0)) * 0.25);
	} else {
		float day = smoothstep(-0.2, 0.15, sun.y) * (0.04 + 0.96 * sun_lit); // an eclipse's totality: deep dusk, the stars out
		vec3 plain = col;
		// Faded out just below the horizon, not cut: seen from high up there is no hard line.
		float starlight = max(1.0 - day, stars_by_day) * (1.0 - overcast) * smoothstep(-0.08, 0.0, dir.y);
		// Bodies show through a clear sky by day as well as by night, faded by the daylight's glare.
		// They stand in front of the stars, so no star shows through a world or a ring.
		vec4 solid = bodies(dir, sun);
		vec4 b = solid * (1.0 - overcast * 0.9);
		// The stars' points apart from the soft light round them: a thin veil of cloud dims the
		// points faster, so it blurs the night into its glow before it hides it.
		vec3 points = vec3(0.0);
		if (starlight > 0.0) {
			vec3 soft = night_sky(dir, false);
			vec3 full = AT_CUBEMAP_PASS ? soft : night_sky(dir, true);
			points = (full - soft) * starlight * (1.0 - solid.a);
			col += full * starlight * (1.0 - solid.a);
		}
		col = col * (1.0 - b.a * mix(0.88, 1.0, 1.0 - day)) + b.rgb;

		// The sun itself, in front of everything but cloud: dimmer and warmer low down, behind more air.
		// Low over a hot world it wavers in the rising air.
		vec3 sun_seen = dir;
		float shimmer = sun_heat * (1.0 - smoothstep(0.0, 0.35, sun.y)) * (sun_measure ? 0.0 : 1.0);
		if (shimmer > 0.0) {
			vec2 w = texture(cloud_noise, dir.xz * 37.0 + vec2(sun_time * 0.07, sun_time * 0.05)).rg - 0.5;
			sun_seen = normalize(dir + (sun_east(sun) * w.x + vec3(0.0, w.y, 0.0)) * shimmer * sun_radius * 0.3);
		}
		// Its real size and look (the include's); what crosses the sun hides it.
		vec3 disc = sun_body(sun_seen, sun) * (1.0 - b.a);
		float through = sun_air(sun.y);
		vec3 disc_color = mix(sunset, star_color, smoothstep(0.0, 0.3, sun.y)) * through * through;
		// Well under the glow threshold near the horizon: there it's a coloured disc, it doesn't bloom.
		float disc_energy = mix(1.5, 18.0, smoothstep(0.02, 0.4, sun.y));
		col += disc_color * disc * sun_energy * disc_energy * (1.0 - overcast) * smoothstep(-0.03, 0.0, dir.y);

		// The clouds over the plain sky (`clouded`, laid over it by `laid`), and whatever else is up there
		// (stars, galaxy, hole, bodies, the sun) where they let it through: all of it behind them, all
		// of it hidden by the one transmittance (`cover` is how much they hide).
		float cover, laid;
		vec3 clouded;
		if (AT_CUBEMAP_PASS) {
			// Reflections, ambient light and the fog's aerial perspective: just the overall cover, no
			// march, eased in over the horizon (a hard step there showed in fogged distance and water);
			// and a cheap look at the layer along the way for what it hides (no stars in the sea under a storm).
			laid = cloud_cover * 0.6 * smoothstep(-0.02, 0.12, dir.y);
			clouded = mix(plain, mix(cloud_shade, cloud_lit, 0.5) * (0.2 + 0.8 * day), laid);
			cover = (cloud_cover < 0.02 && front_shape.x <= 0.001) ? laid : 1.0 - (1.0 - laid) * (1.0 - far_cover(POSITION, dir));
		} else {
			clouded = HALF_RES_COLOR.rgb;
			clouded = clouded * clouded * 4.0;
			cover = clamp(QUARTER_RES_COLOR.r, 0.0, 1.0);
			laid = clamp(QUARTER_RES_COLOR.g, 0.0, 1.0);
		}
		float through_clouds = 1.0 - cover;
		col = clouded + (col - plain) * through_clouds - points * (through_clouds - through_clouds * through_clouds);
		// Cirrus: high thin streaks, a texture far overhead rather than a volume, combed along the
		// wind's way; they sit on top of the clouds below them only where those let the sky through.
		if (cirrus > 0.01 && tower_count == 0 && dir.y > 0.02 && !AT_CUBEMAP_PASS) {
			// The deck's perspective is eased (the streaks used to fan out to a point on the horizon) and the
			// combing warped, so they wander like real cirrus; patches of it come and go across the sky.
			vec2 q = dir.xz / (dir.y + 0.55) + cloud_drift * 0.00002;
			vec2 r = vec2(q.x * 0.82 + q.y * 0.57, q.y * 0.82 - q.x * 0.57);
			r += (textureLod(cloud_noise, q * 0.3 + 0.6, 0.0).rg - 0.5) * 0.7;
			float s = textureLod(cloud_noise, r * vec2(0.14, 0.42), 0.0).r * 0.7 + textureLod(cloud_noise, r * vec2(0.34, 1.1) + 0.2, 0.0).r * 0.3;
			float patch = smoothstep(0.35, 0.6, textureLod(cloud_noise, q * 0.12 + 0.3, 0.0).r);
			float wisps = smoothstep(0.5, 0.78, s) * patch * smoothstep(0.02, 0.22, dir.y) * (1.0 - cover);
			vec3 lit = mix(sunset * 1.4, cloud_lit, smoothstep(0.02, 0.32, sun.y)) * (0.08 + 0.92 * day);
			col = mix(col, lit, wisps * cirrus * 0.5);
		}
		if (debug_view == 1) col = vec3(cover);
		if (debug_view == 3) col = night_sky(dir, true) * 6.0;
		if (debug_view == 2) col = HALF_RES_COLOR.rgb * HALF_RES_COLOR.rgb * 4.0;
		// Overcast greys the whole sky. By night its flat grey is the night light's and takes less of it, so the
		// deck's own shape, lit as it is (the moon over it, the ground and the lamps under it, a strike), still
		// shows. (Not in the reflection cube: its cloud is a flat guess, and the night's ambient light stays as dark.)
		vec3 flat_grey = overcast_color * (0.15 + 0.85 * day) + night_color * night_light.w * 0.03 * (1.0 - day);
		float flat_share = AT_CUBEMAP_PASS ? 0.85 : mix(0.85, 0.45, 1.0 - day);
		col = mix(col, flat_grey, overcast * smoothstep(-0.1, 0.25, dir.y) * flat_share);
		// The air under the level goes first: nothing is left there to light but the world's own rim.
		if (space_fade > 0.0) {
			float gone = mix(space_fade, sqrt(space_fade), smoothstep(0.3, -0.25, dir.y));
			float dark = smoothstep(0.78, 1.0, space_fade);
			// The air goes; the sun and everything else up there stays, in front of the backdrop's stars;
			// and any cloud still over you stays in front of all of it: the backdrop's dark where they
			// are laid over the air, the rest of the backdrop only as far as they let it through.
			vec3 night = vec3(0.003, 0.003, 0.006);
			vec3 behind = space_backdrop(space_turn * dir, star_turn * dir, max(dark * dark, 1.0 - day), !AT_CUBEMAP_PASS) * (1.0 - solid.a) + solid.rgb;
			vec3 spaced = max(clouded - (plain - night) * (1.0 - laid), vec3(0.0)) + (behind - night) * through_clouds;
			col = mix(col, spaced, gone);
		}
		COLOR = col;
	}
}
